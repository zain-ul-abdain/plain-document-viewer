using System.Buffers.Binary;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml;
using ExcelNumberFormat;
namespace PlainViewer.Core;

// Reads Excel 97-2003 (.xls) and OpenDocument (.ods) spreadsheets as display text, like the .xlsx reader: only saved
// values are shown and formulas are never evaluated. (Converting these files with LibreOffice recalculated them, which
// the specification forbids: tested with an .xls whose saved result had been changed.) Nothing a file refers to is
// opened. Sheets are kept in memory up to the same limits as the .xlsx preview (10,000 rows, 256 columns and 300,000
// cells), with a notice when a sheet is cut.
public static class LegacySpreadsheets
{
    public static readonly string[] Extensions = [".xls", ".ods"];
    public static bool Handles(string path) => Extensions.Contains(Path.GetExtension(path).ToLowerInvariant());

    public static DocumentView Load(string path, CultureInfo? culture = null)
    {
        culture ??= CultureInfo.CurrentCulture;
        TextFiles.ValidateLocalPath(path);
        string extension = Path.GetExtension(path).ToLowerInvariant();
        if (!Handles(path)) throw new DocumentException($"{extension} files do not open in this view.");
        byte[] bytes;
        using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        {
            long length = stream.Length;
            var modified = File.GetLastWriteTimeUtc(path);
            if (length == 0) throw new DocumentException("This spreadsheet is empty (0 bytes). It may not have finished downloading or copying. Get a complete copy and try again.");
            if (length > Spreadsheets.SizeLimit) throw new DocumentException("This spreadsheet is larger than 256 MB, which is more than this viewer can open safely.");
            bytes = new byte[length];
            stream.ReadExactly(bytes);
            if (File.GetLastWriteTimeUtc(path) != modified || new FileInfo(path).Length != length)
                throw new DocumentException("The file changed while it was being opened. Wait until it has finished saving, then open it again.");
        }
        ReadOnlySpan<byte> head = bytes;
        string Named() => $"This file is named {extension}, but its contents are not a spreadsheet this view can show. Open it with an application for its actual format.";
        try
        {
            if (CompoundFile.IsCompoundFile(head))
            {
                var file = new CompoundFile(bytes, "Excel 97–2003 workbook");
                if (file.Has("EncryptionInfo") || file.Has("EncryptedPackage"))
                    throw new DocumentException("This workbook is protected with a password. Password-protected workbooks cannot be opened in this version. Remove the password in Excel, or ask the sender for an unprotected copy.");
                if (!file.Has("Workbook") && !file.Has("Book")) throw new DocumentException(Named());
                if (file.Find("Workbook") is null) throw new DocumentException("This is an Excel 5.0 or Excel 95 workbook, which is older than this viewer supports. Save it in a newer format to view it.");
                return new Excel97(file, culture).Read();
            }
            if (head.StartsWith("PK\u0003\u0004"u8))
            {
                using var zip = new ZipArchive(new MemoryStream(bytes, false), ZipArchiveMode.Read);
                if (zip.GetEntry("[Content_Types].xml") is not null)
                    throw new DocumentException($"This is a newer Excel file (.xlsx) saved with a {extension} name. Rename it to end in .xlsx to view it.");
                string mime = "";
                if (zip.GetEntry("mimetype") is { Length: < 200 } entry) using (var reader = new StreamReader(entry.Open())) mime = reader.ReadToEnd().Trim();
                if (mime != "application/vnd.oasis.opendocument.spreadsheet") throw new DocumentException(Named());
                return new OpenDocumentSheets(zip).Read();
            }
        }
        catch (InvalidDataException) { throw Damaged(); }
        catch (XmlException) { throw Damaged(); }
        catch (Exception ex) when (ex is ArgumentOutOfRangeException or IndexOutOfRangeException or OverflowException) { throw Damaged(); }
        throw new DocumentException(Named());
    }

    private static DocumentException Damaged() => new("This spreadsheet is damaged or incomplete, so it cannot be shown. Try another copy of the file.");

    // Rows and cells collected for one sheet, then laid out as SheetData (dense rows from A1, as the .xlsx reader does).
    private sealed class SheetBuilder(string name)
    {
        public readonly SheetData Sheet = new() { Name = name };
        public readonly SortedDictionary<int, SortedDictionary<int, (string Text, char Align)>> Cells = [];
        public readonly Dictionary<int, double> Widths = [];          // column -> Excel character width (0 = hidden)
        public double DefaultWidth = 8.43;
        public bool Truncated;
        public void Add(int row, int column, string text, char align, ref int budget)
        {
            if (row >= Spreadsheets.MaxRowsPerSheet || column >= Spreadsheets.MaxColumns || budget <= 0) { Truncated = true; return; }
            if (text.Length == 0) return;
            if (!Cells.TryGetValue(row, out var line)) Cells[row] = line = [];
            if (!line.ContainsKey(column)) budget--;
            line[column] = (text, align);
        }
        public SheetData Build()
        {
            int columns = Math.Max(Sheet.FrozenColumns, Cells.Count == 0 ? 0 : Cells.Values.Max(r => r.Count == 0 ? 0 : r.Keys.Max() + 1));
            int rows = Math.Max(Sheet.FrozenRows, Cells.Count == 0 ? 0 : Cells.Keys.Max() + 1);
            for (int r = 0; r < rows; r++)
            {
                var text = new string[columns]; var align = new char[columns];
                Array.Fill(text, ""); Array.Fill(align, 'l');
                if (Cells.TryGetValue(r, out var line)) foreach (var (c, (t, a)) in line) { text[c] = t; align[c] = a; }
                Sheet.Rows.Add(text); Sheet.Align.Add(new string(align));
            }
            for (int c = 0; c < columns; c++) Sheet.ColumnWidths.Add(Math.Round(Widths.TryGetValue(c, out double w) ? w : DefaultWidth, 2));
            Sheet.RowCount = rows;
            Sheet.HiddenRows = Sheet.HiddenRows.Where(r => r <= rows).ToList();
            Sheet.Merges = Sheet.Merges.Where(m => m[0] < rows && m[1] < columns)
                .Select(m => new[] { m[0], m[1], Math.Min(m[2], rows - 1), Math.Min(m[3], columns - 1) }).ToList();
            if (rows == 0) Sheet.Notice = "This sheet is empty.";
            return Sheet;
        }
    }

    private static string Notes(int hidden, bool truncated, List<string> extra)
    {
        var notes = new List<string>(extra);
        if (hidden > 0) notes.Add(hidden == 1 ? "1 hidden sheet stays hidden." : $"{hidden} hidden sheets stay hidden.");
        if (truncated) notes.Add($"Preview limit: only the first {Spreadsheets.MaxRowsPerSheet:N0} rows and {Spreadsheets.MaxColumns} columns of each sheet, up to {Spreadsheets.MaxCellsPerWorkbook:N0} cells in total, are shown.");
        return string.Join(" ", notes);
    }

    // ---- Excel 97-2003 (BIFF8) ----

    private sealed class Excel97(CompoundFile file, CultureInfo culture)
    {
        private const string Label = "Excel 97–2003 workbook";
        private byte[] data = [];
        private readonly List<string> strings = [];
        private readonly Dictionary<int, string> custom = [];
        private readonly List<int> formats = [];             // per XF: number format id
        private readonly List<char> horizontal = [];         // per XF: l, r, c or '\0' (general)
        private readonly Dictionary<string, NumberFormat> cache = [];
        private bool date1904, truncated;
        private int budget = Spreadsheets.MaxCellsPerWorkbook, macros;

        private readonly record struct Record(int Type, int Offset, int Length);

        private List<Record> Records(int from)
        {
            var list = new List<Record>();
            for (int at = from; at + 4 <= data.Length;)
            {
                int type = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(at)), length = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(at + 2));
                if (at + 4 + length > data.Length) throw Damaged();
                list.Add(new Record(type, at + 4, length));
                at += 4 + length;
                if (type == 0x000A) break;                         // EOF of this substream
            }
            return list;
        }

        public DocumentView Read()
        {
            data = file.Read(file.Find("Workbook")!, Label);
            var globals = Records(0);
            if (globals.Count == 0 || globals[0].Type != 0x0809) throw Damaged();
            var sheets = new List<(string Name, int State, int Type, int Offset)>();
            for (int i = 0; i < globals.Count; i++)
            {
                var (type, at, length) = globals[i];
                switch (type)
                {
                    case 0x002F: throw new DocumentException("This workbook is protected with a password. Password-protected workbooks cannot be opened in this version. Remove the password in Excel, or ask the sender for an unprotected copy.");
                    case 0x0022: date1904 = length >= 2 && BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(at)) == 1; break;
                    case 0x041E when length >= 5:
                        custom[BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(at))] = XlString(at + 2, at + length, twoByteCount: true, out _);
                        break;
                    case 0x00E0 when length >= 10:
                        formats.Add(BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(at + 2)));
                        horizontal.Add((data[at + 6] & 0x07) switch { 1 => 'l', 2 => 'c', 3 => 'r', 5 => 'l', 6 => 'c', 7 => 'l', _ => '\0' });
                        break;
                    case 0x0085 when length >= 8:
                        sheets.Add((XlString(at + 6, at + length, twoByteCount: false, out _), data[at + 4] & 0x03, data[at + 5], BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(at))));
                        break;
                    case 0x00FC: ReadStrings(globals, i); break;
                }
            }
            macros = file.Has("_VBA_PROJECT_CUR") ? 1 : 0;
            var view = new DocumentView { Kind = "sheet", Encoding = Label, CellStyles = [new()] };
            int hidden = 0;
            var extra = new List<string>();
            foreach (var (name, state, type, offset) in sheets)
            {
                if (state != 0) { hidden++; continue; }
                if (type != 0) { extra.Add($"The {(type == 2 ? "chart" : "macro")} sheet \"{name}\" is not shown in this version."); continue; }
                if (offset < 0 || offset >= data.Length) throw Damaged();
                view.Sheets.Add(ReadSheet(name, offset));
            }
            if (view.Sheets.Count == 0) throw new DocumentException("This workbook has no visible worksheets to show.");
            if (macros > 0) extra.Insert(0, "This workbook contains macros. They were ignored and never ran.");
            view.Notice = Notes(hidden, truncated, extra);
            return view;
        }

        // The shared string table, which continues across CONTINUE records; each continuation of a string's
        // characters starts with its own flags byte.
        private void ReadStrings(List<Record> records, int index)
        {
            var chunks = new List<(int Start, int End)> { (records[index].Offset, records[index].Offset + records[index].Length) };
            for (int i = index + 1; i < records.Count && records[i].Type == 0x003C; i++) chunks.Add((records[i].Offset, records[i].Offset + records[i].Length));
            int chunk = 0, at = chunks[0].Start + 8;
            int unique = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(chunks[0].Start + 4));
            long characters = 0;
            void Need(int bytes) { if (at + bytes > chunks[chunk].End) { if (at != chunks[chunk].End || ++chunk >= chunks.Count) throw Damaged(); at = chunks[chunk].Start; } }
            byte Byte() { Need(1); return data[at++]; }
            int Int16() { Need(2); int v = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(at)); at += 2; return v; }
            int Int32() { Need(4); int v = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(at)); at += 4; return v; }
            void Skip(long bytes)
            {
                while (bytes > 0)
                {
                    if (at == chunks[chunk].End) { if (++chunk >= chunks.Count) throw Damaged(); at = chunks[chunk].Start; }
                    int take = (int)Math.Min(bytes, chunks[chunk].End - at); at += take; bytes -= take;
                }
            }
            for (int s = 0; s < unique && s < 1_000_000; s++)
            {
                int count = Int16(); byte flags = Byte();
                int runs = (flags & 0x08) != 0 ? Int16() : 0;
                int extended = (flags & 0x04) != 0 ? Int32() : 0;
                bool wide = (flags & 0x01) != 0;
                var text = new StringBuilder(count);
                while (text.Length < count)
                {
                    if (at == chunks[chunk].End) { if (++chunk >= chunks.Count) throw Damaged(); at = chunks[chunk].Start; wide = (data[at++] & 0x01) != 0; }
                    int available = (chunks[chunk].End - at) / (wide ? 2 : 1), take = Math.Min(count - text.Length, available);
                    if (take <= 0) throw Damaged();
                    text.Append(wide ? Encoding.Unicode.GetString(data, at, take * 2) : Encoding.Latin1.GetString(data, at, take));
                    at += take * (wide ? 2 : 1);
                }
                Skip(runs * 4L); Skip(extended);
                characters += count;
                if (characters > 32L * 1024 * 1024) throw new DocumentException("This workbook contains more text than this viewer can show safely.");
                strings.Add(text.ToString());
            }
        }

        // XLUnicodeString (two-byte count) or ShortXLUnicodeString (one-byte count) within one record.
        private string XlString(int at, int end, bool twoByteCount, out int next)
        {
            int count = twoByteCount ? BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(at)) : data[at];
            at += twoByteCount ? 2 : 1;
            bool wide = (data[at++] & 0x01) != 0;
            int bytes = count * (wide ? 2 : 1);
            if (at + bytes > end) throw Damaged();
            next = at + bytes;
            return wide ? Encoding.Unicode.GetString(data, at, bytes) : Encoding.Latin1.GetString(data, at, bytes);
        }

        private string Number(double value, int xf) =>
            Spreadsheets.FormatValue(value, xf >= 0 && xf < formats.Count ? formats[xf] : 0, custom, culture, date1904, cache);

        private char Align(int xf, char natural) => xf >= 0 && xf < horizontal.Count && horizontal[xf] != '\0' ? horizontal[xf] : natural;

        private static double Rk(uint rk)
        {
            double value = (rk & 0x02) != 0 ? (int)rk >> 2 : BitConverter.Int64BitsToDouble((long)(rk & 0xFFFFFFFC) << 32);
            return (rk & 0x01) != 0 ? value / 100 : value;
        }

        private static string Error(byte code) => code switch
        {
            0x00 => "#NULL!", 0x07 => "#DIV/0!", 0x0F => "#VALUE!", 0x17 => "#REF!", 0x1D => "#NAME?", 0x24 => "#NUM!", 0x2A => "#N/A", _ => "#N/A"
        };

        private SheetData ReadSheet(string name, int offset)
        {
            var records = Records(offset);
            if (records.Count == 0 || records[0].Type != 0x0809) throw Damaged();
            var sheet = new SheetBuilder(name);
            for (int i = 0; i < records.Count; i++)
            {
                var (type, at, length) = records[i];
                int Row() => BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(at));
                int Column() => BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(at + 2));
                int Xf() => BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(at + 4));
                switch (type)
                {
                    case 0x0203 when length >= 14: sheet.Add(Row(), Column(), Number(BinaryPrimitives.ReadDoubleLittleEndian(data.AsSpan(at + 6)), Xf()), Align(Xf(), 'r'), ref budget); break;
                    case 0x027E when length >= 10: sheet.Add(Row(), Column(), Number(Rk(BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(at + 6))), Xf()), Align(Xf(), 'r'), ref budget); break;
                    case 0x00BD when length >= 6:
                        for (int k = 0, first = Column(); 4 + k * 6 + 6 <= length - 2; k++)
                        {
                            int xf = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(at + 4 + k * 6));
                            sheet.Add(Row(), first + k, Number(Rk(BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(at + 6 + k * 6))), xf), Align(xf, 'r'), ref budget);
                        }
                        break;
                    case 0x00FD when length >= 10:
                        int index = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(at + 6));
                        sheet.Add(Row(), Column(), index >= 0 && index < strings.Count ? strings[index] : "", Align(Xf(), 'l'), ref budget);
                        break;
                    case 0x0204 when length >= 9: sheet.Add(Row(), Column(), XlString(at + 6, at + length, twoByteCount: true, out _), Align(Xf(), 'l'), ref budget); break;
                    case 0x0205 when length >= 8:
                        sheet.Add(Row(), Column(), data[at + 7] == 0 ? (data[at + 6] != 0 ? "TRUE" : "FALSE") : Error(data[at + 6]), Align(Xf(), 'c'), ref budget);
                        break;
                    case 0x0006 when length >= 20:
                        // The saved result: a number, or (when the last two bytes are 0xFFFF) a string in the next STRING
                        // record, a boolean, an error or an empty string.
                        if (BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(at + 12)) != 0xFFFF)
                            sheet.Add(Row(), Column(), Number(BinaryPrimitives.ReadDoubleLittleEndian(data.AsSpan(at + 6)), Xf()), Align(Xf(), 'r'), ref budget);
                        else
                            switch (data[at + 6])
                            {
                                case 0:
                                    if (i + 1 < records.Count && records[i + 1].Type == 0x0207 && records[i + 1].Length >= 3)
                                        sheet.Add(Row(), Column(), XlString(records[i + 1].Offset, records[i + 1].Offset + records[i + 1].Length, twoByteCount: true, out _), Align(Xf(), 'l'), ref budget);
                                    break;
                                case 1: sheet.Add(Row(), Column(), data[at + 8] != 0 ? "TRUE" : "FALSE", Align(Xf(), 'c'), ref budget); break;
                                case 2: sheet.Add(Row(), Column(), Error(data[at + 8]), Align(Xf(), 'c'), ref budget); break;
                            }
                        break;
                    case 0x0208 when length >= 16:
                        if ((BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(at + 12)) & 0x0020) != 0) sheet.Sheet.HiddenRows.Add(Row() + 1);
                        break;
                    case 0x007D when length >= 10:
                        {
                            int first = Row(), last = Math.Min(Column(), Spreadsheets.MaxColumns - 1);
                            double width = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(at + 4)) / 256.0;
                            bool isHidden = (BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(at + 8)) & 0x0001) != 0;
                            for (int c = first; c <= last; c++) sheet.Widths[c] = isHidden ? 0 : width;
                        }
                        break;
                    case 0x0055 when length >= 2: sheet.DefaultWidth = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(at)) + 0.71; break;
                    case 0x00E5 when length >= 2:
                        for (int k = 0, count = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(at)); k < count && 2 + k * 8 + 8 <= length; k++)
                        {
                            var span = data.AsSpan(at + 2 + k * 8);
                            sheet.Sheet.Merges.Add([BinaryPrimitives.ReadUInt16LittleEndian(span), BinaryPrimitives.ReadUInt16LittleEndian(span[4..]),
                                BinaryPrimitives.ReadUInt16LittleEndian(span[2..]), BinaryPrimitives.ReadUInt16LittleEndian(span[6..])]);
                        }
                        break;
                    case 0x023E when length >= 2: sheet.Sheet.RightToLeft = (BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(at)) & 0x0040) != 0; break;
                    case 0x0041 when length >= 4:
                        sheet.Sheet.FrozenColumns = Math.Min(Spreadsheets.MaxColumns, (int)BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(at)));
                        sheet.Sheet.FrozenRows = Math.Min(Spreadsheets.MaxRowsPerSheet, (int)BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(at + 2)));
                        break;
                }
            }
            truncated |= sheet.Truncated;
            return sheet.Build();
        }
    }

    // ---- OpenDocument spreadsheet ----

    // Each cell's paragraphs are the text the producing application displayed when it saved the file, so they are
    // shown as they are (no formatting is re-applied and no formula is evaluated).
    private sealed class OpenDocumentSheets(ZipArchive zip)
    {
        private readonly Dictionary<string, double> columnWidths = [];   // column style -> Excel character width
        private readonly HashSet<string> hiddenTables = [];              // table styles with display="false"
        private int budget = Spreadsheets.MaxCellsPerWorkbook;
        private bool truncated;

        public DocumentView Read()
        {
            ArchiveSafety.Validate(zip, maximumBytes: 2L * 1024 * 1024 * 1024, maximumEntries: 10000, maximumRatio: 500);
            if (zip.GetEntry("META-INF/manifest.xml") is { } manifest)
                using (var reader = new StreamReader(manifest.Open()))
                    if (reader.ReadToEnd().Contains("encryption-data", StringComparison.Ordinal))
                        throw new DocumentException("This spreadsheet is protected with a password. Password-protected files cannot be opened in this version. Remove the password in the application that made it, or ask the sender for an unprotected copy.");
            var content = zip.GetEntry("content.xml") ?? throw Damaged();
            var frozen = ReadFrozen();
            var view = new DocumentView { Kind = "sheet", Encoding = "OpenDocument spreadsheet", CellStyles = [new()] };
            int hidden = 0;
            var extra = new List<string>();
            if (zip.Entries.Any(e => e.FullName.StartsWith("Basic/", StringComparison.OrdinalIgnoreCase) || e.FullName.StartsWith("Scripts/", StringComparison.OrdinalIgnoreCase)))
                extra.Add("This spreadsheet contains macros. They were ignored and never ran.");
            using var r = Open(content);
            SheetBuilder? sheet = null;
            int row = -1;
            while (r.Read())
            {
                if (r.NodeType == XmlNodeType.EndElement && r.LocalName == "table" && sheet is not null)
                { truncated |= sheet.Truncated; view.Sheets.Add(sheet.Build()); sheet = null; continue; }
                if (r.NodeType != XmlNodeType.Element) continue;
                switch (r.LocalName)
                {
                    case "style" when sheet is null: ReadStyle(r); break;
                    case "table" when r.NamespaceURI.EndsWith(":table:1.0", StringComparison.Ordinal):
                        string name = r.GetAttribute("name", TableNs) ?? $"Sheet{view.Sheets.Count + hidden + 1}";
                        if (hiddenTables.Contains(r.GetAttribute("style-name", TableNs) ?? "")) { hidden++; r.Skip(); continue; }
                        sheet = new SheetBuilder(name); row = -1;
                        if (frozen.TryGetValue(name, out var split)) { sheet.Sheet.FrozenColumns = split.Columns; sheet.Sheet.FrozenRows = split.Rows; }
                        if (r.IsEmptyElement) { view.Sheets.Add(sheet.Build()); sheet = null; }
                        break;
                    case "table-column" when sheet is not null:
                        {
                            int repeat = Repeat(r, "number-columns-repeated");
                            double width = columnWidths.GetValueOrDefault(r.GetAttribute("style-name", TableNs) ?? "", sheet.DefaultWidth);
                            if (r.GetAttribute("visibility", TableNs) is "collapse" or "filter") width = 0;
                            int start = sheet.Widths.Count;
                            for (int k = 0; k < repeat && start + k < Spreadsheets.MaxColumns; k++) sheet.Widths[start + k] = width;
                        }
                        break;
                    case "table-row" when sheet is not null:
                        {
                            int repeat = Repeat(r, "number-rows-repeated");
                            bool hiddenRow = r.GetAttribute("visibility", TableNs) is "collapse" or "filter";
                            if (r.IsEmptyElement) { row += repeat; break; }
                            row++;
                            // A repeated row with content repeats its cells too; only the first copy is read, and further
                            // copies are materialised only while within the limits.
                            if (hiddenRow) sheet.Sheet.HiddenRows.Add(row + 1);
                            ReadRow(r, sheet, row, repeat);
                            if (repeat > 1) row += repeat - 1;
                        }
                        break;
                }
            }
            if (view.Sheets.Count == 0) throw new DocumentException("This spreadsheet has no visible sheets to show.");
            view.Notice = Notes(hidden, truncated, extra);
            return view;
        }

        private const string TableNs = "urn:oasis:names:tc:opendocument:xmlns:table:1.0";
        private const string OfficeNs = "urn:oasis:names:tc:opendocument:xmlns:office:1.0";

        private static XmlReader Open(ZipArchiveEntry entry) => XmlReader.Create(entry.Open(), new XmlReaderSettings
        { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersFromEntities = 1024, IgnoreComments = true, IgnoreProcessingInstructions = true, CloseInput = true });

        private static int Repeat(XmlReader r, string attribute) =>
            int.TryParse(r.GetAttribute(attribute, TableNs), NumberStyles.None, CultureInfo.InvariantCulture, out int n) && n > 0 ? n : 1;

        // Automatic styles: column widths (converted to Excel character units, as the grid expects) and hidden tables.
        private void ReadStyle(XmlReader r)
        {
            string name = r.GetAttribute("name", "urn:oasis:names:tc:opendocument:xmlns:style:1.0") ?? "";
            if (r.IsEmptyElement) return;
            int depth = r.Depth;
            while (r.Read() && r.Depth > depth)
            {
                if (r.NodeType != XmlNodeType.Element) continue;
                if (r.LocalName == "table-column-properties" && Length(r.GetAttribute("column-width", "urn:oasis:names:tc:opendocument:xmlns:style:1.0")) is double px)
                    columnWidths[name] = Math.Max(0, Math.Round((px - 5) / 7, 2));
                if (r.LocalName == "table-properties" && r.GetAttribute("display", TableNs) == "false") hiddenTables.Add(name);
            }
        }

        // A length such as "2.258cm" in pixels at 96 dpi.
        private static double? Length(string? value)
        {
            if (value is null) return null;
            foreach (var (unit, factor) in new[] { ("cm", 96 / 2.54), ("mm", 96 / 25.4), ("in", 96.0), ("pt", 96 / 72.0), ("pc", 16.0), ("px", 1.0) })
                if (value.EndsWith(unit, StringComparison.Ordinal) && double.TryParse(value[..^unit.Length], NumberStyles.Float, CultureInfo.InvariantCulture, out double n)) return n * factor;
            return null;
        }

        private void ReadRow(XmlReader r, SheetBuilder sheet, int row, int rowRepeat)
        {
            int depth = r.Depth, column = 0;
            var cells = new List<(int Column, string Text, char Align)>();
            while (r.Read() && r.Depth > depth)
            {
                if (r.NodeType != XmlNodeType.Element || r.Depth != depth + 1) continue;
                if (r.LocalName is not ("table-cell" or "covered-table-cell")) { r.Skip(); continue; }
                int repeat = Repeat(r, "number-columns-repeated");
                string type = r.GetAttribute("value-type", OfficeNs) ?? "";
                int spanColumns = Repeat(r, "number-columns-spanned"), spanRows = Repeat(r, "number-rows-spanned");
                if (r.LocalName == "table-cell" && (spanColumns > 1 || spanRows > 1) && column < Spreadsheets.MaxColumns && row < Spreadsheets.MaxRowsPerSheet)
                    sheet.Sheet.Merges.Add([row, column, row + spanRows - 1, Math.Min(Spreadsheets.MaxColumns - 1, column + spanColumns - 1)]);
                string text = CellText(r);
                char align = type switch { "float" or "percentage" or "currency" or "date" or "time" => 'r', "boolean" => 'c', _ => 'l' };
                if (text.Length > 0) for (int k = 0; k < repeat && column + k < Spreadsheets.MaxColumns && k < 1024; k++) cells.Add((column + k, text, align));
                column += repeat;
            }
            // Rows repeated with the same content (rare apart from empty rows) are laid out while within the limits.
            for (int k = 0; k < rowRepeat && (k == 0 || cells.Count > 0); k++)
            {
                if (row + k >= Spreadsheets.MaxRowsPerSheet) { if (cells.Count > 0) sheet.Truncated = true; break; }
                foreach (var (c, t, a) in cells) sheet.Add(row + k, c, t, a, ref budget);
            }
        }

        // The cell's paragraphs (text:p) joined by line breaks, with <text:s/>, <text:tab/> and <text:line-break/>.
        private static string CellText(XmlReader r)
        {
            if (r.IsEmptyElement) return "";
            var text = new StringBuilder();
            int depth = r.Depth, paragraphs = 0;
            while (r.Read() && r.Depth > depth)
            {
                if (r.NodeType == XmlNodeType.Element)
                {
                    switch (r.LocalName)
                    {
                        case "p": if (paragraphs++ > 0) text.Append('\n'); break;
                        case "s": text.Append(' ', Math.Min(1000, Repeat2(r))); break;
                        case "tab": text.Append('\t'); break;
                        case "line-break": text.Append('\n'); break;
                        case "annotation": r.Skip(); break;                // comments are not cell text
                    }
                }
                else if (r.NodeType is XmlNodeType.Text or XmlNodeType.SignificantWhitespace or XmlNodeType.Whitespace && r.Depth > depth + 1) text.Append(r.Value);
                if (text.Length > 32767) break;
            }
            return text.ToString();
        }

        private static int Repeat2(XmlReader r) =>
            int.TryParse(r.GetAttribute("c", "urn:oasis:names:tc:opendocument:xmlns:text:1.0"), NumberStyles.None, CultureInfo.InvariantCulture, out int n) && n > 0 ? n : 1;

        // Frozen rows and columns per sheet, from settings.xml (split mode 2 = frozen).
        private Dictionary<string, (int Columns, int Rows)> ReadFrozen()
        {
            var result = new Dictionary<string, (int, int)>();
            if (zip.GetEntry("settings.xml") is not { } settings) return result;
            using var r = Open(settings);
            string? table = null; var values = new Dictionary<string, string>();
            int tablesDepth = -1;
            while (r.Read())
            {
                if (r.NodeType == XmlNodeType.Element && r.LocalName == "config-item-map-named" && r.GetAttribute("name", "urn:oasis:names:tc:opendocument:xmlns:config:1.0") == "Tables") tablesDepth = r.Depth;
                else if (tablesDepth >= 0 && r.NodeType == XmlNodeType.Element && r.LocalName == "config-item-map-entry" && r.Depth == tablesDepth + 1)
                { table = r.GetAttribute("name", "urn:oasis:names:tc:opendocument:xmlns:config:1.0"); values.Clear(); }
                else if (table is not null && r.NodeType == XmlNodeType.Element && r.LocalName == "config-item")
                    values[r.GetAttribute("name", "urn:oasis:names:tc:opendocument:xmlns:config:1.0") ?? ""] = r.ReadElementContentAsString();
                else if (table is not null && r.NodeType == XmlNodeType.EndElement && r.LocalName == "config-item-map-entry" && r.Depth == tablesDepth + 1)
                {
                    int Value(string key) => values.TryGetValue(key, out var v) && int.TryParse(v, out int n) ? n : 0;
                    int columns = Value("HorizontalSplitMode") == 2 ? Value("HorizontalSplitPosition") : 0, rows = Value("VerticalSplitMode") == 2 ? Value("VerticalSplitPosition") : 0;
                    if (columns > 0 || rows > 0) result[table] = (Math.Min(columns, Spreadsheets.MaxColumns), Math.Min(rows, Spreadsheets.MaxRowsPerSheet));
                    table = null;
                }
                else if (r.NodeType == XmlNodeType.EndElement && r.LocalName == "config-item-map-named" && r.Depth == tablesDepth) tablesDepth = -1;
            }
            return result;
        }
    }
}
