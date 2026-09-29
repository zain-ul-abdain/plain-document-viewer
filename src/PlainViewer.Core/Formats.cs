namespace PlainViewer.Core;

// Every file type the viewer opens, grouped as the Open dialog shows them. The installer's "Open with" list must
// match (a core test checks installer/PlainViewer.iss against All).
public static class Formats
{
    public static readonly (string Name, string[] Extensions)[] Groups =
    [
        ("PDF documents", [".pdf"]),
        ("Word documents", OfficePackages.WordExtensions),
        ("Excel workbooks", Spreadsheets.Extensions),
        ("PowerPoint presentations", OfficePackages.SlideExtensions),
        ("Pictures", ImageFiles.Extensions),
        ("Text, CSV, Markdown and data files", TextFiles.Extensions),
    ];

    public static IEnumerable<string> All => Groups.SelectMany(group => group.Extensions);

    public static string OpenDialogFilter =>
        string.Join("|", Groups.Prepend(("All supported files", All.ToArray()))
            .Select(group => $"{group.Item1}|{string.Join(";", group.Item2.Select(extension => "*" + extension))}"));
}
