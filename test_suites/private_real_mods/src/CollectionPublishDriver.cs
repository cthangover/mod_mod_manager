using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Cthangover.Core.Bootstrap;
using Cthangover.Core.Mods;
using Cthangover.Core.Scenes;
using Cthangover.Core.Utils;
using Cthangover.Mods.Interface.View;
using Godot;

namespace Mods.ModManager.PrivateRealModsTest
{
    /// <summary>Exercises both collection entry points and explicit destinations against two live catalogs.</summary>
    public partial class CollectionPublishDriver : Node
    {
        private const string Form = "Center/Panel/Margin/Form/";
        private const string Id = "test_published_3d_collection";
        private Task operation;
        private double elapsed;
        private bool finished;
        private string second;
        private string output;
        private ModPublishDialog dialog;
        private Control screen;
        private bool published;

        public override void _Process(double delta)
        {
            if (finished) return;
            elapsed += delta;
            try
            {
                if (elapsed > 240) throw new InvalidOperationException("collection publication timeout");
                if (operation == null)
                {
                    if (!Boot.IsReady || CatalogService.IsFetching) return;
                    if (GetTree().CurrentScene?.FindChild("LauncherPlay", true, false) is Button play)
                    { play.EmitSignal(BaseButton.SignalName.Pressed); return; }
                    if (GetTree().CurrentScene?.FindChild("play", true, false) is not Button) return;
                    operation = Run();
                }
                if (!operation.IsCompleted) return;
                operation.GetAwaiter().GetResult();
                Finish(null);
            }
            catch (Exception error) { Finish(error.ToString()); }
        }

        private async Task Run()
        {
            output = Required("CTHANGOVER_E2E_OUTPUT");
            var first = Required("CTHANGOVER_PRIVATE_CATALOG_URL");
            second = Required("CTHANGOVER_SECOND_CATALOG_URL");
            var profile = Path.GetFullPath(GamePaths.Globalize("user://"));
            if (!profile.StartsWith(Path.GetFullPath(Required("CTHANGOVER_E2E_PROFILE_ROOT")) + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                throw new InvalidOperationException("publication requires isolated profile");
            ModConfig.Instance.CatalogUrls = new() { first, second, Required("CTHANGOVER_PUBLIC_CATALOG_URL") };
            ModConfig.Instance.CatalogTokenFiles = new() { [first] = Required("CTHANGOVER_PRIVATE_TOKEN_FILE"), [second] = Required("CTHANGOVER_SECOND_TOKEN_FILE") };
            await CatalogService.RefreshAsync(forceRefresh: true);
            for (var release = 0; release < 3; release++)
            {
                var collection = ModCollectionStore.LoadAll().Single(c => c.Id == Id);
                if (release == 2)
                {
                    collection.Version = "0.1.1";
                    File.WriteAllText(Path.Combine(collection.Package.RootPath, "collection.json"), JsonSerializer.Serialize(collection, JsonOptionsProvider.WriteIndentedIgnoreNullOptions));
                }
                var directory = Path.Combine(output, "collection-exports", release.ToString(System.Globalization.CultureInfo.InvariantCulture));
                var exported = CollectionPackage.Export(collection, directory);
                File.WriteAllText(Path.Combine(directory, Id + ".json"), exported.Json);
                if (release == 1)
                {
                    var editor = TscnScenes.LoadAndBuild("scenes/mod_collections_screen.tscn") as ModCollectionsScreen
                        ?? throw new InvalidOperationException("collection editor absent");
                    screen = editor; GetTree().Root.AddChild(screen); editor.EnsureConstructed(); editor.OpenCollection(Id);
                    await Draw(screen);
                    Press(Find<Button>(screen, "PublishCollection"));
                }
                else
                {
                    var hub = TscnScenes.LoadAndBuild("scenes/mods_hub_screen.tscn") as ModHubScreen
                        ?? throw new InvalidOperationException("hub absent");
                    screen = hub;GetTree().Root.AddChild(screen);hub.EnsureConstructed();await Draw(screen);
                    Press(FindText(screen, TranslationServer.Translate("mod_manager/hub_tab_collections")));
                    var gallery = Find<CollectionsGallery>(screen);gallery.ApplyFilter(Id);await Draw(screen);
                    Press(Find<Button>(gallery, "PublishCollection"));
                }
                dialog = Find<ModPublishDialog>(GetTree().Root) ?? throw new InvalidOperationException("collection Publish action did not open form");
                if (!dialog.GetNode<Control>(Form + "Requirements").Visible || !dialog.GetNode<Label>(Form + "Requirements").Text.Contains("test_unavailable_collection"))
                    throw new InvalidOperationException("required references/warning absent");
                await Publish("collection-publish-" + release, release == 0);
                var entry = CatalogService.CachedCollections.Single(c => c.Id == Id);
                if (entry.Version != collection.Version || entry.Downloads.Count != (release == 2 ? 2 : 1)
                    || entry.SourceUrls.Count != 1 || entry.SourceUrls[0] != second)
                    throw new InvalidOperationException("collection destination/history differs");
                var metadata = JsonSerializer.Deserialize<ModCollection>(exported.Json, JsonOptionsProvider.ReadOptions);
                if (entry.Downloads[0].ArchiveSha256 != metadata.Downloads[0].ArchiveSha256)
                    throw new InvalidOperationException("published collection differs from exported package");
                CloseScreens();await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            }
            var mod = (ModInfo)ModRegistry.Instance.AllDiscoveredMods["world3d_editor"];
            var manager = TscnScenes.LoadAndBuild("scenes/mod_manager_screen.tscn") as ModManagerScreen;
            screen = manager;GetTree().Root.AddChild(screen);manager.EnsureConstructed();manager.SetSearch(mod.Id);await Draw(screen);
            var card = Find<ModCard>(manager);
            using (var click = new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true }) card.EmitSignal(Control.SignalName.GuiInput, click);
            await Draw(screen);
            Press(manager.GetNode<Button>("Margin/Root/Body/Content/Detail/Scroll/Content/ActionsRow/Publish"));
            dialog = Find<ModPublishDialog>(manager) ?? throw new InvalidOperationException("mod publish form absent");
            await Publish("mod-second-repository", true);
            if (CatalogMerger.FindEntry(CatalogService.CachedEntries, mod.Id, second) == null)
                throw new InvalidOperationException("mod absent from second destination");
        }

        private async Task Publish(string shot, bool mustChoose)
        {
            published = false;dialog.Published += OnPublished;
            var servers = dialog.GetNode<OptionButton>(Form + "Server");
            while (servers.Disabled || servers.ItemCount < 2) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var publish = dialog.GetNode<Button>(Form + "Actions/Publish");
            if (!servers.GetItemText(0).Contains(Required("CTHANGOVER_PRIVATE_CATALOG_URL")) || !servers.GetItemText(1).Contains(second))
                throw new InvalidOperationException("destinations do not show full URLs");
            if (mustChoose)
            {
                if (servers.Selected != -1 || !publish.Disabled) throw new InvalidOperationException("multiple destinations require explicit choice");
                if (DisplayServer.GetName() != "headless") servers.ShowPopup();
                await Capture(shot + "-choice");
                servers.GetPopup().Hide();
                servers.Select(1);servers.EmitSignal(OptionButton.SignalName.ItemSelected, 1L);
            }
            else if (servers.Selected != 1) throw new InvalidOperationException("previous successful collection destination not remembered");
            var identity = dialog.GetNode<Label>(Form + "Identity");
            while (identity.Text.Length == 0) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            var consent = dialog.GetNode<CheckBox>(Form + "Consent/Accept");
            if (consent.IsVisibleInTree())
            {
                if (!publish.Disabled) throw new InvalidOperationException("author consent not enforced");
                consent.ButtonPressed = true;
            }
            while (publish.Disabled) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await Capture(shot + "-form");Press(publish);
            while (!published) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            while (CatalogService.IsFetching) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (!dialog.GetNode<Label>(Form + "Status").Text.Contains(second)) throw new InvalidOperationException("result does not name destination");
            await Capture(shot + "-after");
            dialog.Published -= OnPublished;
        }

        private async void OnPublished() { await CatalogService.RefreshAsync(forceRefresh: true); published = true; }
        private async Task Draw(Control control)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (DisplayServer.GetName() != "headless") await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            if (!control.IsVisibleInTree() || control.Size.X < 700 || control.Size.Y < 400 || !control.GetGlobalRect().Intersects(control.GetViewportRect())) throw new InvalidOperationException("publication UI not laid out");
        }
        private async Task Capture(string name)
        {
            await Draw(screen);await Draw(dialog);await Draw(dialog.GetNode<Control>("Center/Panel"));
            var path = System.Environment.GetEnvironmentVariable("CTHANGOVER_E2E_SHOTS");
            if (string.IsNullOrEmpty(path)) return;
            Directory.CreateDirectory(path);
            if (!ViewportCapture.SavePng(GetViewport(), Path.Combine(path, name + ".png"))) throw new InvalidOperationException("capture failed");
        }
        private static void Press(Button button)
        {
            if (button == null || !button.IsVisibleInTree() || button.Size.X < 20 || button.Size.Y < 16 || !button.GetGlobalRect().Intersects(button.GetViewportRect()) || button.Disabled) throw new InvalidOperationException("GUI button not available");
            button.EmitSignal(BaseButton.SignalName.Pressed);
        }
        private static T Find<T>(Node node, string name = null) where T : Node
        {
            if (node is T typed && !node.IsQueuedForDeletion() && (name == null || node.Name == name)) return typed;
            foreach (var child in node.GetChildren()) { var found = Find<T>(child, name); if (found != null) return found; } return null;
        }
        private static Button FindText(Node node, string text)
        {
            if (node is Button button && button.Text == text) return button;
            foreach (var child in node.GetChildren()) { var found = FindText(child, text); if (found != null) return found; } return null;
        }
        private static string Required(string name) => System.Environment.GetEnvironmentVariable(name) ?? throw new InvalidOperationException(name);
        private void CloseScreens() { if (GodotObject.IsInstanceValid(dialog)) dialog.QueueFree();if (GodotObject.IsInstanceValid(screen)) screen.QueueFree();dialog = null;screen = null; }
        private void Finish(string error)
        {
            if (finished) return;finished = true;CloseScreens();
            if (error != null) GameLogger.Log("TEST", error, LogLevel.Error);
            GameLogger.Log("TEST", $"[TEST_RESULT] name=private_real_mods passed={(error == null ? 1 : 0)} failed={(error == null ? 0 : 1)}");
            ApplicationExit.Quit(GetTree(), error == null ? 0 : 1);
        }
    }
}
