using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Threading;
using WpfMarkdownEditor.Core.Parsing;
using WpfMarkdownEditor.Wpf.Rendering;
using WpfMarkdownEditor.Wpf.Services;
using WpfMarkdownEditor.Wpf.SyntaxHighlighting;
using WpfMarkdownEditor.Wpf.Theming;
using CoreBlock = WpfMarkdownEditor.Core.Parsing.Block;
using WpfBlock = System.Windows.Documents.Block;

namespace WpfMarkdownEditor.Wpf.Controls;

/// <summary>
/// Owns the debounced parse/render loop for the preview pane, including the parsed-AST
/// cache, adaptive debounce interval and the image resolver cache.
/// </summary>
internal sealed class PreviewRenderController : IDisposable
{
    private static readonly object PreviewErrorTag = new();

    private const int ProgressiveBlockThreshold = 400;
    private const int InitialChunkBlocks = 120;
    private const int ChunkBlocks = 80;
    private const int ChunkBudgetMs = 8;

    private readonly MarkdownParser _parser = new();
    private readonly SyntaxHighlighter _highlighter = new();
    private readonly DispatcherTimer _debounceTimer;
    private readonly FlowDocumentScrollViewer _previewViewer;
    private readonly Func<string> _markdownProvider;
    private readonly Func<EditorTheme> _themeProvider;
    private readonly Func<string> _baseDirectoryProvider;
    private readonly PreviewViewportController? _viewportController;
    private readonly CodeBlockHighlightScheduler _codeHighlighter;
    private readonly Action<PreviewRenderedEventArgs>? _onRendered;

    private ImageLoader _imageLoader;
    private CachingImageResolver _imageResolver;
    private FlowDocumentRenderer? _renderer;
    private CancellationTokenSource? _cts;
    private int _renderVersion;
    private FlowDocument? _previewDocument;
    private bool _layoutRefreshQueued;
    private string? _translatedMarkdown;
    private PreviewRenderInput? _cachedInput;
    private int _lastRenderDurationMs;
    private int _fastRenderCount;
    private ProgressiveRenderSession? _progressiveSession;
    private bool _disposed;

    public PreviewRenderController(
        FlowDocumentScrollViewer previewViewer,
        Func<string> markdownProvider,
        Func<EditorTheme> themeProvider,
        Func<string> baseDirectoryProvider,
        PreviewViewportController? viewportController = null,
        Action<PreviewRenderedEventArgs>? onRendered = null)
    {
        _previewViewer = previewViewer;
        _markdownProvider = markdownProvider;
        _themeProvider = themeProvider;
        _baseDirectoryProvider = baseDirectoryProvider;
        _viewportController = viewportController;
        _onRendered = onRendered;
        _imageLoader = new ImageLoader(baseDirectoryProvider());
        _imageResolver = new CachingImageResolver(_imageLoader);
        _codeHighlighter = new CodeBlockHighlightScheduler(previewViewer, RequestPreviewLayoutRefresh);

        _debounceTimer = new DispatcherTimer(DispatcherPriority.Background, previewViewer.Dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(100),
        };
        _debounceTimer.Tick += OnDebounceTick;

        RefreshRenderer();
    }

    /// <summary>
    /// Statistics of the most recent incremental render. Used by tests and diagnostics.
    /// </summary>
    internal PreviewRenderStats LastRenderStats => _renderer?.LastRenderStats ?? default;

    /// <summary>
    /// True while a large document is being appended after its first paint chunk.
    /// </summary>
    internal bool IsProgressiveRendering => _progressiveSession is { IsComplete: false };

    /// <summary>
    /// Number of top-level blocks currently mounted in the preview document.
    /// </summary>
    internal int RenderedPreviewBlockCount =>
        _progressiveSession?.RenderedBlockCount ?? _previewDocument?.Blocks.Count ?? 0;

    /// <summary>
    /// Time spent mounting the first progressive chunk, including parse time.
    /// </summary>
    internal int LastFirstPaintElapsedMs { get; private set; }

    /// <summary>
    /// Marks the markdown as changed, clears any translated preview, and schedules a render.
    /// </summary>
    public void OnMarkdownChanged()
    {
        if (_disposed)
            return;

        _translatedMarkdown = null;
        ScheduleRender();
    }

    /// <summary>
    /// Updates the image base directory after the document path changed, then re-renders.
    /// The parsed-AST cache stays valid; only the image resolver cache is invalidated.
    /// </summary>
    public void OnDocumentPathChanged()
    {
        var previous = _imageLoader;
        _imageLoader = new ImageLoader(_baseDirectoryProvider());
        _imageResolver = new CachingImageResolver(_imageLoader);
        previous.Dispose();

        RefreshRenderer();
        ScheduleRender();
    }

    /// <summary>
    /// Rebuilds the renderer for a new theme and renders immediately.
    /// The parsed AST and image data caches are kept across theme switches.
    /// </summary>
    public void OnThemeChanged()
    {
        RefreshRenderer();
        RenderNow();
    }

    public void RenderTranslatedPreview(string translatedMarkdown)
    {
        _translatedMarkdown = translatedMarkdown;
        _progressiveSession = null;
        _previewDocument = null;
        RenderNow();
    }

    public void ClearTranslatedPreview()
    {
        _translatedMarkdown = null;
        _progressiveSession = null;
        _previewDocument = null;
        RenderNow();
    }

    public bool TryGetPrintablePreviewDocument(out FlowDocument document)
    {
        document = null!;
        if (IsProgressiveRendering)
            return false;

        var candidate = _previewViewer.Document;
        if (candidate is null || candidate.Blocks.Count == 0 || ReferenceEquals(candidate.Tag, PreviewErrorTag))
            return false;

        document = candidate;
        return true;
    }

    /// <summary>
    /// Renders the current content immediately, bypassing the debounce timer.
    /// </summary>
    public void RenderNow()
    {
        if (_disposed)
            return;

        _debounceTimer.Stop();
        var version = Interlocked.Increment(ref _renderVersion);
        var oldCts = SwapCts();
        oldCts?.Cancel();
        oldCts?.Dispose();
        var ct = _cts!.Token;

        _ = RenderNowAsync(version, ct);
    }

    private async Task RenderNowAsync(int version, CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var markdown = _translatedMarkdown ?? _markdownProvider();
            var renderer = _renderer;
            if (renderer is null)
                return;

            List<CoreBlock> blocks;
            SourceLineIndex? lineIndex = null;
            BlockSignature[]? signatures = null;

            if (_translatedMarkdown is not null)
            {
                var input = await Task.Run(
                    () =>
                    {
                        var parsed = _parser.Parse(markdown);
                        var index = SourceLineIndex.Create(markdown);
                        return new PreviewRenderInput(
                            markdown,
                            parsed,
                            index,
                            BlockSignatureFactory.Create(parsed, index));
                    },
                    ct);

                if (version != Volatile.Read(ref _renderVersion))
                    return;

                blocks = input.Blocks;
                lineIndex = input.LineIndex;
                signatures = input.Signatures;
            }
            else
            {
                var cached = _cachedInput;
                if (cached is not null && ReferenceEquals(cached.Markdown, markdown))
                {
                    blocks = cached.Blocks;
                    lineIndex = cached.LineIndex;
                    signatures = cached.Signatures;
                }
                else
                {
                    var input = await Task.Run(
                        () =>
                        {
                            var parsed = _parser.Parse(markdown);
                            var index = SourceLineIndex.Create(markdown);
                            return new PreviewRenderInput(
                                markdown,
                                parsed,
                                index,
                                BlockSignatureFactory.Create(parsed, index));
                        },
                        ct);

                    if (version != Volatile.Read(ref _renderVersion))
                        return;

                    _cachedInput = input;
                    blocks = input.Blocks;
                    lineIndex = input.LineIndex;
                    signatures = input.Signatures;
                }
            }

            // Version check — discard stale results
            if (version != Volatile.Read(ref _renderVersion))
                return;
            ct.ThrowIfCancellationRequested();

            var renderIncrementally = _translatedMarkdown is null;

            if (_progressiveSession is { IsComplete: false } activeSession)
            {
                if (TryResumeProgressive(activeSession, version, markdown, blocks, lineIndex!, signatures!))
                    return;

                _progressiveSession = null;
                if (ReferenceEquals(_previewViewer.Document, activeSession.Document))
                    _previewViewer.Document = null;
                _previewDocument = null;
            }

            var anchor = _viewportController?.CaptureAnchor();
            _viewportController?.OnBeforeRender();
            _codeHighlighter.OnBeforeRender();

            var existingDocument = renderIncrementally ? _previewDocument : null;
            if ((existingDocument is null || existingDocument.Blocks.Count == 0)
                && blocks.Count >= ProgressiveBlockThreshold)
            {
                StartProgressiveRender(version, markdown, blocks, lineIndex!, signatures!, anchor, stopwatch);
                return;
            }

            if (existingDocument is not null && ReferenceEquals(_previewViewer.Document, existingDocument))
                _previewViewer.Document = null;

            var document = renderIncrementally
                ? renderer.RenderIncremental(existingDocument, blocks, lineIndex!, signatures!)
                : renderer.Render(blocks);

            // Final version check before UI update
            if (version != Volatile.Read(ref _renderVersion))
                return;

            _previewDocument = document;
            _previewViewer.Document = document;
            if (anchor is { } captured)
                _viewportController?.RestoreAnchor(captured);
            _viewportController?.OnAfterRender();
            _codeHighlighter.OnDocumentRendered();

            RaiseRendered(markdown, blocks, isComplete: true);

            _lastRenderDurationMs = (int)stopwatch.ElapsedMilliseconds;
            UpdateDebounceInterval();
        }
        catch (OperationCanceledException)
        {
            // Newer render superseded this one
        }
        catch (Exception)
        {
            _progressiveSession = null;
            _previewDocument = BuildErrorDocument("Preview unavailable");
            _previewViewer.Document = _previewDocument;
        }
    }

    private void StartProgressiveRender(
        int version,
        string markdown,
        List<CoreBlock> blocks,
        SourceLineIndex lineIndex,
        BlockSignature[] signatures,
        PreviewAnchor? anchor,
        Stopwatch stopwatch)
    {
        var renderer = _renderer;
        if (renderer is null)
        {
            _previewDocument = BuildErrorDocument("Preview unavailable");
            _previewViewer.Document = _previewDocument;
            return;
        }

        var document = renderer.CreateProgressiveDocument();
        var initialCount = Math.Min(InitialChunkBlocks, blocks.Count);
        var append = renderer.AppendBlockRange(document, blocks, signatures, 0, initialCount);

        var session = new ProgressiveRenderSession(
            version,
            markdown,
            blocks,
            lineIndex,
            signatures,
            document,
            append.NextIndex,
            append.RenderedCount,
            (int)stopwatch.ElapsedMilliseconds);

        _progressiveSession = session;
        _previewDocument = document;
        _previewViewer.Document = document;
        LastFirstPaintElapsedMs = session.FirstPaintElapsedMs;

        if (anchor is { } captured)
            _viewportController?.RestoreAnchor(captured);
        _viewportController?.OnAfterRender();
        _codeHighlighter.OnBlocksAppended(append.AppendedBlocks);

        _lastRenderDurationMs = session.FirstPaintElapsedMs;
        UpdateDebounceInterval();

        if (session.IsComplete)
        {
            CompleteProgressive(session);
            return;
        }

        RaiseRendered(markdown, blocks, isComplete: false);
        ScheduleNextChunk(session);
    }

    private bool TryResumeProgressive(
        ProgressiveRenderSession session,
        int version,
        string markdown,
        List<CoreBlock> blocks,
        SourceLineIndex lineIndex,
        BlockSignature[] signatures)
    {
        var renderedBlocks = session.Document.Blocks.Cast<WpfBlock>().ToList();
        if (session.RenderedBlockCount != renderedBlocks.Count || renderedBlocks.Count > blocks.Count)
            return false;

        for (var i = 0; i < renderedBlocks.Count; i++)
        {
            if (!RenderedBlockMetadata.TryGet(renderedBlocks[i], out var info))
                return false;

            var newSignature = signatures[i];
            if (!info.Signature.IsReusable
                || !newSignature.IsReusable
                || info.Signature != newSignature)
            {
                return false;
            }
        }

        session.Version = version;
        session.Markdown = markdown;
        session.Blocks = blocks;
        session.LineIndex = lineIndex;
        session.Signatures = signatures;
        session.NextIndex = renderedBlocks.Count;
        session.RenderedBlockCount = renderedBlocks.Count;

        if (session.IsComplete)
            CompleteProgressive(session);
        else
            ScheduleNextChunk(session);

        return true;
    }

    private void ScheduleNextChunk(ProgressiveRenderSession session)
    {
        if (_disposed || session.IsChunkScheduled)
            return;

        session.IsChunkScheduled = true;
        _previewViewer.Dispatcher.BeginInvoke(
            DispatcherPriority.Background,
            new Action(() =>
            {
                session.IsChunkScheduled = false;
                RenderNextChunk(session);
            }));
    }

    private void RenderNextChunk(ProgressiveRenderSession session)
    {
        if (_disposed
            || !ReferenceEquals(_progressiveSession, session)
            || session.Version != Volatile.Read(ref _renderVersion))
        {
            return;
        }

        var renderer = _renderer;
        if (renderer is null)
        {
            AbortProgressive(session);
            return;
        }

        try
        {
            var stopwatch = Stopwatch.StartNew();
            var appended = new List<WpfBlock>();
            var renderedBlockCount = session.RenderedBlockCount;
            var processed = 0;

            while (!session.IsComplete
                   && processed < ChunkBlocks
                   && (processed == 0 || stopwatch.ElapsedMilliseconds < ChunkBudgetMs))
            {
                var append = renderer.AppendBlockRange(
                    session.Document,
                    session.Blocks,
                    session.Signatures,
                    session.NextIndex,
                    1);

                if (append.NextIndex <= session.NextIndex)
                    break;

                session.NextIndex = append.NextIndex;
                renderedBlockCount += append.RenderedCount;
                if (append.AppendedBlocks.Count > 0)
                    appended.AddRange(append.AppendedBlocks);

                processed++;
            }

            session.RenderedBlockCount = renderedBlockCount;

            if (appended.Count > 0)
                _codeHighlighter.OnBlocksAppended(appended);

            if (session.IsComplete)
                CompleteProgressive(session);
            else
                ScheduleNextChunk(session);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            AbortProgressive(session);
        }
    }

    private void CompleteProgressive(ProgressiveRenderSession session)
    {
        if (!ReferenceEquals(_progressiveSession, session))
            return;

        _progressiveSession = null;
        _viewportController?.RefreshHighlight();
        RaiseRendered(session.Markdown, session.Blocks, isComplete: true);
    }

    private void AbortProgressive(ProgressiveRenderSession session)
    {
        if (!ReferenceEquals(_progressiveSession, session))
            return;

        _progressiveSession = null;
        _previewDocument = BuildErrorDocument("Preview unavailable");
        _previewViewer.Document = _previewDocument;
    }

    private void RaiseRendered(string markdown, IReadOnlyList<CoreBlock> blocks, bool isComplete)
    {
        try
        {
            _onRendered?.Invoke(new PreviewRenderedEventArgs
            {
                Markdown = markdown,
                Blocks = blocks,
                IsComplete = isComplete,
            });
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            // A consumer (for example the outline pane) must not break preview rendering.
        }
    }

    /// <summary>
    /// Stops a pending debounce render and starts a new one.
    /// </summary>
    private void ScheduleRender()    {
        _debounceTimer.Stop();
        var oldCts = SwapCts();
        oldCts?.Cancel();
        oldCts?.Dispose();
        _debounceTimer.Start();
    }

    private void OnDebounceTick(object? sender, EventArgs e)
    {
        _debounceTimer.Stop();
        RenderNow();
    }

    private void UpdateDebounceInterval()
    {
        double interval;
        if (_lastRenderDurationMs > 150)
        {
            interval = 400;
            _fastRenderCount = 0;
        }
        else if (_lastRenderDurationMs > 60)
        {
            interval = 250;
            _fastRenderCount = 0;
        }
        else
        {
            _fastRenderCount++;
            interval = _fastRenderCount >= 2 ? 100 : _debounceTimer.Interval.TotalMilliseconds;
        }

        _debounceTimer.Interval = TimeSpan.FromMilliseconds(interval);
    }

    private void RefreshRenderer()
    {
        _progressiveSession = null;
        _previewDocument = null;
        _renderer = new FlowDocumentRenderer(_themeProvider(), _imageResolver, _highlighter, RequestPreviewLayoutRefresh);
    }

    private void RequestPreviewLayoutRefresh()
    {
        if (_layoutRefreshQueued)
            return;

        _layoutRefreshQueued = true;
        _previewViewer.Dispatcher.BeginInvoke(
            DispatcherPriority.Render,
            new Action(() =>
            {
                _layoutRefreshQueued = false;
                var document = _previewViewer.Document;
                _previewViewer.InvalidateMeasure();
                _previewViewer.InvalidateArrange();
                _previewViewer.InvalidateVisual();
                _previewViewer.UpdateLayout();
            }));
    }

    private static FlowDocument BuildErrorDocument(string message)
    {
        var doc = new FlowDocument { Tag = PreviewErrorTag };
        doc.Blocks.Add(new Paragraph(new Run(message)) { FontStyle = FontStyles.Italic });
        return doc;
    }

    /// <summary>
    /// Atomically swap the CancellationTokenSource with a new one. Returns the old CTS for disposal.
    /// </summary>
    private CancellationTokenSource? SwapCts()
    {
        var newCts = new CancellationTokenSource();
        return Interlocked.Exchange(ref _cts, newCts);
    }

    public void Dispose()
    {
        _disposed = true;
        _progressiveSession = null;
        _codeHighlighter.Dispose();
        _imageLoader.Dispose();
        _debounceTimer.Stop();
        var oldCts = Interlocked.Exchange(ref _cts, null);
        oldCts?.Cancel();
        oldCts?.Dispose();
    }
}
