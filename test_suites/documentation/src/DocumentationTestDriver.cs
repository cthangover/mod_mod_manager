using System;
using System.IO;
using System.Linq;
using Cthangover.Core.Bootstrap;
using Cthangover.Core.Localization;
using Cthangover.Core.Mods;
using Cthangover.Core.UI.Menu;
using Cthangover.Core.Utils;
using Cthangover.Core.Settings;
using Cthangover.BaseMenu;
using Cthangover.Mods.Interface.Menu;
using Cthangover.Mods.Interface.View;
using Godot;

namespace Mods.ModManager.DocumentationTest
{
    /// <summary>
    /// Exercises the reference page, or reviews a real English page with
    /// --doc-mod=id. --doc-settings additionally captures the real Sound tab;
    /// the original settings file and language are restored even after failure.
    /// </summary>
    public partial class DocumentationTestDriver : Node
    {
        private double elapsed;
        private double stepAt;
        private int step;
        private int passed;
        private bool done;
        private string originalLocale;
        private ModHubScreen hub;
        private ModManagerScreen manager;
        private ModDetailView detail;
        private readonly string reviewMod = ReviewMod();
        private string reviewTitle;
        private SettingsMenu settings;
        private string settingsPath;
        private byte[] settingsBackup;
        private string settingsLanguage;

        public override void _Process(double delta)
        {
            if (done)
                return;
            elapsed += delta;
            if (elapsed > 60)
            {
                Finish("timeout at step " + step);
                return;
            }
            if (elapsed - stepAt < 0.6)
                return;
            stepAt = elapsed;
            try
            {
                if (string.IsNullOrEmpty(reviewMod))
                    RunStep();
                else
                    RunReviewStep();
            }
            catch (Exception ex)
            {
                Finish(ex.ToString());
            }
        }

        private void RunStep()
        {
            switch (step)
            {
                case 0:
                    if (!Boot.IsReady)
                        return;
                    if (GetTree().CurrentScene?.FindChild("LauncherPlay", true, false) is Button launcherPlay)
                    {
                        launcherPlay.EmitSignal(BaseButton.SignalName.Pressed);
                        return;
                    }
                    if (GetTree().CurrentScene?.FindChild("play", true, false) is not Button)
                        return;
                    originalLocale = TranslationServer.GetLocale();
                    LocaleLoader.LoadLocale("ru");
                    var entry = MenuRegistry.GetButtonById("mods_hub");
                    hub = MenuBuilder.OpenScene((Control)GetTree().CurrentScene, entry.ScenePath, "mods_hub") as ModHubScreen;
                    Check(hub != null, "hub opens");
                    break;
                case 1:
                    manager = hub.FindChildren("*", "", true, false).OfType<ModManagerScreen>().First();
                    manager.SetSearch("music_pack");
                    break;
                case 2:
                    var card = hub.FindChildren("*", "", true, false).OfType<ModCard>().First(IsVisibleCard);
                    card.EmitSignal(Control.SignalName.GuiInput, new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true });
                    break;
                case 3:
                    detail = hub.FindChildren("*", "", true, false).OfType<ModDetailView>().First(IsVisibleDetail);
                    Check(Body().GetParsedText().Contains("музыка для вашей игры"), "automatic Russian document");
                    Check(Body().GetContentHeight() > 600, "long document lays out");
                    var codeFont = Body().GetThemeFont("mono_font");
                    Check(codeFont.HasChar('└') && codeFont.HasChar('─') && codeFont.HasChar('├'), "bundled monospace font covers tree glyphs");
                    Check(ModDocumentation.Discover(ModRegistry.Instance.AllDiscoveredMods["music_pack"].FileProvider).Count == LanguageCode.Supported.Length, "every project language is bundled");
                    Shot("music_pack_ru");
                    SelectLanguage("EN");
                    break;
                case 4:
                    Check(Body().GetParsedText().Contains("a soundtrack for your game"), "manual English document");
                    Check(!Body().GetParsedText().Contains(TranslationServer.Translate("mod_manager/docs_image_unavailable").ToString()), "bundled image resolves");
                    Shot("music_pack_en");
                    ScrollToText("// Add the dependency");
                    break;
                case 5:
                    Shot("music_pack_code_en");
                    var languages = Languages();
                    languages.Select(0);
                    languages.EmitSignal(OptionButton.SignalName.ItemSelected, 0L);
                    LocaleLoader.LoadLocale("ja");
                    break;
                case 6:
                    Check(Body().GetParsedText().Contains("あなたのゲームのためのサウンドトラック"), "auto language follows the interface language (Japanese)");
                    Check(detail.GetNode<Label>("Scroll/Content/DocumentationBox/Source").Text.Contains("JA"), "actual document language is visible");
                    LocaleLoader.LoadLocale("de");
                    break;
                case 7:
                    Check(Body().GetParsedText().Contains("a soundtrack for your game"), "unbundled German falls back to English after locale change");
                    LocaleLoader.LoadLocale("en");
                    break;
                case 8:
                    Check(detail.GetNode<Label>("Scroll/Content/DocumentationBox/Source").Text.Contains("EN"), "document language is visible for a bundled UI language");
                    // A controlled legacy record stays valid as documentation
                    // is gradually added to every real mod in the project.
                    detail.Open(new ModCardData { Id = "documentation_legacy_fixture", Title = "Legacy fixture", Description = "Manifest-only description" });
                    break;
                case 9:
                    Check(!detail.GetNode<Control>("Scroll/Content/DocumentationBox").Visible, "legacy mod keeps manifest description");
                    Check(!string.IsNullOrWhiteSpace(detail.GetNode<Label>("Scroll/Content/DescriptionBox/Text").Text), "legacy description remains readable");
                    Finish(null);
                    break;
            }
            step++;
        }

        private static string ReviewMod()
        {
            foreach (var arg in OS.GetCmdlineUserArgs())
                if (arg.StartsWith("--doc-mod=", StringComparison.Ordinal))
                    return arg.Substring("--doc-mod=".Length);
            return null;
        }

        private void RunReviewStep()
        {
            switch (step)
            {
                case 0:
                    if (!Boot.IsReady)
                        return;
                    if (GetTree().CurrentScene?.FindChild("LauncherPlay", true, false) is Button launcherPlay)
                    {
                        launcherPlay.EmitSignal(BaseButton.SignalName.Pressed);
                        return;
                    }
                    if (GetTree().CurrentScene?.FindChild("play", true, false) is not Button)
                        return;
                    originalLocale = TranslationServer.GetLocale();
                    settingsLanguage = GameData.Instance.Settings.Language;
                    settingsPath = ProjectSettings.GlobalizePath("user://settings.json");
                    settingsBackup = File.Exists(settingsPath) ? File.ReadAllBytes(settingsPath) : null;
                    settings = GetTree().CurrentScene.GetNode<SettingsMenu>("SettingsPanel");
                    settings.Open();
                    var language = settings.GetNode<OptionButton>("Window/Column/Body/Pages/General/LangRow/LangSelect");
                    var english = Array.IndexOf(LanguageCode.Supported, "en");
                    language.Select(english);
                    language.EmitSignal(OptionButton.SignalName.ItemSelected, (long)english);
                    settings.ShowTab(1);
                    break;
                case 1:
                    Check(TranslationServer.GetLocale().StartsWith("en", StringComparison.Ordinal), "English UI selected through Settings");
                    if (OS.GetCmdlineUserArgs().Contains("--doc-settings") || OS.GetCmdlineUserArgs().Contains("--doc-settings-only"))
                    {
                        var audio = settings.GetNode<Control>("Window/Column/Body/Pages/Audio");
                        Check(audio.IsVisibleInTree(), "actual Sound tab visible");
                        Shot(reviewMod + "_sound_settings");
                    }
                    if (OS.GetCmdlineUserArgs().Contains("--doc-settings-only"))
                    {
                        Finish(null);
                        return;
                    }
                    settings.Visible = false;
                    var entry = MenuRegistry.GetButtonById("mods_hub");
                    hub = MenuBuilder.OpenScene((Control)GetTree().CurrentScene, entry.ScenePath, "mods_hub") as ModHubScreen;
                    Check(hub != null, "hub opens for documentation review");
                    break;
                case 2:
                    manager = hub.FindChildren("*", "", true, false).OfType<ModManagerScreen>().First();
                    manager.SetSearch(reviewMod);
                    break;
                case 3:
                    // The search also matches test suites such as mod_manager__documentation; open the reviewed mod itself.
                    reviewTitle = ModRegistry.Instance.AllDiscoveredMods[reviewMod].DisplayTitle ?? reviewMod;
                    var card = hub.FindChildren("*", "", true, false).OfType<ModCard>().First(IsReviewedCard);
                    card.EmitSignal(Control.SignalName.GuiInput, new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true });
                    break;
                case 4:
                    detail = hub.FindChildren("*", "", true, false).OfType<ModDetailView>().First(IsVisibleDetail);
                    var provider = ModRegistry.Instance.AllDiscoveredMods[reviewMod].FileProvider;
                    var document = ModDocumentation.Select(ModDocumentation.Discover(provider), "en");
                    Check(document?.Path == "docs/en.md", "English entry point exists for " + reviewMod);
                    var heading = document.Text.Split('\n')[0].TrimStart('#', ' ', '\uFEFF').TrimEnd('\r');
                    var shown = Body().GetParsedText();
                    Check(shown.Contains(heading), "actual page displays the selected English document; heading '" + heading + "', shown '" + shown.Substring(0, Math.Min(160, shown.Length)) + "'");
                    Check(!Body().GetParsedText().Contains(TranslationServer.Translate("mod_manager/docs_image_unavailable").ToString()), "all page images resolve");
                    Check(Body().GetContentHeight() > 300, "page lays out for scrolling");
                    Shot(reviewMod + "_documentation_en");
                    LocaleLoader.LoadLocale("ru");
                    break;
                case 5:
                    Check(detail.GetNode<Label>("Scroll/Content/DocumentationBox/Source").Text.Contains("EN"), "actual English fallback is identified");
                    Check(!string.IsNullOrWhiteSpace(Body().GetParsedText()), "fallback document remains readable");
                    Shot(reviewMod + "_documentation_fallback");
                    Finish(null);
                    break;
            }
            step++;
        }

        private ModMarkdownView Body() => detail.GetNode<ModMarkdownView>("Scroll/Content/DocumentationBox/Body");
        private OptionButton Languages() => detail.GetNode<OptionButton>("Scroll/Content/DocumentationBox/Header/Languages");
        private static bool IsVisibleCard(ModCard card) => card.IsVisibleInTree();
        private bool IsReviewedCard(ModCard card) => IsVisibleCard(card) && card.GetNode<Label>("VBox/Body/Box/Title").Text == reviewTitle;
        private static bool IsVisibleDetail(ModDetailView page) => page.IsVisibleInTree();
        private static bool IsScreenshotArg(string arg) => arg.StartsWith("--shots=");

        private void ScrollToText(string needle)
        {
            var text = Body().GetParsedText();
            var offset = text.IndexOf(needle, StringComparison.Ordinal);
            Check(offset >= 0, "code example remains literal and selectable");
            var paragraph = 0;
            for (var i = 0; i < offset; i++)
                if (text[i] == '\n')
                    paragraph++;
            var scroll = detail.GetNode<ScrollContainer>("Scroll");
            var content = detail.GetNode<Control>("Scroll/Content");
            scroll.ScrollVertical = (int)(Body().GlobalPosition.Y - content.GlobalPosition.Y + Body().GetParagraphOffset(paragraph)) - 32;
        }

        private void SelectLanguage(string language)
        {
            var languages = Languages();
            for (var i = 0; i < languages.ItemCount; i++)
                if (languages.GetItemText(i) == language)
                {
                    languages.Select(i);
                    languages.EmitSignal(OptionButton.SignalName.ItemSelected, (long)i);
                    return;
                }
            throw new InvalidOperationException("Missing language " + language);
        }

        private void Check(bool ok, string label)
        {
            if (!ok)
                throw new InvalidOperationException(label);
            passed++;
            GameLogger.Log("TEST", "Documentation: [OK] " + label);
        }

        private void Shot(string name)
        {
            var arg = OS.GetCmdlineUserArgs().FirstOrDefault(IsScreenshotArg);
            if (arg == null || DisplayServer.GetName() == "headless")
                return;
            var directory = ProjectSettings.GlobalizePath(arg.Substring(8));
            Directory.CreateDirectory(directory);
            ViewportCapture.SavePng(GetViewport(), Path.Combine(directory, name + ".png"));
        }

        private async void Finish(string failure)
        {
            if (done)
                return;
            done = true;
            hub?.QueueFree();
            if (settingsPath != null)
            {
                GameData.Instance.Settings.Language = settingsLanguage;
                if (settingsBackup == null)
                    File.Delete(settingsPath);
                else
                    File.WriteAllBytes(settingsPath, settingsBackup);
            }
            if (originalLocale != null)
                LocaleLoader.LoadLocale(originalLocale);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (failure != null)
                GameLogger.Log("TEST", "Documentation: " + failure, LogLevel.Error);
            GameLogger.Log("TEST", $"[TEST_RESULT] name=mod_documentation passed={passed} failed={(failure == null ? 0 : 1)}");
            GetTree().Quit(failure == null ? 0 : 1);
        }
    }
}
