using System.Collections.Generic;
using System.Text;
using Cthangover.Core.Mods.Testing;

namespace Mods.ModManager.Tests
{
    /// <summary>Colouring preserves source literally and recognises each supported fence grammar.</summary>
    public class ModCodeSyntaxTests
    {
        [ModTest("documentation/code_languages")]
        static void Languages(ModTestContext ctx)
        {
            ModAssert.Equal("csharp", ModCodeSyntax.Language(" C# title=example "));
            ModAssert.Equal("gdscript", ModCodeSyntax.Language("GD"));
            ModAssert.Equal("bash", ModCodeSyntax.Language("shell"));
            ModAssert.Equal("yaml", ModCodeSyntax.Language("yml"));
            ModAssert.Equal("glsl", ModCodeSyntax.Language("gdshader"));
            ModAssert.Equal("", ModCodeSyntax.Language(null));
            ModAssert.Equal("unknown", ModCodeSyntax.Language("unknown"));
        }

        [ModTest("documentation/code_jsonc")]
        static void Jsonc(ModTestContext ctx)
        {
            const string source = "{\r\n\t\"name\": \"// literal \\\"quoted\\\" Русский\", // comment\r\n  \"enabled\": true, /* two\nlines */ \"count\": 22\n}  ";
            var spans = ModCodeSyntax.Tokenize("jsonc", source);
            ModAssert.Equal(source, Join(spans), "all whitespace and Unicode preserved");
            Expect(spans, "\"name\"", "attr");
            Expect(spans, "\"// literal \\\"quoted\\\" Русский\"", "string");
            Expect(spans, "// comment", "comment");
            Expect(spans, "/* two\nlines */", "comment");
            Expect(spans, "true", "literal");
            Expect(spans, "22", "number");
        }

        [ModTest("documentation/code_site_languages")]
        static void SiteLanguages(ModTestContext ctx)
        {
            Expect(ModCodeSyntax.Tokenize("csharp", "public class Example { var value = 42; }"), "public", "keyword");
            Expect(ModCodeSyntax.Tokenize("gdscript", "func start():\n    # comment\n    return true"), "func", "keyword");
            var scenario = ModCodeSyntax.Tokenize("scenario", "scene: intro\n---\ntext \"# literal\" key=intro\noption \"Go\" -> :next\n-- comment");
            Expect(scenario, "text", "keyword");
            Expect(scenario, "\"# literal\"", "string");
            Expect(scenario, "key", "attr");
            Expect(scenario, ":next", "label");
            Expect(scenario, "---", "meta");
            Expect(scenario, "-- comment", "comment");
            var tree = ModCodeSyntax.Tokenize("filestree", "mods/\n└── music_pack/ # shared music\n    └── manifest.json");
            Expect(tree, "music_pack/", "directory");
            Expect(tree, "# shared music", "treecomment");
            Expect(ModCodeSyntax.Tokenize("glsl", "uniform vec4 color;\nvoid fragment() { COLOR = color; }"), "uniform", "keyword");
            Expect(ModCodeSyntax.Tokenize("bash", "export GAME=demo # comment\necho $GAME"), "$GAME", "variable");
            Expect(ModCodeSyntax.Tokenize("python", "def start():\n    return True"), "def", "keyword");
            Expect(ModCodeSyntax.Tokenize("yaml", "enabled: true\ncount: 22"), "enabled", "attr");
            Expect(ModCodeSyntax.Tokenize("ini", "[section]\nname=value\n# comment"), "[section]", "meta");
            Expect(ModCodeSyntax.Tokenize("properties", "mod/name=Музыка\n"), "Музыка", "string");
        }

        [ModTest("documentation/code_plain_fallback")]
        static void PlainFallback(ModTestContext ctx)
        {
            const string literal = "[url=javascript:bad]literal[/url] <script> // # true\n\t  ";
            foreach (var language in new[] { "", "text", "unknown", "mermaid" })
            {
                var spans = ModCodeSyntax.Tokenize(language, literal);
                ModAssert.Equal(literal, Join(spans), language + " preserves literal source");
                foreach (var span in spans)
                    ModAssert.Equal("", span.Kind, language + " does not guess a language");
            }
            var large = new string('x', 65537);
            var oversized = ModCodeSyntax.Tokenize("json", large);
            ModAssert.Equal(large, Join(oversized), "oversized source remains readable");
            ModAssert.Equal("", oversized[0].Kind, "oversized source skips colouring");
        }

        private static string Join(IReadOnlyList<(string Text, string Kind)> spans)
        {
            var result = new StringBuilder();
            foreach (var span in spans)
                result.Append(span.Text);
            return result.ToString();
        }

        private static void Expect(IReadOnlyList<(string Text, string Kind)> spans, string text, string kind)
        {
            foreach (var span in spans)
                if (span.Text == text && span.Kind == kind)
                    return;
            ModAssert.True(false, "expected " + kind + " token: " + text);
        }
    }
}
