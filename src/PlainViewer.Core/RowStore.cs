using System.Buffers.Binary;
using System.Text;
using Microsoft.Win32.SafeHandles;
namespace PlainViewer.Core;

// Rows of text kept on disk, so large CSV files, text files and spreadsheet sheets can be shown without loading them
// into memory. The worker writes a store into its work folder; the app reads only the rows it shows or searches.
// The app treats the store as untrusted (it comes from the low-integrity worker): every offset, count and length is
// checked before use.
//   <name>.rows   rows back to back: field count, then each field as byte length and UTF-8 bytes (7-bit integers)
//   <name>.index  "PVRI", format version, row count, widest row (32-bit each), then row count + 1 offsets (64-bit)
public sealed class RowStoreWriter : IDisposable
{
    private const int HeaderSize = 16;
    private readonly FileStream rows, index;
    private byte[] buffer = new byte[1 << 16];
    private readonly byte[] offset = new byte[8];
    public int Count { get; private set; }
    public int Columns { get; private set; }

    public RowStoreWriter(string folder, string name)
    {
        rows = new FileStream(Path.Combine(folder, name + ".rows"), FileMode.CreateNew, FileAccess.Write, FileShare.None, 1 << 16);
        index = new FileStream(Path.Combine(folder, name + ".index"), FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 1 << 16);
        index.Write(new byte[HeaderSize]);
        WriteOffset(0);
    }

    public void Add(IReadOnlyList<string> fields)
    {
        if (fields.Count > RowStore.MaxColumns) throw new DocumentException($"A row has more than {RowStore.MaxColumns:N0} columns, which this viewer cannot show.");
        int length = 0;
        Put(ref length, (uint)fields.Count);
        foreach (var field in fields)
        {
            int bytes = Encoding.UTF8.GetByteCount(field);
            Reserve(length + 5 + bytes);
            Put(ref length, (uint)bytes);
            length += Encoding.UTF8.GetBytes(field, 0, field.Length, buffer, length);
        }
        if (length > RowStore.MaxRowBytes) throw new DocumentException("A row is larger than the 16 MB this viewer can show.");
        rows.Write(buffer, 0, length);
        Count++; Columns = Math.Max(Columns, fields.Count);
        WriteOffset(rows.Position);
    }

    // Writes the header; a store without it is rejected when opened.
    public void Complete()
    {
        rows.Flush();
        var header = new byte[HeaderSize];
        "PVRI"u8.CopyTo(header);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(4), 1);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(8), Count);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(12), Columns);
        index.Position = 0; index.Write(header); index.Flush();
    }

    public void Dispose() { rows.Dispose(); index.Dispose(); }

    private void WriteOffset(long position) { BinaryPrimitives.WriteInt64LittleEndian(offset, position); index.Write(offset); }
    private void Reserve(int size) { if (size > buffer.Length) Array.Resize(ref buffer, Math.Max(size, buffer.Length * 2)); }
    private void Put(ref int length, uint value)
    {
        Reserve(length + 5);
        while (value >= 0x80) { buffer[length++] = (byte)(value | 0x80); value >>= 7; }
        buffer[length++] = (byte)value;
    }
}

public sealed class RowStore : IDisposable
{
    public const int MaxColumns = 16384;
    public const int MaxRowBytes = 16 * 1024 * 1024;
    private const int HeaderSize = 16;
    private const int MaxReadRows = 100_000;
    private readonly SafeFileHandle rows, index;
    private readonly long rowsLength;
    public int Count { get; }
    public int Columns { get; }

    private RowStore(SafeFileHandle rows, SafeFileHandle index, int count, int columns)
    { this.rows = rows; this.index = index; rowsLength = RandomAccess.GetLength(rows); Count = count; Columns = columns; }

    public static RowStore Open(string folder, string name)
    {
        var rows = File.OpenHandle(Path.Combine(folder, name + ".rows"), FileMode.Open, FileAccess.Read, FileShare.Read);
        try
        {
            var index = File.OpenHandle(Path.Combine(folder, name + ".index"), FileMode.Open, FileAccess.Read, FileShare.Read);
            try
            {
                var header = new byte[HeaderSize];
                if (RandomAccess.Read(index, header, 0) != HeaderSize || !header.AsSpan(0, 4).SequenceEqual("PVRI"u8) || BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(4)) != 1) throw Damaged();
                int count = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(8)), columns = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(12));
                if (count < 0 || columns < 0 || columns > MaxColumns || RandomAccess.GetLength(index) != HeaderSize + (count + 1L) * 8) throw Damaged();
                return new RowStore(rows, index, count, columns);
            }
            catch { index.Dispose(); throw; }
        }
        catch { rows.Dispose(); throw; }
    }

    // Rows start .. start + count - 1. Safe to call from several threads at once.
    public string[][] Read(int start, int count)
    {
        if (start < 0 || count < 0 || count > MaxReadRows || start > Count - count) throw new ArgumentOutOfRangeException(nameof(start));
        var offsetBytes = new byte[(count + 1) * 8];
        if (RandomAccess.Read(index, offsetBytes, HeaderSize + start * 8L) != offsetBytes.Length) throw Damaged();
        var offsets = new long[count + 1];
        for (int i = 0; i <= count; i++)
        {
            offsets[i] = BinaryPrimitives.ReadInt64LittleEndian(offsetBytes.AsSpan(i * 8));
            if (offsets[i] < 0 || offsets[i] > rowsLength || (i > 0 && (offsets[i] < offsets[i - 1] || offsets[i] - offsets[i - 1] > MaxRowBytes))) throw Damaged();
        }
        long span = offsets[count] - offsets[0];
        if (span > int.MaxValue) throw Damaged();
        var data = new byte[span];
        if (RandomAccess.Read(rows, data, offsets[0]) != data.Length) throw Damaged();
        var result = new string[count][];
        for (int i = 0; i < count; i++)
        {
            int position = (int)(offsets[i] - offsets[0]), end = (int)(offsets[i + 1] - offsets[0]);
            int fields = (int)Next(data, ref position, end);
            if (fields > Columns) throw Damaged();
            var row = new string[fields];
            for (int f = 0; f < fields; f++)
            {
                uint length = Next(data, ref position, end);
                if (length > end - position) throw Damaged();
                row[f] = Encoding.UTF8.GetString(data, position, (int)length);
                position += (int)length;
            }
            if (position != end) throw Damaged();
            result[i] = row;
        }
        return result;
    }

    public void Dispose() { rows.Dispose(); index.Dispose(); }

    private static uint Next(byte[] data, ref int position, int end)
    {
        uint value = 0;
        for (int shift = 0; shift < 35; shift += 7)
        {
            if (position >= end) throw Damaged();
            byte b = data[position++];
            value |= (uint)(b & 0x7f) << shift;
            if ((b & 0x80) == 0) return value;
        }
        throw Damaged();
    }

    private static InvalidDataException Damaged() => new("The viewer's temporary copy of this document is damaged. Open the file again.");
}
