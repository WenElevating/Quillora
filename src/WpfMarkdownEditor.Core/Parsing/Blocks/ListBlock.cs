using WpfMarkdownEditor.Core.Parsing;

namespace WpfMarkdownEditor.Core.Parsing.Blocks;

public sealed class ListBlock : Block
{
    public bool IsOrdered { get; set; }
    public int StartNumber { get; set; } = 1;
    public List<ListItem> Items { get; set; } = [];
}

public sealed class ListItem
{
    public List<Block> Blocks { get; set; } = [];
    public bool IsTask { get; set; }
    public bool IsTaskChecked { get; set; }
}
