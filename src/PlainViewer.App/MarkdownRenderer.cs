using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using PlainViewer.Core;
namespace PlainViewer.App;

public static class MarkdownRenderer
{
    public static void ResizeCode(FlowDocument flow, double viewportWidth)
    {
        if (viewportWidth <= 0) return;
        foreach (var block in MarkdownSearch.CodeBlocks(flow.Blocks))
            ((TextBox)block.Child).MaxWidth = Math.Max(120, viewportWidth - 100);
    }
    // Every size and space follows the zoom, so the whole document scales together. Paragraphs inside table cells get
    // no outer spacing: the cell's padding separates them from the borders.
    public static Block Render(ViewBlock model, double zoom, Action<string> openLink, bool inCell = false)
    {
        if (model.Kind == "table")
        {
            var table = new Table { CellSpacing = 0 }; var group = new TableRowGroup(); table.RowGroups.Add(group);
            foreach (var row in model.Children)
            {
                // GitHub-style tables always start with their header row.
                var targetRow = new TableRow { FontWeight = group.Rows.Count == 0 ? FontWeights.SemiBold : FontWeights.Normal }; group.Rows.Add(targetRow);
                foreach (var cell in row.Children)
                {
                    var target = new TableCell { Padding = new Thickness(8 * zoom, 5 * zoom, 8 * zoom, 5 * zoom), BorderThickness = new Thickness(0.5), BorderBrush = SystemColors.GrayTextBrush,
                        TextAlignment = cell.Alignment switch { "center" => TextAlignment.Center, "right" => TextAlignment.Right, _ => TextAlignment.Left } };
                    foreach (var child in cell.Children) target.Blocks.Add(Render(child, zoom, openLink, inCell: true));
                    targetRow.Cells.Add(target);
                }
            }
            return table;
        }
        if (model.Kind is "list" or "ordered")
        {
            bool zeroStart = model.Kind == "ordered" && model.StartNumber == 0;
            var list = new System.Windows.Documents.List { Margin = new Thickness(0, 0, 0, 10 * zoom), Padding = new Thickness(24 * zoom, 0, 0, 0), MarkerStyle = zeroStart ? TextMarkerStyle.None : model.Kind == "ordered" ? TextMarkerStyle.Decimal : TextMarkerStyle.Disc,
                StartIndex = Math.Clamp(model.StartNumber, 1, 999999999) };
            int number = 0;
            foreach (var item in model.Children)
            {
                var target = new ListItem(); foreach (var child in item.Children) target.Blocks.Add(Render(child, zoom, openLink, inCell));
                // WPF rejects StartIndex=0, although CommonMark permits it. Use literal markers for that list.
                if (zeroStart)
                {
                    if (target.Blocks.FirstBlock is not Paragraph first)
                    {
                        first = new Paragraph();
                        if (target.Blocks.FirstBlock is { } firstBlock) target.Blocks.InsertBefore(firstBlock, first); else target.Blocks.Add(first);
                    }
                    var marker = new Run($"{number++}. ");
                    if (first.Inlines.FirstInline is { } inline) first.Inlines.InsertBefore(inline, marker); else first.Inlines.Add(marker);
                }
                list.ListItems.Add(target);
            }
            return list;
        }
        if (model.Children.Count > 0)
        {
            var section = new Section { Margin = model.Kind == "quote" ? new Thickness(20 * zoom, 6 * zoom, 0, 6 * zoom) : new Thickness(0) };
            foreach (var child in model.Children) section.Blocks.Add(Render(child, zoom, openLink, inCell)); return section;
        }
        var paragraph = new Paragraph { Margin = inCell ? new Thickness(0) : new Thickness(0, 4 * zoom, 0, 10 * zoom) };
        if (model.Kind == "heading") { paragraph.FontSize = (32 - Math.Min(model.Level, 6) * 2) * zoom; paragraph.FontWeight = FontWeights.SemiBold; }
        if (model.Kind == "code")
        {
            var code = new TextBox { Text = model.Text, IsReadOnly = true, AcceptsTab = false, AcceptsReturn = true,
                TextWrapping = TextWrapping.NoWrap, FontFamily = new FontFamily("Consolas"), FontSize = 16 * zoom,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                MaxWidth = 600 * Math.Max(1, zoom), MaxHeight = 400 * zoom, Padding = new Thickness(10 * zoom), Margin = new Thickness(0, 4 * zoom, 0, 10 * zoom),
                HorizontalAlignment = HorizontalAlignment.Left };
            System.Windows.Automation.AutomationProperties.SetName(code, "Read-only code block");
            return new BlockUIContainer(code);
        }
        if (model.Kind == "rule") { paragraph.Inlines.Add(new Run("────────────────────────")); return paragraph; }
        foreach (var item in model.Runs)
        {
            var run = new Run(item.Text) { FontWeight = item.Bold ? FontWeights.Bold : FontWeights.Normal, FontStyle = item.Italic ? FontStyles.Italic : FontStyles.Normal };
            if (item.Code) run.FontFamily = new FontFamily("Consolas");
            if (item.Link is { } address && LinkPolicy.CanOpen(address))
            {
                var link = new Hyperlink(run) { ToolTip = address };
                link.Click += (_, _) => openLink(address); paragraph.Inlines.Add(link);
            }
            else paragraph.Inlines.Add(run);
        }
        return paragraph;
    }
}
