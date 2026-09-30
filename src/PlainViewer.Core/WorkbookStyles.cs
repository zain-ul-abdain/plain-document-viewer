using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
namespace PlainViewer.Core;

// Cell formats of an .xlsx workbook (xl/styles.xml, with theme colours from xl/theme/theme1.xml): the number format
// of each cell format ("xf"), its horizontal alignment, and a CellStyle for fonts, fills, borders, wrapping and
// vertical alignment. Only values parsed from attributes reach a CellStyle: colours become "#rrggbb", borders a width,
// a known line kind and a colour, font names only letters, digits, spaces and hyphens.
internal sealed partial class WorkbookStyles
{
    public readonly Dictionary<int, string> CustomFormats = [];
    public readonly List<int> NumberFormats = [];       // per cell format
    public readonly List<char> Horizontal = [];         // per cell format: l, r, c, or '\0' for "general"
    public readonly List<int> StyleIds = [];            // per cell format, into Table
    public readonly List<CellStyle> Table = [new()];    // 0: the plain default

    private readonly List<string> theme = [];           // XML order: dk1 lt1 dk2 lt2 accent1..6 hlink folHlink
    public IReadOnlyList<string> Theme => theme;
    private readonly List<Font> fonts = [];
    private readonly List<string?> fills = [];
    private readonly List<string?[]> borders = [];      // left, right, top, bottom
    private readonly Dictionary<string, int> known = [];

    private sealed record Font(bool Bold, bool Italic, bool Underline, bool Strike, double Size, string? Color, string? Name);

    public static WorkbookStyles Read(Func<string, XmlReader> open, string? stylesPart, string? themePart)
    {
        var styles = new WorkbookStyles();
        if (themePart is not null) styles.ReadTheme(open(themePart));
        if (stylesPart is not null) styles.ReadStyles(open(stylesPart));
        return styles;
    }

    private void ReadTheme(XmlReader r)
    {
        using (r)
        {
            bool inScheme = false; string? slot = null;
            while (r.Read())
            {
                if (r.NodeType == XmlNodeType.Element)
                {
                    if (r.LocalName == "clrScheme") inScheme = true;
                    else if (inScheme && r.LocalName is "dk1" or "lt1" or "dk2" or "lt2" or "accent1" or "accent2" or "accent3" or "accent4" or "accent5" or "accent6" or "hlink" or "folHlink") slot = r.LocalName;
                    else if (inScheme && slot is not null && r.LocalName is "srgbClr" or "sysClr")
                    {
                        string? hex = r.LocalName == "srgbClr" ? r.GetAttribute("val") : r.GetAttribute("lastClr");
                        theme.Add(Hex(hex) ?? "#000000"); slot = null;
                    }
                }
                else if (r.NodeType == XmlNodeType.EndElement && r.LocalName == "clrScheme") break;
            }
        }
    }

    private void ReadStyles(XmlReader r)
    {
        using (r)
        {
            string section = "";                 // fonts, fills, borders, cellXfs, dxfs (others are ignored)
            Font? font = null; string? fill = null; string?[]? border = null; string? edge = null;
            Dxf? dxf = null; string part = "";   // dxfs: the format being read and its font, fill or border part
            (int NumberFormat, int Font, int Fill, int Border, char Horizontal, string? Vertical, bool Wrap, int Indent)? xf = null;
            while (r.Read())
            {
                if (r.NodeType == XmlNodeType.EndElement)
                {
                    if (section == "dxfs")
                    {
                        switch (r.LocalName)
                        {
                            case "dxfs": section = ""; break;
                            case "dxf" when dxf is not null: if (Dxfs.Count < 10_000) Dxfs.Add(dxf); dxf = null; break;
                            case "font" or "fill" or "border": part = ""; break;
                            case "left" or "right" or "top" or "bottom" or "start" or "end": edge = null; break;
                        }
                        continue;
                    }
                    switch (r.LocalName)
                    {
                        case "fonts" or "fills" or "borders" or "cellXfs" when r.LocalName == section: section = ""; break;
                        case "font" when section == "fonts" && font is not null: fonts.Add(font); font = null; break;
                        case "fill" when section == "fills": fills.Add(fill); fill = null; break;
                        case "border" when section == "borders" && border is not null: borders.Add(border); border = null; break;
                        case "left" or "right" or "top" or "bottom": edge = null; break;
                        case "xf" when section == "cellXfs" && xf is not null: AddFormat(xf.Value); xf = null; break;
                    }
                    continue;
                }
                if (r.NodeType != XmlNodeType.Element) continue;
                string name = r.LocalName;
                bool empty = r.IsEmptyElement;
                if (name == "numFmt" && int.TryParse(r.GetAttribute("numFmtId"), out int id) && r.GetAttribute("formatCode") is { } code) { CustomFormats[id] = code; continue; }
                if (name is "fonts" or "fills" or "borders" or "cellXfs" or "dxfs" && section == "") { if (!empty) section = name; continue; }
                switch (section)
                {
                    // Formats used by conditional formatting rules: only what they set (a solid fill's colour is bgColor).
                    case "dxfs":
                        if (name == "dxf") { dxf = new Dxf(); if (empty) { Dxfs.Add(dxf); dxf = null; } }
                        else if (dxf is null) break;
                        else if (name is "font" or "fill" or "border") part = empty ? "" : name;
                        else if (part == "font")
                            switch (name)
                            {
                                case "b": dxf.Bold = On(r); break;
                                case "i": dxf.Italic = On(r); break;
                                case "u": dxf.Underline = r.GetAttribute("val") is not "none"; break;
                                case "strike": dxf.Strike = On(r); break;
                                case "color": dxf.Color = Color(r); break;
                            }
                        else if (part == "fill" && name == "bgColor") dxf.Fill = Color(r) ?? dxf.Fill;
                        else if (part == "fill" && name == "fgColor") dxf.Fill ??= Color(r);
                        else if (part == "border" && name is "left" or "right" or "top" or "bottom" or "start" or "end")
                        {
                            string? line = Line(r.GetAttribute("style"), "#000000");
                            dxf.SetBorder(name, line);
                            edge = line is null || empty ? null : name;
                        }
                        else if (part == "border" && edge is not null && name == "color" && Color(r) is { } lineColour && dxf.Border(edge) is { } current)
                            dxf.SetBorder(edge, current[..current.LastIndexOf(' ')] + " " + lineColour);
                        break;
                    case "fonts":
                        if (name == "font") { font = new Font(false, false, false, false, 0, null, null); if (empty) { fonts.Add(font); font = null; } }
                        else if (font is not null)
                            font = name switch
                            {
                                "b" => font with { Bold = On(r) },
                                "i" => font with { Italic = On(r) },
                                "u" => font with { Underline = r.GetAttribute("val") is not "none" },
                                "strike" => font with { Strike = On(r) },
                                "sz" => font with { Size = Number(r.GetAttribute("val")) },
                                "color" => font with { Color = Color(r) },
                                "name" => font with { Name = SafeName(r.GetAttribute("val")) },
                                _ => font
                            };
                        break;
                    case "fills":
                        if (name == "fill") { fill = null; if (empty) fills.Add(null); }
                        else if (name == "patternFill") fill = r.GetAttribute("patternType") is "solid" ? "" : null;   // colour follows
                        else if (name == "fgColor" && fill == "") fill = Color(r);
                        else if (name == "stop" && fill is null) fill = "";                                            // gradient: first stop
                        else if (name == "color" && fill == "") fill = Color(r);
                        break;
                    case "borders":
                        if (name == "border") { border = new string?[4]; if (empty) { borders.Add(border); border = null; } }
                        else if (border is not null && name is "left" or "right" or "top" or "bottom" or "start" or "end")
                        {
                            int side = name switch { "left" or "start" => 0, "right" or "end" => 1, "top" => 2, _ => 3 };
                            border[side] = Line(r.GetAttribute("style"), "#000000");
                            edge = border[side] is null ? null : name; if (empty) edge = null;
                        }
                        else if (border is not null && edge is not null && name == "color")
                        {
                            int side = edge switch { "left" or "start" => 0, "right" or "end" => 1, "top" => 2, _ => 3 };
                            if (Color(r) is { } colour) border[side] = border[side]![..border[side]!.LastIndexOf(' ')] + " " + colour;
                        }
                        break;
                    case "cellXfs":
                        if (name == "xf")
                        {
                            xf = (Int(r, "numFmtId"), Int(r, "fontId"), Int(r, "fillId"), Int(r, "borderId"), '\0', null, false, 0);
                            if (empty) { AddFormat(xf.Value); xf = null; }
                        }
                        else if (name == "alignment" && xf is not null)
                            xf = xf.Value with
                            {
                                Horizontal = r.GetAttribute("horizontal") switch { "left" or "justify" or "distributed" or "fill" => 'l', "right" => 'r', "center" or "centerContinuous" => 'c', _ => '\0' },
                                Vertical = r.GetAttribute("vertical") switch { "top" => "top", "center" => "middle", _ => null },
                                Wrap = On(r, "wrapText", false),
                                Indent = Math.Clamp(Int(r, "indent"), 0, 15)
                            };
                        break;
                }
            }
        }
    }

    private void AddFormat((int NumberFormat, int Font, int Fill, int Border, char Horizontal, string? Vertical, bool Wrap, int Indent) xf)
    {
        NumberFormats.Add(xf.NumberFormat);
        Horizontal.Add(xf.Horizontal);
        var baseFont = fonts.Count > 0 ? fonts[0] : null;
        var f = xf.Font >= 0 && xf.Font < fonts.Count ? fonts[xf.Font] : baseFont;
        var b = xf.Border >= 0 && xf.Border < borders.Count ? borders[xf.Border] : null;
        var style = new CellStyle
        {
            Bold = f?.Bold ?? false, Italic = f?.Italic ?? false, Underline = f?.Underline ?? false, Strike = f?.Strike ?? false,
            // The workbook's default text colour and font are "automatic": they follow the viewer's theme.
            Color = f?.Color is { } c && c != baseFont?.Color ? c : null,
            Font = f?.Name is { } n && n != baseFont?.Name ? n : null,
            Size = f is { Size: > 0 } && baseFont is { Size: > 0 } && Math.Abs(f.Size - baseFont.Size) > 0.01 ? Math.Round(f.Size / baseFont.Size, 3) : 0,
            Fill = xf.Fill >= 2 && xf.Fill < fills.Count && fills[xf.Fill] is { Length: > 0 } fillColour ? fillColour : null,   // 0 and 1 are reserved
            Wrap = xf.Wrap, VAlign = xf.Vertical, Indent = xf.Indent,
            Left = b?[0], Right = b?[1], Top = b?[2], Bottom = b?[3]
        };
        StyleIds.Add(Intern(style));
    }

    // The style's number in Table, adding it if it is new (-1 when the table is full).
    public const int MaxStyles = 30_000;
    public int Intern(CellStyle style)
    {
        string key = JsonSerializer.Serialize(style);
        if (known.Count == 0) known[JsonSerializer.Serialize(Table[0])] = 0;
        if (known.TryGetValue(key, out int index)) return index;
        if (Table.Count >= MaxStyles) return -1;
        index = known[key] = Table.Count; Table.Add(style);
        return index;
    }

    // A conditional format (dxf): only the properties it sets; null leaves the cell's own.
    public sealed class Dxf
    {
        public bool? Bold, Italic, Underline, Strike;
        public string? Color, Fill, Left, Right, Top, Bottom;
        public string? Border(string side) => side switch { "left" or "start" => Left, "right" or "end" => Right, "top" => Top, _ => Bottom };
        public void SetBorder(string side, string? line)
        {
            switch (side) { case "left" or "start": Left = line; break; case "right" or "end": Right = line; break; case "top": Top = line; break; default: Bottom = line; break; }
        }
    }
    public readonly List<Dxf> Dxfs = [];

    private static bool On(XmlReader r, string attribute = "val", bool missing = true) =>
        r.GetAttribute(attribute) is not { } v ? missing : v is "1" or "true";
    private static int Int(XmlReader r, string attribute) => int.TryParse(r.GetAttribute(attribute), NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : 0;
    private static double Number(string? text) => double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) && v is > 0 and < 500 ? v : 0;

    // Excel's border kinds as a CSS width and line kind; unknown kinds and "none" draw nothing.
    internal static string? Line(string? kind, string colour) => kind switch
    {
        "thin" or "hair" => $"1 solid {colour}",
        "dotted" => $"1 dotted {colour}",
        "dashed" or "dashDot" or "dashDotDot" or "slantDashDot" => $"1 dashed {colour}",
        "medium" => $"2 solid {colour}",
        "mediumDashed" or "mediumDashDot" or "mediumDashDotDot" => $"2 dashed {colour}",
        "thick" => $"3 solid {colour}",
        "double" => $"3 double {colour}",
        _ => null
    };

    internal static string? SafeName(string? name) => name is not null && SafeFontName().IsMatch(name) ? name : null;
    [GeneratedRegex(@"^[\p{L}\p{N} \-]{1,64}$")] private static partial Regex SafeFontName();

    internal static string? Hex(string? value)
    {
        if (value is null) return null;
        if (value.Length == 8) value = value[2..];                   // ARGB: the alpha byte is ignored, as Excel does
        return value.Length == 6 && value.All(char.IsAsciiHexDigit) ? "#" + value.ToLowerInvariant() : null;
    }

    // rgb, theme (with tint) or indexed colour; null for automatic or unknown.
    private string? Color(XmlReader r) => Color(r.GetAttribute);
    public string? Color(System.Xml.Linq.XElement e) => Color(name => e.Attribute(name)?.Value);

    private string? Color(Func<string, string?> attribute)
    {
        if (attribute("auto") is "1" or "true") return null;
        string? colour = Hex(attribute("rgb"));
        if (colour is null && int.TryParse(attribute("theme"), out int t) && t >= 0 && t < theme.Count)
            colour = theme[t < 4 ? t ^ 1 : t];                      // Excel numbers lt1, dk1, lt2, dk2 first
        if (colour is null && int.TryParse(attribute("indexed"), out int i) && i >= 0 && i < Palette.Length) colour = Palette[i];
        if (colour is null) return null;
        double tint = Number(attribute("tint")?.TrimStart('-')) * (attribute("tint")?.StartsWith('-') == true ? -1 : 1);
        return tint == 0 ? colour : Tint(colour, Math.Clamp(tint, -1, 1));
    }

    // Excel's tint: lightens (positive) or darkens (negative) the colour's HSL lightness.
    private static string Tint(string colour, double tint)
    {
        double red = Convert.ToInt32(colour[1..3], 16) / 255.0, green = Convert.ToInt32(colour[3..5], 16) / 255.0, blue = Convert.ToInt32(colour[5..7], 16) / 255.0;
        double max = Math.Max(red, Math.Max(green, blue)), min = Math.Min(red, Math.Min(green, blue)), l = (max + min) / 2, h = 0, s = 0;
        if (max != min)
        {
            double d = max - min;
            s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
            h = max == red ? (green - blue) / d + (green < blue ? 6 : 0) : max == green ? (blue - red) / d + 2 : (red - green) / d + 4;
            h /= 6;
        }
        l = tint < 0 ? l * (1 + tint) : l * (1 - tint) + tint;
        double q = l < 0.5 ? l * (1 + s) : l + s - l * s, p = 2 * l - q;
        double Channel(double x)
        {
            x = x < 0 ? x + 1 : x > 1 ? x - 1 : x;
            return x < 1.0 / 6 ? p + (q - p) * 6 * x : x < 0.5 ? q : x < 2.0 / 3 ? p + (q - p) * (2.0 / 3 - x) * 6 : p;
        }
        (red, green, blue) = s == 0 ? (l, l, l) : (Channel(h + 1.0 / 3), Channel(h), Channel(h - 1.0 / 3));
        return $"#{(int)Math.Round(red * 255):x2}{(int)Math.Round(green * 255):x2}{(int)Math.Round(blue * 255):x2}";
    }

    // Excel's default indexed palette (0-63); 64 and 65 are the system text and window colours (automatic).
    internal static readonly string[] Palette =
    [
        "#000000", "#ffffff", "#ff0000", "#00ff00", "#0000ff", "#ffff00", "#ff00ff", "#00ffff",
        "#000000", "#ffffff", "#ff0000", "#00ff00", "#0000ff", "#ffff00", "#ff00ff", "#00ffff",
        "#800000", "#008000", "#000080", "#808000", "#800080", "#008080", "#c0c0c0", "#808080",
        "#9999ff", "#993366", "#ffffcc", "#ccffff", "#660066", "#ff8080", "#0066cc", "#ccccff",
        "#000080", "#ff00ff", "#ffff00", "#00ffff", "#800080", "#800000", "#008080", "#0000ff",
        "#00ccff", "#ccffff", "#ccffcc", "#ffff99", "#99ccff", "#ff99cc", "#cc99ff", "#ffcc99",
        "#3366ff", "#33cccc", "#99cc00", "#ffcc00", "#ff9900", "#ff6600", "#666699", "#969696",
        "#003366", "#339966", "#003300", "#333300", "#993300", "#993366", "#333399", "#333333"
    ];
}
