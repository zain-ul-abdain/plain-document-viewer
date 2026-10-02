using System.Globalization;
using System.IO.Compression;
using System.Xml;
using System.Xml.Linq;
namespace PlainViewer.Core;

// Pictures and charts placed on an OpenDocument spreadsheet (draw:frame elements, anchored to a cell or to the sheet).
// Pictures stored in the file are checked and written to the work folder like .xlsx pictures (SheetDrawings); pictures
// referred to outside the file are counted and never opened. A chart is an embedded chart object ("Object N/content.xml")
// whose cached data table (the "local-table" the producing application saved with it) is shown; nothing is recalculated.
internal static class OpenDocumentDrawings
{
    private const string DrawNs = "urn:oasis:names:tc:opendocument:xmlns:drawing:1.0";
    private const string SvgNs = "urn:oasis:names:tc:opendocument:xmlns:svg-compatible:1.0";
    private const string TableNs = "urn:oasis:names:tc:opendocument:xmlns:table:1.0";
    private const string XlinkNs = "http://www.w3.org/1999/xlink";
    private const string ChartNs = "urn:oasis:names:tc:opendocument:xmlns:chart:1.0";
    private const string StyleNs = "urn:oasis:names:tc:opendocument:xmlns:style:1.0";
    private const string OfficeNs = "urn:oasis:names:tc:opendocument:xmlns:office:1.0";

    // A frame anchored to a cell. A frame anchored to the sheet is passed with row and column 0; its offsets are then
    // its position on the sheet, which the caller converts to a cell once the column widths are known. Returns the placed picture or chart, or null if the frame holds neither.
    public static SheetPicture? Frame(XElement frame, ZipArchive zip, string? folder, int sheet, SheetDrawings.Budget budget, List<SheetPicture> result, int row, int column)
    {
        if (result.Count >= SheetDrawings.MaxPictures) return null;
        var placed = new SheetPicture
        {
            Row = row, Column = column, RowOffset = Length(Attr(frame, SvgNs, "y")), ColumnOffset = Length(Attr(frame, SvgNs, "x")),
            Width = Length(Attr(frame, SvgNs, "width")), Height = Length(Attr(frame, SvgNs, "height")),
            Description = frame.Element(XName.Get("desc", SvgNs))?.Value ?? frame.Element(XName.Get("title", SvgNs))?.Value ?? ""
        };
        if (Attr(frame, TableNs, "end-cell-address") is { } end && EndCell(end) is var (toRow, toColumn))
        {
            placed.ToRow = toRow; placed.ToColumn = toColumn;
            placed.ToRowOffset = Length(Attr(frame, TableNs, "end-y")); placed.ToColumnOffset = Length(Attr(frame, TableNs, "end-x"));
        }
        if (frame.Element(XName.Get("object", DrawNs)) is { } embedded)
        {
            // Only a chart object inside this file; its replacement picture is not used.
            if (Inside(Attr(embedded, XlinkNs, "href")) is not { } part || zip.GetEntry(part + "/content.xml") is not { } content) return null;
            XDocument chart;
            using (var reader = XmlReader.Create(content.Open(), Settings)) chart = XDocument.Load(reader);
            if (chart.Descendants(XName.Get("chart", ChartNs)).FirstOrDefault() is not { } root) return null;
            placed.Chart = ReadChart(chart, root);
            if (placed.Description.Length == 0) placed.Description = placed.Chart.Title;
            result.Add(placed);
            return placed;
        }
        bool linked = false, unsupported = false;
        foreach (var image in frame.Elements(XName.Get("image", DrawNs)))
        {
            // A picture stored in the file, or embedded as base64; an address outside the file is never opened.
            byte[]? bytes = null;
            if (image.Element(XName.Get("binary-data", OfficeNs)) is { } binary)
            {
                if (binary.Value.Length / 4L * 3 > SheetDrawings.MaxPictureBytes) { budget.Skipped++; return null; }
                try { bytes = Convert.FromBase64String(binary.Value); } catch (FormatException) { unsupported = true; continue; }
            }
            else if (Attr(image, XlinkNs, "href") is { } href)
            {
                if (Inside(href) is not { } path || zip.GetEntry(path) is not { } entry) { linked = true; continue; }
                if (!SheetDrawings.Fits(entry.Length, budget)) return null;
                bytes = new byte[entry.Length];
                using (var stream = entry.Open()) stream.ReadExactly(bytes);
            }
            if (bytes is null) continue;
            var picture = ImageFiles.Identify(bytes);
            // LibreOffice stores an SVG picture with a PNG copy after it: the first picture the page can show is used.
            if (picture is null || picture.Format is "SVG" or "HEIF") { unsupported = true; continue; }
            int before = result.Count;
            SheetDrawings.AddPicture(bytes, placed, folder, sheet, budget, result);
            return result.Count > before ? placed : null;
        }
        if (linked) budget.Linked++;
        else if (unsupported) budget.Unsupported++;
        return null;
    }

    private static readonly XmlReaderSettings Settings = new()
    { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersFromEntities = 1024, IgnoreComments = true, IgnoreProcessingInstructions = true, CloseInput = true };

    private static string? Attr(XElement element, string ns, string name) => element.Attribute(XName.Get(name, ns))?.Value;

    // A path inside the package ("Pictures/x.png", "./Object 1"); null for anything that points elsewhere.
    private static string? Inside(string? href)
    {
        if (string.IsNullOrWhiteSpace(href)) return null;
        string path = href.StartsWith("./", StringComparison.Ordinal) ? href[2..] : href;
        if (path.Contains(':') || path.Contains('\\') || path.StartsWith('/') || path.Split('/').Any(p => p is ".." or "") || path.Contains('%')) return null;
        return path;
    }

    // "Sheet1.E22" or "'My sheet'.$E$22" as a zero-based row and column.
    private static (int Row, int Column)? EndCell(string address)
    {
        string cell = address[(address.LastIndexOf('.') + 1)..].Replace("$", "");
        return Spreadsheets.TryCell(cell, out int row, out int column) ? (row - 1, column) : null;
    }

    // "2.5cm", "0.65in" and so on in pixels at 96 dpi; 0 when missing.
    private static double Length(string? value)
    {
        if (value is null) return 0;
        foreach (var (unit, factor) in new[] { ("cm", 96 / 2.54), ("mm", 96 / 25.4), ("in", 96.0), ("pt", 96 / 72.0), ("pc", 16.0), ("px", 1.0) })
            if (value.EndsWith(unit, StringComparison.Ordinal) && double.TryParse(value[..^unit.Length], NumberStyles.Float, CultureInfo.InvariantCulture, out double n) && double.IsFinite(n))
                return Math.Clamp(n * factor, 0, 1_000_000);
        return 0;
    }

    // The chart's kind, title, series names and colours, and its cached data table (first column: categories, or x
    // values for a scatter chart; then one column per series).
    public static ChartData ReadChart(XDocument document, XElement chart)
    {
        var data = new ChartData();
        var styles = document.Descendants(XName.Get("style", StyleNs))
            .Where(s => Attr(s, StyleNs, "name") is not null).GroupBy(s => Attr(s, StyleNs, "name")!).ToDictionary(g => g.Key, g => g.First());
        string? Property(string? style, string element, string ns, string name) =>
            style is not null && styles.TryGetValue(style, out var s) ? s.Element(XName.Get(element, StyleNs))?.Attribute(XName.Get(name, ns))?.Value : null;
        data.Title = string.Join(" ", chart.Element(XName.Get("title", ChartNs))?.Elements().Select(p => p.Value.Trim()) ?? []).Trim();
        string kind = Attr(chart, ChartNs, "class") ?? "";
        var plot = chart.Element(XName.Get("plot-area", ChartNs));
        string? plotStyle = plot is null ? null : Attr(plot, ChartNs, "style-name");
        data.Type = kind switch
        {
            "chart:bar" => Property(plotStyle, "chart-properties", ChartNs, "vertical") == "true" ? "bar" : "column",
            "chart:line" => "line", "chart:area" => "area", "chart:circle" => "pie", "chart:ring" => "doughnut", "chart:scatter" => "scatter", _ => ""
        };
        if (data.Type.Length == 0 || plot is null) { data.Type = "column"; data.Notice = "This kind of chart is not shown in this version."; return data; }
        data.Stacked = Property(plotStyle, "chart-properties", ChartNs, "stacked") == "true" || Property(plotStyle, "chart-properties", ChartNs, "percentage") == "true";
        data.Percent = Property(plotStyle, "chart-properties", ChartNs, "percentage") == "true";

        // The cached table.
        var table = chart.Element(XName.Get("table", TableNs));
        var header = table?.Element(XName.Get("table-header-rows", TableNs))?.Element(XName.Get("table-row", TableNs));
        var names = header is null ? [] : Cells(header).Skip(1).Select(Text).ToList();
        var rows = table?.Element(XName.Get("table-rows", TableNs))?.Elements(XName.Get("table-row", TableNs)).Take(SheetDrawings.MaxPoints).Select(r => Cells(r).ToList()).ToList() ?? [];
        var series = plot.Elements(XName.Get("series", ChartNs)).Take(SheetDrawings.MaxSeries).ToList();
        if (series.Any(s => Attr(s, ChartNs, "class") is { } c && c != kind)) data.Notice = "Only the first part of this combined chart is shown.";
        var ofKind = series.Select((s, i) => (Series: s, Column: i + 1)).Where(p => (Attr(p.Series, ChartNs, "class") ?? kind) == kind).ToList();
        // The table keeps the layout of the data it came from: series in columns (categories in the first column), or,
        // when a series' own range is one row, series in rows (categories in the header row).
        bool byRow = series.Count > 0 && OneRow(Attr(series[0], ChartNs, "values-cell-range-address"));
        var headerCells = header is null ? [] : Cells(header).ToList();
        data.Categories = byRow ? headerCells.Skip(1).Select(Text).ToList() : rows.Select(r => r.Count > 0 ? Text(r[0]) : "").ToList();
        foreach (var (element, index) in ofKind)
        {
            string? style = Attr(element, ChartNs, "style-name");
            string? colour = data.Type is "line" or "scatter" ? Property(style, "graphic-properties", SvgNs, "stroke-color") : Property(style, "graphic-properties", DrawNs, "fill-color");
            var line = byRow && index - 1 < rows.Count ? rows[index - 1] : [];
            string name = byRow ? (line.Count > 0 ? Text(line[0]) : "") : index - 1 < names.Count ? names[index - 1] : "";
            var item = new ChartSeries
            {
                Name = name.Length > 0 ? name : $"Series {index}",
                Color = WorkbookStyles.Hex(colour?.TrimStart('#')),
                Values = byRow ? line.Skip(1).Select(Value).ToList() : rows.Select(r => index < r.Count ? Value(r[index]) : null).ToList()
            };
            if (data.Type == "scatter") item.X = byRow ? headerCells.Skip(1).Select(c => double.TryParse(Text(c), NumberStyles.Float, CultureInfo.InvariantCulture, out double x) ? x : (double?)null).ToList()
                : rows.Select(r => r.Count > 0 ? Value(r[0]) : null).ToList();
            // Data labels: the series style's number part (value, percentage or both) and text part (the category).
            string? number = Property(style, "chart-properties", ChartNs, "data-label-number");
            item.PointLabels = SheetDrawings.PointLabels(data, item, number is "value" or "value-and-percentage", number is "percentage" or "value-and-percentage",
                Property(style, "chart-properties", ChartNs, "data-label-text") == "true", null);
            data.Series.Add(item);
        }
        // Axis titles: x is the category (scatter: X) axis, y the value axis.
        foreach (var axis in plot.Elements(XName.Get("axis", ChartNs)).Take(4))
        {
            string text = string.Join(" ", axis.Element(XName.Get("title", ChartNs))?.Elements().Select(p => p.Value.Trim()) ?? []).Trim();
            if (text.Length == 0) continue;
            string? dimension = Attr(axis, ChartNs, "dimension");
            if (dimension == "x" && data.CategoryTitle.Length == 0) data.CategoryTitle = text;
            else if (dimension == "y" && data.ValueTitle.Length == 0) data.ValueTitle = text;
        }
        if (data.Series.Count == 0) data.Notice = "This chart has no data to show.";
        return data;
    }

    // Whether a range such as "Sales.B2:Sales.C2" or "local-table.$B$2:.$C$2" spans several columns of one row.
    private static bool OneRow(string? range)
    {
        var ends = range?.Split(' ')[0].Split(':');
        if (ends is not { Length: 2 }) return false;
        (int Row, int Column)? Cell(string end) =>
            Spreadsheets.TryCell(end[(end.LastIndexOf('.') + 1)..].Replace("$", ""), out int row, out int column) ? (row, column) : null;
        return Cell(ends[0]) is { } a && Cell(ends[1]) is { } b && a.Row == b.Row && b.Column > a.Column;
    }

    // A row's cells, with repeated cells expanded (up to the series limit).
    private static IEnumerable<XElement> Cells(XElement row)
    {
        foreach (var cell in row.Elements().Where(e => e.Name.LocalName is "table-cell" or "covered-table-cell"))
        {
            int repeat = int.TryParse(Attr(cell, TableNs, "number-columns-repeated"), out int n) ? Math.Clamp(n, 1, SheetDrawings.MaxSeries + 1) : 1;
            for (int k = 0; k < repeat; k++) yield return cell;
        }
    }

    // A cell's paragraphs (not the range notes LibreOffice adds in draw:g).
    private static string Text(XElement cell) => string.Join("\n", cell.Elements(XName.Get("p", "urn:oasis:names:tc:opendocument:xmlns:text:1.0")).Select(p => p.Value)).Trim();

    private static double? Value(XElement cell) =>
        double.TryParse(Attr(cell, OfficeNs, "value"), NumberStyles.Float, CultureInfo.InvariantCulture, out double v) && double.IsFinite(v) ? v : null;
}
