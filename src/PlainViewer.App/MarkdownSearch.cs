using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
namespace PlainViewer.App;

// Embedded code controls remain separate accessible text providers. Search and whole-document
// copying combine them with the surrounding FlowDocument in reading order.
public static class MarkdownSearch
{
    private sealed record Region(TextPointer Start, TextPointer End, TextBox? Code)
    {
        public string Text => Code?.Text ?? new TextRange(Start, End).Text;
    }
    public sealed record Match(RichTextBox Host, TextPointer Start, TextPointer End, TextBox? Code, int Offset, int Length)
    {
        public void Select()
        {
            if (Code is { } code)
            {
                Host.Selection.Select(Host.Document.ContentStart, Host.Document.ContentStart);
                code.BringIntoView(); code.Focus(); code.Select(Offset, Length);
                code.ScrollToLine(code.GetLineIndexFromCharacterIndex(Offset));
                var caret = code.GetRectFromCharacterIndex(Offset + Length);
                if (!caret.IsEmpty) code.ScrollToHorizontalOffset(Math.Max(0, code.HorizontalOffset + caret.Right - code.ViewportWidth + 24));
            }
            else
            {
                Host.Focus(); Host.Selection.Select(PointerAt(Start, End, Offset), PointerAt(Start, End, Offset + Length));
                Host.Selection.Start.Paragraph?.BringIntoView();
            }
        }
    }
    public static List<Match> Find(RichTextBox host, string query)
    {
        var matches = new List<Match>();
        if (query.Length == 0) return matches;
        foreach (var region in Regions(host.Document))
        {
            string text = region.Text;
            for (int offset = 0; offset <= text.Length - query.Length;)
            {
                int found = text.IndexOf(query, offset, StringComparison.OrdinalIgnoreCase);
                if (found < 0) break;
                matches.Add(new Match(host, region.Start, region.End, region.Code, found, query.Length));
                offset = found + query.Length;
            }
        }
        return matches;
    }
    public static string SelectedText(RichTextBox host)
    {
        var result = new StringBuilder(); var start = host.Selection.Start; var end = host.Selection.End;
        foreach (var region in Regions(host.Document))
        {
            if (region.End.CompareTo(start) <= 0 || region.Start.CompareTo(end) >= 0) continue;
            if (region.Code is not null) { result.Append(region.Text); result.AppendLine(); }
            else result.Append(new TextRange(region.Start.CompareTo(start) < 0 ? start : region.Start,
                region.End.CompareTo(end) > 0 ? end : region.End).Text);
        }
        return result.ToString();
    }
    private static IEnumerable<Region> Regions(FlowDocument flow)
    {
        var previous = flow.ContentStart;
        foreach (var block in CodeBlocks(flow.Blocks))
        {
            yield return new Region(previous, block.ContentStart, null);
            yield return new Region(block.ContentStart, block.ContentEnd, (TextBox)block.Child);
            previous = block.ContentEnd;
        }
        yield return new Region(previous, flow.ContentEnd, null);
    }
    internal static IEnumerable<BlockUIContainer> CodeBlocks(BlockCollection blocks)
    {
        foreach (var block in blocks)
        {
            if (block is BlockUIContainer { Child: TextBox } code) yield return code;
            IEnumerable<BlockCollection> children = block switch {
                Section section => [section.Blocks],
                System.Windows.Documents.List list => list.ListItems.Cast<ListItem>().Select(item => item.Blocks),
                Table table => table.RowGroups.Cast<TableRowGroup>().SelectMany(group => group.Rows.Cast<TableRow>()).SelectMany(row => row.Cells.Cast<TableCell>()).Select(cell => cell.Blocks),
                _ => [] };
            foreach (var collection in children) foreach (var nested in CodeBlocks(collection)) yield return nested;
        }
    }
    private static TextPointer PointerAt(TextPointer start, TextPointer end, int offset)
    {
        int low = 0, high = start.GetOffsetToPosition(end);
        while (low < high)
        {
            int middle = low + (high - low) / 2;
            if (new TextRange(start, start.GetPositionAtOffset(middle)!).Text.Length < offset) low = middle + 1; else high = middle;
        }
        return start.GetPositionAtOffset(low)!.GetInsertionPosition(LogicalDirection.Forward);
    }
}
