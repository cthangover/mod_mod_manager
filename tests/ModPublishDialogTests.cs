using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Cthangover.Core.Mods;
using Cthangover.Core.Mods.Testing;
using Cthangover.Core.Scenes;
using Cthangover.Core.Utils;
using Godot;

namespace Mods.ModManager.Tests
{
    /// <summary>Exercises the scene and its checkbox against real HTTP permission replies, including future revisions and legacy servers.</summary>
    public class ModPublishDialogTests
    {
        private const string Form = "Center/Panel/Margin/Form/";

        [ModTest("publishing/consent_form_versions")]
        static async Task ConsentFormVersions(ModTestContext context)
        {
            if (!GameEnvironment.IsRuntime) throw new InvalidOperationException("This case requires Godot");
            // The dialog translates through the loaded dictionary, so every checked language must be
            // loaded the way the settings menu does it — a bare TranslationServer.SetLocale would
            // leave the previous language's strings as the only registered translation.
            try
            {
                foreach (var language in new[] { "ru", "en" })
                {
                    Cthangover.Core.Localization.LocaleLoader.LoadLocale(language);
                    await CheckFormAsync(context, "2026-11-09", false);
                    await CheckFormAsync(context, "future-revision", false);
                }
            }
            finally { Cthangover.Core.Localization.LocaleLoader.LoadCurrentLanguage(); }
        }

        [ModTest("publishing/consent_form_accepted_and_legacy")]
        static async Task ConsentFormAcceptedAndLegacy(ModTestContext context)
        {
            if (!GameEnvironment.IsRuntime) throw new InvalidOperationException("This case requires Godot");
            await CheckFormAsync(context, "2026-11-09", true);
            await CheckFormAsync(context, null, false);
        }

        private static async Task CheckFormAsync(ModTestContext context, string version, bool accepted)
        {
            var permission = new PublishingPermissions
            {
                AuthorId = "test-author", Author = "Test author", Sections = new List<string> { "story" }, MaxArchiveBytes = 200L << 20,
                AuthorConsent = version == null ? null : new AuthorConsentPermissions
                {
                    Version = version, Url = "https://cthangover.ru/consent-author.html", Accepted = accepted
                }
            };
            using var server = new PublishingHttpFixture((200, JsonSerializer.Serialize(permission)));
            var serverFile = GamePaths.Globalize("user://catalog_servers.json");
            var savedFile = File.Exists(serverFile) ? File.ReadAllBytes(serverFile) : null;
            var savedUrls = ModConfig.Instance.CatalogUrls;
            var savedTokens = ModConfig.Instance.CatalogTokenFiles;
            ModPublishDialog dialog = null;
            try
            {
                File.Delete(serverFile);
                ModConfig.Instance.CatalogUrls = new List<string> { server.CatalogUrl };
                ModConfig.Instance.CatalogTokenFiles = new Dictionary<string, string> { [server.CatalogUrl] = server.TokenFile };
                dialog = (ModPublishDialog)TscnScenes.LoadAndBuild("scenes/components/mod_publish_dialog.tscn");
                ((SceneTree)Godot.Engine.GetMainLoop()).Root.AddChild(dialog);
                dialog.Open(new ModInfo { Id = "consent-preview", Name = "Consent preview", Manifest = new ModManifest { Version = "1.0" } });
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(15));
                await server.Completion.WaitAsync(timeout.Token);
                while (dialog.GetNode<Label>(Form + "Identity").Text.Length == 0)
                    await Task.Delay(10, timeout.Token);
                var pending = version != null && !accepted;
                ModAssert.True(dialog.GetNode<Control>(Form + "Consent").Visible == pending, "separate consent visibility");
                var checkbox = dialog.GetNode<CheckBox>(Form + "Consent/Accept");
                var publish = dialog.GetNode<Button>(Form + "Actions/Publish");
                ModAssert.False(checkbox.ButtonPressed, "consent must never be prechecked");
                ModAssert.True(publish.Disabled == pending, "only pending consent gates an authorized section");
                if (!pending) return;
                var text = dialog.GetNode<RichTextLabel>(Form + "Consent/Text");
                ModAssert.False(text.BbcodeEnabled, "legal text is plain text");
                if (version == "2026-11-09")
                {
                    ModAssert.True(text.Visible && text.ScrollActive, "complete text can be scrolled");
                    var documentLanguage = Cthangover.Core.Localization.LanguageCode.Normalize(TranslationServer.GetLocale());
                    var document = documentLanguage == "ru"
                        ? "legal/consent_author_2026-11-09.txt"
                        : $"legal/consent_author_2026-11-09_{documentLanguage}.txt";
                    ModAssert.Equal(context.ReadFileText(document), text.Text, "consent resource is shown verbatim in the reader's language");
                    ModAssert.Equal(dialog.Tr("mod_manager/publish_consent_language_native"), dialog.GetNode<Label>(Form + "Consent/Language").Text, "a bundled translation selects the native-language note");
                    ModAssert.True(text.AutoTranslateMode == Node.AutoTranslateModeEnum.Disabled, "consent cannot be auto-translated");
                }
                else
                {
                    ModAssert.False(text.Visible, "bundled revision cannot represent a future consent");
                    ModAssert.Equal("", text.Text, "old text is not retained");
                    ModAssert.True(checkbox.Text.Contains(version), "future revision is identified on the checkbox");
                }
                ModAssert.False(dialog.GetNode<Button>(Form + "Consent/Website").Disabled, "HTTPS consent link available");
                checkbox.ButtonPressed = true;
                ModAssert.False(publish.Disabled, "deliberate consent enables publication");
                checkbox.ButtonPressed = false;
                ModAssert.True(publish.Disabled, "clearing consent blocks publication again");
            }
            finally
            {
                dialog?.Free();
                ModConfig.Instance.CatalogUrls = savedUrls;
                ModConfig.Instance.CatalogTokenFiles = savedTokens;
                if (savedFile == null) File.Delete(serverFile);
                else File.WriteAllBytes(serverFile, savedFile);
            }
        }
    }
}
