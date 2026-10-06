using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using CodeSearch.Core;
using ICSharpCode.AvalonEdit.Highlighting;

namespace CodeSearch;

public sealed class LangItem { public string Code { get; init; } public string Display { get; init; } public override string ToString() => Display; }

public sealed class DriveRow : INotifyPropertyChanged
{
    bool _sel;
    public string Root { get; init; }
    public string Label { get; init; }
    public string Detail { get; init; }
    public bool Selected { get => _sel; set { _sel = value; PropertyChanged?.Invoke(this, new(nameof(Selected))); } }
    public event PropertyChangedEventHandler PropertyChanged;
}

public partial class MainWindow : Window
{
    public static readonly RoutedCommand ModeNameCmd = new(), ModeContentCmd = new(), FocusSearchCmd = new(), ToggleModeCmd = new();

    readonly IndexManager _index = new();
    AppSettings _settings = AppSettings.Load();
    readonly DispatcherTimer _debounce;
    CancellationTokenSource _searchCts;
    CancellationTokenSource _scanCts;
    int _searchSeq;
    CancellationTokenSource _warmCts = new();
    bool _scanning;
    readonly MatchColorizer _matchColorizer = new();
    readonly HitLineRenderer _hitRenderer = new();
    bool _langInit;

    bool ContentMode => ModeContent.IsChecked == true;

    public MainWindow()
    {
        Loc.Set(_settings.Language);
        InitializeComponent();

        _langInit = true;
        var langs = Loc.Languages.Select(l => new LangItem { Code = l.Code, Display = "🌐  " + l.Name }).ToList();
        LangBox.ItemsSource = langs;
        LangBox.SelectedItem = langs.First(l => l.Code == Loc.Lang);
        _langInit = false;
        Loc.LanguageChanged += OnLanguageChanged;

        Preview.TextArea.TextView.LineTransformers.Add(_matchColorizer);
        Preview.TextArea.TextView.BackgroundRenderers.Add(_hitRenderer);
        Preview.Options.EnableHyperlinks = false;
        Preview.Options.EnableEmailHyperlinks = false;
        _debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(60) };
        _debounce.Tick += (_, _) => { _debounce.Stop(); RunSearch(); };

        CommandBindings.Add(new CommandBinding(ModeNameCmd, (_, _) => ModeName.IsChecked = true));
        CommandBindings.Add(new CommandBinding(ModeContentCmd, (_, _) => ModeContent.IsChecked = true));
        CommandBindings.Add(new CommandBinding(ToggleModeCmd, (_, _) => { if (ContentMode) ModeName.IsChecked = true; else ModeContent.IsChecked = true; }));
        CommandBindings.Add(new CommandBinding(FocusSearchCmd, (_, _) => { SearchBox.Focus(); SearchBox.SelectAll(); }));

        Loaded += OnLoaded;
        Closing += (_, e) =>
        {
            if (_scanning && MessageBox.Show(Loc.T("exit_scanning"), "CodeSearch", MessageBoxButton.YesNo) != MessageBoxResult.Yes)
                e.Cancel = true;
            else { _scanCts?.Cancel(); _warmCts.Cancel(); }
        };
        ApplyMode();
    }

    async void OnLoaded(object sender, RoutedEventArgs e)
    {
        UpdateTitle();
        AdminPanel.Visibility = Elevation.IsAdmin() ? Visibility.Collapsed : Visibility.Visible;
        FillSetupForm();
        SearchBox.Focus();
        Status.Text = Loc.T("loading_index");
        var sw = Stopwatch.StartNew();
        await Task.Run(() => _index.LoadExisting());
        _index.Content.WarmUp(_warmCts.Token);
        UpdateIndexStatus();
        if (!_index.HasIndex || Environment.GetCommandLineArgs().Contains("--setup"))
        {
            Status.Text = Loc.T("no_index_yet");
            ShowSetup();
            if (Environment.GetCommandLineArgs().Contains("--autoscan")) StartScan_Click(this, null);
        }
        else Status.Text = Loc.F("index_loaded", sw.ElapsedMilliseconds);
    }

    // ------------------------------------------------------------------ language

    void UpdateTitle() => Title = Elevation.IsAdmin() ? "CodeSearch  " + Loc.T("admin_suffix") : "CodeSearch";

    void LangBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_langInit || LangBox.SelectedItem is not LangItem li || li.Code == Loc.Lang) return;
        _settings.Language = li.Code;
        _settings.Save();
        Loc.Set(li.Code);
    }

    void OnLanguageChanged()
    {
        UpdateTitle();
        ApplyMode();
        UpdateIndexStatus();
        if (!_scanning) { FillSetupForm(drivesOnly: true); StartScanBtn.Content = Loc.T(_index.HasIndex ? "rescan" : "start_scan"); }
        if (ContentList.SelectedItem is ContentRow r) ShowPreview(r);
        else PreviewTitle.Text = Loc.T("preview");
        RunSearch(); // rebuilds rows (dates, sizes) and the status text in the new culture
    }

    // ------------------------------------------------------------------ mode / options

    void Mode_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsInitialized || NameList == null) return;
        ApplyMode();
        RunSearch();
        SearchBox.Focus();
    }

    void ApplyMode()
    {
        if (NameList == null) return;
        bool c = ContentMode;
        NameList.Visibility = c ? Visibility.Collapsed : Visibility.Visible;
        ContentList.Visibility = c ? Visibility.Visible : Visibility.Collapsed;
        PreviewPanel.Visibility = c ? Visibility.Visible : Visibility.Collapsed;
        PreviewSplitter.Visibility = PreviewPanel.Visibility;
        PreviewRow.Height = c ? new GridLength(260) : new GridLength(0);
        OptWord.Visibility = c ? Visibility.Visible : Visibility.Collapsed;
        OptRegex.Visibility = c ? Visibility.Visible : Visibility.Collapsed;
        FilterPanel.Visibility = c ? Visibility.Visible : Visibility.Collapsed;
        HelpText.Text = Loc.T(c ? "help_content" : "help_name");
        Placeholder.Text = Loc.T(c ? "ph_content" : "ph_name");
    }

    void Option_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsInitialized) return;
        ScheduleSearch();
    }

    void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        Placeholder.Visibility = SearchBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        ScheduleSearch();
    }

    void ScheduleSearch()
    {
        _debounce.Stop();
        _debounce.Interval = TimeSpan.FromMilliseconds(ContentMode ? 140 : 50);
        _debounce.Start();
    }

    void SearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var list = ContentMode ? ContentList : NameList;
        if (e.Key == Key.Down && list.Items.Count > 0)
        {
            list.SelectedIndex = Math.Max(0, list.SelectedIndex);
            list.UpdateLayout();
            (list.ItemContainerGenerator.ContainerFromIndex(list.SelectedIndex) as ListViewItem)?.Focus();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter) { _debounce.Stop(); RunSearch(); e.Handled = true; }
        else if (e.Key == Key.Escape) { SearchBox.Clear(); e.Handled = true; }
    }

    // ------------------------------------------------------------------ search

    async void RunSearch()
    {
        _searchCts?.Cancel();
        var cts = _searchCts = new CancellationTokenSource();
        int seq = ++_searchSeq;
        string text = SearchBox.Text;
        bool cs = OptCase.IsChecked == true;

        if (string.IsNullOrWhiteSpace(text))
        {
            NameList.ItemsSource = null; ContentList.ItemsSource = null;
            ClearPreview();
            Status.Text = Loc.T(_index.HasIndex ? "type_to_search" : "no_index_yet");
            return;
        }

        var sw = Stopwatch.StartNew();
        try
        {
            if (!ContentMode)
            {
                var q = new NameQuery(text, cs);
                var files = _index.Files;
                int total = 0;
                var ids = await Task.Run(() =>
                {
                    _index.Lock.EnterReadLock();
                    try { return NameSearch.Search(files, q, 1_000_000, cts.Token, out total); }
                    finally { _index.Lock.ExitReadLock(); }
                });
                if (seq != _searchSeq) return;
                Hl.SetTerms(q.HighlightTerms);
                var rows = new List<NameRow>(ids.Count);
                foreach (var id in ids) rows.Add(new NameRow(files, id));
                NameList.ItemsSource = rows;
                if (rows.Count > 0) NameList.ScrollIntoView(rows[0]);
                Status.Text = Loc.F("name_results", total) + (total > rows.Count ? Loc.F("shown_first", rows.Count) : "") + $"  •  {sw.Elapsed.TotalMilliseconds:N1} ms";
            }
            else
            {
                var q = new ContentQuery
                {
                    Text = text, CaseSensitive = cs, WholeWord = OptWord.IsChecked == true, Regex = OptRegex.IsChecked == true,
                    FileFilter = FileFilter.Text
                };
                Regex hlRx = null;
                if (q.Regex)
                {
                    try { hlRx = new Regex(q.WholeWord ? $@"\b(?:{text})\b" : text, cs ? RegexOptions.None : RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(250)); }
                    catch (ArgumentException ex) { Status.Text = Loc.F("invalid_regex", ex.Message); return; }
                }
                var store = _index.Content;
                bool truncated = false; int fileHits = 0;
                var hits = await Task.Run(() =>
                {
                    _index.Lock.EnterReadLock();
                    try { return store.Search(q, cts.Token, out truncated, out fileHits); }
                    finally { _index.Lock.ExitReadLock(); }
                });
                if (seq != _searchSeq) return;
                if (hlRx != null) Hl.SetRegex(hlRx); else Hl.SetTerms(new[] { text });
                var rows = hits.Select(h => new ContentRow(store, h)).ToList();
                ContentList.ItemsSource = rows;
                if (rows.Count > 0) { ContentList.ScrollIntoView(rows[0]); ContentList.SelectedIndex = 0; }
                else ClearPreview();
                Status.Text = Loc.F("content_results", rows.Count, fileHits) + (truncated ? Loc.T("truncated") : "") +
                              "  •  " + Loc.F("searched_in", store.TotalBytes / 1048576.0, sw.Elapsed.TotalMilliseconds);
            }
        }
        catch (OperationCanceledException) { }
        catch (ArgumentException ex) { if (seq == _searchSeq) Status.Text = Loc.F("invalid_search", ex.Message); }
    }

    // ------------------------------------------------------------------ preview

    void ContentList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ContentList.SelectedItem is ContentRow r) ShowPreview(r);
    }

    string _previewPath;

    void ClearPreview()
    {
        _previewPath = null;
        _hitRenderer.Line = 0;
        Preview.Document.Text = "";
        PreviewTitle.Text = Loc.T("preview");
    }

    void ShowPreview(ContentRow r)
    {
        var (def, langName) = SyntaxMap.For(r.FullPath);
        PreviewTitle.Text = Loc.F("preview_title", r.FullPath, r.Line, langName ?? Loc.T("plain_text"));
        if (_previewPath != r.FullPath)
        {
            string text;
            _index.Lock.EnterReadLock();
            try { text = SafeRead(r); }
            finally { _index.Lock.ExitReadLock(); }
            Preview.SyntaxHighlighting = def;
            Preview.Document.Text = text;
            _previewPath = r.FullPath;
        }
        else Preview.TextArea.TextView.Redraw(); // same file, possibly new search terms

        _hitRenderer.Line = Math.Clamp(r.Line, 1, Math.Max(1, Preview.Document.LineCount));
        Preview.TextArea.TextView.InvalidateLayer(ICSharpCode.AvalonEdit.Rendering.KnownLayer.Background);
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            Preview.UpdateLayout();
            double y = Preview.TextArea.TextView.GetVisualTopByDocumentLine(_hitRenderer.Line);
            Preview.ScrollToVerticalOffset(Math.Max(0, y - Preview.ViewportHeight / 3));
            Preview.ScrollToHorizontalOffset(0);
        });
    }

    string SafeRead(ContentRow r)
    {
        try
        {
            if (ReferenceEquals(r.Store, _index.Content)) return _index.Content.ReadText(r.Hit.FileId);
            return File.ReadAllText(r.FullPath);
        }
        catch { try { return File.ReadAllText(r.FullPath); } catch { return Loc.T("cannot_read"); } }
    }

    // ------------------------------------------------------------------ open / actions

    IEnumerable<object> Selected() => (ContentMode ? ContentList : NameList).SelectedItems.Cast<object>();

    static (string path, int line) PathOf(object o) => o switch
    {
        NameRow n => (n.FullPath, 0),
        ContentRow c => (c.FullPath, c.Line),
        _ => (null, 0)
    };

    void List_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is FrameworkElement fe && fe.DataContext is NameRow or ContentRow)
            OpenItem(fe.DataContext, ContentMode);
    }

    void List_KeyDown(object sender, KeyEventArgs e)
    {
        var item = (sender as ListView)?.SelectedItem;
        if (item == null) return;
        if (e.Key == Key.Enter)
        {
            if (Keyboard.Modifiers == ModifierKeys.Control) Launcher.Reveal(PathOf(item).path);
            else OpenItem(item, ContentMode);
            e.Handled = true;
        }
        else if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control)
        {
            CopyText(string.Join(Environment.NewLine, Selected().Select(o => PathOf(o).path)));
            e.Handled = true;
        }
    }

    void OpenItem(object item, bool inCode)
    {
        var (p, line) = PathOf(item);
        if (inCode) Launcher.OpenInCode(p, line);
        else Launcher.Open(p);
    }

    static void CopyText(string s)
    {
        for (int i = 0; i < 5; i++)
        {
            try { Clipboard.SetText(s); return; } catch { Thread.Sleep(30); }
        }
    }

    void Ctx_Open(object s, RoutedEventArgs e) { foreach (var o in Selected().Take(20)) Launcher.Open(PathOf(o).path); }
    void Ctx_Code(object s, RoutedEventArgs e) { foreach (var o in Selected().Take(20)) { var (p, l) = PathOf(o); Launcher.OpenInCode(p, l); } }
    void Ctx_Reveal(object s, RoutedEventArgs e) { var o = Selected().FirstOrDefault(); if (o != null) Launcher.Reveal(PathOf(o).path); }
    void Ctx_CopyPath(object s, RoutedEventArgs e) => CopyText(string.Join(Environment.NewLine, Selected().Select(o => PathOf(o).path)));
    void Ctx_CopyName(object s, RoutedEventArgs e) => CopyText(string.Join(Environment.NewLine, Selected().OfType<NameRow>().Select(n => n.Name)));
    void Ctx_CopyLine(object s, RoutedEventArgs e) => CopyText(string.Join(Environment.NewLine, Selected().OfType<ContentRow>().Select(c => c.Text)));

    // ------------------------------------------------------------------ setup / scan

    void UpdateIndexStatus()
    {
        var i = _index.Info;
        if (i == null) { IndexStatus.Text = Loc.T("no_index"); return; }
        var f = _index.Files;
        IndexStatus.Text = $"{string.Join(" ", i.Drives.Select(d => d.Substring(0, 2)))}  •  " + Loc.F("idx_status", f.Count - f.DirCount, f.DirCount) + "  •  " +
                           Loc.F("idx_code", i.ContentFiles, i.ContentBytes / 1048576.0, i.MaxFileKb) + $"  •  {i.ScannedAt:g}" +
                           (i.UsedMft ? "  •  MFT" : "");
    }

    /// <param name="drivesOnly">Language switch: re-render drive descriptions but keep the user's unsaved edits.</param>
    void FillSetupForm(bool drivesOnly = false)
    {
        var current = (DriveList.ItemsSource as List<DriveRow>)?.ToDictionary(r => r.Root, r => r.Selected, StringComparer.OrdinalIgnoreCase);
        var rows = new List<DriveRow>();
        foreach (var d in DriveInfo.GetDrives())
        {
            try
            {
                if (!d.IsReady) continue;
                string root = d.Name.Substring(0, 2);
                var label = string.IsNullOrEmpty(d.VolumeLabel) ? root : $"{root}  {d.VolumeLabel}";
                var detail = $"{Loc.T("drv_" + d.DriveType)}, {d.DriveFormat}, " + Loc.F("drive_used", NameRow.FormatSize(d.TotalSize - d.TotalFreeSpace), NameRow.FormatSize(d.TotalSize));
                rows.Add(new DriveRow
                {
                    Root = root, Label = label, Detail = detail,
                    Selected = drivesOnly && current != null && current.TryGetValue(root, out var sel) ? sel
                             : _settings.Drives.Count == 0 ? d.DriveType == DriveType.Fixed && root.Equals("C:", StringComparison.OrdinalIgnoreCase)
                             : _settings.Drives.Contains(root, StringComparer.OrdinalIgnoreCase)
                });
            }
            catch { }
        }
        DriveList.ItemsSource = rows;
        if (drivesOnly) return;
        MaxKbBox.Text = _settings.MaxFileKb.ToString();
        ExtBox.Text = _settings.Extensions;
        ExcludeBox.Text = _settings.ExcludeDirs;
    }

    void ShowSetup()
    {
        SetupOverlay.Visibility = Visibility.Visible;
        CloseSetupBtn.IsEnabled = _index.HasIndex && !_scanning;
    }

    void Settings_Click(object sender, RoutedEventArgs e)
    {
        if (!_scanning) FillSetupForm();
        ShowSetup();
    }

    void CloseSetup_Click(object sender, RoutedEventArgs e)
    {
        SetupOverlay.Visibility = Visibility.Collapsed;
        SearchBox.Focus();
    }

    void Preset_Click(object sender, RoutedEventArgs e) => MaxKbBox.Text = (string)((Button)sender).Tag;

    void Admin_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = true, Verb = "runas", Arguments = "--setup" });
            Application.Current.Shutdown();
        }
        catch { /* user declined UAC */ }
    }

    void CancelScan_Click(object sender, RoutedEventArgs e) => _scanCts?.Cancel();

    async void StartScan_Click(object sender, RoutedEventArgs e)
    {
        if (_scanning) return;
        var drives = ((List<DriveRow>)DriveList.ItemsSource).Where(d => d.Selected).Select(d => d.Root).ToList();
        if (drives.Count == 0) { ScanText.Text = Loc.T("pick_drive"); return; }
        if (!int.TryParse(MaxKbBox.Text.Trim(), out int kb) || kb <= 0 || kb > 64 * 1024) { ScanText.Text = Loc.T("bad_kb"); return; }

        _settings.Drives = drives;
        _settings.MaxFileKb = kb;
        _settings.Extensions = ExtBox.Text;
        _settings.ExcludeDirs = ExcludeBox.Text;
        _settings.Save();

        _scanning = true;
        _scanCts = new CancellationTokenSource();
        SetupForm.IsEnabled = false; StartScanBtn.IsEnabled = false; CloseSetupBtn.IsEnabled = false; AdminBtn.IsEnabled = false;
        CancelScanBtn.Visibility = Visibility.Visible;
        ScanProgressBar.Visibility = Visibility.Visible;
        ScanProgressBar.IsIndeterminate = true;

        var prog = new Progress<ScanProgress>(p =>
        {
            ScanText.Text = p.Text;
            if (p.Percent < 0) ScanProgressBar.IsIndeterminate = true;
            else { ScanProgressBar.IsIndeterminate = false; ScanProgressBar.Value = p.Percent; }
        });
        var sw = Stopwatch.StartNew();
        var settings = _settings;
        var token = _scanCts.Token;
        try
        {
            _warmCts.Cancel(); _warmCts = new CancellationTokenSource();
            await Task.Run(() => _index.Scan(settings, prog, token));
            _index.Content.WarmUp(_warmCts.Token);
            var i = _index.Info;
            ScanText.Text = Loc.F("scan_done", sw.Elapsed.TotalSeconds, i.NameSeconds, i.UsedMft ? ", MFT" : "", i.ContentSeconds);
            UpdateIndexStatus();
            SetupOverlay.Visibility = Visibility.Collapsed;
            Status.Text = ScanText.Text;
            SearchBox.Focus();
            if (SearchBox.Text.Length > 0) RunSearch();
        }
        catch (OperationCanceledException) { ScanText.Text = Loc.T("scan_cancelled"); }
        catch (Exception ex) { ScanText.Text = Loc.F("scan_error", ex.Message); }
        finally
        {
            _scanning = false;
            SetupForm.IsEnabled = true; StartScanBtn.IsEnabled = true; AdminBtn.IsEnabled = true;
            CloseSetupBtn.IsEnabled = _index.HasIndex;
            CancelScanBtn.Visibility = Visibility.Collapsed;
            ScanProgressBar.Visibility = Visibility.Collapsed;
            StartScanBtn.Content = Loc.T("rescan");
        }
    }
}
