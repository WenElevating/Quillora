using WpfMarkdownEditor.Core.Parsing;

namespace WpfMarkdownEditor.Wpf.Rendering;

/// <summary>
/// Identifies a rendered top-level block by its AST type and a hash of its source text.
/// Line numbers are deliberately excluded so inserting text above a block does not
/// invalidate that block's rendered element.
/// </summary>
internal readonly record struct BlockSignature(Type BlockType, ulong ContentHash, bool IsReusable);

internal static class BlockSignatureFactory
{
    public static BlockSignature Create(Block block, SourceLineIndex index)
    {
        if (!index.TryComputeHash(block.LineStart, block.LineEnd, out var hash))
            return new BlockSignature(block.GetType(), 0, IsReusable: false);

        return new BlockSignature(block.GetType(), hash, IsReusable: true);
    }

    public static BlockSignature[] Create(IReadOnlyList<Block> blocks, SourceLineIndex index)
    {
        var signatures = new BlockSignature[blocks.Count];
        for (var i = 0; i < blocks.Count; i++)
            signatures[i] = Create(blocks[i], index);

        return signatures;
    }
}
