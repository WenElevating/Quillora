using System.Windows;
using System.Windows.Documents;

namespace WpfMarkdownEditor.Wpf.Rendering;

/// <summary>
/// Source mapping attached to a rendered top-level block. The signature doubles as the
/// reuse key for incremental rendering; the line range maps preview blocks back to source.
/// </summary>
internal readonly record struct RenderedBlockInfo(
    BlockSignature Signature,
    int SourceStartLine,
    int SourceEndLine);

internal static class RenderedBlockMetadata
{
    private static readonly DependencyProperty InfoProperty = DependencyProperty.RegisterAttached(
        "Info",
        typeof(RenderedBlockInfo?),
        typeof(RenderedBlockMetadata),
        new PropertyMetadata(null));

    public static void Set(System.Windows.Documents.Block block, BlockSignature signature, int sourceStartLine, int sourceEndLine)
    {
        block.SetValue(
            InfoProperty,
            new RenderedBlockInfo(signature, sourceStartLine, sourceEndLine));
    }

    public static bool TryGet(System.Windows.Documents.Block block, out RenderedBlockInfo info)
    {
        if (block.GetValue(InfoProperty) is RenderedBlockInfo value)
        {
            info = value;
            return true;
        }

        info = default;
        return false;
    }
}
