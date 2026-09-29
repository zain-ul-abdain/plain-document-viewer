using System.Buffers.Binary;
using System.Text;
namespace PlainViewer.Core;

// A minimal reader for the Compound File Binary format (older .doc, .xls and .ppt files) that can also change bytes
// of a stream in place. It only follows the file's own sector chains, with every index and chain length checked, so
// a damaged or hostile file ends in a DocumentException rather than a loop or an out-of-range read. Nothing is added
// or removed: a changed stream keeps its length, so the file's structure is untouched.
public sealed class CompoundFile
{
    private const uint EndOfChain = 0xFFFFFFFE, FreeSector = 0xFFFFFFFF;
    private readonly byte[] data;
    private readonly int sectorShift;
    private readonly uint[] fat;
    private readonly uint[] miniFat;
    private readonly List<(long Offset, int Length)> miniStream;
    public IReadOnlyList<Entry> Entries { get; }

    public sealed record Entry(string Name, int Type, uint Start, long Size, int Index);   // Type: 1 storage, 2 stream, 5 root

    public static bool IsCompoundFile(ReadOnlySpan<byte> bytes) =>
        bytes.StartsWith(new byte[] { 0xd0, 0xcf, 0x11, 0xe0, 0xa1, 0xb1, 0x1a, 0xe1 });

    public CompoundFile(byte[] bytes, string kind)
    {
        data = bytes;
        DocumentException Damaged() => new($"This {kind} is damaged or incomplete, so it cannot be shown. Try another copy of the file.");
        if (data.Length < 512 || !IsCompoundFile(data)) throw Damaged();
        sectorShift = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(0x1e));
        if (sectorShift is not (9 or 12) || BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(0x20)) != 6) throw Damaged();
        long sectors = (data.Length >> sectorShift) + 1;

        // FAT: the first 109 FAT sector numbers are in the header, the rest in a chain of DIFAT sectors.
        uint fatSectors = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(0x2c));
        if (fatSectors > sectors) throw Damaged();
        var fatList = new List<uint>();
        for (int i = 0; i < 109 && fatList.Count < fatSectors; i++) fatList.Add(BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(0x4c + i * 4)));
        uint difat = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(0x44));
        int perSector = (1 << sectorShift) / 4;
        for (int guard = 0; fatList.Count < fatSectors; guard++)
        {
            if (guard > sectors || difat >= sectors) throw Damaged();
            long at = Offset(difat);
            for (int i = 0; i < perSector - 1 && fatList.Count < fatSectors; i++) fatList.Add(BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan((int)(at + i * 4))));
            difat = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan((int)(at + (perSector - 1) * 4)));
        }
        fat = new uint[fatList.Count * perSector];
        for (int s = 0; s < fatList.Count; s++)
        {
            if (fatList[s] >= sectors) throw Damaged();
            long at = Offset(fatList[s]);
            for (int i = 0; i < perSector; i++) fat[s * perSector + i] = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan((int)(at + i * 4)));
        }

        // Directory entries (128 bytes each) along the directory chain.
        var entries = new List<Entry>();
        var directory = Chain(BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(0x30)), long.MaxValue, Damaged);
        int index = 0;
        foreach (var (offset, length) in directory)
            for (int e = 0; e + 128 <= length; e += 128, index++)
            {
                var raw = data.AsSpan((int)(offset + e), 128);
                int nameBytes = BinaryPrimitives.ReadUInt16LittleEndian(raw[0x40..]);
                int type = raw[0x42];
                if (type == 0) continue;
                string name = nameBytes is >= 2 and <= 64 ? Encoding.Unicode.GetString(raw[..(nameBytes - 2)]) : "";
                long size = sectorShift == 9 ? BinaryPrimitives.ReadUInt32LittleEndian(raw[0x78..]) : (long)BinaryPrimitives.ReadUInt64LittleEndian(raw[0x78..]);
                if (size < 0 || size > data.Length * 64L) throw Damaged();
                entries.Add(new Entry(name, type, BinaryPrimitives.ReadUInt32LittleEndian(raw[0x74..]), size, index));
            }
        Entries = entries;
        var root = entries.FirstOrDefault(e => e.Type == 5) ?? throw Damaged();

        // Streams under 4,096 bytes live in the mini stream, in 64-byte mini sectors listed by the mini FAT.
        miniStream = root.Size > 0 ? Chain(root.Start, root.Size, Damaged) : [];
        var miniFatChain = Chain(BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(0x3c)), long.MaxValue, Damaged, allowEmpty: true);
        miniFat = new uint[miniFatChain.Sum(c => c.Length) / 4];
        int m = 0;
        foreach (var (offset, length) in miniFatChain)
            for (int i = 0; i + 4 <= length; i += 4) miniFat[m++] = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan((int)(offset + i)));
    }

    private long Offset(uint sector) => ((long)sector + 1) << sectorShift;

    // The file ranges holding a chain of regular sectors, cut to `size` bytes.
    private List<(long Offset, int Length)> Chain(uint start, long size, Func<DocumentException> damaged, bool allowEmpty = false)
    {
        var result = new List<(long Offset, int Length)>();
        if (start == EndOfChain || start == FreeSector) return allowEmpty || size == 0 ? result : throw damaged();
        int sectorSize = 1 << sectorShift;
        long remaining = size;
        for (uint sector = start, guard = 0; sector != EndOfChain && remaining > 0; guard++)
        {
            if (sector >= fat.Length || guard > fat.Length) throw damaged();
            long offset = Offset(sector);
            if (offset + sectorSize > data.Length) throw damaged();
            int take = (int)Math.Min(sectorSize, remaining);
            result.Add((offset, take)); remaining -= take;
            sector = fat[sector];
        }
        if (remaining > 0 && size != long.MaxValue) throw damaged();
        return result;
    }

    // The file ranges of a stream, in order.
    public List<(long Offset, int Length)> Ranges(Entry entry, string kind)
    {
        DocumentException Damaged() => new($"This {kind} is damaged or incomplete, so it cannot be shown. Try another copy of the file.");
        if (entry.Size == 0) return [];
        if (entry.Size >= 4096) return Chain(entry.Start, entry.Size, Damaged);
        var result = new List<(long, int)>();
        long remaining = entry.Size;
        for (uint mini = entry.Start, guard = 0; remaining > 0; guard++)
        {
            if (mini >= miniFat.Length || guard > miniFat.Length) throw Damaged();
            long position = (long)mini * 64;       // within the mini stream
            int take = (int)Math.Min(64, remaining);
            foreach (var (offset, length) in miniStream)
            {
                if (position < length) { if (position + take > length) throw Damaged(); result.Add((offset + position, take)); break; }
                position -= length;
            }
            if (result.Count == 0 || result.Sum(r => (long)r.Item2) != entry.Size - remaining + take) throw Damaged();
            remaining -= take;
            mini = miniFat[mini];
        }
        return result;
    }

    public byte[] Read(Entry entry, string kind)
    {
        var bytes = new byte[entry.Size];
        int at = 0;
        foreach (var (offset, length) in Ranges(entry, kind)) { data.AsSpan((int)offset, length).CopyTo(bytes.AsSpan(at)); at += length; }
        return bytes;
    }

    // Writes a stream back in place; its length never changes.
    public void Write(Entry entry, byte[] bytes, string kind)
    {
        if (bytes.Length != entry.Size) throw new ArgumentException("A stream must keep its length.");
        int at = 0;
        foreach (var (offset, length) in Ranges(entry, kind)) { bytes.AsSpan(at, length).CopyTo(data.AsSpan((int)offset)); at += length; }
    }

    public Entry? Find(string name) => Entries.FirstOrDefault(e => e.Type == 2 && e.Name == name);
    public bool Has(string name) => Entries.Any(e => e.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
}
