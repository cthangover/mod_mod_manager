namespace Mods.ModManager.SettingsTest
{
    using System;
    using System.IO;
    using System.Linq;
    using System.Text.Json;
    using Cthangover.Core.Bootstrap;
    using Cthangover.Core.Localization;
    using Cthangover.Core.Mods;
    using Cthangover.Core.UI.Menu;
    using Cthangover.Core.Utils;
    using Cthangover.Mods.Interface.Menu;
    using Godot;

    /// <summary>Small-viewport scrolling, cancel, validation, automatic removal and persisted settings through real UI controls.</summary>
    public partial class CollectionSettingsTestDriver : Node
    {
        private const string FixtureId = "collection_settings_ui_test";
        private ModHubScreen hub;
        private ModCollectionsScreen editor;
        private CollectionSettingsDialog dialog;
        private string fixturePath;
        private string originalLocale;
        private int step;
        private int passed;
        private double elapsed;
        private double next;
        private bool done;

        public override void _Process(double delta)
        {
            if (done)
                return;
            elapsed += delta;
            if (elapsed > 90)
            {
                Finish("timeout at step " + step);
                return;
            }
            if (elapsed < next)
                return;
            next = elapsed + 0.35;
            try { RunStep(); }
            catch (Exception ex) { Finish("step " + step + ": " + ex); }
        }

        private void RunStep()
        {
            switch (step)
            {
                case 0:
                    if (!Boot.IsReady)
                        return;
                    if (GetTree().CurrentScene?.FindChild("LauncherPlay", true, false) is Button launcher)
                    {
                        launcher.EmitSignal(BaseButton.SignalName.Pressed);
                        return;
                    }
                    if (GetTree().CurrentScene?.FindChild("play", true, false) is not Button)
                        return;
                    originalLocale = TranslationServer.GetLocale();
                    LocaleLoader.LoadLocale("ru");
                    MakeFixture();
                    var entry = MenuRegistry.GetButtonById("mods_hub");
                    hub = MenuBuilder.OpenScene((Control)GetTree().CurrentScene, entry.ScenePath, "mods_hub") as ModHubScreen;
                    Check(hub != null, "hub opens");
                    hub.OpenCollectionEditor(FixtureId);
                    editor = hub.FindChildren("*", "", true, false).OfType<ModCollectionsScreen>().Single();
                    break;
                case 1:
                    OpenForm();
                    break;
                case 2:
                    Check(dialog.FindChildren("*", "", true, false).OfType<CollectionSettingRow>().Count() == 5, "five fields read without loading game mods");
                    Check(dialog.Title == "Параметры игры", "localized dialog title");
                    Check(Row("battle.core").Value == "missing_core", "unavailable saved choice retained");
                    dialog.Size = new Vector2I(640, 420);
                    break;
                case 3:
                    var scroll = dialog.GetNode<ScrollContainer>("FieldsScroll");
                    Check(dialog.Size.Y <= 420, "dialog minimum does not exceed small viewport");
                    Check(scroll.GetChild<Control>(0).Size.Y > scroll.Size.Y, "long form is contained in scroll viewport");
                    Check(dialog.GetOkButton().GetGlobalRect().End.Y <= dialog.Size.Y, "Apply remains inside window");
                    Check(dialog.GetCancelButton().GetGlobalRect().End.Y <= dialog.Size.Y, "Cancel remains inside window");
                    scroll.ScrollVertical = 100000;
                    break;
                case 4:
                    Check(dialog.GetNode<ScrollContainer>("FieldsScroll").ScrollVertical > 0, "form scrolls to lower fields");
                    Custom(Row("checks.dice"), "bad dice");
                    dialog.EmitSignal(ConfirmationDialog.SignalName.Confirmed);
                    Check(!dialog.IsQueuedForDeletion(), "invalid dice prevents Apply");
                    Custom(Row("checks.dice"), "2d6");
                    Custom(Row("stats.player"), "created_during_play");
                    Select(Row("tile.renderer"), 0);
                    dialog.EmitSignal(ConfirmationDialog.SignalName.Canceled);
                    break;
                case 5:
                    OpenForm();
                    Check(Row("checks.dice").Value == "d20", "Cancel leaves original settings");
                    Custom(Row("checks.dice"), "2d6");
                    Custom(Row("stats.player"), "created_during_play");
                    Select(Row("tile.renderer"), 0);
                    dialog.EmitSignal(ConfirmationDialog.SignalName.Confirmed);
                    break;
                case 6:
                    editor.GetNode<Button>("Margin/Root/Header/Save").EmitSignal(BaseButton.SignalName.Pressed);
                    break;
                case 7:
                    var save = editor.FindChildren("*", "", true, false).OfType<AcceptDialog>().Single(d => d.Visible);
                    save.EmitSignal(AcceptDialog.SignalName.Confirmed);
                    break;
                case 8:
                    var saved = ModCollectionStore.LoadUser().Single(c => c.Id == FixtureId);
                    Check(saved.Settings["checks.dice"] == "2d6", "dice saved to collection package");
                    Check(saved.Settings["stats.player"] == "created_during_play", "custom character saved");
                    Check(saved.Settings["battle.core"] == "missing_core", "unavailable choice survives save");
                    Check(saved.Settings["custom.fixture"] == "keep", "undeclared setting survives save");
                    Check(!saved.Settings.ContainsKey("tile.renderer"), "Automatic deletes explicit key");
                    editor.OpenCollection(FixtureId);
                    OpenForm();
                    break;
                case 9:
                    Check(Row("checks.dice").Value == "2d6", "saved values restored on reopening");
                    Check(Row("tile.renderer").Value == null, "automatic restored on reopening");
                    dialog.EmitSignal(ConfirmationDialog.SignalName.Canceled);
                    Finish(null);
                    return;
            }
            step++;
        }

        private void MakeFixture()
        {
            var path = Path.Combine(ModCollectionStore.UserPath, FixtureId);
            Check(!Directory.Exists(path), "fixture cannot overwrite player data");
            var source = ModCollectionStore.LoadBuiltIn().Single(c => c.Id == "rpg_plus");
            var clone = JsonSerializer.Deserialize<ModCollection>(JsonSerializer.Serialize(source));
            clone.Id = FixtureId;
            clone.Name = "Collection settings test";
            // A fixture must not inherit the game's localized display names.
            clone.Locale = null;
            clone.Settings["custom.fixture"] = "keep";
            clone.Settings["battle.core"] = "missing_core";
            Directory.CreateDirectory(path);
            fixturePath = path;
            File.WriteAllText(Path.Combine(path, "collection.json"), JsonSerializer.Serialize(clone));
        }

        private void OpenForm()
        {
            editor.GetNode<Button>("Margin/Root/Tools/GameSettings").EmitSignal(BaseButton.SignalName.Pressed);
            dialog = editor.FindChildren("*", "", true, false).OfType<CollectionSettingsDialog>().Single(d => !d.IsQueuedForDeletion());
        }

        private CollectionSettingRow Row(string key) => dialog.FindChildren("*", "", true, false).OfType<CollectionSettingRow>().Single(r => r.Key == key);

        private static void Custom(CollectionSettingRow row, string value)
        {
            var select = row.FindChildren("*", "", true, false).OfType<OptionButton>().First();
            Select(row, select.ItemCount - 1);
            row.FindChildren("*", "", true, false).OfType<LineEdit>().First().Text = value;
        }

        private static void Select(CollectionSettingRow row, int index)
        {
            var select = row.FindChildren("*", "", true, false).OfType<OptionButton>().First();
            select.Select(index);
            select.EmitSignal(OptionButton.SignalName.ItemSelected, (long)index);
        }

        private void Check(bool condition, string label)
        {
            if (!condition)
                throw new InvalidOperationException(label);
            passed++;
            GameLogger.Log("TEST", "Collection settings: [OK] " + label);
        }

        private void Finish(string failure)
        {
            done = true;
            hub?.QueueFree();
            CleanupFixture();
            if (originalLocale != null)
                LocaleLoader.LoadLocale(originalLocale);
            if (failure != null)
                GameLogger.Log("TEST", "Collection settings: " + failure, LogLevel.Error);
            GameLogger.Log("TEST", $"[TEST_RESULT] name=collection_settings passed={passed} failed={(failure == null ? 0 : 1)}");
            GetTree().Quit(failure == null ? 0 : 1);
        }

        /// <summary>Also remove the owned fixture when the suite exits before Finish.</summary>
        public override void _ExitTree() => CleanupFixture();

        private void CleanupFixture()
        {
            if (fixturePath != null && Directory.Exists(fixturePath))
                Directory.Delete(fixturePath, true);
            fixturePath = null;
        }
    }
}
