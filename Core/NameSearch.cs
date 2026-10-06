using System.Text.RegularExpressions;

namespace CodeSearch.Core;

/// <summary>
/// Everything-style query: space separated terms are AND-ed.
///   foo        name contains foo
///   !foo       name does not contain foo
///   *.cs / a?c wildcard on the whole name
///   src\util   term with a slash matches the full path
///   ext:cs;js  extension filter
///   file: / folder:  only files / only folders
///   "a b"      quoted term with spaces
/// </summary>
public sealed class NameQuery
{
    public readonly List<string> Plain = new();
    public readonly List<string> NotPlain = new();
    public readonly List<Regex> Wild = new();
    public readonly List<string> PathTerms = new();
    public readonly HashSet<string> Exts = new(StringComparer.OrdinalIgnoreCase);
    public bool OnlyFiles, OnlyDirs;
    public bool MatchCase;

    public bool IsEmpty => Plain.Count == 0 && NotPlain.Count == 0 && Wild.Count == 0 && PathTerms.Count == 0 && Exts.Count == 0 && !OnlyFiles && !OnlyDirs;

    public static List<string> Tokenize(string s)
    {
        var res = new List<string>();
        var sb = new System.Text.StringBuilder();
        bool q = false;
        foreach (var c in s)
        {
            if (c == '"') { q = !q; continue; }
            if (c == ' ' && !q) { if (sb.Length > 0) { res.Add(sb.ToString()); sb.Clear(); } continue; }
            sb.Append(c);
        }
        if (sb.Length > 0) res.Add(sb.ToString());
        return res;
    }

    public NameQuery(string text, bool matchCase)
    {
        MatchCase = matchCase;
        foreach (var tok in Tokenize(text ?? ""))
        {
            var t = tok;
            if (t.StartsWith("ext:", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var e in t[4..].Split(';', ',', StringSplitOptions.RemoveEmptyEntries)) Exts.Add(e.TrimStart('*', '.'));
                continue;
            }
            if (t.Equals("file:", StringComparison.OrdinalIgnoreCase)) { OnlyFiles = true; continue; }
            if (t.Equals("folder:", StringComparison.OrdinalIgnoreCase)) { OnlyDirs = true; continue; }
            bool neg = t.StartsWith('!') && t.Length > 1;
            if (neg) t = t[1..];
            if (!matchCase) t = t.ToLowerInvariant();
            if (t.Contains('*') || t.Contains('?'))
            {
                var rx = "^" + Regex.Escape(t).Replace(@"\*", ".*").Replace(@"\?", ".") + "$";
                var r = new Regex(rx, RegexOptions.CultureInvariant | RegexOptions.Singleline | (matchCase ? 0 : RegexOptions.IgnoreCase));
                if (neg) NotWild.Add(r); else Wild.Add(r);
            }
            else if (t.Contains('\\') || t.Contains('/'))
            {
                t = t.Replace('/', '\\');
                if (neg) NotPath.Add(t); else PathTerms.Add(t);
            }
            else if (neg) NotPlain.Add(t);
            else Plain.Add(t);
        }
    }

    public readonly List<Regex> NotWild = new();
    public readonly List<string> NotPath = new();

    /// <summary>Terms used for highlighting the result rows.</summary>
    public IEnumerable<string> HighlightTerms => Plain.Concat(PathTerms.Select(p => p.Split('\\').Last()));
}

public static class NameSearch
{
    public static List<int> Search(FileIndex idx, NameQuery q, int maxResults, CancellationToken ct, out int total)
    {
        total = 0;
        if (q.IsEmpty || idx.Count == 0) return new List<int>();

        char[] blob = q.MatchCase ? idx.Names : idx.Lower;
        string anchor = q.Plain.OrderByDescending(s => s.Length).FirstOrDefault();

        int parts = Environment.ProcessorCount * 8;
        int per = (idx.Count + parts - 1) / parts;
        var results = new List<int>[parts];
        int found = 0;

        var po = new ParallelOptions { CancellationToken = ct };
        Parallel.For(0, parts, po, p =>
        {
            int from = p * per, to = Math.Min(idx.Count, from + per);
            if (from >= to) return;
            var list = new List<int>();
            results[p] = list;
            if (anchor != null)
            {
                int blobEnd = idx.Starts[to];
                int pos = idx.Starts[from];
                var a = anchor.AsSpan();
                while (pos < blobEnd)
                {
                    int rel = blob.AsSpan(pos, blobEnd - pos).IndexOf(a);
                    if (rel < 0) break;
                    int hit = pos + rel;
                    int e = idx.EntryAt(hit);
                    if (hit + a.Length > idx.Starts[e + 1]) { pos = hit + 1; continue; } // spans two names
                    if (Check(idx, q, e, blob, anchor)) list.Add(e);
                    pos = idx.Starts[e + 1];
                    if ((list.Count & 1023) == 0 && ct.IsCancellationRequested) return;
                }
            }
            else
            {
                for (int e = from; e < to; e++)
                {
                    if (Check(idx, q, e, blob, null)) list.Add(e);
                    if ((e & 4095) == 0 && ct.IsCancellationRequested) return;
                }
            }
            Interlocked.Add(ref found, list.Count);
        });
        ct.ThrowIfCancellationRequested();

        total = found;
        var all = new List<int>(Math.Min(found, maxResults));
        foreach (var l in results)
        {
            if (l == null) continue;
            foreach (var e in l) { if (all.Count >= maxResults) break; all.Add(e); }
        }
        // Sort by name for a stable, Everything-like ordering (cheap span compare on the lowercase blob).
        if (all.Count <= 300_000)
        {
            var arr = all.ToArray();
            Array.Sort(arr, (x, y) =>
            {
                int c = idx.LowerName(x).SequenceCompareTo(idx.LowerName(y));
                return c != 0 ? c : x.CompareTo(y);
            });
            return new List<int>(arr);
        }
        return all;
    }

    static bool Check(FileIndex idx, NameQuery q, int e, char[] blob, string anchor)
    {
        bool dir = idx.IsDir(e);
        if (q.OnlyFiles && dir) return false;
        if (q.OnlyDirs && !dir) return false;
        var name = blob.AsSpan(idx.Starts[e], idx.Starts[e + 1] - idx.Starts[e]);
        if (q.Exts.Count > 0)
        {
            if (dir) return false;
            int dot = name.LastIndexOf('.');
            if (dot < 0 || !q.Exts.Contains(name[(dot + 1)..].ToString())) return false;
        }
        foreach (var t in q.Plain) if (!ReferenceEquals(t, anchor) && name.IndexOf(t) < 0) return false;
        foreach (var t in q.NotPlain) if (name.IndexOf(t) >= 0) return false;
        if (q.Wild.Count > 0 || q.NotWild.Count > 0)
        {
            var s = name.ToString();
            foreach (var r in q.Wild) if (!r.IsMatch(s)) return false;
            foreach (var r in q.NotWild) if (r.IsMatch(s)) return false;
        }
        if (q.PathTerms.Count > 0 || q.NotPath.Count > 0)
        {
            var full = idx.FullPath(e);
            if (!q.MatchCase) full = full.ToLowerInvariant();
            foreach (var t in q.PathTerms) if (full.IndexOf(t, StringComparison.Ordinal) < 0) return false;
            foreach (var t in q.NotPath) if (full.IndexOf(t, StringComparison.Ordinal) >= 0) return false;
        }
        return true;
    }
}
