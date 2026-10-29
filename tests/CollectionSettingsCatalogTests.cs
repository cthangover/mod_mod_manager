namespace Mods.ModManager.Tests
{
    using System;
    using System.IO;
    using System.IO.Compression;
    using System.Linq;
    using System.Text.Json;
    using Cthangover.Core.Mods;
    using Cthangover.Core.Mods.Providers;
    using Cthangover.Core.Mods.Testing;

    /// <summary>Declarations work before mod activation, including ZIP storage, translated resources and unavailable choices.</summary>
    public class CollectionSettingsCatalogTests
    {
        [ModTest("collection_settings/roundtrip")]
        static void Roundtrip(ModTestContext ctx)
        {
            var original = new CollectionSettingsFile
            {
                Settings = new() { new() { Key = "test.choice", Name = "field", Description = "hint", Group = "group", Type = "resource", Source = "charsets", AllowCustom = true, Automatic = "fallback", Pattern = "^d[0-9]+$", Options = new() { new() { Key = "test.choice", Value = "d20", Name = "twenty" } } } },
                Options = new() { new() { Key = "test.choice", Value = "d6", Name = "six" } },
            };
            var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
            var restored = JsonSerializer.Deserialize<CollectionSettingsFile>(JsonSerializer.Serialize(original, options), options);
            ModAssert.Equal(JsonSerializer.Serialize(original, options), JsonSerializer.Serialize(restored, options), "all declaration fields survive serialization");
            ModAssert.Equal("d20", restored.Settings[0].Options[0].Value);
            ModAssert.Equal("test.choice", restored.Options[0].Key);
        }

        [ModTest("collection_settings/offline_folder_zip")]
        static void OfflineFolderZip(ModTestContext ctx)
        {
            var root = Path.Combine(ctx.SandboxDir, "collection_settings");
            var zip = root + ".zip";
            Directory.CreateDirectory(Path.Combine(root, "locale"));
            Directory.CreateDirectory(Path.Combine(root, "charsets", "village"));
            Directory.CreateDirectory(Path.Combine(root, "characters"));
            File.WriteAllText(Path.Combine(root, "collection_settings.json"), "{\"settings\":[{\"key\":\"look\",\"name\":\"appearance\",\"type\":\"resource\",\"source\":\"charsets\"},{\"key\":\"who\",\"type\":\"character\",\"source\":\"characters\",\"allow_custom\":true},{\"key\":\"dice\",\"allow_custom\":true,\"pattern\":\"^(d20|2d6|none)$\"}],\"options\":[{\"key\":\"dice\",\"value\":\"d20\",\"name\":\"twenty\"}]}");
            File.WriteAllText(Path.Combine(root, "locale", "settings_en.properties"), "appearance=Appearance\nhero=Hero\ntwenty=Twenty\n");
            File.WriteAllText(Path.Combine(root, "locale", "settings_ru.properties"), "appearance=Внешность\nhero=Герой\n");
            File.WriteAllBytes(Path.Combine(root, "charsets", "village", "hero.png"), new byte[] { 1 });
            File.WriteAllText(Path.Combine(root, "characters", "heroes.json"), "{\"Kind\":\"character\",\"Items\":[{\"Id\":\"hero\",\"Name\":\"hero\"}]}");
            try
            {
                ZipFile.CreateFromDirectory(root, zip);
                using var folder = new FolderModFileProvider(root, "fixture");
                using var archive = new ZipModFileProvider(zip, "fixture");
                foreach (var provider in new IModFileProvider[] { folder, archive })
                {
                    var mods = new IModInfo[] { new ModInfo { Id = "fixture", FileProvider = provider } };
                    var catalog = new CollectionSettingsCatalog(mods, new[] { "fixture" }, "ru_RU");
                    ModAssert.Equal(3, catalog.Definitions.Count, "declarations without loaded assemblies");
                    ModAssert.Equal("Внешность", catalog.Translate("appearance"), "regional locale");
                    ModAssert.Equal("Twenty", catalog.Translate("twenty"), "English fallback");
                    var look = catalog.Definitions.Single(d => d.Key == "look");
                    var sprite = catalog.Choices(look).Single();
                    ModAssert.Equal("village/hero", sprite.Value, "resource id keeps subfolder");
                    ModAssert.Equal("charsets/village/hero.png", sprite.PreviewPath);
                    var who = catalog.Definitions.Single(d => d.Key == "who");
                    ModAssert.Equal("Герой", catalog.Translate(catalog.Choices(who).Single().Name));
                    ModAssert.True(catalog.IsValidCustom(who, "created_at_runtime"), "runtime character ids allowed");
                    var dice = catalog.Definitions.Single(d => d.Key == "dice");
                    ModAssert.True(catalog.IsValidCustom(dice, "2d6"), "valid custom syntax");
                    ModAssert.False(catalog.IsValidCustom(dice, "typo"), "invalid custom syntax");
                    var inactive = new CollectionSettingsCatalog(mods, Array.Empty<string>(), "en");
                    var inactiveDice = inactive.Definitions.Single(d => d.Key == "dice");
                    ModAssert.False(inactiveDice.Available, "removed owner's saved keys remain describable");
                    ModAssert.False(inactive.Choices(inactiveDice).Single().Available, "choice from removed mod is unavailable");
                    ModAssert.Equal(0, inactive.Choices(inactive.Definitions.Single(d => d.Key == "look")).Count, "unselected resources excluded");
                }
            }
            finally
            {
                File.Delete(zip);
                Directory.Delete(root, true);
            }
        }
    }
}
