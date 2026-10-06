using System.IO;
using System.Security;
using System.Windows;
using System.Windows.Media;
using System.Xml;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;
using ICSharpCode.AvalonEdit.Rendering;

namespace CodeSearch;

/// <summary>Maps a file extension to an AvalonEdit syntax highlighting definition.</summary>
public static class SyntaxMap
{
    static readonly Dictionary<string, string> Builtin = new(StringComparer.OrdinalIgnoreCase)
    {
        ["cs"] = "C#", ["csx"] = "C#",
        ["js"] = "JavaScript", ["mjs"] = "JavaScript", ["cjs"] = "JavaScript", ["jsx"] = "JavaScript",
        ["json"] = "Json", ["ipynb"] = "Json", ["jsonc"] = "Json", ["asmdef"] = "Json", ["asmref"] = "Json",
        ["html"] = "HTML", ["htm"] = "HTML", ["vue"] = "HTML", ["svelte"] = "HTML",
        ["cshtml"] = "ASP/XHTML", ["razor"] = "ASP/XHTML", ["aspx"] = "ASP/XHTML", ["ascx"] = "ASP/XHTML",
        ["css"] = "CSS", ["scss"] = "CSS", ["sass"] = "CSS", ["less"] = "CSS", ["uss"] = "CSS",
        ["c"] = "C++", ["h"] = "C++", ["cpp"] = "C++", ["hpp"] = "C++", ["cc"] = "C++", ["cxx"] = "C++", ["hh"] = "C++",
        ["m"] = "C++", ["mm"] = "C++", ["ino"] = "C++",
        ["java"] = "Java",
        ["ps1"] = "PowerShell", ["psm1"] = "PowerShell", ["psd1"] = "PowerShell",
        ["php"] = "PHP", ["py"] = "Python", ["pyw"] = "Python",
        ["sql"] = "TSQL", ["vb"] = "VB", ["boo"] = "Boo", ["tex"] = "TeX", ["patch"] = "Patch", ["diff"] = "Patch",
        ["md"] = "MarkDown", ["markdown"] = "MarkDown",
        ["xml"] = "XML", ["xaml"] = "XML", ["axaml"] = "XML", ["csproj"] = "XML", ["vbproj"] = "XML", ["fsproj"] = "XML",
        ["props"] = "XML", ["targets"] = "XML", ["config"] = "XML", ["nuspec"] = "XML", ["resx"] = "XML", ["svg"] = "XML",
        ["uxml"] = "XML", ["xsd"] = "XML", ["xsl"] = "XML", ["xslt"] = "XML", ["plist"] = "XML", ["manifest"] = "XML",
    };

    static readonly Dictionary<string, string> Custom = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ts"] = "TypeScript", ["tsx"] = "TypeScript", ["mts"] = "TypeScript", ["cts"] = "TypeScript",
        ["go"] = "Go", ["rs"] = "Rust", ["kt"] = "Kotlin", ["kts"] = "Kotlin", ["gradle"] = "Kotlin",
        ["swift"] = "Swift", ["dart"] = "Dart", ["fs"] = "F#", ["fsx"] = "F#",
        ["hlsl"] = "Shader", ["glsl"] = "Shader", ["shader"] = "Shader", ["cginc"] = "Shader", ["compute"] = "Shader",
        ["fx"] = "Shader", ["vert"] = "Shader", ["frag"] = "Shader", ["wgsl"] = "Shader",
        ["proto"] = "Protobuf", ["graphql"] = "GraphQL", ["gql"] = "GraphQL",
        ["gd"] = "GDScript", ["sh"] = "Shell", ["bash"] = "Shell", ["zsh"] = "Shell", ["rb"] = "Ruby",
        ["r"] = "R", ["pl"] = "Perl", ["lua"] = "Lua", ["bat"] = "Batch", ["cmd"] = "Batch",
        ["yaml"] = "YAML", ["yml"] = "YAML", ["toml"] = "INI", ["ini"] = "INI", ["cfg"] = "INI", ["properties"] = "INI",
        ["editorconfig"] = "INI", ["tscn"] = "INI", ["tres"] = "INI", ["godot"] = "INI", ["tf"] = "Terraform", ["cmake"] = "CMake",
    };

    static readonly Dictionary<string, IHighlightingDefinition> Cache = new();

    public static (IHighlightingDefinition def, string name) For(string path)
    {
        var ext = Path.GetExtension(path).TrimStart('.');
        if (ext.Length == 0) ext = Path.GetFileName(path).TrimStart('.');
        if (Builtin.TryGetValue(ext, out var b))
        {
            var d = HighlightingManager.Instance.GetDefinition(b);
            if (d != null) return (d, b);
        }
        if (Custom.TryGetValue(ext, out var c))
        {
            if (!Cache.TryGetValue(c, out var d))
            {
                try { d = Load(Build(c)); } catch { d = null; }
                Cache[c] = d;
            }
            if (d != null) return (d, c);
        }
        return (null, null);
    }

    static IHighlightingDefinition Load(string xshd)
    {
        using var r = XmlReader.Create(new StringReader(xshd));
        return HighlightingLoader.Load(HighlightingLoader.LoadXshd(r), HighlightingManager.Instance);
    }

    // ------------------------------------------------------------------ custom definitions

    const string Num = @"\b0[xX][0-9a-fA-F_]+[uUlL]*|\b0[bB][01_]+|(\b\d[\d_]*(\.\d+)?|\.\d+)([eE][+\-]?\d+)?[fFdDmMuUlL]*";

    static string Build(string lang) => lang switch
    {
        "TypeScript" => CLike(lang,
            "abstract as asserts async await break case catch class const constructor continue debugger declare default delete do else enum export extends finally for from function get if implements import in infer instanceof interface is keyof let module namespace new of override private protected public readonly return satisfies set static super switch this throw try type typeof unique var void while with yield",
            "any bigint boolean never null number object string symbol undefined unknown true false",
            backtick: true),
        "Go" => CLike(lang,
            "break case chan const continue default defer else fallthrough for func go goto if import interface map package range return select struct switch type var",
            "bool byte complex64 complex128 error float32 float64 int int8 int16 int32 int64 rune string uint uint8 uint16 uint32 uint64 uintptr true false nil iota any",
            backtick: true),
        "Rust" => CLike(lang,
            "as async await break const continue crate dyn else enum extern fn for if impl in let loop match mod move mut pub ref return self Self static struct super trait type unsafe use where while macro_rules",
            "bool char f32 f64 i8 i16 i32 i64 i128 isize str u8 u16 u32 u64 u128 usize String Vec Option Result Box Some None Ok Err true false",
            singleQuote: false),
        "Kotlin" => CLike(lang,
            "abstract actual annotation as break by catch class companion const constructor continue crossinline data do else enum expect external final finally for fun get if import in infix init inline inner interface internal is lateinit noinline object open operator out override package private protected public reified return sealed set super suspend tailrec this throw try typealias val var vararg when where while",
            "Any Boolean Byte Char Double Float Int Long Nothing Short String Unit Array List Map Set null true false"),
        "Swift" => CLike(lang,
            "associatedtype break case catch class continue default defer deinit do else enum extension fallthrough fileprivate for func guard if import in init inout internal is let open operator private protocol public repeat rethrows return self Self some static struct subscript super switch throw throws try typealias var where while async await actor",
            "Any Bool Character Double Float Int String Void Array Dictionary Optional nil true false"),
        "Dart" => CLike(lang,
            "abstract as assert async await break case catch class const continue covariant default deferred do dynamic else enum export extends extension external factory final finally for get if implements import in interface is late library mixin new on operator part required rethrow return sealed set show static super switch sync this throw try typedef var void when while with yield",
            "bool double int num String List Map Set Future Stream Object null true false"),
        "F#" => CLike(lang,
            "abstract and as assert base begin class default delegate do done downcast downto elif else end exception extern for fun function global if in inherit inline interface internal lazy let match member module mutable namespace new of open or override private public rec return static struct then to try type upcast use val void when while with yield",
            "bool byte char decimal double float int int64 obj sbyte string uint unit list option seq true false null",
            blockBegin: @"\(\*", blockEnd: @"\*\)"),
        "Shader" => CLike(lang,
            "break case const continue default discard do else for if in inout out return struct switch uniform varying attribute layout precision static cbuffer register sampler sampler2D samplerCUBE Texture2D SamplerState technique pass CGPROGRAM ENDCG HLSLPROGRAM ENDHLSL Shader Properties SubShader Pass Tags Cull ZWrite ZTest Blend LOD Fallback",
            "void bool int uint float half fixed double float2 float3 float4 float4x4 float3x3 half2 half3 half4 fixed2 fixed3 fixed4 int2 int3 int4 vec2 vec3 vec4 mat2 mat3 mat4 ivec2 ivec3 ivec4 true false",
            preprocessor: true),
        "Protobuf" => CLike(lang,
            "syntax package import option message enum service rpc returns oneof map reserved repeated optional required extend stream",
            "double float int32 int64 uint32 uint64 sint32 sint64 fixed32 fixed64 sfixed32 sfixed64 bool string bytes true false"),
        "GraphQL" => Hash(lang,
            "query mutation subscription fragment on type interface union enum input scalar schema extend directive implements",
            "Int Float String Boolean ID true false null"),
        "Terraform" => Hash(lang,
            "resource data variable output locals module provider terraform for_each count depends_on lifecycle dynamic content",
            "string number bool list map set object any true false null", alsoSlashComments: true),
        "GDScript" => Hash(lang,
            "and as assert await break breakpoint class class_name const continue elif else enum export extends for func if in is match not onready or pass preload return self setget signal static super tool var while yield @export @onready @tool",
            "bool int float String Vector2 Vector3 Color Array Dictionary Node null true false PI TAU INF NAN"),
        "Shell" => Hash(lang,
            "if then else elif fi case esac for while until do done in function return local export readonly declare unset shift exit break continue source alias echo cd set trap eval exec",
            "true false"),
        "Ruby" => Hash(lang,
            "alias and begin break case class def defined? do else elsif end ensure for if in module next not or redo rescue retry return self super then undef unless until when while yield require require_relative attr_accessor attr_reader puts",
            "nil true false"),
        "R" => Hash(lang, "if else repeat while function for in next break library require return", "TRUE FALSE NULL NA Inf NaN"),
        "Perl" => Hash(lang, "my our local sub if elsif else unless while until for foreach last next redo return use require package print", "undef"),
        "CMake" => Hash(lang,
            "if elseif else endif foreach endforeach while endwhile function endfunction macro endmacro set unset project add_executable add_library target_link_libraries target_include_directories include find_package option message install",
            "ON OFF TRUE FALSE PUBLIC PRIVATE INTERFACE REQUIRED", ignoreCase: true),
        "Lua" => Hash(lang,
            "and break do else elseif end for function goto if in local not or repeat return then until while",
            "nil true false self", comment: "--"),
        "Batch" => Hash(lang,
            "if else for in do goto call exit set setlocal endlocal echo not exist defined errorlevel shift pause start rem",
            "on off nul", comment: @"(?i)\b(rem)\b|::", ignoreCase: true),
        "YAML" => Hash(lang, "", "true false null yes no on off ~", keyRule: @"^\s*-?\s*[\w\-\.\$/""']+(?=\s*:(\s|$))"),
        "INI" => Hash(lang, "", "true false", comment: "[#;]", keyRule: @"^\s*[\w\-\.\$/]+(?=\s*=)", sectionRule: @"^\s*\[[^\]]*\]"),
        _ => null
    };

    static string Words(string list) =>
        string.Concat(list.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(w => $"<Word>{SecurityElement.Escape(w)}</Word>"));

    static string Head(string name, bool ignoreCase) => $@"<?xml version=""1.0""?>
<SyntaxDefinition name=""{name}"" xmlns=""http://icsharpcode.net/sharpdevelop/syntaxdefinition/2008"">
  <Color name=""Comment"" foreground=""#008000"" />
  <Color name=""String"" foreground=""#A31515"" />
  <Color name=""Keyword"" foreground=""#0000FF"" />
  <Color name=""Type"" foreground=""#2B91AF"" />
  <Color name=""Number"" foreground=""#098658"" />
  <Color name=""Preprocessor"" foreground=""#808080"" />
  <Color name=""Key"" foreground=""#0451A5"" />
  <Color name=""Section"" foreground=""#800080"" fontWeight=""bold"" />
  <RuleSet ignoreCase=""{(ignoreCase ? "true" : "false")}"">";

    const string Tail = "\n  </RuleSet>\n</SyntaxDefinition>";

    static string Str(char q, bool multiline = false) =>
        $@"<Span color=""String"" multiline=""{(multiline ? "true" : "false")}""><Begin>{SecurityElement.Escape(q.ToString())}</Begin><End>{SecurityElement.Escape(q.ToString())}</End><RuleSet><Span begin=""\\"" end=""."" /></RuleSet></Span>";

    static string CLike(string name, string keywords, string types, bool backtick = false, bool singleQuote = true,
                        bool preprocessor = false, string blockBegin = @"/\*", string blockEnd = @"\*/") =>
        Head(name, false) +
        (preprocessor ? @"<Span color=""Preprocessor"" begin=""^\s*\#"" />" : "") +
        @"<Span color=""Comment"" begin=""//"" />" +
        $@"<Span color=""Comment"" multiline=""true"" begin=""{blockBegin}"" end=""{blockEnd}"" />" +
        Str('"') + (singleQuote ? Str('\'') : "") + (backtick ? Str('`', true) : "") +
        $@"<Keywords color=""Keyword"">{Words(keywords)}</Keywords>" +
        $@"<Keywords color=""Type"">{Words(types)}</Keywords>" +
        $@"<Rule color=""Number"">{Num}</Rule>" + Tail;

    static string Hash(string name, string keywords, string literals, string comment = "\\#", bool ignoreCase = false,
                       bool alsoSlashComments = false, string keyRule = null, string sectionRule = null) =>
        Head(name, ignoreCase) +
        $@"<Span color=""Comment"" begin=""{SecurityElement.Escape(comment)}"" />" +
        (alsoSlashComments ? @"<Span color=""Comment"" begin=""//"" /><Span color=""Comment"" multiline=""true"" begin=""/\*"" end=""\*/"" />" : "") +
        Str('"') + Str('\'') +
        (sectionRule != null ? $@"<Rule color=""Section"">{SecurityElement.Escape(sectionRule)}</Rule>" : "") +
        (keyRule != null ? $@"<Rule color=""Key"">{SecurityElement.Escape(keyRule)}</Rule>" : "") +
        (keywords.Length > 0 ? $@"<Keywords color=""Keyword"">{Words(keywords)}</Keywords>" : "") +
        (literals.Length > 0 ? $@"<Keywords color=""Type"">{Words(literals)}</Keywords>" : "") +
        $@"<Rule color=""Number"">{Num}</Rule>" + Tail;
}

/// <summary>Paints the search matches (current <see cref="Hl.Matcher"/>) with a yellow background.</summary>
public sealed class MatchColorizer : DocumentColorizingTransformer
{
    static readonly Brush Back = new SolidColorBrush(Color.FromRgb(0xFD, 0xE0, 0x47));
    static MatchColorizer() => Back.Freeze();

    protected override void ColorizeLine(DocumentLine line)
    {
        if (line.Length == 0 || line.Length > 5000) return;
        var text = CurrentContext.Document.GetText(line);
        foreach (var (s, len) in Hl.Matcher(text))
        {
            if (len <= 0 || s < 0 || s + len > text.Length) continue;
            ChangeLinePart(line.Offset + s, line.Offset + s + len, el =>
            {
                el.BackgroundBrush = Back;
                el.TextRunProperties.SetTypeface(new Typeface(el.TextRunProperties.Typeface.FontFamily,
                    FontStyles.Normal, FontWeights.Bold, FontStretches.Normal));
            });
        }
    }
}

/// <summary>Draws a full-width band behind the line that contains the selected hit.</summary>
public sealed class HitLineRenderer : IBackgroundRenderer
{
    static readonly Brush Fill = new SolidColorBrush(Color.FromRgb(0xE0, 0xEC, 0xFF));
    static readonly Brush Match = new SolidColorBrush(Color.FromRgb(0xFD, 0xE0, 0x47));
    static readonly Pen Edge = new(new SolidColorBrush(Color.FromRgb(0x60, 0xA5, 0xFA)), 1);
    static HitLineRenderer() { Fill.Freeze(); Match.Freeze(); Edge.Freeze(); }

    public int Line { get; set; }
    public KnownLayer Layer => KnownLayer.Background;

    public void Draw(TextView textView, DrawingContext dc)
    {
        if (Line <= 0 || textView.Document == null || Line > textView.Document.LineCount) return;
        foreach (var v in textView.VisualLines)
        {
            if (v.FirstDocumentLine.LineNumber > Line || v.LastDocumentLine.LineNumber < Line) continue;
            double y = v.VisualTop - textView.VerticalOffset;
            var r = new Rect(0, y, textView.ActualWidth, v.Height);
            dc.DrawRectangle(Fill, null, r);
            dc.DrawLine(Edge, new Point(0, y + 0.5), new Point(r.Width, y + 0.5));
            dc.DrawLine(Edge, new Point(0, y + v.Height - 0.5), new Point(r.Width, y + v.Height - 0.5));

            // Re-paint the match boxes above the band so they stay visible on the hit line.
            var docLine = textView.Document.GetLineByNumber(Line);
            if (docLine.Length > 5000) continue;
            var text = textView.Document.GetText(docLine);
            foreach (var (s, len) in Hl.Matcher(text))
            {
                if (len <= 0 || s < 0 || s + len > text.Length) continue;
                var seg = new TextSegment { StartOffset = docLine.Offset + s, Length = len };
                foreach (var rect in BackgroundGeometryBuilder.GetRectsForSegment(textView, seg))
                    dc.DrawRectangle(Match, null, rect);
            }
        }
    }
}
