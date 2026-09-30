using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
namespace PlainViewer.Core;

// Conditional formatting of .xls and .ods sheets, read into the same rules as .xlsx (ConditionalFormats) and applied to
// the saved values after the sheet is read.
public static partial class LegacySpreadsheets
{
    // ---- Excel 97-2003: CONDFMT and CF records ----

    private sealed partial class Excel97
    {
        // CONDFMT: the ranges the CF records after it apply to (SqRefU after ccf, flags and the bounding range).
        private List<int[]> ConditionRanges(int at, int length)
        {
            var ranges = new List<int[]>();
            if (length < 14) return ranges;
            int count = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(at + 12));
            for (int k = 0; k < count && k < 100 && 14 + k * 8 + 8 <= length; k++)
            {
                var span = data.AsSpan(at + 14 + k * 8);
                int row1 = BinaryPrimitives.ReadUInt16LittleEndian(span), row2 = BinaryPrimitives.ReadUInt16LittleEndian(span[2..]);
                int column1 = BinaryPrimitives.ReadUInt16LittleEndian(span[4..]), column2 = BinaryPrimitives.ReadUInt16LittleEndian(span[6..]);
                if (row1 <= row2 && column1 <= column2) ranges.Add([row1, column1, row2, column2]);
            }
            return ranges;
        }

        // CF: one rule of the CONDFMT before it, with its format (DXFN) and one or two parsed formulas. Excel 97-2003 rules
        // compare the cell's value (shown when the formulas are constants) or are formulas (never evaluated). In a
        // CONDFMT the first true rule wins, so every rule stops the ones after it.
        private void ReadCondition(int at, int length, List<int[]> ranges, int priority, ConditionalFormats target)
        {
            int end = at + length;
            if (length < 12 || ranges.Count == 0) { target.NotShown++; return; }
            int kind = data[at], comparison = data[at + 1];
            int cce1 = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(at + 2)), cce2 = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(at + 4));
            uint flags = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(at + 6));
            int more = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(at + 10));
            bool Has(int bit) => (flags & (1u << bit)) != 0;
            int p = at + 12;
            var format = new WorkbookStyles.Dxf();
            if (Has(25)) p += (more & 1) != 0 && p + 2 <= end ? Math.Max(2, (int)BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(p))) : 2;  // number format: not shown
            if (Has(26))
            {
                // DXFFntD: Stxp at 64 (height, ts, bls, sss, uls), then icvFore at 80 and the "not changed" flags.
                if (p + 118 > end) { target.NotShown++; return; }
                uint ts = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(p + 68)), tsNinch = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(p + 88));
                int bls = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(p + 72));
                uint colour = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(p + 80));
                if ((tsNinch & 0x02) == 0) format.Italic = (ts & 0x02) != 0;
                if ((tsNinch & 0x80) == 0) format.Strike = (ts & 0x80) != 0;
                if (BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(p + 96)) == 0) format.Underline = data[p + 76] != 0;
                if (BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(p + 100)) == 0) format.Bold = bls >= 600;
                if (colour < 64) format.Color = Colour((int)colour);
                p += 118;
            }
            if (Has(27)) p += 8;                                                      // alignment: not changed by rules
            if (Has(28))
            {
                // DXFBdr: the XF record's border layout; a side is set only when its "not changed" flag is clear.
                if (p + 8 > end) { target.NotShown++; return; }
                uint lines = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(p)), colours = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(p + 4));
                if (!Has(10)) format.Left = Border((int)(lines & 0xF), Colour((int)((lines >> 16) & 0x7F)));
                if (!Has(11)) format.Right = Border((int)((lines >> 4) & 0xF), Colour((int)((lines >> 23) & 0x7F)));
                if (!Has(12)) format.Top = Border((int)((lines >> 8) & 0xF), Colour((int)(colours & 0x7F)));
                if (!Has(13)) format.Bottom = Border((int)((lines >> 12) & 0xF), Colour((int)((colours >> 7) & 0x7F)));
                p += 8;
            }
            if (Has(29))
            {
                // DXFPat: a solid fill's colour is the background colour (as in an .xlsx dxf).
                if (p + 4 > end) { target.NotShown++; return; }
                uint pattern = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(p));
                int fill = (int)((pattern >> 10) & 0x3F), fore = (int)((pattern >> 16) & 0x7F), back = (int)((pattern >> 23) & 0x7F);
                if (Has(16) || fill == 1) format.Fill = !Has(18) ? Colour(back) : !Has(17) ? Colour(fore) : null;
                p += 4;
            }
            if (Has(30)) p += 2;                                                      // protection
            string? first = cce1 > 0 && p + cce1 <= end ? Constant(p, cce1) : null;
            string? second = cce2 > 0 && p + cce1 + cce2 <= end ? Constant(p + cce1, cce2) : null;
            string? operation = comparison switch
            {
                1 => "between", 2 => "notBetween", 3 => "equal", 4 => "notEqual",
                5 => "greaterThan", 6 => "lessThan", 7 => "greaterThanOrEqual", 8 => "lessThanOrEqual", _ => null
            };
            bool pair = comparison is 1 or 2;
            if (kind != 1 || operation is null || first is null || pair && second is null) { target.NotShown++; return; }
            var rule = new ConditionalFormats.Rule { Type = "cellIs", Operator = operation, Format = format, Priority = priority, Stop = true };
            rule.Formulas.Add(first);
            if (pair) rule.Formulas.Add(second!);
            rule.Ranges.AddRange(ranges);
            target.Add(rule);
        }

        // A parsed formula that is a single constant (a number, possibly negated, text or TRUE/FALSE) as the rules'
        // constant form; null for anything to calculate.
        private string? Constant(int at, int length)
        {
            var tokens = data.AsSpan(at, length);
            bool negate = tokens.Length > 1 && tokens[^1] == 0x13;                     // tUminus after the number
            if (negate) tokens = tokens[..^1];
            double? number = tokens[0] switch
            {
                0x1E when tokens.Length == 3 => BinaryPrimitives.ReadUInt16LittleEndian(tokens[1..]),
                0x1F when tokens.Length == 9 => BinaryPrimitives.ReadDoubleLittleEndian(tokens[1..]),
                _ => null
            };
            if (number is double n) return double.IsFinite(n) ? (negate ? -n : n).ToString("R", CultureInfo.InvariantCulture) : null;
            if (negate) return null;
            if (tokens[0] == 0x1D && tokens.Length == 2) return tokens[1] != 0 ? "TRUE" : "FALSE";
            if (tokens[0] == 0x17 && tokens.Length >= 3)
            {
                int count = tokens[1];
                bool wide = (tokens[2] & 0x01) != 0;
                if (tokens.Length != 3 + count * (wide ? 2 : 1)) return null;
                string text = wide ? Encoding.Unicode.GetString(tokens.Slice(3, count * 2)) : Encoding.Latin1.GetString(tokens.Slice(3, count));
                return "\"" + text.Replace("\"", "\"\"") + "\"";
            }
            return null;
        }
    }

    // ---- OpenDocument: calcext:conditional-formats (LibreOffice) ----

    private const string CalcExtNs = "urn:org:documentfoundation:names:experimental:calc:xmlns:calcext:1.0";
    private static string? Ext(XElement e, string name) => e.Attribute(XName.Get(name, CalcExtNs))?.Value;

    // A sheet's conditional formats: conditions (with a cell style), colour scales, data bars and icon sets. Conditions
    // written as formulas and date conditions are counted as not shown.
    private static void ReadConditions(XElement formats, ConditionalFormats target, Func<string, WorkbookStyles.Dxf?> style)
    {
        int priority = 0;
        foreach (var format in formats.Elements(XName.Get("conditional-format", CalcExtNs)).Take(1000))
        {
            var ranges = OpenDocumentRanges(Ext(format, "target-range-address") ?? "");
            foreach (var e in format.Elements().Take(200))
            {
                priority++;
                var rule = e.Name.NamespaceName != CalcExtNs ? null : e.Name.LocalName switch
                {
                    "condition" => Condition(e, style),
                    "color-scale" => Scale(e, "colorScale"),
                    "data-bar" => Scale(e, "dataBar"),
                    "icon-set" => Scale(e, "iconSet"),
                    _ => null                                                          // date-is and others
                };
                if (rule is null || ranges.Count == 0) { target.NotShown++; continue; }
                rule.Priority = priority;
                rule.Ranges.AddRange(ranges);
                target.Add(rule);
            }
        }
    }

    // "Sheet1.A2:Sheet1.A7 'My sheet'.C1" as zero-based [row1, column1, row2, column2] ranges.
    private static List<int[]> OpenDocumentRanges(string address)
    {
        var ranges = new List<int[]>();
        foreach (Match m in RangeAddress().Matches(address))
        {
            if (ranges.Count >= 100) break;
            if (!Spreadsheets.TryCell(m.Groups[1].Value + m.Groups[2].Value, out int row1, out int column1)) continue;
            int row2 = row1, column2 = column1;
            if (m.Groups[3].Success && !Spreadsheets.TryCell(m.Groups[3].Value + m.Groups[4].Value, out row2, out column2)) continue;
            ranges.Add([Math.Min(row1, row2) - 1, Math.Min(column1, column2), Math.Max(row1, row2) - 1, Math.Max(column1, column2)]);
        }
        return ranges;
    }
    [GeneratedRegex(@"(?:'(?:[^']|'')*'|[^\s.:']*)\.\$?([A-Za-z]{1,3})\$?(\d{1,7})(?::(?:'(?:[^']|'')*'|[^\s.:']*)\.\$?([A-Za-z]{1,3})\$?(\d{1,7}))?")]
    private static partial Regex RangeAddress();

    // calcext:value of a condition: a comparison ("&gt;35", "between(1,2)") or a named test ("contains-text(\"x\")").
    private static ConditionalFormats.Rule? Condition(XElement e, Func<string, WorkbookStyles.Dxf?> style)
    {
        string value = (Ext(e, "value") ?? "").Trim();
        var rule = new ConditionalFormats.Rule { Format = style(Ext(e, "apply-style-name") ?? "") };
        foreach (var (prefix, operation) in new[] { ("<=", "lessThanOrEqual"), (">=", "greaterThanOrEqual"), ("!=", "notEqual"), ("<", "lessThan"), (">", "greaterThan"), ("=", "equal") })
            if (value.StartsWith(prefix, StringComparison.Ordinal))
            {
                rule.Type = "cellIs"; rule.Operator = operation;
                rule.Formulas.Add(value[prefix.Length..].Trim());
                return rule;
            }
        int open = value.IndexOf('(');
        string name = open < 0 ? value : value[..open];
        var args = open < 0 || !value.EndsWith(')') ? [] : Arguments(value[(open + 1)..^1]);
        int Rank() => args.Count == 1 && int.TryParse(args[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) ? Math.Clamp(n, 1, 1000) : -1;
        switch (name)
        {
            case "between" or "not-between" when args.Count == 2:
                rule.Type = "cellIs"; rule.Operator = name == "between" ? "between" : "notBetween"; rule.Formulas.AddRange(args); return rule;
            case "duplicate": rule.Type = "duplicateValues"; return rule;
            case "unique": rule.Type = "uniqueValues"; return rule;
            case "top-elements" or "bottom-elements" or "top-percent" or "bottom-percent" when Rank() > 0:
                rule.Type = "top10"; rule.Rank = Rank(); rule.Bottom = name.StartsWith("bottom", StringComparison.Ordinal); rule.Percent = name.EndsWith("percent", StringComparison.Ordinal);
                return rule;
            case "above-average" or "below-average" or "above-equal-average" or "below-equal-average":
                rule.Type = "aboveAverage"; rule.Above = name.StartsWith("above", StringComparison.Ordinal); rule.EqualAverage = name.Contains("equal", StringComparison.Ordinal);
                return rule;
            case "is-error": rule.Type = "containsErrors"; return rule;
            case "is-no-error": rule.Type = "notContainsErrors"; return rule;
            case "begins-with" or "ends-with" or "contains-text" or "not-contains-text" when args.Count == 1 && Unquote(args[0]) is { Length: > 0 } text:
                rule.Type = name switch { "begins-with" => "beginsWith", "ends-with" => "endsWith", "contains-text" => "containsText", _ => "notContainsText" };
                rule.Text = text;
                return rule;
            default: return null;                                                  // formula-is and others
        }
    }

    // Arguments separated by commas or semicolons outside quoted text.
    private static List<string> Arguments(string text)
    {
        var args = new List<string>();
        var current = new StringBuilder();
        bool quoted = false;
        foreach (char c in text)
        {
            if (c == '"') quoted = !quoted;
            if (!quoted && c is ',' or ';') { args.Add(current.ToString().Trim()); current.Clear(); continue; }
            current.Append(c);
        }
        if (current.Length > 0 || args.Count > 0) args.Add(current.ToString().Trim());
        return args.Take(4).ToList();
    }

    private static string? Unquote(string text) =>
        text.Length >= 2 && text[0] == '"' && text[^1] == '"' && !text[1..^1].Replace("\"\"", "").Contains('"') ? text[1..^1].Replace("\"\"", "\"") : null;

    // Colour scales, data bars and icon sets, with their entries as .xlsx scale points.
    private static ConditionalFormats.Rule? Scale(XElement e, string type)
    {
        var rule = new ConditionalFormats.Rule { Type = type };
        foreach (var entry in e.Elements().Where(c => c.Name.NamespaceName == CalcExtNs && c.Name.LocalName is "color-scale-entry" or "formatting-entry").Take(5))
        {
            string? kind = Ext(entry, "type") switch
            {
                "minimum" => "min", "maximum" => "max", "auto-minimum" => "autoMin", "auto-maximum" => "autoMax",
                "percent" => "percent", "percentile" => "percentile", "number" => "num", _ => null      // formula: not evaluated
            };
            if (kind is null) return null;
            rule.Points.Add((kind, Ext(entry, "value") ?? "0", true));
            if (type == "colorScale") rule.Colours.Add(WorkbookStyles.Hex(Ext(entry, "color")?.TrimStart('#')));
        }
        static int Length(string? text, int missing) => int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) ? Math.Clamp(n, 0, 100) : missing;
        if (type == "dataBar")
        {
            rule.Colours.Add(WorkbookStyles.Hex(Ext(e, "positive-color")?.TrimStart('#')));
            rule.MinLength = Length(Ext(e, "min-length"), 10);
            rule.MaxLength = Math.Max(rule.MinLength, Length(Ext(e, "max-length"), 90));
        }
        if (type == "iconSet") rule.IconSet = Ext(e, "icon-set-type") ?? "";
        if (Ext(e, "show-value") == "false") rule.ShowValue = false;
        return rule;
    }
}
