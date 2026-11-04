using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Cthangover.Core.Bootstrap;
using Cthangover.Core.Mods;
using Cthangover.Core.Scenes;
using Cthangover.Core.Utils;
using Godot;

namespace Mods.ModManager.PrivateCatalogTest
{
    /// <summary>Real server → manager card → install button → installed bytes; then removes access while keeping a public source.</summary>
    public partial class PrivateCatalogTestDriver : Node
    {
        private ModManagerScreen manager;
        private List<string> originalUrls;
        private Dictionary<string, string> originalTokenFiles;
        private string cachePath;
        private byte[] originalCache;
        private string installedPath;
        private string badToken;
        private Task operation;
        private double elapsed;
        private int step;
        private bool finished;
        private bool installCompleted;
        private bool installSucceeded;

        public override void _Process(double delta)
        {
            if (finished)
                return;
            elapsed += delta;
            try
            {
                if (elapsed > 100)
                    throw new InvalidOperationException("private catalog timeout at step " + step);
                if (!Boot.IsReady)
                    return;
                if (operation != null && !operation.IsCompleted)
                    return;
                operation?.GetAwaiter().GetResult();
                operation = null;
                RunStep();
            }
            catch (Exception ex) { Finish(ex.ToString()); }
        }

        private void RunStep()
        {
            switch (step)
            {
                case 0:
                    if (GetTree().CurrentScene?.FindChild("LauncherPlay", true, false) is Button play)
                    {
                        play.EmitSignal(BaseButton.SignalName.Pressed);
                        return;
                    }
                    if (GetTree().CurrentScene?.FindChild("play", true, false) is not Button || CatalogService.IsFetching)
                        return;
                    Prepare();
                    operation = CatalogService.RefreshAsync(forceRefresh: true);
                    step++;
                    break;
                case 1:
                    RequireEntries(true);
                    var instance = TscnScenes.LoadAndBuild("scenes/mod_manager_screen.tscn");
                    try
                    {
                        manager = instance as ModManagerScreen ?? throw new InvalidOperationException("manager scene not built");
                        AddChild(manager);
                    }
                    finally
                    {
                        if (GodotObject.IsInstanceValid(instance) && instance.GetParent() == null)
                            instance.Free();
                    }
                    manager.EnsureConstructed();
                    manager.SetSearch("private_test_pack");
                    step++;
                    break;
                case 2:
                    var card = FindCard(manager);
                    if (card == null)
                        throw new InvalidOperationException("private mod card is missing");
                    ModDownloader.OnCompleted += OnInstalled;
                    card.GetNode<Button>("VBox/Body/Box/ActionRow/ActionHost/Action").EmitSignal(BaseButton.SignalName.Pressed);
                    step++;
                    break;
                case 3:
                    if (!installCompleted)
                        return;
                    if (!installSucceeded || File.ReadAllText(Path.Combine(installedPath, "data/check.txt")) != "Private catalog download works.")
                        throw new InvalidOperationException("manager did not install the expected archive bytes");
                    var disk = File.ReadAllText(cachePath);
                    if (disk.Contains("private_test_pack") || disk.Contains("/v1/download/") || !disk.Contains("public_test_pack"))
                        throw new InvalidOperationException("disk cache did not isolate private data from public data");
                    badToken = Path.Combine(Path.GetTempPath(), "catalog-bad-" + Guid.NewGuid().ToString("N") + ".token");
                    File.WriteAllText(badToken, new string('A', 1024));
                    ModConfig.Instance.CatalogTokenFiles[ModConfig.Instance.CatalogUrls[0]] = badToken;
                    operation = CatalogService.RefreshAsync(forceRefresh: true);
                    step++;
                    break;
                case 4:
                    RequireEntries(false);
                    Finish(null);
                    break;
            }
        }

        private void Prepare()
        {
            var url = System.Environment.GetEnvironmentVariable("CTHANGOVER_PRIVATE_CATALOG_URL");
            var token = System.Environment.GetEnvironmentVariable("CTHANGOVER_PRIVATE_TOKEN_FILE");
            var publicUrl = System.Environment.GetEnvironmentVariable("CTHANGOVER_PUBLIC_CATALOG_URL");
            if (string.IsNullOrEmpty(url) || string.IsNullOrEmpty(token) || string.IsNullOrEmpty(publicUrl))
                throw new InvalidOperationException("run this suite through cthangover-keyserver/tests/integration_windows.py");
            var target = GamePaths.Globalize("user://mods/private_test_pack");
            if (Directory.Exists(target) || ModRegistry.Instance.AllDiscoveredMods.ContainsKey("private_test_pack"))
                throw new InvalidOperationException("private_test_pack already exists; refusing to replace it");
            installedPath = target;
            cachePath = GamePaths.Globalize("user://catalog_cache.json");
            originalCache = File.Exists(cachePath) ? File.ReadAllBytes(cachePath) : null;
            originalUrls = ModConfig.Instance.CatalogUrls;
            originalTokenFiles = ModConfig.Instance.CatalogTokenFiles;
            ModConfig.Instance.CatalogUrls = new List<string> { url, publicUrl };
            ModConfig.Instance.CatalogTokenFiles = new Dictionary<string, string> { [url] = token };
        }

        private static void RequireEntries(bool privateExpected)
        {
            var entries = CatalogService.CachedEntries ?? throw new InvalidOperationException("catalog is missing");
            if (entries.Any(IsPrivate) != privateExpected || !entries.Any(IsPublic))
                throw new InvalidOperationException("private permission or independent public catalog mismatch");
        }

        private static bool IsPrivate(CatalogExportEntry entry) => entry.Id == "private_test_pack";
        private static bool IsPublic(CatalogExportEntry entry) => entry.Id == "public_test_pack";
        private void OnInstalled(string id, bool success)
        {
            if (id != "private_test_pack")
                return;
            installCompleted = true;
            installSucceeded = success;
        }

        private static ModCard FindCard(Node node)
        {
            if (node is ModCard card && !node.IsQueuedForDeletion())
                return card;
            foreach (var child in node.GetChildren())
            {
                var found = FindCard(child);
                if (found != null)
                    return found;
            }
            return null;
        }

        private void Finish(string failure)
        {
            if (finished)
                return;
            finished = true;
            ModDownloader.OnCompleted -= OnInstalled;
            if (GodotObject.IsInstanceValid(manager))
                manager.QueueFree();
            if (originalUrls != null)
            {
                ModConfig.Instance.CatalogUrls = originalUrls;
                ModConfig.Instance.CatalogTokenFiles = originalTokenFiles;
                CatalogService.ClearCache();
                if (originalCache != null)
                    File.WriteAllBytes(cachePath, originalCache);
            }
            if (installedPath != null && Directory.Exists(installedPath))
                Directory.Delete(installedPath, true);
            if (badToken != null)
                File.Delete(badToken);
            if (failure != null)
                GameLogger.Log("TEST", failure, LogLevel.Error);
            GameLogger.Log("TEST", $"[TEST_RESULT] name=private_catalog passed={(failure == null ? 1 : 0)} failed={(failure == null ? 0 : 1)}");
            ApplicationExit.Quit(GetTree(), failure == null ? 0 : 1);
        }
    }
}
