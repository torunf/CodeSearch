using System.IO;
using System.Text.Json;

namespace CodeSearch.Core;

public sealed class AppSettings
{
    public List<string> Drives { get; set; } = new();
    public int MaxFileKb { get; set; } = 512;
    /// <summary>UI language code ("" = Windows display language).</summary>
    public string Language { get; set; } = "";

    public string Extensions { get; set; } =
        "cs;js;ts;tsx;jsx;mjs;cjs;vue;svelte;json;xml;xaml;axaml;csproj;vbproj;fsproj;sln;props;targets;config;" +
        "html;htm;cshtml;razor;css;scss;sass;less;py;java;kt;kts;go;rs;c;h;cpp;hpp;cc;cxx;hh;m;mm;swift;" +
        "php;rb;lua;sql;sh;bash;ps1;psm1;psd1;bat;cmd;yaml;yml;toml;ini;gradle;dart;vb;fs;fsx;r;pl;" +
        "shader;hlsl;glsl;cginc;compute;uss;uxml;gd;tscn;tres;godot;cmake;md;proto;graphql;gql;tf;ipynb";

    public string ExcludeDirs { get; set; } =
        ".git;.svn;.hg;node_modules;bin;obj;.vs;.idea;$Recycle.Bin;System Volume Information;" +
        "C:\\Windows;C:\\Program Files\\WindowsApps;C:\\ProgramData\\Microsoft;C:\\$WinREAgent";

    public static string DataDir
    {
        get
        {
            var d = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CodeSearch");
            Directory.CreateDirectory(d);
            return d;
        }
    }

    static string FilePath => Path.Combine(DataDir, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
        }
        catch { }
        return new AppSettings();
    }

    public void Save()
    {
        try { File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true })); }
        catch { }
    }

    public HashSet<string> ExtensionSet() =>
        new(Extensions.Split(new[] { ';', ',', ' ', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(e => e.Trim().TrimStart('*').TrimStart('.').ToLowerInvariant()), StringComparer.OrdinalIgnoreCase);

    /// <summary>Folder names (anywhere) and absolute path prefixes excluded from content indexing.</summary>
    public (HashSet<string> names, List<string> prefixes) ExcludeRules()
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var prefixes = new List<string>();
        foreach (var raw in ExcludeDirs.Split(new[] { ';', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var s = raw.Trim().TrimEnd('\\', '/');
            if (s.Length == 0) continue;
            if (s.Contains(':') || s.Contains('\\') || s.Contains('/')) prefixes.Add(s.Replace('/', '\\'));
            else names.Add(s);
        }
        return (names, prefixes);
    }
}
