namespace PlainViewer.Core;

// Every file type the viewer opens, grouped as the Open dialog shows them. The installer's "Open with" list must
// match (a core test checks installer/PlainViewer.iss against All).
public static class Formats
{
    public static readonly (string Name, string[] Extensions)[] Groups =
    [
        ("PDF documents", [".pdf"]),
        ("Word and other text documents", [.. OfficePackages.WordExtensions, .. ConvertedDocuments.WordExtensions]),
        ("Excel and other spreadsheets", [.. Spreadsheets.Extensions, .. LegacySpreadsheets.Extensions]),
        ("PowerPoint and other presentations", [.. OfficePackages.SlideExtensions, .. ConvertedDocuments.SlideExtensions]),
        ("Pictures", [.. ImageFiles.Extensions, .. ConvertedDocuments.PictureExtensions]),
        ("Text, CSV, Markdown and data files", TextFiles.Extensions),
    ];

    public static IEnumerable<string> All => Groups.SelectMany(group => group.Extensions);

    public static string OpenDialogFilter =>
        string.Join("|", Groups.Prepend(("All supported files", All.ToArray()))
            .Select(group => $"{group.Item1}|{string.Join(";", group.Item2.Select(extension => "*" + extension))}"));
}
