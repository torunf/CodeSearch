using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace CodeSearch.Core;

public sealed class IndexInfo
{
    public DateTime ScannedAt { get; set; }
    public List<string> Drives { get; set; } = new();
    public int MaxFileKb { get; set; }
    public bool UsedMft { get; set; }
    public double NameSeconds { get; set; }
    public double ContentSeconds { get; set; }
    public int ContentFiles { get; set; }
    public int SkippedFiles { get; set; }
    public long ContentBytes { get; set; }
}

public sealed class ScanProgress
{
    public string Text;
    public double Percent = -1; // -1 = indeterminate
}

public sealed class IndexManager : IDisposable
{
    public FileIndex Files { get; private set; } = FileIndex.Empty();
    public ContentStore Content { get; private set; } = ContentStore.Empty();
    public IndexInfo Info { get; private set; }
    /// <summary>Searches hold the read lock; swapping in a fresh index takes the write lock.</summary>
    public readonly ReaderWriterLockSlim Lock = new(LockRecursionPolicy.SupportsRecursion);

    static string IndexDir => Path.Combine(AppSettings.DataDir, "index");
    static string NewDir => Path.Combine(AppSettings.DataDir, "index_new");

    public bool HasIndex => Info != null;

    public void LoadExisting()
    {
        try
        {
            var dir = IndexDir;
            if (!File.Exists(Path.Combine(dir, "info.json"))) return;
            var info = JsonSerializer.Deserialize<IndexInfo>(File.ReadAllText(Path.Combine(dir, "info.json")));
            var files = FileIndex.Load(Path.Combine(dir, "names.idx"));
            var content = ContentStore.Open(dir);
            Files = files; Content = content; Info = info;
        }
        catch
        {
            Files = FileIndex.Empty(); Content = ContentStore.Empty(); Info = null;
        }
    }

    public void Scan(AppSettings s, IProgress<ScanProgress> progress, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        bool admin = Elevation.IsAdmin();
        bool usedMft = false;
        var b = new IndexBuilder();

        foreach (var drive in s.Drives)
        {
            ct.ThrowIfCancellationRequested();
            string d = drive.Substring(0, 2);
            bool ok = false;
            if (admin)
            {
                progress.Report(new ScanProgress { Text = Loc.F("p_mft", d, 0) });
                int before = b.Count;
                ok = MftScanner.TryScan(d, b, ct, n => progress.Report(new ScanProgress { Text = Loc.F("p_mft", d, n) }));
                if (ok) usedMft = true;
                else if (b.Count != before) throw new InvalidOperationException("MFT scan partially failed");
            }
            if (!ok)
            {
                int baseCount = b.Count;
                DirScanner.Scan(d, b, ct, n => progress.Report(new ScanProgress { Text = Loc.F("p_dirs", d, n) }));
            }
        }
        progress.Report(new ScanProgress { Text = Loc.T("p_build_names") });
        var files = b.Build();
        double nameSec = sw.Elapsed.TotalSeconds;

        // ---- choose files for content indexing
        progress.Report(new ScanProgress { Text = Loc.T("p_pick_files") });
        var candidates = CollectCodeFiles(files, s, ct);

        DeleteWithRetry(NewDir);
        Directory.CreateDirectory(NewDir);
        files.Save(Path.Combine(NewDir, "names.idx"));

        var sw2 = Stopwatch.StartNew();
        ContentStore.BuildStats last = null;
        ContentStore.Build(NewDir, candidates, Math.Max(1, s.MaxFileKb) * 1024, ct, st =>
        {
            last = st;
            progress.Report(new ScanProgress
            {
                Text = Loc.F("p_content", st.Processed, st.Total, st.Indexed, st.Bytes / 1048576.0),
                Percent = st.Total == 0 ? 100 : st.Processed * 100.0 / st.Total
            });
        });

        var info = new IndexInfo
        {
            ScannedAt = DateTime.Now, Drives = s.Drives.ToList(), MaxFileKb = s.MaxFileKb, UsedMft = usedMft,
            NameSeconds = nameSec, ContentSeconds = sw2.Elapsed.TotalSeconds,
            ContentFiles = last?.Indexed ?? 0, SkippedFiles = last?.Skipped ?? 0, ContentBytes = last?.Bytes ?? 0
        };
        File.WriteAllText(Path.Combine(NewDir, "info.json"), JsonSerializer.Serialize(info, new JsonSerializerOptions { WriteIndented = true }));

        // ---- swap in
        progress.Report(new ScanProgress { Text = Loc.T("p_loading") });
        Lock.EnterWriteLock();
        try
        {
            var old = Content;
            Content = ContentStore.Empty();
            old.Dispose();
            DeleteWithRetry(IndexDir);
            Directory.Move(NewDir, IndexDir);
            Content = ContentStore.Open(IndexDir);
            Files = files;
            Info = info;
        }
        finally { Lock.ExitWriteLock(); }
    }

    /// <summary>Background readers (cache warm-up) may hold a file for a moment after being cancelled.</summary>
    static void DeleteWithRetry(string dir)
    {
        for (int attempt = 0; ; attempt++)
        {
            try { if (Directory.Exists(dir)) Directory.Delete(dir, true); return; }
            catch (IOException) when (attempt < 20) { Thread.Sleep(100); }
            catch (UnauthorizedAccessException) when (attempt < 20) { Thread.Sleep(100); }
        }
    }

    static List<string> CollectCodeFiles(FileIndex idx, AppSettings s, CancellationToken ct)
    {
        var exts = s.ExtensionSet();
        var (exNames, exPrefixes) = s.ExcludeRules();
        var prefixLast = new HashSet<string>(exPrefixes.Select(p => Path.GetFileName(p.TrimEnd('\\')) is { Length: > 0 } n ? n : p),
            StringComparer.OrdinalIgnoreCase);

        // dir state: 0 unknown, 1 included, 2 excluded
        var state = new byte[idx.Count];
        var stack = new Stack<int>();

        byte Resolve(int d)
        {
            int c = d;
            while (c >= 0 && state[c] == 0) { stack.Push(c); c = idx.Parents[c]; }
            byte st = c >= 0 ? state[c] : (byte)1;
            while (stack.Count > 0)
            {
                int x = stack.Pop();
                if (st != 2)
                {
                    var name = idx.Name(x).ToString();
                    if (exNames.Contains(name)) st = 2;
                    else if (prefixLast.Contains(name))
                    {
                        var full = idx.FullPath(x);
                        if (exPrefixes.Any(p => string.Equals(p, full, StringComparison.OrdinalIgnoreCase))) st = 2;
                    }
                }
                state[x] = st;
            }
            return st;
        }

        var list = new List<string>();
        for (int e = 0; e < idx.Count; e++)
        {
            if (idx.IsDir(e)) continue;
            if ((e & 0xFFFF) == 0) ct.ThrowIfCancellationRequested();
            var name = idx.Name(e);
            int dot = name.LastIndexOf('.');
            if (dot < 0 || dot == name.Length - 1) continue;
            if (!exts.Contains(name[(dot + 1)..].ToString())) continue;
            int p = idx.Parents[e];
            if (p >= 0 && Resolve(p) == 2) continue;
            list.Add(idx.FullPath(e));
        }
        list.Sort(StringComparer.OrdinalIgnoreCase);
        return list;
    }

    public void Dispose() => Content?.Dispose();
}
