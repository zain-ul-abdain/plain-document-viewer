using System.Text;
namespace PlainViewer.Core;

public static class Csv
{
    public static char DetectDelimiter(string sample)
    {
        var counts = new Dictionary<char, int> { [','] = 0, [';'] = 0, ['\t'] = 0 }; bool quoted = false;
        for (int i = 0; i < sample.Length; i++)
        {
            char c = sample[i];
            if (c == '"') { if (quoted && i + 1 < sample.Length && sample[i + 1] == '"') i++; else quoted = !quoted; }
            else if (!quoted && (c == '\r' || c == '\n')) break;
            else if (!quoted && counts.ContainsKey(c)) counts[c]++;
        }
        return counts.OrderByDescending(pair => pair.Value).First().Key;
    }
    public static IEnumerable<string[]> Read(TextReader reader, char delimiter)
    {
        var row = new List<string>(); var field = new StringBuilder(); bool quoted = false, closed = false, touched = false; int rowChars = 0;
        while (true)
        {
            int value = reader.Read();
            if (value == -1)
            {
                if (quoted) throw new DocumentException("The CSV contains an unfinished quoted field. Repair the file and try again.");
                if (touched || row.Count > 0 || field.Length > 0) { row.Add(field.ToString()); yield return row.ToArray(); }
                yield break;
            }
            char c = (char)value; touched = true;
            if (++rowChars > 1024 * 1024) throw new DocumentException("A CSV row exceeds the 1 MB preview limit.");
            if (quoted)
            {
                if (c == '"') { if (reader.Peek() == '"') { reader.Read(); field.Append('"'); } else { quoted = false; closed = true; } }
                else field.Append(c);
                continue;
            }
            if (c == delimiter)
            {
                row.Add(field.ToString()); field.Clear(); closed = false;
                if (row.Count >= 512) throw new DocumentException("This CSV exceeds the 512-column preview limit.");
            }
            else if (c is '\r' or '\n')
            {
                if (c == '\r' && reader.Peek() == '\n') reader.Read();
                row.Add(field.ToString()); yield return row.ToArray(); row.Clear(); field.Clear(); closed = false; touched = false; rowChars = 0;
            }
            else if (c == '"' && field.Length == 0 && !closed) quoted = true;
            else if (closed || c == '"') throw new DocumentException("The CSV contains misplaced quotation marks. Check its delimiter or repair the file.");
            else field.Append(c);
        }
    }
}
