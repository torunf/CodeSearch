using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using CodeSearch.Core;

namespace CodeSearch;

public sealed class NameRow
{
    readonly FileIndex _idx;
    public readonly int Id;
    string _name, _folder, _size, _date;
    bool _statDone;

    public NameRow(FileIndex idx, int id) { _idx = idx; Id = id; }

    public bool IsDir => _idx.IsDir(Id);
    public string Icon => IsDir ? "📁" : "📄";
    public string Name => _name ??= _idx.Name(Id).ToString();
    public string Folder => _folder ??= _idx.FolderPath(Id);
    public string FullPath => _idx.Parents[Id] < 0 ? Name + "\\" : Path.Join(Folder, Name);
    public string Size { get { Stat(); return _size; } }
    public string Modified { get { Stat(); return _date; } }

    void Stat()
    {
        if (_statDone) return;
        _statDone = true;
        try
        {
            if (IsDir) { var di = new DirectoryInfo(FullPath); _size = ""; _date = di.Exists ? di.LastWriteTime.ToString("g") : ""; }
            else
            {
                var fi = new FileInfo(FullPath);
                if (fi.Exists) { _size = FormatSize(fi.Length); _date = fi.LastWriteTime.ToString("g"); }
                else { _size = ""; _date = Loc.T("deleted"); }
            }
        }
        catch { _size = ""; _date = ""; }
    }

    public static string FormatSize(long b) =>
        b < 1024 ? $"{b} B" : b < 1048576 ? $"{b / 1024.0:N0} KB" : b < 1073741824 ? $"{b / 1048576.0:N1} MB" : $"{b / 1073741824.0:N2} GB";
}

public sealed class ContentRow
{
    readonly ContentStore _cs;
    public readonly ContentHit Hit;
    public ContentRow(ContentStore cs, ContentHit h) { _cs = cs; Hit = h; }
    public ContentStore Store => _cs;
    public string FullPath => _cs.Paths[Hit.FileId];
    public string FileName => Path.GetFileName(FullPath);
    public string Folder => Path.GetDirectoryName(FullPath);
    public int Line => Hit.Line;
    public string Text => Hit.Text;
}

/// <summary>Attached property that renders text with highlighted query matches.</summary>
public static class Hl
{
    public static Func<string, IEnumerable<(int start, int len)>> Matcher = _ => Array.Empty<(int, int)>();
    static readonly Brush HlBrush = new SolidColorBrush(Color.FromRgb(0xFD, 0xE6, 0x8A));

    public static void SetTerms(IEnumerable<string> terms)
    {
        var list = terms.Where(t => !string.IsNullOrEmpty(t)).Distinct().ToList();
        Matcher = s => FindTerms(s, list);
    }

    public static void SetRegex(Regex rx) => Matcher = s =>
    {
        try { return rx.Matches(s).Where(m => m.Length > 0).Select(m => (m.Index, m.Length)).ToList(); }
        catch { return Array.Empty<(int, int)>(); }
    };

    static IEnumerable<(int, int)> FindTerms(string s, List<string> terms)
    {
        var res = new List<(int, int)>();
        foreach (var t in terms)
        {
            int i = 0;
            while ((i = s.IndexOf(t, i, StringComparison.OrdinalIgnoreCase)) >= 0) { res.Add((i, t.Length)); i += t.Length; }
        }
        return res;
    }

    public static readonly DependencyProperty TextProperty =
        DependencyProperty.RegisterAttached("Text", typeof(string), typeof(Hl), new PropertyMetadata(null, OnText));

    public static string GetText(DependencyObject d) => (string)d.GetValue(TextProperty);
    public static void SetText(DependencyObject d, string v) => d.SetValue(TextProperty, v);

    static void OnText(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBlock tb) return;
        var s = e.NewValue as string ?? "";
        tb.Inlines.Clear();
        var ranges = Matcher(s).OrderBy(r => r.start).ToList();
        int p = 0;
        foreach (var (st, len) in ranges)
        {
            if (st < p) continue;
            if (st > p) tb.Inlines.Add(new Run(s.Substring(p, st - p)));
            tb.Inlines.Add(new Run(s.Substring(st, len)) { Background = HlBrush, FontWeight = FontWeights.SemiBold });
            p = st + len;
        }
        if (p < s.Length) tb.Inlines.Add(new Run(s.Substring(p)));
    }
}
