namespace Mods.ModManager.NewModTest
{
    using System;
    using System.IO;
    using Cthangover.Core.Bootstrap;
    using Cthangover.Core.Games;
    using Cthangover.Core.Mods;
    using Cthangover.Core.Scenes;
    using Cthangover.Core.Utils;
    using Godot;

    /// <summary>
    /// The next launch after the wizard: the fixture game starts with <c>--new</c>, the mod the wizard made is loaded,
    /// and the scene the game starts in is the one picked in the editor. Then the fixture collection and the mod folder
    /// are removed.
    /// </summary>
    public partial class NewModCheckTestDriver : Node
    {
        private const string FixtureId = "new_mod_itest_game";
        private const string ModId = "itest_wizard";
        private const string Scene = "rpg_forest";
        private const float TimeoutSeconds = 60f;

        private int passed;
        private int failed;
        private double elapsed;
        private double next;
        private bool done;
        private bool checkedStart;

        public override void _Process(double delta)
        {
            if (done)
                return;
            elapsed += delta;
            if (elapsed < next)
                return;
            next = elapsed + 0.3;
            if (elapsed > TimeoutSeconds)
            {
                Finish($"timeout: the game is in '{SceneManager.GetCurrentSceneName()}'");
                return;
            }

            if (!Boot.IsReady)
                return;

            if (!checkedStart)
            {
                checkedStart = true;
                Check(ActiveGame.Id == FixtureId, $"the fixture game is the active game ({ActiveGame.Id})");
                Check(ModRegistry.Instance.GetMod(ModId) != null, "the mod the wizard made is loaded by the game");
                Check(ActiveGame.EntryScene == Scene, $"the game's start scene is the picked one ({ActiveGame.EntryScene})");
            }

            if (SceneManager.GetCurrentSceneName() == Scene)
            {
                Check(true, $"New game enters {Scene}");
                Finish(null);
            }
        }

        private void Check(bool condition, string label)
        {
            if (condition)
            {
                passed++;
                GameLogger.Log("TEST", "New mod check: [OK] " + label);
            }
            else
            {
                failed++;
                GameLogger.Log("TEST", "New mod check: [FAIL] " + label, LogLevel.Error);
            }
        }

        private void Finish(string failure)
        {
            done = true;
            if (failure != null)
            {
                failed++;
                GameLogger.Log("TEST", "New mod check: " + failure, LogLevel.Error);
            }

            Cleanup();
            GameLogger.Log("TEST", $"[TEST_RESULT] name=new_mod_check passed={passed} failed={failed}");
            ApplicationExit.Quit(GetTree(), failed == 0 ? 0 : 1);
        }

        private static void Cleanup()
        {
            var collection = Path.Combine(ModCollectionStore.UserPath, FixtureId);
            if (Directory.Exists(collection))
                Directory.Delete(collection, true);
            var mod = Path.Combine(ModRoots.ModsPath, ModId);
            if (Directory.Exists(mod))
                Directory.Delete(mod, true);
        }
    }
}
