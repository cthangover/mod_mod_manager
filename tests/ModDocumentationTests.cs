using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Cthangover.Core.Mods.Providers;
using Cthangover.Core.Mods.Testing;

namespace Mods.ModManager.Tests
{
    /// <summary>Storage and fallback contracts, including malformed files and compressed mods.</summary>
    public class ModDocumentationTests
    {
        [ModTest("documentation/language_fallback")]
        static void LanguageFallback(ModTestContext ctx)
        {
            var docs = new[]
            {
                new ModDocument("de", "docs/de.md", "Deutsch"),
                new ModDocument("en", "docs/en.md", "English"),
                new ModDocument("ru", "docs/ru.md", "Русский"),
                new ModDocument("default", "docs/default.md", "Default"),
            };
            ModAssert.Equal("ru", ModDocumentation.Select(docs, "RU_ru").Language, "regional language");
            ModAssert.Equal("default", ModDocumentation.Select(docs, "ja").Language, "author fallback");
            ModAssert.Equal("en", ModDocumentation.Select(docs.Take(3).ToArray(), "ja").Language, "English fallback");
            ModAssert.Equal("de", ModDocumentation.Select(docs.Take(1).ToArray(), "ja").Language, "first available");
            ModAssert.Null(ModDocumentation.Select(Array.Empty<ModDocument>(), "ru"), "legacy mod");
        }

        [ModTest("documentation/folder_and_zip")]
        static void FolderAndZip(ModTestContext ctx)
        {
            var root = Path.Combine(ctx.SandboxDir, "documentation");
            var zip = Path.Combine(ctx.SandboxDir, "documentation.zip");
            Directory.CreateDirectory(Path.Combine(root, "docs", "assets"));
            File.WriteAllText(Path.Combine(root, "docs", "ru.md"), "# Русский\n![Example](assets/picture.png)");
            File.WriteAllText(Path.Combine(root, "docs", "en.md"), "# English");
            File.WriteAllText(Path.Combine(root, "docs", "README.md"), "Not a language");
            File.WriteAllText(Path.Combine(root, "docs", "ja.md"), " \n ");
            File.WriteAllBytes(Path.Combine(root, "docs", "ko.md"), new byte[] { 0xFF, 0xFF });
            File.WriteAllBytes(Path.Combine(root, "docs", "assets", "picture.png"), new byte[] { 1, 2, 3 });
            try
            {
                using var folder = new FolderModFileProvider(root, "documentation_fixture");
                var local = ModDocumentation.Discover(folder);
                ModAssert.Equal(2, local.Count, "only readable language files");
                ModAssert.True(ModDocumentation.Select(local, "ru").Text.Contains("Русский"), "UTF-8 preserved");
                ModAssert.Null(ModDocumentation.ReadResource(folder, "docs/en.md", 2), "size limit");
                ModAssert.Null(ModDocumentation.ReadResource(folder, "docs/missing.md", 100), "missing file");
                ZipFile.CreateFromDirectory(root, zip);
                using var archive = new ZipModFileProvider(zip, "documentation_fixture");
                var packed = ModDocumentation.Discover(archive);
                ModAssert.Equal(local.Count, packed.Count, "ZIP discovery");
                ModAssert.Equal(ModDocumentation.Select(local, "ru").Text, ModDocumentation.Select(packed, "ru").Text, "ZIP text");
                ModAssert.Equal(3, ModDocumentation.ReadResource(archive, "docs/assets/picture.png", 100).Length, "ZIP image bytes");
            }
            finally
            {
                if (File.Exists(zip))
                    File.Delete(zip);
                Directory.Delete(root, true);
            }
        }

        [ModTest("documentation/resource_paths")]
        static void ResourcePaths(ModTestContext ctx)
        {
            ModAssert.Equal("docs/assets/a.png", ModDocumentation.ResolveResource("docs/ru.md", "assets/a.png"));
            ModAssert.Equal("docs/assets/a.png", ModDocumentation.ResolveResource("docs/ru/page.md", "../assets/a.png"));
            foreach (var path in new[] { "../../secret.png", "/tmp/a.png", "C:\\a.png", "https://example.com/a.png", "file:///etc/passwd", "\\server\\a.png" })
                ModAssert.Null(ModDocumentation.ResolveResource("docs/ru.md", path), path);
        }

        [ModTest("documentation/markdown_literals")]
        static void MarkdownLiterals(ModTestContext ctx)
        {
            var view = new ModMarkdownView();
            ctx.SandboxRoot.AddChild(view);
            view.Display(null, new ModDocument("en", "docs/en.md", "# Heading\n\n**Bold** and `code`\n\n```json\n[url=javascript:bad]literal[/url]\n```\n\n<script>literal HTML</script>\n\n[Visible](javascript:bad)"));
            var text = view.GetParsedText();
            ModAssert.True(text.Contains("Heading") && text.Contains("Bold and code"), "Markdown text preserved");
            ModAssert.True(text.Contains("[url=javascript:bad]literal[/url]"), "code never becomes BBCode");
            ModAssert.True(text.Contains("<script>literal HTML</script>"), "HTML never executes");
            ModAssert.True(text.Contains("Visible"), "unsupported link retains label");
            view.Display(null, null);
            ModAssert.Equal("", view.GetParsedText(), "clearing previous document");
        }
    }
}
