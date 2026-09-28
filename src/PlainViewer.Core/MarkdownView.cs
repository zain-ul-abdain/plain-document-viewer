using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Markdig.Extensions.Tables;
using Markdig.Extensions.TaskLists;
using Markdig.Extensions.Mathematics;
namespace PlainViewer.Core;

public static class MarkdownView
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder().UsePipeTables().UseTaskLists().UseMathematics().Build();
    public static List<ViewBlock> Parse(string source)
    {
        var result = new List<ViewBlock>();
        // Front matter is retained verbatim, not interpreted as metadata.
        if (source.StartsWith("---\n") || source.StartsWith("---\r\n"))
        {
            using var reader = new StringReader(source); reader.ReadLine();
            var front = new System.Text.StringBuilder("---\n"); string? line; bool ended = false;
            while ((line = reader.ReadLine()) is not null) { front.AppendLine(line); if (line is "---" or "...") { ended = true; break; } }
            if (ended) { result.Add(new ViewBlock { Kind = "code", Text = front.ToString() }); source = reader.ReadToEnd(); }
        }
        var document = Markdown.Parse(source, Pipeline);
        foreach (var block in document) result.Add(ConvertBlock(block, source, 0));
        return result;
    }
    private static ViewBlock ConvertBlock(Block block, string source, int depth)
    {
        if (depth > 64) throw new DocumentException("The Markdown nesting is too deep for this preview.");
        var output = new ViewBlock();
        switch (block)
        {
            case HeadingBlock heading: output.Kind = "heading"; output.Level = heading.Level; break;
            case MathBlock math: return new ViewBlock { Kind = "code", Text = SourceText(math, source) };
            case CodeBlock code: return new ViewBlock { Kind = "code", Text = code.Lines.ToString() };
            case HtmlBlock html: return new ViewBlock { Kind = "code", Text = html.Lines.ToString() };
            case ThematicBreakBlock: return new ViewBlock { Kind = "rule" };
            case Table: output.Kind = "table"; break;
            case TableRow: output.Kind = "row"; break;
            case TableCell: output.Kind = "cell"; break;
            case ListBlock list:
                output.Kind = list.IsOrdered ? "ordered" : "list";
                if (int.TryParse(list.OrderedStart, out int number)) output.StartNumber = number;
                break;
            case ListItemBlock: output.Kind = "item"; break;
            case QuoteBlock: output.Kind = "quote"; break;
        }
        if (block is ContainerBlock container)
            foreach (var child in container) output.Children.Add(ConvertBlock(child, source, depth + 1));
        if (block is Table table)
            foreach (var row in output.Children)
                for (int i = 0; i < Math.Min(row.Children.Count, table.ColumnDefinitions.Count); i++)
                    row.Children[i].Alignment = table.ColumnDefinitions[i].Alignment?.ToString().ToLowerInvariant() ?? "left";
        if (block is LeafBlock leaf && leaf.Inline is not null) output.Runs = Runs(leaf.Inline, false, false, 0, source);
        return output;
    }
    private static string SourceText(MarkdownObject node, string source) =>
        node.Span.Start >= 0 && node.Span.End >= node.Span.Start && node.Span.End < source.Length
            ? source.Substring(node.Span.Start, node.Span.Length) : "";
    private static List<ViewRun> Runs(ContainerInline container, bool bold, bool italic, int depth, string source)
    {
        if (depth > 64) throw new DocumentException("The Markdown nesting is too deep for this preview.");
        var result = new List<ViewRun>();
        foreach (var inline in container)
        {
            switch (inline)
            {
                case LinkInline link:
                    var label = Runs(link, bold, italic, depth + 1, source);
                    string text = string.Concat(label.Select(r => r.Text));
                    if (link.IsImage) result.Add(new ViewRun { Text = "[Image: " + text + "]" });
                    else if (LinkPolicy.CanOpen(link.Url)) { foreach (var run in label) run.Link = link.Url; result.AddRange(label); }
                    else result.Add(new ViewRun { Text = text + " (" + link.Url + ")", Bold = bold, Italic = italic });
                    break;
                case EmphasisInline emphasis: result.AddRange(Runs(emphasis, bold || emphasis.DelimiterCount >= 2, italic || emphasis.DelimiterCount % 2 == 1, depth + 1, source)); break;
                case MathInline math: result.Add(new ViewRun { Text = SourceText(math, source), Code = true }); break;
                case HtmlEntityInline entity: result.Add(new ViewRun { Text = entity.Transcoded.ToString(), Bold = bold, Italic = italic }); break;
                case CodeInline code: result.Add(new ViewRun { Text = code.Content, Code = true }); break;
                case LiteralInline literal: result.Add(new ViewRun { Text = literal.Content.ToString(), Bold = bold, Italic = italic }); break;
                case HtmlInline html: result.Add(new ViewRun { Text = html.Tag, Code = true }); break;
                case LineBreakInline line: result.Add(new ViewRun { Text = line.IsHard ? "\n" : " " }); break;
                case TaskList task: result.Add(new ViewRun { Text = task.Checked ? "☑ " : "☐ " }); break;
                case AutolinkInline auto:
                    string address = auto.IsEmail ? "mailto:" + auto.Url : auto.Url;
                    result.Add(new ViewRun { Text = auto.Url, Link = LinkPolicy.CanOpen(address) ? address : null }); break;
                case ContainerInline nested: result.AddRange(Runs(nested, bold, italic, depth + 1, source)); break;
                default:
                    // Unsupported syntax must stay visible rather than silently losing text.
                    result.Add(new ViewRun { Text = SourceText(inline, source), Bold = bold, Italic = italic }); break;
            }
        }
        return result;
    }
}
