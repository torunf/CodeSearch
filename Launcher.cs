using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using CodeSearch.Core;

namespace CodeSearch;

/// <summary>
/// Opens search results. File paths come from the disk, so they are never passed through cmd.exe
/// (which would interpret %, ^ and & in file names), and the app never hands its own elevation to
/// the programs it starts.
/// </summary>
public static class Launcher
{
    static readonly Lazy<(string exe, string cli)?> VsCode = new(FindVsCode);
    static readonly bool IsAdmin = Elevation.IsAdmin();

    public static bool HasVsCode => VsCode.Value != null;

    /// <summary>Opens a file or folder with its default handler.</summary>
    public static void Open(string path)
    {
        if (string.IsNullOrEmpty(path)) return;
        try
        {
            // When elevated (MFT mode), let Explorer open it so the target runs with normal user rights.
            if (IsAdmin) Start("explorer.exe", path);
            else Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, Loc.T("open_failed")); }
    }

    /// <summary>Opens a file in VS Code, at <paramref name="line"/> when it is positive.</summary>
    public static void OpenInCode(string path, int line)
    {
        if (string.IsNullOrEmpty(path)) return;
        var vs = VsCode.Value;
        if (vs == null) { Open(path); return; }
        try
        {
            // Equivalent of bin\code.cmd, without the batch file: Code.exe runs the CLI script as Node.
            var psi = new ProcessStartInfo(vs.Value.exe) { UseShellExecute = false, CreateNoWindow = true };
            psi.Environment["ELECTRON_RUN_AS_NODE"] = "1";
            psi.Environment.Remove("VSCODE_DEV");
            psi.ArgumentList.Add(vs.Value.cli);
            if (line > 0) { psi.ArgumentList.Add("-g"); psi.ArgumentList.Add($"{path}:{line}"); }
            else psi.ArgumentList.Add(path);
            Process.Start(psi);
        }
        catch { Open(path); }
    }

    /// <summary>Shows the item selected in an Explorer window.</summary>
    public static void Reveal(string path)
    {
        if (string.IsNullOrEmpty(path)) return;
        try
        {
            var psi = new ProcessStartInfo("explorer.exe") { UseShellExecute = false };
            psi.ArgumentList.Add("/select," + path.TrimEnd('\\'));
            Process.Start(psi);
        }
        catch { }
    }

    static void Start(string exe, string arg)
    {
        var psi = new ProcessStartInfo(exe) { UseShellExecute = false };
        psi.ArgumentList.Add(arg);
        Process.Start(psi);
    }

    /// <summary>Locates Code.exe and its cli.js by reading the code.cmd launcher (the CLI folder is versioned).</summary>
    static (string exe, string cli)? FindVsCode()
    {
        try
        {
            var cmd = FindOnPath("code.cmd") ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Microsoft VS Code", "bin", "code.cmd");
            if (!File.Exists(cmd)) return null;
            var root = Path.GetDirectoryName(Path.GetDirectoryName(cmd));
            var exe = Path.Combine(root, "Code.exe");
            if (!File.Exists(exe)) return null;
            foreach (var line in File.ReadLines(cmd))
            {
                var m = Regex.Match(line, @"%~dp0\.\.\\([^""%]*?cli\.js)", RegexOptions.IgnoreCase);
                if (!m.Success) continue;
                var cli = Path.GetFullPath(Path.Combine(root, m.Groups[1].Value));
                if (File.Exists(cli)) return (exe, cli);
            }
        }
        catch { }
        return null;
    }

    static string FindOnPath(string file)
    {
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var d = dir.Trim().Trim('"');
                if (!Path.IsPathFullyQualified(d)) continue; // ignore relative PATH entries
                var f = Path.Combine(d, file);
                if (File.Exists(f)) return f;
            }
            catch { }
        }
        return null;
    }
}
