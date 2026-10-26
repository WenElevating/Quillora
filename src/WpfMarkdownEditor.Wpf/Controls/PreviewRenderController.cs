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

namespace WpfMarkdownEditor.Wpf.Controls;

/// <summary>
/// Owns the debounced parse/render loop for the preview pane, including the parsed-AST
/// cache, adaptive debounce interval and the image resolver cache.
/// </summary>
internal sealed class PreviewRenderController : IDisposable
{
    private static readonly object PreviewErrorTag = new();

    private readonly MarkdownParser _parser = new();
    private readonly SyntaxHighlighter _highlighter = new();
    private readonly DispatcherTimer _debounceTimer;
    private readonly FlowDocumentScrollViewer _previewViewer;
    private readonly Func<string> _markdownProvider;
    private readonly Func<EditorTheme> _themeProvider;
    private readonly Func<string> _baseDirectoryProvider;
    private readonly PreviewViewportController? _viewportController;

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

    public PreviewRenderController(
        FlowDocumentScrollViewer previewViewer,
        Func<string> markdownProvider,
        Func<EditorTheme> themeProvider,
        Func<string> baseDirectoryProvider,
        PreviewViewportController? viewportController = null)
    {
        _previewViewer = previewViewer;
        _markdownProvider = markdownProvider;
        _themeProvider = themeProvider;
        _baseDirectoryProvider = baseDirectoryProvider;
        _viewportController = viewportController;
        _imageLoader = new ImageLoader(baseDirectoryProvider());
        _imageResolver = new CachingImageResolver(_imageLoader);

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
    /// Marks the markdown as changed, clears any translated preview, and schedules a render.
    /// </summary>
    public void OnMarkdownChanged()
    {
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
        RenderNow();
    }

    public void ClearTranslatedPreview()
    {
        _translatedMarkdown = null;
        RenderNow();
    }

    public bool TryGetPrintablePreviewDocument(out FlowDocument document)
    {
        document = null!;
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
                blocks = await Task.Run(() => _parser.Parse(markdown), ct);
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

            var anchor = _viewportController?.CaptureAnchor();
            var renderIncrementally = _translatedMarkdown is null;
            var existingDocument = renderIncrementally ? _previewDocument : null;
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

            _lastRenderDurationMs = (int)stopwatch.ElapsedMilliseconds;
            UpdateDebounceInterval();
        }
        catch (OperationCanceledException)
        {
            // Newer render superseded this one
        }
        catch (Exception)
        {
            _previewDocument = BuildErrorDocument("Preview unavailable");
            _previewViewer.Document = _previewDocument;
        }
    }

    /// <summary>
    /// Stops a pending debounce render and starts a new one.
    /// </summary>
    private void ScheduleRender()
    {
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
        _imageLoader.Dispose();
        _debounceTimer.Stop();
        var oldCts = Interlocked.Exchange(ref _cts, null);
        oldCts?.Cancel();
        oldCts?.Dispose();
    }
}
