using System.Globalization;
using System.IO.Compression;
using System.Xml;
using System.Xml.Linq;
namespace PlainViewer.Core;

// Pictures and charts placed on an .xlsx worksheet (its drawing part). Pictures stored in the file are checked by
// ImageFiles (format from the bytes, size limits) and written to the work folder for the grid page, which decodes them
// in its sandbox; linked pictures (stored elsewhere) are never fetched. Charts become ChartData from the values saved
// in the chart itself, never recalculated from the cells. Shapes and text boxes are not shown.
internal static class SheetDrawings
{
    private const double Emu = 9525;                          // EMUs per pixel at 96 dpi
    public const int MaxPictures = 200, MaxSeries = 50, MaxPoints = 10_000;
    public const long MaxPictureBytes = 20L * 1024 * 1024, MaxTotalBytes = 64L * 1024 * 1024;

    public sealed class Budget { public int Pictures = MaxPictures; public long Bytes = MaxTotalBytes; public int Skipped, Unsupported, Linked; }

    public static List<SheetPicture> Read(string drawingPart, Func<string, Dictionary<string, (string Type, string Target)>> relationships,
        Func<string, XmlReader> open, Func<string, ZipArchiveEntry?> entry, string? folder, int sheet, Budget budget, IReadOnlyList<string> theme)
    {
        var result = new List<SheetPicture>();
        var rels = relationships(drawingPart);
        XDocument drawing;
        using (var reader = open(drawingPart)) drawing = XDocument.Load(reader);
        foreach (var anchor in drawing.Root?.Elements().Where(e => e.Name.LocalName is "twoCellAnchor" or "oneCellAnchor" or "absoluteAnchor") ?? [])
        {
            if (result.Count >= MaxPictures) break;
            var placed = Place(anchor);
            if (placed is null) continue;
            var content = anchor.Elements().FirstOrDefault(e => e.Name.LocalName is "pic" or "graphicFrame" or "grpSp" or "sp");
            if (content?.Name.LocalName == "pic")
            {
                var blip = content.Descendants().FirstOrDefault(e => e.Name.LocalName == "blip");
                placed.Description = Attribute(content.Descendants().FirstOrDefault(e => e.Name.LocalName == "cNvPr"), "descr") ?? "";
                string? embed = blip?.Attributes().FirstOrDefault(a => a.Name.LocalName == "embed")?.Value;
                if (embed is null) { if (blip?.Attributes().Any(a => a.Name.LocalName == "link") == true) budget.Linked++; continue; }
                if (!rels.TryGetValue(embed, out var rel) || entry(rel.Target) is not { } media) continue;
                if (!Fits(media.Length, budget)) continue;
                byte[] bytes = new byte[media.Length];
                using (var stream = media.Open()) stream.ReadExactly(bytes);
                AddPicture(bytes, placed, folder, sheet, budget, result);
            }
            else if (content?.Name.LocalName == "graphicFrame")
            {
                var chartRef = content.Descendants().FirstOrDefault(e => e.Name.LocalName == "chart");
                string? id = chartRef?.Attributes().FirstOrDefault(a => a.Name.LocalName == "id")?.Value;
                if (id is null || !rels.TryGetValue(id, out var rel) || entry(rel.Target) is null) continue;
                XDocument chart;
                using (var reader = open(rel.Target)) chart = XDocument.Load(reader);
                placed.Chart = ReadChart(chart, theme);
                placed.Description = placed.Chart.Title;
                result.Add(placed);
            }
        }
        return result;
    }

    // Whether a picture of this many bytes may still be read (counted as skipped if not).
    public static bool Fits(long length, Budget budget)
    {
        if (length <= MaxPictureBytes && length <= budget.Bytes && budget.Pictures > 0) return true;
        budget.Skipped++;
        return false;
    }

    // A picture's bytes, checked by ImageFiles (format from the bytes, pixel limit) and written to the work folder as
    // media-<sheet>-<n>.<type>, the only names the app serves. Formats the page cannot show are counted, not added.
    public static void AddPicture(byte[] bytes, SheetPicture placed, string? folder, int sheet, Budget budget, List<SheetPicture> result)
    {
        if (!Fits(bytes.Length, budget)) return;
        var picture = ImageFiles.Identify(bytes);
        if (picture is null || picture.Format is "SVG" or "HEIF" || (long)picture.Width * picture.Height > ImageFiles.PixelLimit) { budget.Unsupported++; return; }
        budget.Bytes -= bytes.Length; budget.Pictures--;
        string name = $"media-{sheet}-{result.Count}.{picture.ContentType.Split('/')[1].Replace("x-icon", "ico")}";
        if (folder is not null) File.WriteAllBytes(Path.Combine(folder, name), bytes);
        placed.Media = name;
        result.Add(placed);
    }

    // The workbook's notices about pictures that are not shown.
    public static IEnumerable<string> Notes(Budget budget)
    {
        if (budget.Unsupported > 0) yield return $"{budget.Unsupported} picture{(budget.Unsupported == 1 ? " is" : "s are")} in a format this viewer cannot show (for example EMF or WMF) and {(budget.Unsupported == 1 ? "is" : "are")} left out.";
        if (budget.Skipped > 0) yield return $"{budget.Skipped} picture{(budget.Skipped == 1 ? " is" : "s are")} left out because the workbook's pictures are larger than this viewer shows at once.";
        if (budget.Linked > 0) yield return $"{budget.Linked} linked picture{(budget.Linked == 1 ? " is" : "s are")} stored outside this file and {(budget.Linked == 1 ? "is" : "are")} not loaded.";
    }

    // Grid geometry shared with the page (sheet.js): a column is width × 7 + 5 pixels, a row 20 Excel pixels.
    public const double RowPixels = 20;
    public static double ColumnPixels(SheetData sheet, int column)
    {
        double width = column < sheet.ColumnWidths.Count ? sheet.ColumnWidths[column] : 8.43;
        return width <= 0 ? 0 : Math.Round(width * 7 + 5);
    }

    // A position in pixels from the sheet's top-left corner as a cell and an offset within it.
    public static (int Column, double ColumnOffset, int Row, double RowOffset) CellAt(SheetData sheet, double x, double y)
    {
        int column = 0;
        while (column < Spreadsheets.MaxColumns - 1 && x >= ColumnPixels(sheet, column)) { x -= ColumnPixels(sheet, column); column++; }
        int row = (int)Math.Min(Spreadsheets.MaxStoredRows - 1, Math.Floor(Math.Max(0, y) / RowPixels));
        return (column, Math.Max(0, x), row, Math.Max(0, y) - row * RowPixels);
    }

    private static string? Attribute(XElement? element, string name) => element?.Attributes().FirstOrDefault(a => a.Name.LocalName == name)?.Value;
    private static XElement? Child(XElement? element, string name) => element?.Elements().FirstOrDefault(e => e.Name.LocalName == name);
    private static double Number(string? text) => double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) && double.IsFinite(v) ? v : 0;
    private static int Cell(string? text) => int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? Math.Clamp(v, 0, Spreadsheets.MaxStoredRows) : 0;

    // The anchor as a cell position plus offsets, and either an end cell or a size in pixels.
    private static SheetPicture? Place(XElement anchor)
    {
        var picture = new SheetPicture();
        if (Child(anchor, "from") is { } from)
        {
            picture.Column = Cell(Child(from, "col")?.Value); picture.ColumnOffset = Number(Child(from, "colOff")?.Value) / Emu;
            picture.Row = Cell(Child(from, "row")?.Value); picture.RowOffset = Number(Child(from, "rowOff")?.Value) / Emu;
        }
        else if (Child(anchor, "pos") is { } pos)
        { picture.ColumnOffset = Number(Attribute(pos, "x")) / Emu; picture.RowOffset = Number(Attribute(pos, "y")) / Emu; }
        else return null;
        if (Child(anchor, "to") is { } to)
        {
            picture.ToColumn = Cell(Child(to, "col")?.Value); picture.ToColumnOffset = Number(Child(to, "colOff")?.Value) / Emu;
            picture.ToRow = Cell(Child(to, "row")?.Value); picture.ToRowOffset = Number(Child(to, "rowOff")?.Value) / Emu;
        }
        else if (Child(anchor, "ext") is { } ext)
        { picture.Width = Number(Attribute(ext, "cx")) / Emu; picture.Height = Number(Attribute(ext, "cy")) / Emu; }
        else return null;
        return picture;
    }

    // A chart part (c:chartSpace) as its saved data. The first kind of chart in its plot area is used.
    public static ChartData ReadChart(XDocument document, IReadOnlyList<string> theme)
    {
        var data = new ChartData();
        var chart = document.Descendants().FirstOrDefault(e => e.Name.LocalName == "chart" && e.Parent?.Name.LocalName == "chartSpace");
        if (chart is null) { data.Notice = "This chart could not be read."; return data; }
        if (Child(chart, "title") is { } title) data.Title = string.Concat(title.Descendants().Where(e => e.Name.LocalName == "t").Select(e => e.Value)).Trim();
        var plot = Child(chart, "plotArea");
        string[] known = ["barChart", "bar3DChart", "lineChart", "line3DChart", "areaChart", "area3DChart", "pieChart", "pie3DChart", "ofPieChart", "doughnutChart", "scatterChart"];
        var kinds = plot?.Elements().Where(e => e.Name.LocalName.EndsWith("Chart", StringComparison.Ordinal)).ToList() ?? [];
        var kind = kinds.FirstOrDefault(e => known.Contains(e.Name.LocalName));
        if (kind is null) { data.Notice = kinds.Count == 0 ? "This chart has no data to show." : "This kind of chart is not shown in this version."; return data; }
        if (kinds.Count > 1) data.Notice = "Only the first part of this combined chart is shown.";
        string name = kind.Name.LocalName;
        string grouping = Attribute(Child(kind, "grouping"), "val") ?? "";
        data.Type = name switch
        {
            "barChart" or "bar3DChart" => Attribute(Child(kind, "barDir"), "val") == "bar" ? "bar" : "column",
            "lineChart" or "line3DChart" => "line", "areaChart" or "area3DChart" => "area",
            "doughnutChart" => "doughnut", "scatterChart" => "scatter", _ => "pie"
        };
        data.Stacked = grouping is "stacked" or "percentStacked";
        data.Percent = grouping == "percentStacked";
        int seriesIndex = 0;
        foreach (var series in kind.Elements().Where(e => e.Name.LocalName == "ser").Take(MaxSeries))
        {
            var item = new ChartSeries { Name = Text(Child(series, "tx")) ?? $"Series {seriesIndex + 1}", Color = Colour(Child(series, "spPr"), theme) };
            if (data.Type == "scatter")
            {
                item.X = Values(Child(series, "xVal"));
                item.Values = Values(Child(series, "yVal"));
            }
            else
            {
                item.Values = Values(Child(series, "val"));
                if (data.Categories.Count == 0) data.Categories = Labels(Child(series, "cat"));
            }
            data.Series.Add(item);
            seriesIndex++;
        }
        int points = data.Series.Count == 0 ? 0 : data.Series.Max(s => s.Values.Count);
        while (data.Categories.Count < points) data.Categories.Add((data.Categories.Count + 1).ToString(CultureInfo.InvariantCulture));
        return data;
    }

    // A series name: literal text (c:v) or the cached text of a reference (c:strRef/c:strCache).
    private static string? Text(XElement? tx)
    {
        if (tx is null) return null;
        var value = tx.Descendants().FirstOrDefault(e => e.Name.LocalName == "v");
        return value?.Value is { Length: > 0 } text ? text : null;
    }

    // Cached points (c:pt idx="n"), in order of their index; missing points are null.
    private static List<(int Index, string Value)> Points(XElement? container)
    {
        var cache = container?.Descendants().FirstOrDefault(e => e.Name.LocalName is "numCache" or "strCache" or "numLit" or "strLit");
        if (cache is null) return [];
        return cache.Elements().Where(e => e.Name.LocalName == "pt")
            .Select(e => (Index: Cell(Attribute(e, "idx")), Value: Child(e, "v")?.Value ?? ""))
            .Where(p => p.Index < MaxPoints).OrderBy(p => p.Index).ToList();
    }

    private static List<double?> Values(XElement? container)
    {
        var points = Points(container);
        var values = new List<double?>();
        foreach (var (index, value) in points)
        {
            while (values.Count < index) values.Add(null);
            values.Add(double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) && double.IsFinite(v) ? v : null);
        }
        return values;
    }

    // Category labels; numbers keep the cache's number format when it has one (dates, for example).
    private static List<string> Labels(XElement? container)
    {
        var points = Points(container);
        string? format = container?.Descendants().FirstOrDefault(e => e.Name.LocalName == "formatCode")?.Value;
        var labels = new List<string>();
        foreach (var (index, value) in points)
        {
            while (labels.Count < index) labels.Add("");
            if (format is { Length: > 0 } && format != "General" && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double number))
            {
                try { var f = new ExcelNumberFormat.NumberFormat(format); labels.Add(f.IsValid ? f.Format(number, CultureInfo.CurrentCulture) : value); }
                catch (Exception) { labels.Add(value); }
            }
            else labels.Add(value);
        }
        return labels;
    }

    // The series' fill (bars, areas, slices) or line colour: an sRGB value or a theme colour.
    private static string? Colour(XElement? shape, IReadOnlyList<string> theme)
    {
        var fill = Child(shape, "solidFill") ?? Child(Child(shape, "ln"), "solidFill");
        var colour = fill?.Elements().FirstOrDefault();
        if (colour is null) return null;
        if (colour.Name.LocalName == "srgbClr") return WorkbookStyles.Hex(Attribute(colour, "val"));
        if (colour.Name.LocalName == "schemeClr" && theme.Count >= 10)
            return Attribute(colour, "val") switch
            {
                "accent1" => theme[4], "accent2" => theme[5], "accent3" => theme[6], "accent4" => theme[7], "accent5" => theme[8], "accent6" => theme[9],
                "tx1" or "dk1" => theme[0], "dk2" => theme[2], _ => null
            };
        return null;
    }
}
