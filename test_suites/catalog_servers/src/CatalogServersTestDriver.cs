using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Cthangover.Core.Bootstrap;
using Cthangover.Core.Mods;
using Cthangover.Core.Utils;
using Godot;

namespace Mods.ModManager.CatalogServersTest
{
    /// <summary>Creates and edits a private source with real controls, installs from it, then removes every source and restores the profile.</summary>
    public partial class CatalogServersTestDriver : Node
    {
        private const string Form = "Center/Panel/Margin/Form/Body/Editor/";
        private const string ListPath = "Center/Panel/Margin/Form/Body/Servers/List";
        private ModHubScreen hub;
        private CatalogServersDialog dialog;
        private List<string> originalUrls;
        private Dictionary<string, string> originalTokenFiles;
        private byte[] originalCache;
        private byte[] originalServers;
        private string cachePath;
        private string serversPath;
        private string token;
        private string privateUrl;
        private string ownedKey;
        private readonly HashSet<string> createdKeys = new(StringComparer.Ordinal);
        private string installedPath;
        private Task operation;
        private bool installComplete;
        private bool installSuccess;
        private bool finished;
        private double elapsed;
        private int step;

        public override void _Process(double delta)
        {
            if (finished || !GameEnvironment.IsRuntime)
                return;
            elapsed += delta;
            try
            {
                if (elapsed > 110)
                    throw new InvalidOperationException("catalog servers timeout at step " + step);
                if (!Boot.IsReady)
                    return;
                if (operation != null && !operation.IsCompleted)
                    return;
                operation?.GetAwaiter().GetResult();
                operation = null;
                if (CatalogService.IsFetching)
                    return;
                if (GodotObject.IsInstanceValid(dialog) && dialog.GetNode<Button>(Form + "Actions/Save").Disabled)
                    return;
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
                    if (GetTree().CurrentScene?.FindChild("play", true, false) is not Button)
                        return;
                    Prepare();
                    operation = CatalogService.RefreshAsync(forceRefresh: true);
                    step++;
                    break;
                case 1:
                    var open = GetTree().CurrentScene.FindChild("mods_hub", true, false) as Button
                        ?? throw new InvalidOperationException("main menu has no mods hub button");
                    open.EmitSignal(BaseButton.SignalName.Pressed);
                    hub = GetTree().CurrentScene.FindChild("ModHubScreen", true, false) as ModHubScreen
                        ?? throw new InvalidOperationException("menu button did not open the hub");
                    if (!hub.IsVisibleInTree() || hub.Size.X < 800 || hub.Size.Y < 500)
                        throw new InvalidOperationException("hub overlay has no visible layout");
                    OpenSources();
                    RequireCount(1);
                    dialog.GetNode<Button>("Center/Panel/Margin/Form/Body/Servers/New").EmitSignal(BaseButton.SignalName.Pressed);
                    dialog.GetNode<LineEdit>(Form + "Name").Text = "Local from UI";
                    dialog.GetNode<LineEdit>(Form + "Url").Text = privateUrl.Replace("http://", "").Replace("/v1/catalog", "");
                    var secret = dialog.GetNode<LineEdit>(Form + "Key");
                    if (!secret.Secret)
                        throw new InvalidOperationException("catalog key is not masked");
                    secret.Text = token;
                    Save();
                    step++;
                    break;
                case 2:
                    RequireCount(2);
                    RequireEntries(true);
                    var added = FindPrivate();
                    ownedKey = added.TokenFile;
                    createdKeys.Add(ownedKey);
                    if (File.ReadAllText(ownedKey).Trim() != token || dialog.GetNode<LineEdit>(Form + "Key").Text != "")
                        throw new InvalidOperationException("key not persisted privately or input not cleared");
                    dialog.GetNode<Button>("Center/Panel/Margin/Form/Close").EmitSignal(BaseButton.SignalName.Pressed);
                    step++;
                    break;
                case 3:
                    if (GodotObject.IsInstanceValid(dialog))
                        return;
                    OpenSources();
                    SelectPrivate();
                    if (dialog.GetNode<LineEdit>(Form + "Name").Text != "Local from UI")
                        throw new InvalidOperationException("saved source was not restored after reopening");
                    dialog.GetNode<LineEdit>(Form + "Name").Text = "Edited local source";
                    dialog.GetNode<CheckBox>(Form + "Enabled").ButtonPressed = false;
                    Save();
                    step++;
                    break;
                case 4:
                    RequireCount(2);
                    RequireEntries(false);
                    var disabled = FindPrivate();
                    if (disabled.Enabled || disabled.Name != "Edited local source" || disabled.TokenFile != ownedKey)
                        throw new InvalidOperationException("source edit or retained key differs");
                    dialog.GetNode<CheckBox>(Form + "Enabled").ButtonPressed = true;
                    Save();
                    step++;
                    break;
                case 5:
                    RequireEntries(true);
                    dialog.GetNode<LineEdit>(Form + "Key").Text = new string('A', 1024);
                    Save();
                    step++;
                    break;
                case 6:
                    RequireEntries(false);
                    createdKeys.Add(FindPrivate().TokenFile);
                    dialog.GetNode<LineEdit>(Form + "Key").Text = token;
                    Save();
                    step++;
                    break;
                case 7:
                    RequireEntries(true);
                    ownedKey = FindPrivate().TokenFile;
                    createdKeys.Add(ownedKey);
                    operation = SaveScreenshotAsync();
                    step = 12;
                    break;
                case 12:
                    dialog.GetNode<Button>("Center/Panel/Margin/Form/Close").EmitSignal(BaseButton.SignalName.Pressed);
                    step = 8;
                    break;
                case 8:
                    if (GodotObject.IsInstanceValid(dialog))
                        return;
                    var manager = hub.FindChild("ModManagerScreen", true, false) as ModManagerScreen;
                    if (manager == null)
                        throw new InvalidOperationException("manager tab missing");
                    VerifyCatalogFilters(manager);
                    operation = SaveFilterScreenshotAsync();
                    step = 13;
                    break;
                case 13:
                    var filteredManager = hub.FindChild("ModManagerScreen", true, false) as ModManagerScreen;
                    var card = FindCard(filteredManager) ?? throw new InvalidOperationException("private source has no install card");
                    ModDownloader.OnCompleted += OnInstalled;
                    card.GetNode<Button>("VBox/Body/Box/ActionRow/ActionHost/Action").EmitSignal(BaseButton.SignalName.Pressed);
                    step = 9;
                    break;
                case 9:
                    if (!installComplete)
                        return;
                    if (!installSuccess || File.ReadAllText(Path.Combine(installedPath, "data/check.txt")) != "Private catalog download works.")
                        throw new InvalidOperationException("UI-configured source did not install the expected bytes");
                    OpenSources();
                    SelectPrivate();
                    DeleteSelected();
                    step++;
                    break;
                case 10:
                    RequireCount(1);
                    RequireEntries(false);
                    if (File.Exists(ownedKey) || ModConfig.Instance.CatalogTokenFiles.Count != 0)
                        throw new InvalidOperationException("removed source retained an owned key or permission");
                    if (!Directory.Exists(installedPath))
                        throw new InvalidOperationException("removing a catalog removed the installed mod");
                    dialog.GetNode<ItemList>(ListPath).Select(0);
                    dialog.GetNode<ItemList>(ListPath).EmitSignal(ItemList.SignalName.ItemSelected, 0L);
                    DeleteSelected();
                    step++;
                    break;
                case 11:
                    RequireCount(0);
                    if (CatalogService.CachedEntries?.Count != 0 || ModConfig.Instance.CatalogUrls.Count != 0
                        || CatalogServerStore.Default.Load(new ModConfig()).Count != 0)
                        throw new InvalidOperationException("empty source list did not remain authoritative");
                    Finish(null);
                    break;
            }
        }

        private void Prepare()
        {
            privateUrl = System.Environment.GetEnvironmentVariable("CTHANGOVER_PRIVATE_CATALOG_URL");
            var tokenPath = System.Environment.GetEnvironmentVariable("CTHANGOVER_PRIVATE_TOKEN_FILE");
            var publicUrl = System.Environment.GetEnvironmentVariable("CTHANGOVER_PUBLIC_CATALOG_URL");
            if (string.IsNullOrEmpty(privateUrl) || string.IsNullOrEmpty(tokenPath) || string.IsNullOrEmpty(publicUrl))
                throw new InvalidOperationException("use the keyserver integration harness with --suite catalog_servers");
            token = File.ReadAllText(tokenPath).Trim();
            var target = GamePaths.Globalize("user://mods/private_test_pack");
            if (Directory.Exists(target) || ModRegistry.Instance.AllDiscoveredMods.ContainsKey("private_test_pack"))
                throw new InvalidOperationException("probe mod already installed; refusing to replace it");
            installedPath = target;
            cachePath = GamePaths.Globalize("user://catalog_cache.json");
            serversPath = GamePaths.Globalize("user://catalog_servers.json");
            originalCache = File.Exists(cachePath) ? File.ReadAllBytes(cachePath) : null;
            originalServers = File.Exists(serversPath) ? File.ReadAllBytes(serversPath) : null;
            originalUrls = ModConfig.Instance.CatalogUrls;
            originalTokenFiles = ModConfig.Instance.CatalogTokenFiles;
            File.Delete(serversPath);
            ModConfig.Instance.CatalogUrls = new List<string> { publicUrl };
            ModConfig.Instance.CatalogTokenFiles = new Dictionary<string, string>();
        }

        private void OpenSources()
        {
            (hub.FindChild("CatalogsButton", true, false) as Button)?.EmitSignal(BaseButton.SignalName.Pressed);
            dialog = hub.FindChild("CatalogServersDialog", true, false) as CatalogServersDialog
                ?? throw new InvalidOperationException("catalog management button did not open the dialog");
            if (!dialog.IsVisibleInTree())
                throw new InvalidOperationException("catalog manager is not visible");
        }
        private void Save() => dialog.GetNode<Button>(Form + "Actions/Save").EmitSignal(BaseButton.SignalName.Pressed);

        private void VerifyCatalogFilters(ModManagerScreen manager)
        {
            var panel = manager.GetNode<ModFilterPanel>("Margin/Root/Body/Sidebar/SidebarScroll/FilterPanel");
            var select = panel.GetNode<OptionButton>("CatalogSelect");
            if (select.Selected != 0 || select.GetItemText(0) != TranslationServer.Translate("mod_manager/filter_all_catalogs").ToString())
                throw new InvalidOperationException("catalog filter did not default to all sources");
            manager.SetSearch("private_test_pack");
            RequireGridCount(manager, 1);
            var publicUrl = System.Environment.GetEnvironmentVariable("CTHANGOVER_PUBLIC_CATALOG_URL");
            SelectCatalog(select, publicUrl);
            RequireGridCount(manager, 0);
            manager.SetSearch("public_test_pack");
            RequireGridCount(manager, 1);
            SelectCatalog(select, privateUrl);
            RequireGridCount(manager, 0);
            manager.SetSearch("private_test_pack");
            RequireGridCount(manager, 1);
            select.Select(1);
            select.EmitSignal(OptionButton.SignalName.ItemSelected, 1L);
            RequireGridCount(manager, 0);
            panel.GetNode<Button>("Reset").EmitSignal(BaseButton.SignalName.Pressed);
            if (select.Selected != 0 || manager.GetNode<LineEdit>("Margin/Root/Header/Search").Text != "")
                throw new InvalidOperationException("reset did not restore all catalogs and clear search");
            manager.SetSearch("private_test_pack");
            RequireGridCount(manager, 1);
            SelectCatalog(select, privateUrl);
        }

        private static void SelectCatalog(OptionButton select, string url)
        {
            for (var index = 2; index < select.ItemCount; index++)
                if (select.GetItemMetadata(index).AsString() == url)
                {
                    select.Select(index);
                    select.EmitSignal(OptionButton.SignalName.ItemSelected, (long)index);
                    return;
                }
            throw new InvalidOperationException("configured source is missing from the sidebar filter");
        }

        private static void RequireGridCount(Node node, int expected)
        {
            var count = CountCards(node);
            if (count != expected)
                throw new InvalidOperationException($"catalog filter expected {expected} cards, got {count}");
        }

        private static int CountCards(Node node)
        {
            if (node.IsQueuedForDeletion())
                return 0;
            if (node is ModCard)
                return 1;
            var count = 0;
            foreach (var child in node.GetChildren())
                count += CountCards(child);
            return count;
        }

        private async Task SaveFilterScreenshotAsync()
        {
            var path = System.Environment.GetEnvironmentVariable("CTHANGOVER_CATALOG_FILTER_SHOT");
            if (string.IsNullOrEmpty(path) || DisplayServer.GetName() == "headless")
                return;
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            using var image = GetViewport().GetTexture().GetImage();
            if (image.SavePng(path) != Error.Ok)
                throw new InvalidOperationException("catalog filter screenshot could not be saved");
        }
        private void DeleteSelected()
        {
            dialog.GetNode<Button>(Form + "Actions/Delete").EmitSignal(BaseButton.SignalName.Pressed);
            var confirmation = dialog.GetNode<ConfirmationDialog>("ConfirmDelete");
            if (!confirmation.Visible)
                throw new InvalidOperationException("source deletion did not request confirmation");
            confirmation.EmitSignal(ConfirmationDialog.SignalName.Confirmed);
            confirmation.Hide();
        }
        private void SelectPrivate()
        {
            var list = dialog.GetNode<ItemList>(ListPath);
            for (var index = 0; index < list.ItemCount; index++)
                if (list.GetItemTooltip(index) == privateUrl)
                {
                    list.Select(index);
                    list.EmitSignal(ItemList.SignalName.ItemSelected, (long)index);
                    return;
                }
            throw new InvalidOperationException("private source is missing from the editable list");
        }
        private CatalogServer FindPrivate()
        {
            foreach (var entry in CatalogServerStore.Default.Load(ModConfig.Instance))
                if (entry.Url == privateUrl)
                    return entry;
            throw new InvalidOperationException("source was not persisted");
        }
        private void RequireCount(int expected)
        {
            if (dialog.GetNode<ItemList>(ListPath).ItemCount != expected
                || (hub.FindChild("CatalogsButton", true, false) as Button)?.Text != string.Format(TranslationServer.Translate("mod_manager/sources_button"), expected))
                throw new InvalidOperationException("catalog server count is incorrect");
        }
        private static void RequireEntries(bool hasPrivate)
        {
            var privateFound = false;
            var publicFound = false;
            foreach (var entry in CatalogService.CachedEntries ?? new List<CatalogExportEntry>())
            {
                privateFound |= entry.Id == "private_test_pack";
                publicFound |= entry.Id == "public_test_pack";
            }
            if (privateFound != hasPrivate || !publicFound)
                throw new InvalidOperationException("private or public source access differs");
        }
        private void OnInstalled(string id, bool success)
        {
            if (id != "private_test_pack")
                return;
            installComplete = true;
            installSuccess = success;
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
        private async Task SaveScreenshotAsync()
        {
            var path = System.Environment.GetEnvironmentVariable("CTHANGOVER_CATALOG_SHOT");
            if (string.IsNullOrEmpty(path) || DisplayServer.GetName() == "headless")
                return;
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            using var image = GetViewport().GetTexture().GetImage();
            if (image.SavePng(path) != Error.Ok)
                throw new InvalidOperationException("catalog manager screenshot could not be saved");
        }
        private void Finish(string failure)
        {
            if (finished)
                return;
            finished = true;
            ModDownloader.OnCompleted -= OnInstalled;
            if (GodotObject.IsInstanceValid(hub))
                hub.QueueFree();
            if (originalUrls != null)
            {
                ModConfig.Instance.CatalogUrls = originalUrls;
                ModConfig.Instance.CatalogTokenFiles = originalTokenFiles;
                CatalogService.ClearCache();
                if (originalCache != null)
                    File.WriteAllBytes(cachePath, originalCache);
                if (originalServers == null)
                    File.Delete(serversPath);
                else
                    File.WriteAllBytes(serversPath, originalServers);
            }
            foreach (var path in createdKeys)
                File.Delete(path);
            if (installedPath != null && Directory.Exists(installedPath))
                Directory.Delete(installedPath, true);
            if (failure != null)
                GameLogger.Log("TEST", failure, LogLevel.Error);
            GameLogger.Log("TEST", $"[TEST_RESULT] name=catalog_servers passed={(failure == null ? 1 : 0)} failed={(failure == null ? 0 : 1)}");
            ApplicationExit.Quit(GetTree(), failure == null ? 0 : 1);
        }
    }
}
