using System.Diagnostics;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using WpfMarkdownEditor.Core;
using WpfMarkdownEditor.Core.Parsing.Blocks;
using WpfMarkdownEditor.Wpf.Rendering.Renderers;
using WpfMarkdownEditor.Wpf.SyntaxHighlighting;
using WpfMarkdownEditor.Wpf.Theming;
using CoreBlock = WpfMarkdownEditor.Core.Parsing.Block;
using WpfBlock = System.Windows.Documents.Block;

namespace WpfMarkdownEditor.Wpf.Rendering;

internal readonly record struct PreviewRenderStats(int Reused, int Rebuilt, int Removed, long ElapsedMilliseconds);

internal readonly record struct ProgressiveAppendResult(
    int NextIndex,
    IReadOnlyList<WpfBlock> AppendedBlocks,
    int RenderedCount);

/// <summary>
/// Renders Block AST to WPF FlowDocument.
/// </summary>
public sealed class FlowDocumentRenderer
{
    private readonly Dictionary<Type, IBlockRenderer> _renderers;
    private readonly EditorTheme _theme;
    private readonly SolidColorBrush _backgroundBrush;
    private readonly SolidColorBrush _foregroundBrush;

    public FlowDocumentRenderer(EditorTheme theme) : this(theme, null, null) { }

    public FlowDocumentRenderer(EditorTheme theme, IImageResolver? imageResolver) : this(theme, imageResolver, null) { }

    public FlowDocumentRenderer(
        EditorTheme theme,
        IImageResolver? imageResolver,
        SyntaxHighlighter? highlighter = null,
        Action? requestLayoutRefresh = null)
    {
        _theme = theme;
        _backgroundBrush = EditorBrushCache.Get(theme.BackgroundColor);
        _foregroundBrush = EditorBrushCache.Get(theme.ForegroundColor);
        var cachedImageResolver = imageResolver is null
            ? null
            : imageResolver as CachingImageResolver ?? new CachingImageResolver(imageResolver);
        _renderers = new()
        {
            [typeof(HeadingBlock)] = new HeadingRenderer(theme, cachedImageResolver, requestLayoutRefresh),
            [typeof(ParagraphBlock)] = new ParagraphRenderer(theme, cachedImageResolver, requestLayoutRefresh),
            [typeof(CodeBlock)] = new CodeBlockRenderer(theme, highlighter),
            [typeof(HtmlBlock)] = new HtmlRenderer(theme, cachedImageResolver, requestLayoutRefresh, highlighter),
            [typeof(TableBlock)] = new TableRenderer(theme, cachedImageResolver, requestLayoutRefresh),
            [typeof(ListBlock)] = new ListRenderer(theme, cachedImageResolver, requestLayoutRefresh),
            [typeof(ThematicBreakBlock)] = new ThematicBreakRenderer(theme),
            [typeof(ImageBlock)] = new ImageRenderer(theme, cachedImageResolver, requestLayoutRefresh),
        };
        // BlockquoteRenderer references the parent — must be added after dictionary is built
        _renderers[typeof(BlockquoteBlock)] = new BlockquoteRenderer(theme, this);
    }

    /// <summary>
    /// Statistics for the most recent <see cref="RenderIncremental(FlowDocument?, IReadOnlyList{CoreBlock}, string)"/> call.
    /// </summary>
    internal PreviewRenderStats LastRenderStats { get; private set; }

    /// <summary>
    /// Render blocks to FlowDocument.
    /// </summary>
    public FlowDocument Render(IEnumerable<CoreBlock> blocks)
    {
        var document = CreateDocument();

        foreach (var block in blocks)
        {
            var rendered = RenderBlock(block);
            if (rendered is not null)
                document.Blocks.Add(rendered);
        }

        return document;
    }

    /// <summary>
    /// Updates an existing document in place, reusing unchanged top-level blocks.
    /// </summary>
    public FlowDocument RenderIncremental(FlowDocument? document, IReadOnlyList<CoreBlock> blocks, string markdown)
    {
        var index = SourceLineIndex.Create(markdown);
        var signatures = BlockSignatureFactory.Create(blocks, index);
        return RenderIncremental(document, blocks, index, signatures);
    }

    /// <summary>
    /// Incremental rendering with a precomputed source index and block signatures.
    /// The controller computes these on a background thread so the UI thread only diffs
    /// and creates the elements that actually changed.
    /// </summary>
    internal FlowDocument RenderIncremental(
        FlowDocument? document,
        IReadOnlyList<CoreBlock> blocks,
        SourceLineIndex index,
        IReadOnlyList<BlockSignature> signatures)
    {
        var stopwatch = Stopwatch.StartNew();

        if (document is null)
        {
            document = CreateDocument();
            var renderedCount = RenderAll(document, blocks, signatures);
            LastRenderStats = new PreviewRenderStats(0, renderedCount, 0, stopwatch.ElapsedMilliseconds);
            return document;
        }

        ApplyDocumentStyle(document);

        var existingBlocks = document.Blocks.Cast<WpfBlock>().ToList();
        if (existingBlocks.Count == 0)
        {
            var renderedCount = RenderAll(document, blocks, signatures);
            LastRenderStats = new PreviewRenderStats(0, renderedCount, 0, stopwatch.ElapsedMilliseconds);
            return document;
        }

        var oldInfos = new RenderedBlockInfo[existingBlocks.Count];
        for (var i = 0; i < existingBlocks.Count; i++)
        {
            if (!RenderedBlockMetadata.TryGet(existingBlocks[i], out oldInfos[i]))
            {
                var renderedCount = RenderAll(document, blocks, signatures);
                LastRenderStats = new PreviewRenderStats(0, renderedCount, existingBlocks.Count, stopwatch.ElapsedMilliseconds);
                return document;
            }
        }

        var oldCount = existingBlocks.Count;
        var newCount = blocks.Count;
        var sharedCount = Math.Min(oldCount, newCount);

        var prefixCount = 0;
        while (prefixCount < sharedCount
               && IsReusableMatch(oldInfos[prefixCount].Signature, signatures[prefixCount]))
        {
            prefixCount++;
        }

        var suffixCount = 0;
        while (suffixCount < sharedCount - prefixCount
               && IsReusableMatch(oldInfos[oldCount - 1 - suffixCount].Signature, signatures[newCount - 1 - suffixCount]))
        {
            suffixCount++;
        }

        if (prefixCount == oldCount && prefixCount == newCount)
        {
            for (var i = 0; i < prefixCount; i++)
                SetMetadata(existingBlocks[i], oldInfos[i].Signature, blocks[i]);

            var reusedAll = prefixCount;
            LastRenderStats = new PreviewRenderStats(reusedAll, 0, 0, stopwatch.ElapsedMilliseconds);
            return document;
        }

        var oldMiddleStart = prefixCount;
        var oldMiddleEnd = oldCount - suffixCount;
        var newMiddleStart = prefixCount;
        var newMiddleEnd = newCount - suffixCount;
        var oldMiddleCount = oldMiddleEnd - oldMiddleStart;

        // Large replacements are cheaper as a full rebuild than as many collection edits.
        if (oldCount >= 32 && oldMiddleCount > oldCount / 2)
        {
            var renderedCount = RenderAll(document, blocks, signatures);
            LastRenderStats = new PreviewRenderStats(0, renderedCount, oldCount, stopwatch.ElapsedMilliseconds);
            return document;
        }

        var reusableBlocks = new Dictionary<BlockSignature, Queue<WpfBlock>>();
        for (var i = oldMiddleStart; i < oldMiddleEnd; i++)
        {
            var signature = oldInfos[i].Signature;
            if (!signature.IsReusable)
                continue;

            if (!reusableBlocks.TryGetValue(signature, out var queue))
            {
                queue = new Queue<WpfBlock>();
                reusableBlocks[signature] = queue;
            }

            queue.Enqueue(existingBlocks[i]);
        }

        var targetMiddle = new List<WpfBlock>(newMiddleEnd - newMiddleStart);
        var reusedMiddle = 0;
        var rebuiltMiddle = 0;
        for (var i = newMiddleStart; i < newMiddleEnd; i++)
        {
            var signature = signatures[i];
            WpfBlock? rendered = null;

            if (signature.IsReusable
                && reusableBlocks.TryGetValue(signature, out var queue)
                && queue.Count > 0)
            {
                rendered = queue.Dequeue();
                reusedMiddle++;
            }
            else
            {
                rendered = RenderBlock(blocks[i]);
                rebuiltMiddle++;
            }

            if (rendered is null)
                continue;

            SetMetadata(rendered, signature, blocks[i]);
            targetMiddle.Add(rendered);
        }

        for (var i = oldMiddleStart; i < oldMiddleEnd; i++)
            document.Blocks.Remove(existingBlocks[i]);

        // Keep line metadata accurate for blocks whose content stayed the same but whose
        // source lines moved because of an insertion/deletion above them.
        for (var i = 0; i < prefixCount; i++)
            SetMetadata(existingBlocks[i], oldInfos[i].Signature, blocks[i]);

        for (var i = 0; i < suffixCount; i++)
        {
            var oldIndex = oldCount - suffixCount + i;
            var newIndex = newCount - suffixCount + i;
            SetMetadata(existingBlocks[oldIndex], oldInfos[oldIndex].Signature, blocks[newIndex]);
        }

        if (suffixCount > 0)
        {
            var anchor = existingBlocks[oldCount - suffixCount];
            foreach (var block in targetMiddle)
                document.Blocks.InsertBefore(anchor, block);
        }
        else
        {
            foreach (var block in targetMiddle)
                document.Blocks.Add(block);
        }

        LastRenderStats = new PreviewRenderStats(
            prefixCount + suffixCount + reusedMiddle,
            rebuiltMiddle,
            oldMiddleCount - reusedMiddle,
            stopwatch.ElapsedMilliseconds);
        return document;
    }

    /// <summary>
    /// Render a single block. Public for nested rendering (e.g., blockquotes).
    /// </summary>
    public WpfBlock? RenderBlock(CoreBlock block)
    {
        if (_renderers.TryGetValue(block.GetType(), out var renderer))
            return renderer.Render(block);
        return null;
    }

    /// <summary>
    /// Creates an empty, styled document for progressive rendering.
    /// </summary>
    internal FlowDocument CreateProgressiveDocument() => CreateDocument();

    /// <summary>
    /// Appends the next slice of blocks to a document. Used by progressive first-paint
    /// rendering so large documents can be mounted in viewport-sized batches.
    /// </summary>
    internal ProgressiveAppendResult AppendBlockRange(
        FlowDocument document,
        IReadOnlyList<CoreBlock> blocks,
        IReadOnlyList<BlockSignature> signatures,
        int startIndex,
        int count)
    {
        var start = Math.Clamp(startIndex, 0, blocks.Count);
        var end = Math.Clamp(start + Math.Max(0, count), start, blocks.Count);

        var appended = new List<WpfBlock>(end - start);
        var renderedCount = 0;
        for (var i = start; i < end; i++)
        {
            var rendered = RenderBlock(blocks[i]);
            if (rendered is null)
                continue;

            SetMetadata(rendered, signatures[i], blocks[i]);
            document.Blocks.Add(rendered);
            appended.Add(rendered);
            renderedCount++;
        }

        return new ProgressiveAppendResult(end, appended, renderedCount);
    }

    private int RenderAll(FlowDocument document, IReadOnlyList<CoreBlock> blocks, IReadOnlyList<BlockSignature> signatures)
    {
        document.Blocks.Clear();
        return AppendBlockRange(document, blocks, signatures, 0, blocks.Count).RenderedCount;
    }

    private static bool IsReusableMatch(BlockSignature oldSignature, BlockSignature newSignature) =>
        oldSignature.IsReusable && newSignature.IsReusable && oldSignature == newSignature;

    private static void SetMetadata(WpfBlock block, BlockSignature signature, CoreBlock source)
    {
        RenderedBlockMetadata.Set(block, signature, source.LineStart, source.LineEnd);
    }

    private FlowDocument CreateDocument()
    {
        var document = new FlowDocument();
        ApplyDocumentStyle(document);
        return document;
    }

    private void ApplyDocumentStyle(FlowDocument document)
    {
        document.Background = _backgroundBrush;
        document.Foreground = _foregroundBrush;
        document.FontFamily = _theme.BodyFont;
        document.FontSize = _theme.BaseFontSize;
        document.PagePadding = _theme.PagePadding;
        document.TextAlignment = TextAlignment.Left;
        if (!double.IsNaN(_theme.LineHeight) && _theme.LineHeight > 0)
            document.LineHeight = _theme.LineHeight;
        else
            document.ClearValue(FlowDocument.LineHeightProperty);
    }
}
