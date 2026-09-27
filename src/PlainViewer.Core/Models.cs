namespace PlainViewer.Core;

public sealed class DocumentView
{
    public string Kind { get; set; } = "";
    public string Text { get; set; } = "";
    public string Encoding { get; set; } = "";
    public string Notice { get; set; } = "";
    public char Delimiter { get; set; }
    public List<string[]> Rows { get; set; } = [];
    public List<ViewBlock> Blocks { get; set; } = [];
    public List<SheetData> Sheets { get; set; } = [];
}
public sealed class ViewBlock
{
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

// Spreadsheet sheet as display text. Cells hold what Excel would show; formulas are never recalculated.
public sealed class SheetData
{
    public string Name { get; set; } = "";
    public bool RightToLeft { get; set; }
    public int FrozenRows { get; set; }
    public int FrozenColumns { get; set; }
    public List<string[]> Rows { get; set; } = [];          // dense from A1; row i is Excel row i + 1
    public List<string> Align { get; set; } = [];           // one character per cell: l, r or c
    public List<double> ColumnWidths { get; set; } = [];    // Excel character units; 0 means hidden
    public List<int> HiddenRows { get; set; } = [];         // Excel row numbers
    public List<int[]> Merges { get; set; } = [];           // [firstRow, firstColumn, lastRow, lastColumn], zero-based
    public string Notice { get; set; } = "";
}
