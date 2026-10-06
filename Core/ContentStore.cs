using System.Collections.Concurrent;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Text;
using System.Text.RegularExpressions;

namespace CodeSearch.Core;

public sealed class ContentHit
{
    public int FileId;
    public int Line;
    public string Text;
}

public sealed class ContentQuery
{
    public string Text = "";
    public bool CaseSensitive;
    public bool WholeWord;
    public bool Regex;
    public string FileFilter = "";
    public int MaxHits = 50_000;
}

/// <summary>Path filter: "*.cs;.js" → extension OR-set, "foo" → path must contain, "!foo" → must not contain.</summary>
public sealed class PathFilter
{
    readonly HashSet<string> _exts = new(StringComparer.OrdinalIgnoreCase);
    readonly List<string> _must = new(), _not = new();
    public bool IsEmpty => _exts.Count == 0 && _must.Count == 0 && _not.Count == 0;

    public PathFilter(string spec)
    {
        foreach (var raw in (spec ?? "").Split(new[] { ';', ' ', ',' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var t = raw.Trim().Replace('/', '\\');
            if (t.StartsWith("!")) { if (t.Length > 1) _not.Add(t[1..]); }
            else if (t.StartsWith("*.")) _exts.Add(t[2..]);
            else if (t.StartsWith(".") && !t.Contains('\\')) _exts.Add(t[1..]);
            else _must.Add(t);
        }
    }

    public bool Match(string path)
    {
        if (_exts.Count > 0)
        {
            var ext = Path.GetExtension(path.AsSpan());
            if (ext.Length < 2 || !_exts.Contains(ext[1..].ToString())) return false;
        }
        foreach (var m in _must) if (path.IndexOf(m, StringComparison.OrdinalIgnoreCase) < 0) return false;
        foreach (var m in _not) if (path.IndexOf(m, StringComparison.OrdinalIgnoreCase) >= 0) return false;
        return true;
    }
}

/// <summary>
/// Content index: every indexed file is stored twice in large memory-mapped chunk files —
/// original bytes (for display) and ASCII-lowercased bytes (for case-insensitive SIMD search).
/// Lowercasing ASCII keeps byte offsets identical, so a hit in the lower blob maps 1:1 to the original.
/// </summary>
public sealed unsafe class ContentStore : IDisposable
{
    const long ChunkLimit = 1L << 30; // 1 GB per chunk (Span length must fit int)
    const int Magic = 0x544E4F43;   // "CONT"
    const int Version = 1;

    public string[] Paths = Array.Empty<string>();
    public int[] Chunk = Array.Empty<int>();
    public long[] Offset = Array.Empty<long>();
    public int[] Length = Array.Empty<int>();
    public long TotalBytes;
    public int Count => Paths.Length;

    readonly List<Mapped> _low = new(), _org = new();

    sealed class Mapped : IDisposable
    {
        readonly MemoryMappedFile _mmf;
        readonly MemoryMappedViewAccessor _view;
        public byte* Ptr;
        public Mapped(string path)
        {
            if (new FileInfo(path).Length == 0) return;
            var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
            _mmf = MemoryMappedFile.CreateFromFile(fs, null, 0, MemoryMappedFileAccess.Read, HandleInheritability.None, false);
            _view = _mmf.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
            byte* p = null;
            _view.SafeMemoryMappedViewHandle.AcquirePointer(ref p);
            Ptr = p + _view.PointerOffset;
        }
        public void Dispose()
        {
            if (_view != null) { _view.SafeMemoryMappedViewHandle.ReleasePointer(); _view.Dispose(); }
            _mmf?.Dispose();
        }
    }

    public ReadOnlySpan<byte> Low(int f) => Length[f] == 0 ? default : new(_low[Chunk[f]].Ptr + Offset[f], Length[f]);
    public ReadOnlySpan<byte> Org(int f) => Length[f] == 0 ? default : new(_org[Chunk[f]].Ptr + Offset[f], Length[f]);

    public string ReadText(int f) => Encoding.UTF8.GetString(Org(f));

    public static ContentStore Empty() => new();

    public static ContentStore Open(string dir)
    {
        var cs = new ContentStore();
        using (var fs = new FileStream(Path.Combine(dir, "content.meta"), FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20))
        using (var r = new BinaryReader(fs, Encoding.UTF8))
        {
            if (r.ReadInt32() != Magic || r.ReadInt32() != Version) throw new InvalidDataException();
            int chunks = r.ReadInt32();
            int n = r.ReadInt32();
            cs.Paths = new string[n]; cs.Chunk = new int[n]; cs.Offset = new long[n]; cs.Length = new int[n];
            for (int i = 0; i < n; i++)
            {
                cs.Paths[i] = r.ReadString();
                cs.Chunk[i] = r.ReadInt32();
                cs.Offset[i] = r.ReadInt64();
                cs.Length[i] = r.ReadInt32();
                cs.TotalBytes += cs.Length[i];
            }
            for (int c = 0; c < chunks; c++)
            {
                cs._lowFiles.Add(Path.Combine(dir, $"chunk{c}.low"));
                cs._low.Add(new Mapped(cs._lowFiles[^1]));
                cs._org.Add(new Mapped(Path.Combine(dir, $"chunk{c}.org")));
            }
        }
        return cs;
    }

    readonly List<string> _lowFiles = new();

    /// <summary>
    /// Reads the lowercase chunks once in the background so the OS file cache holds them;
    /// the first search then hits soft page faults instead of disk reads.
    /// </summary>
    public void WarmUp(CancellationToken ct)
    {
        var t = new Thread(() =>
        {
            var buf = new byte[1 << 20];
            foreach (var f in _lowFiles)
            {
                try
                {
                    using var fs = new FileStream(f, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1, FileOptions.SequentialScan);
                    while (!ct.IsCancellationRequested && fs.Read(buf, 0, buf.Length) > 0) { }
                }
                catch { }
            }
        }) { IsBackground = true, Priority = ThreadPriority.BelowNormal };
        t.Start();
    }

    public void Dispose()
    {
        foreach (var m in _low) m.Dispose();
        foreach (var m in _org) m.Dispose();
        _low.Clear(); _org.Clear();
    }

    // ---------------------------------------------------------------- build

    public sealed class BuildStats { public int Indexed, Skipped, Processed, Total; public long Bytes; }

    public static void Build(string dir, IReadOnlyList<string> files, int maxBytes, CancellationToken ct, Action<BuildStats> progress)
    {
        var stats = new BuildStats { Total = files.Count };
        var queue = new BlockingCollection<(string path, byte[] org, byte[] low)>(512);
        // If the writer dies (e.g. disk full) the readers must stop too, or they block forever on the full queue.
        using var abort = CancellationTokenSource.CreateLinkedTokenSource(ct);

        var writer = Task.Run(() =>
        {
          try
          {
            var paths = new List<string>(); var chunkL = new List<int>(); var offL = new List<long>(); var lenL = new List<int>();
            int chunk = 0; long off = 0;
            FileStream low = null, org = null;
            void OpenChunk()
            {
                low?.Dispose(); org?.Dispose();
                low = new FileStream(Path.Combine(dir, $"chunk{chunk}.low"), FileMode.Create, FileAccess.Write, FileShare.None, 1 << 22);
                org = new FileStream(Path.Combine(dir, $"chunk{chunk}.org"), FileMode.Create, FileAccess.Write, FileShare.None, 1 << 22);
            }
            OpenChunk();
            try
            {
                foreach (var (path, o, l) in queue.GetConsumingEnumerable())
                {
                    if (off + o.Length > ChunkLimit && off > 0) { chunk++; off = 0; OpenChunk(); }
                    org.Write(o); low.Write(l);
                    paths.Add(path); chunkL.Add(chunk); offL.Add(off); lenL.Add(o.Length);
                    off += o.Length;
                }
            }
            finally { low?.Dispose(); org?.Dispose(); }

            using var fs = new FileStream(Path.Combine(dir, "content.meta"), FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20);
            using var w = new BinaryWriter(fs, Encoding.UTF8);
            w.Write(Magic); w.Write(Version); w.Write(chunk + 1); w.Write(paths.Count);
            for (int i = 0; i < paths.Count; i++) { w.Write(paths[i]); w.Write(chunkL[i]); w.Write(offL[i]); w.Write(lenL[i]); }
          }
          catch { abort.Cancel(); throw; }
        });

        try
        {
            var po = new ParallelOptions { MaxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount * 2, 4, 32), CancellationToken = abort.Token };
            Parallel.ForEach(System.Collections.Concurrent.Partitioner.Create(0, files.Count, 64), po, range =>
            {
                for (int i = range.Item1; i < range.Item2; i++)
                {
                    if (abort.IsCancellationRequested) return;
                    var data = TryRead(files[i], maxBytes);
                    if (data != null)
                    {
                        var low = new byte[data.Length];
                        AsciiLower(data, low);
                        queue.Add((files[i], data, low), abort.Token);
                        Interlocked.Increment(ref stats.Indexed);
                        Interlocked.Add(ref stats.Bytes, data.Length);
                    }
                    else Interlocked.Increment(ref stats.Skipped);
                    if ((Interlocked.Increment(ref stats.Processed) & 0xFF) == 0) progress(stats);
                }
            });
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { /* writer failed: rethrown below */ }
        finally { queue.CompleteAdding(); }
        try { writer.Wait(); }
        catch (AggregateException ae) { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ae.InnerException ?? ae).Throw(); }
        ct.ThrowIfCancellationRequested();
        progress(stats);
    }

    static byte[] TryRead(string path, int maxBytes)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1, FileOptions.SequentialScan);
            long len = fs.Length;
            if (len > maxBytes || len == 0) return null;
            var buf = new byte[len];
            fs.ReadExactly(buf);
            return Normalize(buf);
        }
        catch { return null; }
    }

    /// <summary>Converts UTF-16 to UTF-8, strips BOM, rejects binary files.</summary>
    static byte[] Normalize(byte[] b)
    {
        if (b.Length >= 2 && b[0] == 0xFF && b[1] == 0xFE) return Encoding.UTF8.GetBytes(Encoding.Unicode.GetString(b, 2, b.Length - 2));
        if (b.Length >= 2 && b[0] == 0xFE && b[1] == 0xFF) return Encoding.UTF8.GetBytes(Encoding.BigEndianUnicode.GetString(b, 2, b.Length - 2));
        if (b.Length >= 3 && b[0] == 0xEF && b[1] == 0xBB && b[2] == 0xBF) b = b[3..];
        if (b.AsSpan(0, Math.Min(b.Length, 8192)).IndexOf((byte)0) >= 0) return null; // binary
        return b;
    }

    static void AsciiLower(ReadOnlySpan<byte> src, Span<byte> dst)
    {
        for (int i = 0; i < src.Length; i++)
        {
            byte c = src[i];
            dst[i] = (uint)(c - 'A') <= 'Z' - 'A' ? (byte)(c | 0x20) : c;
        }
    }

    // ---------------------------------------------------------------- search

    public List<ContentHit> Search(ContentQuery q, CancellationToken ct, out bool truncated, out int fileHits)
    {
        truncated = false; fileHits = 0;
        int n = Count;
        if (n == 0 || string.IsNullOrEmpty(q.Text)) return new List<ContentHit>();

        var filter = new PathFilter(q.FileFilter);
        var perFile = new List<ContentHit>[n];
        int hits = 0;
        bool stop = false;

        Regex rx = null;
        byte[] needleLow = null, needleOrg = null;
        if (q.Regex)
        {
            var opt = RegexOptions.Multiline | RegexOptions.CultureInvariant;
            if (!q.CaseSensitive) opt |= RegexOptions.IgnoreCase;
            var pat = q.WholeWord ? $@"\b(?:{q.Text})\b" : q.Text;
            // Linear-time engine first, so a hostile pattern cannot hang the search; fall back for
            // constructs it does not support (backreferences, lookarounds, ...).
            try { rx = new Regex(pat, opt | RegexOptions.NonBacktracking, TimeSpan.FromSeconds(2)); }
            catch (NotSupportedException) { rx = new Regex(pat, opt | RegexOptions.Compiled, TimeSpan.FromSeconds(2)); }
        }
        else
        {
            needleOrg = Encoding.UTF8.GetBytes(q.Text);
            needleLow = new byte[needleOrg.Length];
            AsciiLower(needleOrg, needleLow);
        }

        var po = new ParallelOptions { CancellationToken = ct, MaxDegreeOfParallelism = Environment.ProcessorCount };
        Parallel.ForEach(System.Collections.Concurrent.Partitioner.Create(0, n, Math.Max(16, n / (Environment.ProcessorCount * 8) + 1)), po, range =>
        {
            for (int f = range.Item1; f < range.Item2; f++)
            {
                if (Volatile.Read(ref stop) || ct.IsCancellationRequested) return;
                if (!filter.IsEmpty && !filter.Match(Paths[f])) continue;
                List<ContentHit> list = rx != null ? SearchRegex(f, rx) : SearchLiteral(f, needleLow, needleOrg, q.CaseSensitive, q.WholeWord);
                if (list == null) continue;
                perFile[f] = list;
                if (Interlocked.Add(ref hits, list.Count) >= q.MaxHits) Volatile.Write(ref stop, true);
            }
        });
        ct.ThrowIfCancellationRequested();

        var result = new List<ContentHit>(Math.Min(hits, q.MaxHits));
        foreach (var l in perFile)
        {
            if (l == null) continue;
            fileHits++;
            foreach (var h in l)
            {
                if (result.Count >= q.MaxHits) { truncated = true; return result; }
                result.Add(h);
            }
        }
        truncated = stop;
        return result;
    }

    static bool IsWordByte(byte b) => (uint)(b - 'a') <= 25 || (uint)(b - 'A') <= 25 || (uint)(b - '0') <= 9 || b == '_' || b >= 0x80;

    List<ContentHit> SearchLiteral(int f, byte[] needleLow, byte[] needleOrg, bool cs, bool whole)
    {
        var low = Low(f);
        int pos = low.IndexOf(needleLow);
        if (pos < 0) return null;
        var org = Org(f);
        List<ContentHit> list = null;
        int line = 1, counted = 0;
        while (pos >= 0)
        {
            bool ok = true;
            if (cs && !org.Slice(pos, needleOrg.Length).SequenceEqual(needleOrg)) ok = false;
            if (ok && whole)
            {
                if (pos > 0 && IsWordByte(low[pos - 1]) && IsWordByte(needleLow[0])) ok = false;
                int end = pos + needleLow.Length;
                if (end < low.Length && IsWordByte(low[end]) && IsWordByte(needleLow[^1])) ok = false;
            }
            int next;
            if (ok)
            {
                line += low.Slice(counted, pos - counted).Count((byte)'\n');
                int ls = low.Slice(0, pos).LastIndexOf((byte)'\n') + 1;
                int le = low.Slice(pos).IndexOf((byte)'\n');
                le = le < 0 ? low.Length : pos + le;
                counted = pos;
                (list ??= new List<ContentHit>()).Add(new ContentHit { FileId = f, Line = line, Text = LineText(org.Slice(ls, le - ls)) });
                next = le; // one hit per line
                if (list.Count >= 5000) break;
            }
            else next = pos + 1;
            if (next >= low.Length) break;
            int rel = low.Slice(next).IndexOf(needleLow);
            pos = rel < 0 ? -1 : next + rel;
        }
        return list;
    }

    List<ContentHit> SearchRegex(int f, Regex rx)
    {
        string text;
        try { text = ReadText(f); } catch { return null; }
        List<ContentHit> list = null;
        int line = 1, counted = 0, lastLineEnd = -1;
        try
        {
            for (var m = rx.Match(text); m.Success; m = m.NextMatch())
            {
                int pos = m.Index;
                if (pos < lastLineEnd) continue;
                line += text.AsSpan(counted, pos - counted).Count('\n');
                counted = pos;
                int ls = pos == 0 ? 0 : text.LastIndexOf('\n', pos - 1) + 1;
                int le = text.IndexOf('\n', pos);
                if (le < 0) le = text.Length;
                lastLineEnd = le;
                (list ??= new List<ContentHit>()).Add(new ContentHit { FileId = f, Line = line, Text = TrimLine(text.AsSpan(ls, le - ls)) });
                if (list.Count >= 5000) break;
                if (m.Length == 0 && le >= text.Length) break;
            }
        }
        catch (RegexMatchTimeoutException) { }
        return list;
    }

    static string LineText(ReadOnlySpan<byte> line)
    {
        if (line.Length > 2000) line = line.Slice(0, 2000);
        return TrimLine(Encoding.UTF8.GetString(line));
    }

    static string TrimLine(ReadOnlySpan<char> s)
    {
        s = s.TrimEnd('\r').Trim();
        if (s.Length > 400) s = s.Slice(0, 400);
        return s.ToString().Replace('\t', ' ');
    }
}
