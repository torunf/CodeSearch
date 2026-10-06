using System.Collections.Concurrent;
using System.ComponentModel;
using System.IO;
using System.IO.Enumeration;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Win32.SafeHandles;

namespace CodeSearch.Core;

public static class Elevation
{
    public static bool IsAdmin()
    {
        using var id = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
    }
}

/// <summary>
/// Reads the NTFS Master File Table via FSCTL_ENUM_USN_DATA (same technique as Everything).
/// Needs administrator rights; returns false so the caller can fall back to directory walking.
/// </summary>
public static unsafe class MftScanner
{
    const uint GENERIC_READ = 0x80000000;
    const uint FILE_SHARE_READ = 1, FILE_SHARE_WRITE = 2, FILE_SHARE_DELETE = 4;
    const uint OPEN_EXISTING = 3;
    const uint FSCTL_ENUM_USN_DATA = 0x000900B3;
    const int ERROR_HANDLE_EOF = 38;
    const uint FILE_ATTRIBUTE_DIRECTORY = 0x10;
    const long RootIndex = 5;
    const long IndexMask = 0x0000FFFFFFFFFFFF;

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern SafeFileHandle CreateFileW(string name, uint access, uint share, IntPtr sec, uint disp, uint flags, IntPtr tmpl);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool DeviceIoControl(SafeFileHandle h, uint code, void* inBuf, int inSize, void* outBuf, int outSize, out int returned, IntPtr ov);

    [StructLayout(LayoutKind.Sequential)]
    struct MFT_ENUM_DATA_V0 { public ulong StartFileReferenceNumber; public long LowUsn; public long HighUsn; }

    public static bool TryScan(string drive, IndexBuilder b, CancellationToken ct, Action<long> progress)
    {
        try
        {
            var di = new DriveInfo(drive);
            if (!string.Equals(di.DriveFormat, "NTFS", StringComparison.OrdinalIgnoreCase)) return false;
        }
        catch { return false; }

        string letter = drive.Substring(0, 2); // "C:"
        using var h = CreateFileW(@"\\.\" + letter, GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
            IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
        if (h.IsInvalid) return false;

        // Local collection
        var names = new Buf<char>(1 << 22);
        var starts = new Buf<int>(1 << 18); starts.Add(0);
        var frns = new Buf<long>(1 << 18);
        var parentFrns = new Buf<long>(1 << 18);
        var flags = new Buf<byte>(1 << 18);
        long maxIdx = RootIndex;

        const int bufSize = 1 << 20;
        byte* buf = (byte*)NativeMemory.Alloc(bufSize);
        try
        {
            var med = new MFT_ENUM_DATA_V0 { StartFileReferenceNumber = 0, LowUsn = 0, HighUsn = long.MaxValue };
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                if (!DeviceIoControl(h, FSCTL_ENUM_USN_DATA, &med, sizeof(MFT_ENUM_DATA_V0), buf, bufSize, out int got, IntPtr.Zero))
                {
                    int err = Marshal.GetLastWin32Error();
                    if (err == ERROR_HANDLE_EOF) break;
                    if (frns.Count == 0) return false;
                    break;
                }
                if (got <= 8) break;
                med.StartFileReferenceNumber = *(ulong*)buf;
                int off = 8;
                while (off < got)
                {
                    byte* rec = buf + off;
                    uint recLen = *(uint*)rec;
                    if (recLen == 0) break;
                    ushort major = *(ushort*)(rec + 4);
                    if (major == 2)
                    {
                        long frn = *(long*)(rec + 8) & IndexMask;
                        long pfrn = *(long*)(rec + 16) & IndexMask;
                        uint attrs = *(uint*)(rec + 52);
                        int nameLen = *(ushort*)(rec + 56) / 2;
                        int nameOff = *(ushort*)(rec + 58);
                        names.AddRange(new ReadOnlySpan<char>(rec + nameOff, nameLen));
                        starts.Add(names.Count);
                        frns.Add(frn);
                        parentFrns.Add(pfrn);
                        flags.Add((attrs & FILE_ATTRIBUTE_DIRECTORY) != 0 ? FileIndex.FlagDir : (byte)0);
                        if (frn > maxIdx) maxIdx = frn;
                    }
                    off += (int)recLen;
                }
                progress(frns.Count);
            }
        }
        finally { NativeMemory.Free(buf); }

        int n = frns.Count;
        if (n == 0) return false;
        if (maxIdx > int.MaxValue - 1) return false;

        // MFT record index -> local idx
        var map = new int[maxIdx + 1];
        Array.Fill(map, -1);
        for (int i = 0; i < n; i++) map[frns.Items[i]] = i;

        // local parent: -1 = root, -2 = unresolved
        var lp = new int[n];
        for (int i = 0; i < n; i++)
        {
            long p = parentFrns.Items[i];
            if (frns.Items[i] < 16) lp[i] = -2; // NTFS metafiles ($MFT, $Extend, ...)
            else if (p == RootIndex) lp[i] = -1;
            else if (p <= maxIdx && map[p] >= 0 && map[p] != i) lp[i] = map[p];
            else lp[i] = -2;
        }
        if (map[RootIndex] >= 0) lp[map[RootIndex]] = -2; // root record itself – drive entry represents it

        // validity: chain must reach root. 0=unknown 1=valid 2=invalid 3=visiting
        var state = new byte[n];
        var stack = new Stack<int>();
        for (int i = 0; i < n; i++)
        {
            if (state[i] != 0) continue;
            int c = i;
            while (true)
            {
                if (state[c] == 1 || state[c] == 2) break;
                if (state[c] == 3) { state[c] = 2; break; } // cycle
                int p = lp[c];
                if (p == -1) { state[c] = 1; break; }
                if (p == -2) { state[c] = 2; break; }
                state[c] = 3; stack.Push(c); c = p;
            }
            byte res = state[c];
            while (stack.Count > 0) state[stack.Pop()] = res;
        }

        // compact ids
        var compact = new int[n];
        int k = 0;
        for (int i = 0; i < n; i++) compact[i] = state[i] == 1 ? k++ : -1;

        int rootId = b.Add(letter, -1, FileIndex.FlagDir);
        int baseId = b.Count;
        for (int i = 0; i < n; i++)
        {
            if (compact[i] < 0) continue;
            int p = lp[i] == -1 ? rootId : baseId + compact[lp[i]];
            int s = starts.Items[i], e = starts.Items[i + 1];
            b.Add(names.Items.AsSpan(s, e - s), p, flags.Items[i]);
        }
        progress(k);
        return true;
    }
}

/// <summary>Parallel directory walker – works on any file system without admin rights.</summary>
public static class DirScanner
{
    readonly struct Item
    {
        public readonly string Name; public readonly bool Dir; public readonly bool Reparse;
        public Item(string n, bool d, bool r) { Name = n; Dir = d; Reparse = r; }
    }

    public static void Scan(string drive, IndexBuilder b, CancellationToken ct, Action<long> progress)
    {
        string letter = drive.Substring(0, 2);
        int rootId = b.Add(letter, -1, FileIndex.FlagDir);
        var pending = new ConcurrentStack<(string path, int id)>();
        pending.Push((letter + "\\", rootId));
        int active = 0;
        long total = 0;
        var opts = new EnumerationOptions
        {
            IgnoreInaccessible = true, RecurseSubdirectories = false, AttributesToSkip = 0,
            ReturnSpecialDirectories = false, BufferSize = 1 << 16
        };

        void Worker()
        {
            var items = new List<Item>(256);
            var subdirs = new List<(string, int)>(64);
            int idle = 0;
            while (true)
            {
                if (ct.IsCancellationRequested) return;
                Interlocked.Increment(ref active);
                if (!pending.TryPop(out var job))
                {
                    if (Interlocked.Decrement(ref active) == 0 && pending.IsEmpty) return;
                    if (++idle > 50) Thread.Sleep(1); else Thread.Yield();
                    continue;
                }
                idle = 0;
                try
                {
                    items.Clear(); subdirs.Clear();
                    try
                    {
                        var en = new FileSystemEnumerable<Item>(job.path,
                            (ref FileSystemEntry e) => new Item(e.FileName.ToString(), e.IsDirectory,
                                (e.Attributes & FileAttributes.ReparsePoint) != 0), opts);
                        foreach (var it in en) items.Add(it);
                    }
                    catch { }
                    if (items.Count > 0)
                    {
                        lock (b)
                        {
                            foreach (var it in items)
                            {
                                int id = b.Add(it.Name, job.id, it.Dir ? FileIndex.FlagDir : (byte)0);
                                if (it.Dir && !it.Reparse) subdirs.Add((Path.Join(job.path, it.Name), id));
                            }
                        }
                        foreach (var s in subdirs) pending.Push(s);
                        long t = Interlocked.Add(ref total, items.Count);
                        if ((t & 0x3FFF) < items.Count) progress(t);
                    }
                }
                finally { Interlocked.Decrement(ref active); }
            }
        }

        int workers = Math.Clamp(Environment.ProcessorCount * 2, 4, 32);
        var threads = Enumerable.Range(0, workers).Select(_ => new Thread(Worker) { IsBackground = true }).ToList();
        threads.ForEach(t => t.Start());
        threads.ForEach(t => t.Join());
        ct.ThrowIfCancellationRequested();
        progress(total);
    }
}
