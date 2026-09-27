using System.Collections;
using System.IO;
using PlainViewer.Core;
namespace PlainViewer.App;

// A read-only list over a RowStore for the grid. WPF asks only for the rows it shows (row virtualisation), so rows
// are read from disk a page at a time and only the most recently used pages stay in memory.
internal sealed class StoreRows(RowStore store) : IList
{
    private const int PageSize = 256, PagesKept = 64;
    private readonly Dictionary<int, StoreRow[]> pages = [];
    private readonly LinkedList<int> recent = new();

    public int Count => store.Count;
    public object? this[int index] { get => Row(index); set => throw new NotSupportedException(); }

    public StoreRow Row(int index)
    {
        if ((uint)index >= (uint)store.Count) throw new ArgumentOutOfRangeException(nameof(index));
        int page = index / PageSize;
        if (pages.TryGetValue(page, out var rows)) recent.Remove(page);
        else
        {
            int start = page * PageSize;
            var read = store.Read(start, Math.Min(PageSize, store.Count - start));
            rows = new StoreRow[read.Length];
            for (int i = 0; i < read.Length; i++) rows[i] = new StoreRow(this, start + i, read[i]);
            pages[page] = rows;
            if (pages.Count > PagesKept) { pages.Remove(recent.Last!.Value); recent.RemoveLast(); }
        }
        recent.AddFirst(page);
        return rows[index % PageSize];
    }

    public int IndexOf(object? value) => value is StoreRow row && row.Owner == this ? row.Index : -1;
    public bool Contains(object? value) => IndexOf(value) >= 0;
    public IEnumerator GetEnumerator() { for (int i = 0; i < Count; i++) yield return Row(i); }
    public bool IsReadOnly => true;
    public bool IsFixedSize => true;
    public bool IsSynchronized => false;
    public object SyncRoot => this;
    public void CopyTo(Array array, int index) { for (int i = 0; i < Count; i++) array.SetValue(Row(i), index + i); }
    public int Add(object? value) => throw new NotSupportedException();
    public void Clear() => throw new NotSupportedException();
    public void Insert(int index, object? value) => throw new NotSupportedException();
    public void Remove(object? value) => throw new NotSupportedException();
    public void RemoveAt(int index) => throw new NotSupportedException();
}

// One row; the grid's columns bind to the indexer ("[0]", "[1]", ...).
internal sealed class StoreRow(StoreRows owner, int index, string[] cells)
{
    public StoreRows Owner { get; } = owner;
    public int Index { get; } = index;
    public string this[int column] => column < cells.Length ? cells[column] : "";
}

// Finds rows containing a phrase, in the background, a block at a time; reports progress and stops when disposed.
internal sealed class StoreSearch : IDisposable
{
    private const int Block = 4096, MaxHits = 1_000_000;
    private readonly CancellationTokenSource cancel = new();
    private readonly List<int> hits = [];
    public string Query { get; }
    public bool Done { get; private set; }
    public string? Error { get; private set; }
    public double Progress { get; private set; }
    public bool Truncated { get; private set; }

    public StoreSearch(RowStore store, string query, Action<StoreSearch> changed)
    {
        Query = query;
        var token = cancel.Token;
        _ = Task.Run(() =>
        {
            try
            {
                for (int start = 0; start < store.Count; start += Block)
                {
                    token.ThrowIfCancellationRequested();
                    var rows = store.Read(start, Math.Min(Block, store.Count - start));
                    var found = new List<int>();
                    for (int i = 0; i < rows.Length; i++)
                        foreach (var cell in rows[i]) if (cell.Contains(query, StringComparison.OrdinalIgnoreCase)) { found.Add(start + i); break; }
                    lock (hits) { hits.AddRange(found); if (hits.Count >= MaxHits) { Truncated = true; hits.RemoveRange(MaxHits, hits.Count - MaxHits); } }
                    Progress = (double)(start + rows.Length) / store.Count;
                    if (found.Count > 0 || start % (Block * 16) == 0) changed(this);
                    if (Truncated) break;
                }
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex) when (ex is InvalidDataException or IOException) { Error = ex.Message; }
            Progress = 1; Done = true;
            if (!token.IsCancellationRequested) changed(this);
        }, token);
    }

    public int Count { get { lock (hits) return hits.Count; } }
    public int Hit(int index) { lock (hits) return hits[index]; }
    public void Dispose() => cancel.Cancel();
}
