using System.Text;
using System.Text.Json;
using PlainViewer.Core;

Console.InputEncoding = Encoding.UTF8; Console.OutputEncoding = new UTF8Encoding(false);
// Parent assigns a Job Object before releasing this handshake.
if (Console.ReadLine() != "START") return 2;
WorkerResponse response;
try
{
    if (args.Length is < 1 or > 3) throw new DocumentException("Choose a file to open.");
    response = new WorkerResponse(TextFiles.Load(args[0], args.ElementAtOrDefault(1) ?? "Auto", args.ElementAtOrDefault(2) ?? "Auto"), null);
}
catch (DocumentException ex) { response = new(null, ex.Message); }
catch (DecoderFallbackException) { response = new(null, "The text encoding could not be read. Choose a different encoding and try again."); }
catch (UnauthorizedAccessException) { response = new(null, "The file cannot be read. Check its permissions or copy it to a local folder."); }
catch (FileNotFoundException) { response = new(null, "The file was moved or deleted. Choose it again from its current location."); }
catch (IOException) { response = new(null, "The file could not be read. It may be locked, moved, or damaged. Close other applications and try again."); }
catch (Exception) { response = new(null, "The file could not be displayed. Try another file or report this problem."); }
Console.Write(JsonSerializer.Serialize(response));
return response.Error is null ? 0 : 1;
