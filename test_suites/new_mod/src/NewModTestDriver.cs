namespace Mods.ModManager.NewModTest
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

    /// <summary>
    /// The Mods &amp; Collections editor as a player uses it: "New mod" writes the mod's folder and adds the mod to the
    /// game; "Start scene" picks a scene of the game's mods; Save writes both. The game is a fixture copy of rpg_plus
    /// (<see cref="FixtureId"/>): the next suite, <c>new_mod_check</c>, starts it and removes the fixture and the mod.
    /// </summary>
    public partial class NewModTestDriver : Node
    {
        private const string FixtureId = "new_mod_itest_game";
        private const string ModId = "itest_wizard";
        private const string Scene = "rpg_forest";
        private const string ModTitle = "Wizard test mod";

        private ModHubScreen hub;
        private ModCollectionsScreen editor;
        private string fixturePath;
        private string modPath;
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
            if (elapsed > 120)
            {
                Finish("timeout at step " + step);
                return;
            }

            if (elapsed < next)
                return;
            next = elapsed + 0.35;
            try
            {
                RunStep();
            }
            catch (Exception ex)
            {
                Finish("step " + step + ": " + ex);
            }
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
                    MakeFixture();
                    var entry = MenuRegistry.GetButtonById("mods_hub");
                    hub = MenuBuilder.OpenScene((Control)GetTree().CurrentScene, entry.ScenePath, "mods_hub") as ModHubScreen;
                    Check(hub != null, "the mods hub opens");
                    hub.OpenCollectionEditor(FixtureId);
                    editor = hub.FindChildren("*", "", true, false).OfType<ModCollectionsScreen>().Single();
                    break;
                case 1:
                    PressButton(Tr("mod_manager/collection_new_mod"));
                    break;
                case 2:
                    var form = FindDialog(Tr("mod_manager/newmod_title"));
                    var edits = form.FindChildren("*", "", true, false).OfType<LineEdit>().ToList();
                    Check(edits.Count >= 2, "the wizard asks for a title and an id");
                    Type(edits[0], ModTitle);
                    Check(edits[1].Text == ModId || edits[1].Text == "wizard_test_mod", $"the id follows the title ({edits[1].Text})");
                    Type(edits[1], ModId);
                    form.EmitSignal(AcceptDialog.SignalName.Confirmed);
                    break;
                case 3:
                    modPath = Path.Combine(ModRoots.ModsPath, ModId);
                    Check(Directory.Exists(modPath), "the mod folder is written next to the other mods");
                    var manifest = File.ReadAllText(Path.Combine(modPath, "manifest.json"));
                    using (var doc = JsonDocument.Parse(manifest))
                    {
                        Check(doc.RootElement.GetProperty("id").GetString() == ModId, "the manifest has the id");
                        Check(doc.RootElement.GetProperty("depends")[0].GetString() == "rpg>=1", "the manifest depends on rpg");
                    }
                    Check(File.ReadAllText(Path.Combine(modPath, "locale", "en.properties")).Contains($"mod_{ModId}_name = {ModTitle}"), "the English locale stub has the title");
                    Check(!Directory.Exists(Path.Combine(modPath, ".git")) && !File.Exists(Path.Combine(modPath, ".git", "config")), "no repository is made");
                    var joined = ModCollectionStore.LoadUser().Single(c => c.Id == FixtureId);
                    Check(joined.Mechanics.Any(m => m.Mods != null && m.Mods.Contains(ModId)), "the game lists the mod as a block");
                    PressButton(Tr("mod_manager/collection_start_scene"));
                    break;
                case 4:
                    var picker = FindDialog(Tr("mod_manager/entry_title"));
                    var select = picker.FindChildren("*", "", true, false).OfType<OptionButton>().Single();
                    var index = Enumerable.Range(0, select.ItemCount).FirstOrDefault(i => select.GetItemText(i) == Scene, -1);
                    Check(index >= 0, "the start-scene picker offers the scene of the game's mods");
                    select.Select(index);
                    picker.EmitSignal(AcceptDialog.SignalName.Confirmed);
                    editor.GetNode<Button>("Margin/Root/Header/Save").EmitSignal(BaseButton.SignalName.Pressed);
                    break;
                case 5:
                    var save = FindDialog(Tr("mod_manager/collection_save"));
                    save.EmitSignal(AcceptDialog.SignalName.Confirmed);
                    break;
                case 6:
                    var saved = ModCollectionStore.LoadUser().Single(c => c.Id == FixtureId);
                    Check(saved.Entry?.Scene == Scene, $"the saved game starts in {Scene} ({saved.Entry?.Scene})");
                    Finish(null);
                    return;
            }

            step++;
        }

        private static string Tr(string key) => TranslationServer.Translate(key);

        private void PressButton(string text)
        {
            var button = editor.FindChildren("*", "Button", true, false).OfType<Button>().FirstOrDefault(b => b.Text == text && b.IsVisibleInTree());
            Check(button != null, $"the button '{text}' is on the editor");
            button?.EmitSignal(BaseButton.SignalName.Pressed);
        }

        private AcceptDialog FindDialog(string title)
        {
            var dialog = editor.FindChildren("*", "", true, false).OfType<AcceptDialog>()
                .FirstOrDefault(d => d.Visible && d.Title == title && !d.IsQueuedForDeletion());
            Check(dialog != null, $"the dialog '{title}' is open");
            return dialog;
        }

        private static void Type(LineEdit edit, string text)
        {
            edit.Text = text;
            edit.EmitSignal(LineEdit.SignalName.TextChanged, text);
        }

        /// <summary>The fixture is a copy of rpg_plus under its own id, so the game's entry and mechanics can change freely.</summary>
        private void MakeFixture()
        {
            var path = Path.Combine(ModCollectionStore.UserPath, FixtureId);
            if (Directory.Exists(path))
                Directory.Delete(path, true);
            var source = ModCollectionStore.LoadBuiltIn().Single(c => c.Id == "rpg_plus");
            var clone = JsonSerializer.Deserialize<ModCollection>(JsonSerializer.Serialize(source));
            clone.Id = FixtureId;
            clone.Name = "New mod test game";
            clone.Locale = null;
            clone.Entry = null;
            Directory.CreateDirectory(path);
            fixturePath = path;
            File.WriteAllText(Path.Combine(path, "collection.json"), JsonSerializer.Serialize(clone));
            var leftover = Path.Combine(ModRoots.ModsPath, ModId);
            if (Directory.Exists(leftover))
                Directory.Delete(leftover, true);
        }

        private void Check(bool condition, string label)
        {
            if (!condition)
                throw new InvalidOperationException(label);
            passed++;
            GameLogger.Log("TEST", "New mod: [OK] " + label);
        }

        private void Finish(string failure)
        {
            done = true;
            hub?.QueueFree();
            if (originalLocale != null)
                LocaleLoader.LoadLocale(originalLocale);
            if (failure != null)
                GameLogger.Log("TEST", "New mod: " + failure, LogLevel.Error);
            // The fixture and the mod stay for new_mod_check (a game run); it removes both. A failure removes them here.
            if (failure != null)
                Cleanup();
            GameLogger.Log("TEST", $"[TEST_RESULT] name=new_mod passed={passed} failed={(failure == null ? 0 : 1)}");
            Cthangover.Core.Bootstrap.ApplicationExit.Quit(GetTree(), failure == null ? 0 : 1);
        }

        private void Cleanup()
        {
            if (fixturePath != null && Directory.Exists(fixturePath))
                Directory.Delete(fixturePath, true);
            var mod = Path.Combine(ModRoots.ModsPath, ModId);
            if (Directory.Exists(mod))
                Directory.Delete(mod, true);
        }
    }
}
