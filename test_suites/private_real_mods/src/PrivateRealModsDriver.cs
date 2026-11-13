using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Cthangover.Core.Bootstrap;
using Cthangover.Core.Mods;
using Cthangover.Core.Mods.Providers;
using Cthangover.Core.Scenes;
using Cthangover.Core.Utils;
using Cthangover.Mods.Interface.View;
using Godot;

namespace Mods.ModManager.PrivateRealModsTest
{
    /// <summary>
    /// Drives actual publish/detail/card/gallery controls against isolated real
    /// servers. The harness owns disk copies and profiles; this driver never
    /// changes the discovered registry to pretend a dependency is missing.
    /// </summary>
    public partial class PrivateRealModsDriver : Node
    {
        private const string Form = "Center/Panel/Margin/Form/";
        private readonly string[] ids = { "world3d_base", "world3d_editor" };
        private readonly Dictionary<string, byte[]> manifests = new();
        private readonly HashSet<string> completed = new(StringComparer.OrdinalIgnoreCase);
        private ModManagerScreen manager;
        private ModHubScreen hub;
        private ModPublishDialog dialog;
        private Task operation;
        private string phase;
        private string evidence;
        private string privateUrl;
        private string publicUrl;
        private string secondUrl;
        private string CollectionId => System.Environment.GetEnvironmentVariable("CTHANGOVER_E2E_COLLECTION_ID") ?? "test_private_3d_collection";
        private string profile;
        private ModInfo publishingMod;
        private CatalogExportEntry exported;
        private bool published;
        private bool finished;
        private int step;
        private int release;
        private int installing;
        private double elapsed;
        private double shownAt;

        public override void _Process(double delta)
        {
            if (finished) return;
            elapsed += delta;
            try
            {
                if (elapsed > 240) throw new InvalidOperationException("real private mods timeout: " + phase + "/" + step);
                if (!Boot.IsReady || operation != null && !operation.IsCompleted) return;
                operation?.GetAwaiter().GetResult();
                operation = null;
                if (step == 0)
                {
                    if (GetTree().CurrentScene?.FindChild("LauncherPlay", true, false) is Button play)
                    {
                        play.EmitSignal(BaseButton.SignalName.Pressed);
                        return;
                    }
                    if (GetTree().CurrentScene?.FindChild("play", true, false) is not Button || CatalogService.IsFetching) return;
                    Prepare();
                    step = 1;
                }
                if (phase == "publish") PublishStep();
                else InstallStep();
            }
            catch (Exception error) { Finish(error.ToString()); }
        }

        private void Prepare()
        {
            phase = Required("CTHANGOVER_E2E_PHASE");
            evidence = Required("CTHANGOVER_E2E_OUTPUT");
            privateUrl = Required("CTHANGOVER_PRIVATE_CATALOG_URL");
            secondUrl = Required("CTHANGOVER_SECOND_CATALOG_URL");
            publicUrl = Required("CTHANGOVER_PUBLIC_CATALOG_URL");
            profile = GamePaths.Globalize("user://");
            var isolated = Path.GetFullPath(Required("CTHANGOVER_E2E_PROFILE_ROOT"));
            if (!Path.GetFullPath(profile).StartsWith(isolated + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                throw new InvalidOperationException("refusing to use a non-isolated player profile");
            ModConfig.Instance.CatalogUrls = new List<string> { privateUrl, secondUrl, publicUrl };
            ModConfig.Instance.CatalogTokenFiles = new Dictionary<string, string>
            {
                [privateUrl] = Required("CTHANGOVER_PRIVATE_TOKEN_FILE"),
                [secondUrl] = Required("CTHANGOVER_SECOND_TOKEN_FILE")
            };
            if (phase == "publish")
                ExportPublic("tools_base");
            else
            {
                foreach (var id in ids)
                    RequireAbsent(id);
                if (phase.StartsWith("collection", StringComparison.Ordinal))
                {
                    RequireAbsent("tools_base");
                    RequireAbsent("schema_base");
                    RequireAbsent("expressions_base");
                }
                ModDownloader.OnCompleted += OnInstalled;
                operation = CatalogService.RefreshAsync(forceRefresh: true);
            }
            GameLogger.Log("TEST", "Real private mods phase=" + phase + " profile=" + profile);
        }

        private void ExportPublic(string id)
        {
            var destination = Path.Combine(evidence, "public");
            if (File.Exists(Path.Combine(destination, id + ".json"))) return;
            var mod = ModRegistry.Instance.AllDiscoveredMods[id] as ModInfo
                ?? throw new InvalidOperationException("public export needs a folder mod: " + id);
            var entry = Entry(mod);
            ModCatalogExporter.ExportFromFolder(mod, entry, destination);
            entry.Downloads[0].Urls = new List<string> { new Uri(new Uri(publicUrl), id + ".zip").AbsoluteUri };
            WriteJson(Path.Combine(destination, id + ".json"), entry);
            foreach (var dependency in mod.Manifest.RequiredDependencyIds)
                ExportPublic(dependency);
        }

        private void PublishStep()
        {
            if (step == 1)
            {
                publishingMod = ModRegistry.Instance.AllDiscoveredMods[ids[release / 3]] as ModInfo
                    ?? throw new InvalidOperationException("private mod missing");
                var path = Path.Combine(publishingMod.FileProvider.RootPath, "manifest.json");
                if (!ModRootFolders.Contains(Path.GetFileName(Path.GetDirectoryName(publishingMod.FileProvider.RootPath))))
                    throw new InvalidOperationException("publishing provider is outside a game mod root");
                if (!publishingMod.FileProvider.RootPath.Contains("mods_private"))
                    throw new InvalidOperationException("publishing must exercise the real private root");
                if (!manifests.ContainsKey(path)) manifests[path] = File.ReadAllBytes(path);
                if (release % 3 == 2)
                {
                    var manifest = JsonSerializer.Deserialize<ModManifest>(File.ReadAllText(path), JsonOptionsProvider.ReadOptions);
                    manifest.Version = "0.1.1";
                    WriteJson(path, manifest);
                    // Registry metadata deliberately remains at 0.1.0.
                }
                var version = release % 3 == 2 ? "0.1.1" : "0.1.0";
                exported = Entry(publishingMod);
                exported.Version = version;
                var directory = Path.Combine(evidence, "exports", publishingMod.Id, version);
                ModCatalogExporter.ExportFromFolder(publishingMod, exported, directory);
                exported.Downloads[0].Version = version;
                WriteJson(Path.Combine(directory, publishingMod.Id + ".json"), exported);
                OpenManager(publishingMod.Id);
                step = 2;
            }
            else if (step == 2)
            {
                operation = OpenPublishForm();
                step = 3;
            }
            else if (step == 3)
            {
                var publish = dialog.GetNode<Button>(Form + "Actions/Publish");
                var destinations = dialog.GetNode<OptionButton>(Form + "Server");
                if (destinations.Disabled || destinations.ItemCount < 2) return;
                if (destinations.Selected < 0)
                {
                    if (!publish.Disabled) throw new InvalidOperationException("publishing allowed without explicit destination");
                    destinations.Select(0);
                    destinations.EmitSignal(OptionButton.SignalName.ItemSelected, 0L);
                    return;
                }
                var consent = dialog.GetNode<CheckBox>(Form + "Consent/Accept");
                if (consent.IsVisibleInTree() && !consent.ButtonPressed) consent.ButtonPressed = true;
                if (publish.Disabled || elapsed - shownAt < 0.5) return;
                // The repeat captures the successful result; the first/new
                // releases capture the ready form before upload.
                operation = CaptureAndPress(release % 3 == 1 ? null : "publish-" + release, dialog, publish);
                step = 4;
            }
            else if (step == 4)
            {
                if (!published || CatalogService.IsFetching) return;
                operation = VerifyPublished();
                step = 5;
            }
            else if (step == 5)
            {
                operation = release % 3 == 1
                    ? Capture("publish-" + release, dialog, dialog.GetNode<Button>(Form + "Actions/Close"))
                    : Task.CompletedTask;
                step = 6;
            }
            else if (step == 6)
            {
                manager.QueueFree();
                manager = null;
                dialog = null;
                release++;
                if (release == 6) Finish(null);
                else step = 1;
            }
        }

        private async Task OpenPublishForm()
        {
            await WaitForUi(manager);
            var card = Find<ModCard>(manager) ?? throw new InvalidOperationException("private installed card absent");
            RequireVisible(card, 200, 100);
            using (var click = new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true })
                card.EmitSignal(Control.SignalName.GuiInput, click);
            if (!manager.IsDetailOpen) throw new InvalidOperationException("private detail did not open");
            var button = manager.GetNode<Button>("Margin/Root/Body/Content/Detail/Scroll/Content/ActionsRow/Publish");
            await WaitForUi(manager);
            RequireButton(button);
            button.EmitSignal(BaseButton.SignalName.Pressed);
            dialog = manager.FindChild("ModPublishDialog", true, false) as ModPublishDialog
                ?? throw new InvalidOperationException("real Publish action did not open the form");
            published = false;
            dialog.Published += OnPublished;
            shownAt = elapsed;
        }

        private void OnPublished()
        {
            published = true;
            operation = CatalogService.RefreshAsync(forceRefresh: true);
        }

        private async Task VerifyPublished()
        {
            var entry = CatalogMerger.FindEntry(CatalogService.CachedEntries, publishingMod.Id, privateUrl)
                ?? throw new InvalidOperationException("published real mod missing from server catalog");
            var expectedCount = release % 3 == 2 ? 2 : 1;
            if (entry.Version != exported.Version || entry.Downloads.Count != expectedCount)
                throw new InvalidOperationException("release history/current version differs after publication " + release);
            var expected = exported.Downloads[0];
            var actual = entry.Downloads.First(d => d.Version == exported.Version);
            if (actual.ArchiveSha256 != expected.ArchiveSha256 || actual.InstallSha256 != expected.InstallSha256)
                throw new InvalidOperationException("server checksums differ from local export");
            using var client = new System.Net.Http.HttpClient();
            var bytes = await client.GetByteArrayAsync(actual.Urls[0]);
            var path = Path.Combine(evidence, "download-" + publishingMod.Id + "-" + release + ".zip");
            File.WriteAllBytes(path, bytes);
            if (ModHasher.ComputeArchiveSha256(path) != expected.ArchiveSha256)
                throw new InvalidOperationException("server archive bytes differ from local export");
            using var package = new ZipModFileProvider(path, publishingMod.Id);
            if (ModHasher.ComputeInstallSha256(package, ModConfig.Instance.CatalogIgnorePatterns) != expected.InstallSha256)
                throw new InvalidOperationException("downloaded private install hash differs");
            // Persist evidence without temporary download secrets.
            var snapshot = JsonSerializer.Deserialize<CatalogExportEntry>(JsonSerializer.Serialize(entry), JsonOptionsProvider.ReadOptions);
            foreach (var download in snapshot.Downloads) download.Urls = new List<string>();
            WriteJson(Path.Combine(evidence, "publish-" + release + ".json"), snapshot);
            GameLogger.Log("TEST", "Verified GUI publication " + publishingMod.Id + " " + exported.Version + " history=" + expectedCount);
        }

        private void InstallStep()
        {
            if (step == 1)
            {
                if (operation != null || CatalogService.IsFetching) return;
                foreach (var id in ids)
                    if (CatalogMerger.FindEntry(CatalogService.CachedEntries, id, privateUrl) == null)
                        throw new InvalidOperationException("private catalog entry absent: " + id);
                // The harness invalidates the just-fetched grants. The button's
                // downloader must refresh access, rather than use these stale URLs.
                File.WriteAllText(Path.Combine(evidence, phase + ".expire"), "ready");
                step = 2;
            }
            else if (step == 2)
            {
                if (!File.Exists(Path.Combine(evidence, phase + ".expired"))) return;
                if (phase == "install") OpenManager(ids[installing]);
                else OpenGallery();
                shownAt = elapsed;
                step = 3;
            }
            else if (step == 3)
            {
                if (elapsed - shownAt < 0.5) return;
                Button button;
                if (phase == "install")
                {
                    var card = Find<ModCard>(manager) ?? throw new InvalidOperationException("download card absent");
                    button = card.GetNode<Button>("VBox/Body/Box/ActionRow/ActionHost/Action");
                }
                else
                {
                    var gallery = Find<CollectionsGallery>(hub) ?? throw new InvalidOperationException("gallery absent");
                    var text = string.Format(TranslationServer.Translate("mod_manager/gallery_install_btn_n"), 5);
                    button = FindButton(gallery, text) ?? throw new InvalidOperationException("mixed collection must offer five missing mods");
                    if (FindLabel(gallery, TranslationServer.Translate("mod_manager/gallery_ready")) != null)
                        throw new InvalidOperationException("partial collection incorrectly marked ready");
                }
                operation = CaptureAndPress(phase + "-before-" + installing, phase == "install" ? manager : hub, button);
                step = 4;
            }
            else if (step == 4)
            {
                var expected = phase == "install" ? new[] { ids[installing] } : new[] { "world3d_base", "world3d_editor", "tools_base", "schema_base", "expressions_base" };
                if (!expected.All(completed.Contains)) return;
                foreach (var id in expected) VerifyInstalled(id);
                if (phase == "install" && installing == 0)
                {
                    manager.QueueFree();
                    manager = null;
                    installing++;
                    OpenManager(ids[installing]);
                    shownAt = elapsed;
                    step = 3;
                    return;
                }
                if (phase == "collection_catalog" || phase == "collection_published")
                {
                    var saved = ModCollectionStore.LoadUser().SingleOrDefault(c => c.Id == CollectionId);
                    if (saved == null) return;
                    if (saved.Version != (phase == "collection_published" ? "0.1.1" : "0.1.0") || saved.Mechanics[0].Mods[0] != "world3d_editor>=0.1.0")
                        throw new InvalidOperationException("installed private collection document differs");
                    WriteJson(Path.Combine(evidence, phase + "-installed-collection.json"), saved);
                    if (phase == "collection_published" && (saved.Package?.ReadFileBinary("cover.png")?.Length < 20
                        || !saved.Package.FileExists("locale/en.properties") || !saved.Package.FileExists("locale/ru.properties")))
                        throw new InvalidOperationException("published collection presentation was not installed");
                }
                var cache = File.ReadAllText(GamePaths.Globalize("user://catalog_cache.json"));
                if (cache.Contains("world3d_") || cache.Contains("/v1/download/"))
                    throw new InvalidOperationException("private data leaked into public disk cache");
                File.WriteAllText(Path.Combine(evidence, phase + "-profile.txt"), profile);
                operation = CaptureInstalledState();
                step = 5;
            }
            else if (step == 5)
            {
                Finish(null);
            }
        }

        private async Task CaptureInstalledState()
        {
            await WaitForUi(phase == "install" ? manager : hub);
            Control target;
            if (phase == "install")
            {
                var card = Find<ModCard>(manager) ?? throw new InvalidOperationException("installed card absent");
                var action = card.GetNode<Button>("VBox/Body/Box/ActionRow/ActionHost/Action");
                if (action.Visible && action.Text == TranslationServer.Translate("mod_manager/action_install"))
                    throw new InvalidOperationException("installed card still offers installation");
                target = card;
            }
            else
            {
                var gallery = Find<CollectionsGallery>(hub) ?? throw new InvalidOperationException("installed gallery absent");
                var ready = FindButton(gallery, TranslationServer.Translate("mod_manager/gallery_ready_done"))
                    ?? throw new InvalidOperationException("installed collection is not shown as ready");
                if (!ready.Disabled) throw new InvalidOperationException("ready collection still offers an action");
                target = ready;
            }
            await Capture(phase + "-after", phase == "install" ? manager : hub, target);
        }

        private void OpenManager(string id)
        {
            manager = TscnScenes.LoadAndBuild("scenes/mod_manager_screen.tscn") as ModManagerScreen
                ?? throw new InvalidOperationException("manager prefab failed");
            GetTree().Root.AddChild(manager);
            manager.EnsureConstructed();
            manager.SetSearch(id);
        }

        private void OpenGallery()
        {
            var collection = ModCollectionStore.LoadAll().Single(c => c.Id == CollectionId);
            var graph = ModCollectionResolver.Resolve(collection, ModRegistry.Instance.AllDiscoveredMods,
                DisabledModsStore.Load(), CatalogService.CachedEntries);
            var plan = ModCollectionPlan.Build(graph, CatalogService.CachedEntries);
            WriteJson(Path.Combine(evidence, phase + "-plan.json"), plan);
            var expected = new HashSet<string>(new[] { "world3d_base", "world3d_editor", "tools_base", "schema_base", "expressions_base" });
            if (plan.HasProblems || !expected.SetEquals(plan.Install.Select(item => item.ModId)))
                throw new InvalidOperationException("mixed collection dependency closure differs; see plan evidence");
            var origin = phase == "collection_local" ? ModCollectionOrigin.BuiltIn : ModCollectionOrigin.Catalog;
            if (collection.Origin != origin) throw new InvalidOperationException("wrong collection origin");
            hub = TscnScenes.LoadAndBuild("scenes/mods_hub_screen.tscn") as ModHubScreen
                ?? throw new InvalidOperationException("hub prefab failed");
            GetTree().Root.AddChild(hub);
            hub.EnsureConstructed();
            operation = OpenCollectionTab(collection.Id);
        }

        private async Task OpenCollectionTab(string collectionId)
        {
            await WaitForUi(hub);
            var button = FindButton(hub, TranslationServer.Translate("mod_manager/hub_tab_collections"))
                ?? throw new InvalidOperationException("Collections tab absent");
            RequireButton(button);
            button.EmitSignal(BaseButton.SignalName.Pressed);
            var gallery = Find<CollectionsGallery>(hub) ?? throw new InvalidOperationException("gallery absent");
            gallery.ApplyFilter(collectionId);
        }

        private void OnInstalled(string id, bool success)
        {
            if (!success) throw new InvalidOperationException("GUI installation failed: " + id);
            completed.Add(id);
        }

        private void VerifyInstalled(string id)
        {
            var target = Path.Combine(profile, "mods", id);
            if (!Directory.Exists(target)) throw new InvalidOperationException("download not in user mod root: " + id);
            using var package = new FolderModFileProvider(target, id);
            var manifest = JsonSerializer.Deserialize<ModManifest>(package.ReadFileText("manifest.json"), JsonOptionsProvider.ReadOptions);
            var entry = CatalogMerger.FindEntry(CatalogService.CachedEntries, id, id.StartsWith("world3d_") ? privateUrl : publicUrl);
            var download = entry.Downloads.Single(d => d.Version == manifest.Version);
            var hash = ModHasher.ComputeInstallSha256(package, ModConfig.Instance.CatalogIgnorePatterns);
            if (manifest.Id != id || hash != download.InstallSha256)
                throw new InvalidOperationException("installed manifest/content hash mismatch: " + id);
            WriteJson(Path.Combine(evidence, phase + "-" + id + ".json"), new { id, manifest.Version, install_sha256 = hash, target });
        }

        private static void RequireAbsent(string id)
        {
            if (ModRegistry.Instance.AllDiscoveredMods.ContainsKey(id) || Directory.Exists(GamePaths.Globalize("user://mods/" + id)))
                throw new InvalidOperationException("test requires physically absent mod: " + id);
        }

        private static CatalogExportEntry Entry(ModInfo mod) => new()
        {
            Id = mod.Id, Name = mod.Name, Version = mod.Version, Category = mod.Manifest.Category ?? CategoryType.Other,
            Depends = mod.Manifest.Depends?.Select(d => d.ToString()).ToList()
        };

        private static T Find<T>(Node node) where T : Node
        {
            if (node is T result && !node.IsQueuedForDeletion()) return result;
            foreach (var child in node.GetChildren())
            {
                var found = Find<T>(child);
                if (found != null) return found;
            }
            return null;
        }

        private static Label FindLabel(Node node, string text)
        {
            if (node is Label label && label.Text == text && label.IsVisibleInTree()) return label;
            foreach (var child in node.GetChildren()) { var found = FindLabel(child, text); if (found != null) return found; }
            return null;
        }

        private static Button FindButton(Node node, string text)
        {
            if (node is Button button && button.Text == text && !node.IsQueuedForDeletion()) return button;
            foreach (var child in node.GetChildren())
            {
                var found = FindButton(child, text);
                if (found != null) return found;
            }
            return null;
        }

        private static string Required(string name) => System.Environment.GetEnvironmentVariable(name)
            ?? throw new InvalidOperationException("real-server harness variable missing: " + name);

        private static void WriteJson<T>(string path, T value)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, JsonSerializer.Serialize(value, JsonOptionsProvider.WriteIndentedIgnoreNullOptions));
        }

        private static void RequireVisible(Control control, float width, float height)
        {
            if (!GodotObject.IsInstanceValid(control) || !control.IsVisibleInTree()
                || control.Size.X < width || control.Size.Y < height
                || !control.GetGlobalRect().Intersects(control.GetViewportRect()))
                throw new InvalidOperationException("GUI control is not visibly laid out: " + control?.Name);
        }

        private static void RequireButton(Button button)
        {
            RequireVisible(button, 20, 16);
            if (button.Disabled) throw new InvalidOperationException("GUI action is disabled: " + button.Name);
        }

        private async Task WaitForUi(Control surface)
        {
            var tree = GetTree();
            await ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            await ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            // Dummy headless rendering never emits FramePostDraw. Windowed
            // captures and clicks must wait until the actual viewport was drawn.
            if (DisplayServer.GetName() != "headless")
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            var screen = phase == "publish" || phase == "install" ? (Control)manager : hub;
            if (screen.GetParent() != tree.Root)
                throw new InvalidOperationException("test screen must be a root overlay above the menu");
            RequireVisible(screen, 700, 400);
            RequireVisible(surface, 700, 400);
        }

        private async Task CaptureAndPress(string name, Control surface, Button button)
        {
            await Capture(name, surface, button);
            RequireButton(button);
            button.EmitSignal(BaseButton.SignalName.Pressed);
        }

        private async Task Capture(string name, Control surface, Control target)
        {
            await WaitForUi(surface);
            RequireVisible(target, 20, 16);
            if (phase == "publish") RequireVisible(dialog.GetNode<Control>("Center/Panel"), 700, 400);
            if (name == null) return;
            var shots = System.Environment.GetEnvironmentVariable("CTHANGOVER_E2E_SHOTS");
            if (string.IsNullOrEmpty(shots)) return;
            Directory.CreateDirectory(shots);
            if (!ViewportCapture.SavePng(GetViewport(), Path.Combine(shots, name + ".png")))
                throw new InvalidOperationException("viewport capture failed");
        }

        private void Finish(string error)
        {
            if (finished) return;
            finished = true;
            ModDownloader.OnCompleted -= OnInstalled;
            foreach (var pair in manifests) File.WriteAllBytes(pair.Key, pair.Value);
            if (GodotObject.IsInstanceValid(manager)) manager.QueueFree();
            if (GodotObject.IsInstanceValid(hub)) hub.QueueFree();
            if (error != null) GameLogger.Log("TEST", error, LogLevel.Error);
            GameLogger.Log("TEST", $"[TEST_RESULT] name=private_real_mods passed={(error == null ? 1 : 0)} failed={(error == null ? 0 : 1)}");
            ApplicationExit.Quit(GetTree(), error == null ? 0 : 1);
        }
    }
}
