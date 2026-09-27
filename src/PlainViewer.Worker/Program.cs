using System.Text;
using System.Text.Json;
using PlainViewer.Core;

Console.InputEncoding = Encoding.UTF8; Console.OutputEncoding = new UTF8Encoding(false);
// Parent assigns a Job Object before releasing this handshake.
if (Console.ReadLine() != "START") return 2;
WorkerResponse response;
try
{
    // Before touching the document: from here on this process cannot write the user's files or change other programs.
    // Output for Word/PowerPoint preparation goes under LowIntegrity.Root, which allows low-integrity writes.
    try { LowIntegrity.LowerCurrentProcess(); }
    catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
    { throw new DocumentException("The document worker could not start in its restricted mode, so the file was not opened."); }
    if (args.Length is < 1 or > 4) throw new DocumentException("Choose a file to open.");
    string extension = Path.GetExtension(args[0]).ToLowerInvariant();
    // "<file> --prepare-office <output>": validate a Word/PowerPoint package and write a sanitised copy for conversion.
    // "<file> <encoding> <delimiter> <work folder>": large rows go to a RowStore in the work folder.
    var document = args.ElementAtOrDefault(1) == "--prepare-office" ? OfficePackages.Prepare(args[0], args[2])
        : extension is ".xlsx" or ".xlsm" or ".xltx" or ".xltm" or ".xlsb" ? Spreadsheets.Load(args[0])
        : TextFiles.Load(args[0], args.ElementAtOrDefault(1) ?? "Auto", args.ElementAtOrDefault(2) ?? "Auto", args.ElementAtOrDefault(3));
    response = new WorkerResponse(document, null);
}
catch (DocumentException ex) { response = new(null, ex.Message); }
catch (InvalidDataException) { response = new(null, "This file is damaged or incomplete, so it cannot be shown. Try another copy of the file."); }
catch (System.Xml.XmlException) { response = new(null, "This file is damaged or incomplete, so it cannot be shown. Try another copy of the file."); }
catch (OutOfMemoryException) { response = new(null, "This file needs more memory than the viewer allows for one document."); }
catch (DecoderFallbackException) { response = new(null, "The text encoding could not be read. Choose a different encoding and try again."); }
catch (UnauthorizedAccessException) { response = new(null, "The file cannot be read. Check its permissions or copy it to a local folder."); }
catch (FileNotFoundException) { response = new(null, "The file was moved or deleted. Choose it again from its current location."); }
catch (IOException) { response = new(null, "The file could not be read. It may be locked, moved, or damaged. Close other applications and try again."); }
catch (Exception) { response = new(null, "The file could not be displayed. Try another file or report this problem."); }
Console.Write(JsonSerializer.Serialize(response));
return response.Error is null ? 0 : 1;
