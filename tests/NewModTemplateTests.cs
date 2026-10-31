namespace Mods.ModManager.Tests
{
    using System.Linq;
    using System.Text.Json;
    using Cthangover.Core.Mods.Testing;

    /// <summary>The new-mod wizard writes exactly the manifest and locale stubs its rules describe, and never a repository.</summary>
    public class NewModTemplateTests
    {
        [ModTest("new_mod/id_rules")]
        static void IdRules(ModTestContext ctx)
        {
            ModAssert.True(NewModTemplate.IsValidId("my_first"), "a plain snake_case id");
            ModAssert.True(NewModTemplate.IsValidId("tavern2"), "digits after the first letter");
            ModAssert.False(NewModTemplate.IsValidId("My_First"), "no capitals");
            ModAssert.False(NewModTemplate.IsValidId("ab"), "at least three characters");
            ModAssert.False(NewModTemplate.IsValidId("1first"), "a letter first");
            ModAssert.False(NewModTemplate.IsValidId("my-mod"), "no dashes");
            ModAssert.False(NewModTemplate.IsValidId("core"), "core is the kernel");
            ModAssert.False(NewModTemplate.IsValidId("my_core"), "core as a word is refused");
            ModAssert.False(NewModTemplate.IsValidId("base_items"), "base is a suffix only");
            ModAssert.False(NewModTemplate.IsValidId("items_base_x"), "base as a word is refused");
            ModAssert.False(NewModTemplate.IsValidId(null), "empty is refused");
        }

        [ModTest("new_mod/suggested_id")]
        static void SuggestedId(ModTestContext ctx)
        {
            ModAssert.Equal("my_first_mod", NewModTemplate.SuggestId("My First Mod!"), "latin words joined");
            ModAssert.Equal("my_mod", NewModTemplate.SuggestId("Моя вторая карта"), "no latin text falls back");
            ModAssert.True(NewModTemplate.IsValidId(NewModTemplate.SuggestId("Tavern 2")), "the suggestion is valid");
        }

        [ModTest("new_mod/manifest_is_json_with_one_dependency")]
        static void ManifestIsJson(ModTestContext ctx)
        {
            var text = NewModTemplate.Manifest("my_first", "My \"first\" mod", "Line one\nline two", "rpg>=1");
            ModAssert.False(text.Contains("\r"), "LF line ends");
            ModAssert.True(text.EndsWith("\n") && !text.EndsWith("\n\n"), "one final newline");
            using var document = JsonDocument.Parse(text);
            var root = document.RootElement;
            ModAssert.Equal("my_first", root.GetProperty("id").GetString(), "id");
            ModAssert.Equal("My \"first\" mod", root.GetProperty("name").GetString(), "name keeps its quotes");
            ModAssert.Equal("Line one line two", root.GetProperty("description").GetString(), "description on one line");
            ModAssert.Equal("0.1.0", root.GetProperty("version").GetString(), "version");
            ModAssert.Equal("rpg>=1", root.GetProperty("depends")[0].GetString(), "dependency");
            ModAssert.Equal(1, root.GetProperty("depends").GetArrayLength(), "one dependency");
            ModAssert.Equal("src/**/*.cs", root.GetProperty("sources")[0].GetString(), "sources glob");
            ModAssert.Equal("story", root.GetProperty("category").GetString(), "category is a CategoryType name");
            ModAssert.True(text.Contains("\"rpg>=1\""), "the dependency is written as typed, not escaped");
        }

        [ModTest("new_mod/locale_stub_has_both_titles")]
        static void LocaleStub(ModTestContext ctx)
        {
            var text = NewModTemplate.Locale("my_first", "Первая\nкарта", "Описание");
            ModAssert.True(text.Contains("mod_my_first_name = Первая карта\n"), "name key, one line");
            ModAssert.True(text.Contains("mod_my_first_desc = Описание\n"), "description key");
            ModAssert.Equal(2, text.Split('\n').Count(l => l.StartsWith("mod_", System.StringComparison.Ordinal)), "exactly the two titles");
        }

        [ModTest("new_mod/names")]
        static void Names(ModTestContext ctx)
        {
            ModAssert.False(NewModTemplate.IsValidName("   "), "blank name");
            ModAssert.True(NewModTemplate.IsValidName(" Tavern "), "trimmed name");
            ModAssert.False(NewModTemplate.IsValidName(new string('x', 81)), "too long");
        }

        [ModTest("scene_name/reads_name_field")]
        static void SceneNameReads(ModTestContext ctx)
        {
            ModAssert.Equal("rpg_plus_crossroads", SceneNameReader.Read("{\n  \"name\": \"rpg_plus_crossroads\",\n  \"defaultBackground\": \"x\"\n}"), "name");
            ModAssert.Null(SceneNameReader.Read("{\"background\": \"x\"}"), "no name");
            ModAssert.Null(SceneNameReader.Read("[1, 2]"), "not an object");
            ModAssert.Null(SceneNameReader.Read("{ broken"), "broken json");
        }
    }
}
