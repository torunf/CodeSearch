using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace CodeSearch.Core;

/// <summary>Growable primitive buffer (avoids List&lt;T&gt; overhead for tens of millions of items).</summary>
public sealed class Buf<T> where T : unmanaged
{
    public T[] Items;
    public int Count;
    public Buf(int cap = 1024) => Items = new T[cap];

    public void Add(T v)
    {
        if (Count == Items.Length) Grow(Count + 1);
        Items[Count++] = v;
    }

    public void AddRange(ReadOnlySpan<T> s)
    {
        if (Count + s.Length > Items.Length) Grow(Count + s.Length);
        s.CopyTo(Items.AsSpan(Count));
        Count += s.Length;
    }

    void Grow(int min)
    {
        long n = Math.Max((long)Items.Length * 2, min);
        if (n > Array.MaxLength) n = Array.MaxLength;
        Array.Resize(ref Items, (int)n);
    }

    public T[] ToArray() => Items.AsSpan(0, Count).ToArray();
}

public sealed class IndexBuilder
{
    public readonly Buf<char> Names = new(1 << 20);
    public readonly Buf<int> Starts = new(1 << 16);
    public readonly Buf<int> Parents = new(1 << 16);
    public readonly Buf<byte> Flags = new(1 << 16);

    public IndexBuilder() => Starts.Add(0);

    public int Count => Parents.Count;

    public int Add(ReadOnlySpan<char> name, int parent, byte flags)
    {
        Names.AddRange(name);
        Starts.Add(Names.Count);
        Parents.Add(parent);
        Flags.Add(flags);
        return Parents.Count - 1;
    }

    public FileIndex Build() => new(Names.ToArray(), Starts.ToArray(), Parents.ToArray(), Flags.ToArray());
}

/// <summary>
/// In-memory file/folder name index (Everything-style). All names are concatenated into one char blob,
/// plus a lowercase copy used for SIMD substring search.
/// </summary>
public sealed class FileIndex
{
    public const byte FlagDir = 1;

    public readonly char[] Names;
    public readonly char[] Lower;
    public readonly int[] Starts;   // Count + 1
    public readonly int[] Parents;  // -1 = drive root
    public readonly byte[] Flags;
    public int Count => Parents.Length;
    public int DirCount { get; }

    public FileIndex(char[] names, int[] starts, int[] parents, byte[] flags)
    {
        Names = names; Starts = starts; Parents = parents; Flags = flags;
        Lower = new char[names.Length];
        // Chunked parallel lowercase (char -> char, length preserving)
        const int chunk = 1 << 20;
        Parallel.For(0, (names.Length + chunk - 1) / chunk, i =>
        {
            int s = i * chunk, len = Math.Min(chunk, names.Length - s);
            MemoryExtensions.ToLowerInvariant(names.AsSpan(s, len), Lower.AsSpan(s, len));
        });
        int d = 0;
        foreach (var f in flags) if ((f & FlagDir) != 0) d++;
        DirCount = d;
    }

    public static FileIndex Empty() => new(Array.Empty<char>(), new[] { 0 }, Array.Empty<int>(), Array.Empty<byte>());

    public ReadOnlySpan<char> Name(int i) => Names.AsSpan(Starts[i], Starts[i + 1] - Starts[i]);
    public ReadOnlySpan<char> LowerName(int i) => Lower.AsSpan(Starts[i], Starts[i + 1] - Starts[i]);
    public bool IsDir(int i) => (Flags[i] & FlagDir) != 0;

    public string FullPath(int i)
    {
        Span<int> chain = stackalloc int[256];
        int n = 0, len = 0;
        for (int c = i; c >= 0 && n < chain.Length; c = Parents[c])
        {
            chain[n++] = c;
            len += Starts[c + 1] - Starts[c] + 1;
        }
        return string.Create(Math.Max(len - 1, 0), (this, chain.Slice(0, n).ToArray()), static (dst, st) =>
        {
            var (idx, ch) = st;
            int p = 0;
            for (int k = ch.Length - 1; k >= 0; k--)
            {
                var nm = idx.Name(ch[k]);
                nm.CopyTo(dst.Slice(p));
                p += nm.Length;
                if (k > 0) dst[p++] = '\\';
            }
        });
    }

    public string FolderPath(int i)
    {
        int p = Parents[i];
        if (p < 0) return "";
        var s = FullPath(p);
        return Parents[p] < 0 ? s + "\\" : s;
    }

    /// <summary>Entry whose name range contains blob position <paramref name="pos"/>.</summary>
    public int EntryAt(int pos)
    {
        int idx = Array.BinarySearch(Starts, pos);
        if (idx >= 0)
        {
            // pos is the start of entry idx (skip zero-length names, which cannot match anyway)
            while (idx + 1 < Starts.Length && Starts[idx + 1] == pos) idx++;
            return Math.Min(idx, Count - 1);
        }
        return ~idx - 1;
    }

    // ---------------- persistence ----------------
    const int Magic = 0x58444E49; // "INDX"
    const int Version = 1;

    public void Save(string path)
    {
        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20);
        using var w = new BinaryWriter(fs);
        w.Write(Magic); w.Write(Version);
        w.Write(Names.Length); w.Write(Count);
        fs.Write(MemoryMarshal.AsBytes(Names.AsSpan()));
        fs.Write(MemoryMarshal.AsBytes(Starts.AsSpan()));
        fs.Write(MemoryMarshal.AsBytes(Parents.AsSpan()));
        fs.Write(Flags);
    }

    public static FileIndex Load(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20);
        using var r = new BinaryReader(fs);
        if (r.ReadInt32() != Magic || r.ReadInt32() != Version) throw new InvalidDataException();
        int nameLen = r.ReadInt32(), count = r.ReadInt32();
        var names = new char[nameLen];
        var starts = new int[count + 1];
        var parents = new int[count];
        var flags = new byte[count];
        fs.ReadExactly(MemoryMarshal.AsBytes(names.AsSpan()));
        fs.ReadExactly(MemoryMarshal.AsBytes(starts.AsSpan()));
        fs.ReadExactly(MemoryMarshal.AsBytes(parents.AsSpan()));
        fs.ReadExactly(flags);
        return new FileIndex(names, starts, parents, flags);
    }
}
