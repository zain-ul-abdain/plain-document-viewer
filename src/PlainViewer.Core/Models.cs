namespace PlainViewer.Core;

public sealed class DocumentView
{
    public string Kind { get; set; } = "";
    public string Text { get; set; } = "";
    public string Encoding { get; set; } = "";
    public string Notice { get; set; } = "";
    public char Delimiter { get; set; }
    public List<string[]> Rows { get; set; } = [];
    // CSV and large text: rows are in a RowStore named Store in the work folder the app passed to the worker.
    public string Store { get; set; } = "";
    public int RowCount { get; set; }
    public int Columns { get; set; }
    public List<ViewBlock> Blocks { get; set; } = [];
    public List<SheetData> Sheets { get; set; } = [];
    public List<CellStyle> CellStyles { get; set; } = [];   // workbooks: entry 0 is the plain default style
}
public sealed class ViewBlock
{
    public int StartNumber { get; set; } = 1;
    public string Alignment { get; set; } = "left";
    public string Kind { get; set; } = "paragraph";
    public int Level { get; set; }
    public string Text { get; set; } = "";
    public List<ViewRun> Runs { get; set; } = [];
    public List<ViewBlock> Children { get; set; } = [];
}
public sealed class ViewRun
{
    public string Text { get; set; } = "";
    public bool Bold { get; set; }
    public bool Italic { get; set; }
    public bool Code { get; set; }
    public string? Link { get; set; }
}
public sealed record WorkerResponse(DocumentView? Document, string? Error);
public sealed class DocumentException(string message) : Exception(message);
public static class LinkPolicy
{
    public static bool CanOpen(string? address) => address is not null &&
        Uri.TryCreate(address, UriKind.Absolute, out var uri) && !address.Any(char.IsControl) &&
        (uri.Scheme == "http" || uri.Scheme == "https" || uri.Scheme == "mailto");
}

// How one kind of cell looks, from the workbook's styles. Colours are "#rrggbb"; borders are "<px> <solid|dashed|
// dotted|double> #rrggbb". Built only from parsed numbers and names, never copied as text from the file. Size is
// relative to the workbook's default font (0 = same). Default values are left out of the JSON, so each style stays small.
public sealed class CellStyle
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)] public bool Bold { get; set; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)] public bool Italic { get; set; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)] public bool Underline { get; set; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)] public bool Strike { get; set; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)] public string? Color { get; set; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)] public string? Fill { get; set; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)] public double Size { get; set; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)] public string? Font { get; set; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)] public bool Wrap { get; set; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)] public string? VAlign { get; set; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)] public int Indent { get; set; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)] public string? Top { get; set; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)] public string? Right { get; set; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)] public string? Bottom { get; set; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)] public string? Left { get; set; }
    // A fill or border shows even in an empty cell.
    public bool Visible => Fill is not null || Top is not null || Right is not null || Bottom is not null || Left is not null;
}

// Spreadsheet sheet as display text. Cells hold what Excel would show; formulas are never recalculated.
public sealed class SheetData
{
    public string Name { get; set; } = "";
    public bool RightToLeft { get; set; }
    public int FrozenRows { get; set; }
    public int FrozenColumns { get; set; }
    public List<string[]> Rows { get; set; } = [];          // dense from A1; row i is Excel row i + 1
    // One character per cell (l, r or c); rows with styled cells add "|" and a style number (into
    // DocumentView.CellStyles) per cell, separated by ".", for example "lrc|0.4.4".
    public List<string> Align { get; set; } = [];
    public List<double> ColumnWidths { get; set; } = [];    // Excel character units; 0 means hidden
    public List<int> HiddenRows { get; set; } = [];         // Excel row numbers
    // Row heights in points (Excel's default row is 15 points): the sheet's default, and the rows that differ from it
    // (Excel row number -> height). The grid applies them to sheets held in memory; large sheets keep uniform rows.
    public double DefaultRowHeight { get; set; } = 15;
    public Dictionary<int, double> RowHeights { get; set; } = [];
    public List<int[]> Merges { get; set; } = [];           // [firstRow, firstColumn, lastRow, lastColumn], zero-based
    public string Notice { get; set; } = "";
    // Large sheets: every row is in a RowStore named Store (each stored row is [alignment, cell, cell, ...]);
    // Rows then holds only the first rows, for the first screen. RowCount is the sheet's full row count.
    public string Store { get; set; } = "";
    public int RowCount { get; set; }
    public List<SheetPicture> Pictures { get; set; } = [];  // pictures and charts drawn over the grid
    public bool ChartSheet { get; set; }                    // a chart sheet: no cells, one chart filling the view
}

// A picture or chart placed on a sheet. Position: from a cell (zero-based) plus an offset in pixels; size either to
// another cell (ToRow/ToColumn plus offsets) or in pixels. Media names a picture file the worker wrote to the work
// folder (checked by ImageFiles, never a link); Chart holds a chart's saved data.
public sealed class SheetPicture
{
    public int Row { get; set; }
    public int Column { get; set; }
    public double RowOffset { get; set; }
    public double ColumnOffset { get; set; }
    public int ToRow { get; set; } = -1;
    public int ToColumn { get; set; } = -1;
    public double ToRowOffset { get; set; }
    public double ToColumnOffset { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public string Media { get; set; } = "";
    public string Description { get; set; } = "";
    public ChartData? Chart { get; set; }
}

// A chart as the values saved in the file (the chart's own cache): never recalculated from the cells.
public sealed class ChartData
{
    public string Type { get; set; } = "column";             // column, bar, line, area, pie, doughnut, scatter
    public bool Stacked { get; set; }
    public bool Percent { get; set; }
    public string Title { get; set; } = "";
    public List<string> Categories { get; set; } = [];
    public List<ChartSeries> Series { get; set; } = [];
    public string Notice { get; set; } = "";
}

public sealed class ChartSeries
{
    public string Name { get; set; } = "";
    public List<double?> Values { get; set; } = [];
    public List<double?> X { get; set; } = [];                 // scatter charts
    public string? Color { get; set; }
}
