using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Mods.ModManager
{
    /// <summary>
    /// Lexical colouring for fenced documentation examples. Follows the site's
    /// explicit-language approach and custom scenario/filestree/jsonc rules,
    /// returning literal text spans rather than HTML or BBCode. Unknown languages,
    /// oversized examples and timeouts retain their complete uncoloured source.
    /// </summary>
    public static class ModCodeSyntax
    {
        private const string Strings = "(?<string>\"\"\"[\\s\\S]*?\"\"\"|'''[\\s\\S]*?'''|(?:\\$@|@\\$|@)\"(?:\"\"|[^\"])*\"|\\$?\"(?:\\\\.|[^\"\\\\\\r\\n])*\"|'(?:\\\\.|[^'\\\\\\r\\n])*')";
        private const string Tokens = @"(?<number>\b(?:0[xX][0-9a-fA-F]+|\d+(?:\.\d+)?(?:[eE][+-]?\d+)?)[fFdDmM]?\b)|(?<variable>\$\{[^}\r\n]+\}|\$[A-Za-z_]\w*)|(?<name>[A-Za-z_]\w*)|(?<operator>[{}\[\](),:;=+*/<>!&|.\-?])";
        private static readonly Regex CStyle = Create(@"(?<comment>//[^\r\n]*|/\*[\s\S]*?\*/)|" + Strings + "|" + Tokens);
        private static readonly Regex Json = Create(Strings + "|" + Tokens);
        private static readonly Regex HashStyle = Create(@"(?<comment>\#[^\r\n]*)|" + Strings + "|" + Tokens);
        private static readonly Regex Scenario = Create(@"(?<meta>^---[ \t]*$)|(?<comment>\#[^\r\n]*|--[^\r\n]*)|" + Strings + @"|(?<label>:[A-Za-z_]\w*)|(?<operator>->)|" + Tokens);
        private static readonly Regex Glsl = Create(@"(?<meta>^[ \t]*\#[^\r\n]*)|(?<comment>//[^\r\n]*|/\*[\s\S]*?\*/)|" + Strings + "|" + Tokens);
        private static readonly Regex FileTree = Create(@"(?<treecomment>[#←][^\r\n]*)|(?<comment>[│├└][─ \t]*)|(?<directory>[\p{L}\p{N}_.{}-]+/|/)");
        private static readonly Regex Properties = Create(@"(?<comment>^[ \t]*[#!;][^\r\n]*)|(?<meta>^[ \t]*\[[^\]\r\n]+\])|(?<attr>^[ \t]*[^=\r\n]+(?==))|(?<string>(?<==)[^\r\n]*)");
        private static readonly Dictionary<string, HashSet<string>> Keywords = new(StringComparer.Ordinal)
        {
            ["csharp"] = Words("abstract as async await base bool break byte case catch char checked class const continue decimal default delegate do double dynamic else enum event explicit extern false finally fixed float for foreach get global goto if implicit in init int interface internal is lock long namespace new null object operator out override params partial private protected public readonly record ref required return sbyte sealed set short sizeof stackalloc static string struct switch this throw true try typeof uint ulong unchecked unmanaged unsafe ushort using value var virtual void volatile when where while with yield"),
            ["gdscript"] = Words("and as assert await break breakpoint class class_name const continue elif else enum extends false for func if in is match not null or pass preload return self signal static super true var void while yield"),
            ["scenario"] = Words("scene priority condition is_one_run save_allowed light_use_time background foreground background_color background_show_hide text p_text title select option switch_scene end action music music_play music_pause sound light_set delay effect animation interactive_add interactive_remove interactive_clear interactive_set show_dialog hide_dialog set flag empty goto"),
            ["glsl"] = Words("attribute bool break bvec2 bvec3 bvec4 case centroid coherent const continue default discard do double else flat float for highp if in inout int invariant ivec2 ivec3 ivec4 layout lowp mat2 mat3 mat4 mediump out precision readonly restrict return sampler2D samplerCube smooth struct switch uniform uint uvec2 uvec3 uvec4 varying vec2 vec3 vec4 void volatile while writeonly shader_type render_mode spatial canvas_item particles sky fog"),
            ["bash"] = Words("alias case cd do done echo elif else esac exit export fi for function if in local printf read readonly return set source then unset until while"),
            ["python"] = Words("and as assert async await break class continue def del elif else except False finally for from global if import in is lambda None nonlocal not or pass raise return True try while with yield"),
        };

        /// <summary>Reads the first fence-info word and resolves the aliases used in mod documentation.</summary>
        public static string Language(string info)
        {
            if (string.IsNullOrWhiteSpace(info))
                return "";
            var tag = info.Trim();
            var end = tag.IndexOfAny(new[] { ' ', '\t', '\r', '\n' });
            if (end >= 0)
                tag = tag.Substring(0, end);
            return tag.ToLowerInvariant() switch
            {
                "cs" or "c#" => "csharp",
                "gd" => "gdscript",
                "sh" or "shell" => "bash",
                "py" => "python",
                "yml" => "yaml",
                "cfg" or "toml" => "ini",
                "tscn" or "tres" => "ini",
                "gdshader" or "shader" => "glsl",
                var language => language,
            };
        }

        /// <summary>
        /// Returns spans whose concatenation is exactly source, including newlines,
        /// tabs and trailing spaces. Kind names follow the site's syntax palette.
        /// </summary>
        public static IReadOnlyList<(string Text, string Kind)> Tokenize(string info, string source)
        {
            source ??= "";
            var language = Language(info);
            var grammar = Grammar(language);
            if (grammar == null || source.Length > 65536)
                return Plain(source);
            var result = new List<(string Text, string Kind)>();
            try
            {
                var offset = 0;
                foreach (Match match in grammar.Matches(source))
                {
                    if (match.Index > offset)
                        result.Add((source.Substring(offset, match.Index - offset), ""));
                    var kind = Kind(match, source, language);
                    result.Add((match.Value, kind));
                    offset = match.Index + match.Length;
                    if (result.Count > 10000)
                        return Plain(source);
                }
                if (offset < source.Length)
                    result.Add((source.Substring(offset), ""));
            }
            catch (RegexMatchTimeoutException)
            {
                return Plain(source);
            }
            return result;
        }

        private static IReadOnlyList<(string Text, string Kind)> Plain(string source)
            => new[] { (source, "") };

        private static Regex Create(string pattern)
            => new Regex(pattern, RegexOptions.Multiline | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

        private static HashSet<string> Words(string words)
            => new HashSet<string>(words.Split(' '), StringComparer.Ordinal);

        private static Regex Grammar(string language) => language switch
        {
            "json" => Json,
            "jsonc" or "csharp" => CStyle,
            "scenario" => Scenario,
            "glsl" => Glsl,
            "gdscript" or "bash" or "python" or "yaml" => HashStyle,
            "filestree" => FileTree,
            "ini" or "properties" => Properties,
            _ => null,
        };

        private static string Kind(Match token, string source, string language)
        {
            foreach (var kind in new[] { "comment", "meta", "label", "directory", "treecomment", "attr" })
                if (token.Groups[kind].Success)
                    return kind;
            var next = token.Index + token.Length;
            while (next < source.Length && (source[next] == ' ' || source[next] == '\t'))
                next++;
            var following = next < source.Length ? source[next] : '\0';
            if (token.Groups["string"].Success)
                return (language == "json" || language == "jsonc" || language == "yaml") && following == ':' ? "attr" : "string";
            if (token.Groups["number"].Success)
                return "number";
            if (token.Groups["operator"].Success)
                return "operator";
            if (token.Groups["variable"].Success)
                return language == "bash" ? "variable" : "";
            var word = token.Value;
            if (word == "true" || word == "false" || word == "null" || word == "True" || word == "False" || word == "None")
                return "literal";
            if (language == "scenario" && following == '=' || language == "yaml" && following == ':')
                return "attr";
            if (Keywords.TryGetValue(language, out var keywords) && keywords.Contains(word))
                return "keyword";
            if (language == "glsl" && word.StartsWith("gl_", StringComparison.Ordinal))
                return "built_in";
            if (language == "csharp" || language == "gdscript" || language == "python" || language == "glsl")
            {
                if (following == '(')
                    return "function";
                if (char.IsUpper(word[0]))
                    return "type";
            }
            return "";
        }
    }
}
