using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Cthangover.Core.Bootstrap;
using Cthangover.Core.Mods;
using Cthangover.Core.Mods.Providers;
using Cthangover.Core.Scenes;
using Cthangover.Core.Utils;
using Cthangover.Mods.Interface.View;
using Godot;

namespace Mods.ModManager.PublishingTest
{
    /// <summary>Uses the real publishing controls against a real server, checks downloaded ZIP bytes, then publishes an update with a renewed key.</summary>
    public partial class PublishingTestDriver : Node
    {
        private const string Form = "Center/Panel/Margin/Form/";
        private const string Id = "test_publish_pack";
        private ModPublishDialog dialog;
        private ModManagerScreen manager;
        private double readyAt;
        private ModInfo mod;
        private string folder;
        private string url;
        private string tokenFile;
        private string storageInstalledPath;
        private string serversPath;
        private string cachePath;
        private byte[] savedServers;
        private byte[] savedCache;
        private List<string> savedUrls;
        private Dictionary<string, string> savedTokens;
        private Task operation;
        private bool published;
        private bool finished;
        private double elapsed;
        private int step = -2;

        public override void _Process(double delta)
        {
            if (finished)
                return;
            elapsed += delta;
            try
            {
                if (elapsed > 110)
                    throw new InvalidOperationException("publishing timeout at step " + step);
                if (!Boot.IsReady || CatalogService.IsFetching || operation != null && !operation.IsCompleted)
                    return;
                operation?.GetAwaiter().GetResult();
                operation = null;
                RunStep();
            }
            catch (Exception error) { Finish(error.ToString()); }
        }

        private void RunStep()
        {
            switch (step)
            {
                case -2:
                    if (GetTree().CurrentScene?.FindChild("LauncherPlay", true, false) is Button play)
                    {
                        play.EmitSignal(BaseButton.SignalName.Pressed);
                        return;
                    }
                    if (GetTree().CurrentScene?.FindChild("play", true, false) is not Button)
                        return;
                    Prepare();
                    var managerInstance = TscnScenes.LoadAndBuild("scenes/mod_manager_screen.tscn");
                    try
                    {
                        manager = managerInstance as ModManagerScreen ?? throw new InvalidOperationException("manager scene not built");
                        GetTree().Root.AddChild(manager);
                        manager.EnsureConstructed();
                        manager.SetSearch("mod_manager");
                    }
                    finally
                    {
                        if (GodotObject.IsInstanceValid(managerInstance) && managerInstance.GetParent() == null)
                            managerInstance.Free();
                    }
                    step++;
                    break;
                case -1:
                    var card = FindCard(manager) ?? throw new InvalidOperationException("installed mod card missing");
                    using (var click = new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true })
                        card.EmitSignal(Control.SignalName.GuiInput, click);
                    var detail = manager.GetNode<ModDetailView>("Margin/Root/Body/Content/Detail");
                    if (!manager.IsDetailOpen)
                        throw new InvalidOperationException("mod detail did not open");
                    detail.GetNode<Button>("Scroll/Content/ActionsRow/Publish").EmitSignal(BaseButton.SignalName.Pressed);
                    var entryDialog = manager.FindChild("ModPublishDialog", true, false) as ModPublishDialog
                        ?? throw new InvalidOperationException("publish action did not open its prefab");
                    entryDialog.GetNode<Button>(Form + "Actions/Close").EmitSignal(BaseButton.SignalName.Pressed);
                    OpenDialog();
                    step = 1;
                    break;
                case 1:
                    if (dialog.GetNode<Button>(Form + "Actions/Publish").Disabled)
                        return;
                    if (!dialog.IsVisibleInTree() || dialog.Size.X < 700 || dialog.Size.Y < 400)
                        throw new InvalidOperationException("publishing dialog has no visible viewport layout");
                    if (!dialog.GetNode<Label>(Form + "Identity").Text.Contains("integration-author"))
                        throw new InvalidOperationException("server publisher identity not shown");
                    if (readyAt == 0)
                    {
                        readyAt = elapsed;
                        return;
                    }
                    if (elapsed - readyAt < 0.6)
                        return;
                    var shot = System.Environment.GetEnvironmentVariable("CTHANGOVER_PUBLISH_SHOT");
                    if (!string.IsNullOrEmpty(shot))
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(shot));
                        if (!ViewportCapture.SavePng(GetViewport(), shot))
                            throw new InvalidOperationException("publishing UI capture failed");
                    }
                    dialog.GetNode<Button>(Form + "Actions/Publish").EmitSignal(BaseButton.SignalName.Pressed);
                    step++;
                    break;
                case 2:
                    if (!published)
                        return;
                    operation = VerifyDownload("1.0.0", "first release", 1);
                    step++;
                    break;
                case 3:
                    dialog.QueueFree();
                    mod.Manifest.Version = "2.0.0";
                    WriteManifest();
                    // Simulate an author editing files while the game is running:
                    // publishing must read the packaged manifest, not stale registry metadata.
                    mod.Manifest.Version = "1.0.0";
                    File.WriteAllText(Path.Combine(folder, "content.txt"), "second release");
                    // Same stable identity, a different token file and display name.
                    ModConfig.Instance.CatalogTokenFiles[url] = System.Environment.GetEnvironmentVariable("CTHANGOVER_PUBLISH_RENEWED_TOKEN_FILE");
                    OpenDialog();
                    step++;
                    break;
                case 4:
                    if (dialog.GetNode<Button>(Form + "Actions/Publish").Disabled)
                        return;
                    dialog.GetNode<Button>(Form + "Actions/Publish").EmitSignal(BaseButton.SignalName.Pressed);
                    step++;
                    break;
                case 5:
                    if (!published)
                        return;
                    operation = VerifyDownload("2.0.0", "second release", 2);
                    step++;
                    break;
                case 6:
                    dialog.QueueFree();
                    ModConfig.Instance.CatalogTokenFiles[url] = System.Environment.GetEnvironmentVariable("CTHANGOVER_PUBLISH_ADMIN_TOKEN_FILE");
                    OpenDialog();
                    step++;
                    break;
                case 7:
                    if (!dialog.GetNode<Control>(Form + "CustomSection").Visible)
                        return;
                    var create = dialog.GetNode<Button>(Form + "CustomSection/Create");
                    if (!create.Visible)
                        throw new InvalidOperationException("administrator has no section creation control");
                    dialog.GetNode<LineEdit>(Form + "CustomSection/Name").Text = "admin_new";
                    if (create.Disabled)
                        throw new InvalidOperationException("administrator cannot create a section");
                    create.EmitSignal(BaseButton.SignalName.Pressed);
                    step++;
                    break;
                case 8:
                    if (dialog.GetNode<Button>(Form + "Actions/Close").Disabled)
                        return;
                    if (dialog.GetNode<Label>(Form + "Status").Text != TranslationServer.Translate("mod_manager/publish_section_created"))
                        throw new InvalidOperationException("empty section was not created through the form");
                    mod.Manifest.Version = "3.0.0";
                    WriteManifest();
                    File.WriteAllText(Path.Combine(folder, "content.txt"), "admin release");
                    readyAt = elapsed;
                    step++;
                    break;
                case 9:
                    if (elapsed - readyAt < 0.6)
                        return;
                    var adminShot = System.Environment.GetEnvironmentVariable("CTHANGOVER_PUBLISH_ADMIN_SHOT");
                    if (!string.IsNullOrEmpty(adminShot))
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(adminShot));
                        if (!ViewportCapture.SavePng(GetViewport(), adminShot))
                            throw new InvalidOperationException("admin publishing capture failed");
                    }
                    dialog.GetNode<Button>(Form + "Actions/Publish").EmitSignal(BaseButton.SignalName.Pressed);
                    step++;
                    break;
                case 10:
                    if (!published)
                        return;
                    operation = VerifyDownload("3.0.0", "admin release", 3);
                    step++;
                    break;
                case 11:
                    var ownedEntry = CatalogService.CachedEntries.FirstOrDefault(IsProbe);
                    if (ownedEntry?.Author != "Publisher Renamed")
                        throw new InvalidOperationException("administrator changed original authorship");
                    dialog.QueueFree();
                    ModConfig.Instance.CatalogTokenFiles[url] = System.Environment.GetEnvironmentVariable("CTHANGOVER_PUBLISH_MASKED_TOKEN_FILE");
                    mod.Id = "test_masked_publish";
                    mod.Manifest.Id = mod.Id;
                    mod.Manifest.Version = "1.0.0";
                    WriteManifest();
                    File.WriteAllText(Path.Combine(folder, "content.txt"), "masked release");
                    OpenDialog();
                    step++;
                    break;
                case 12:
                    if (!dialog.GetNode<Control>(Form + "CustomSection").Visible)
                        return;
                    if (dialog.GetNode<Button>(Form + "CustomSection/Create").Visible)
                        throw new InvalidOperationException("masked author gained admin section control");
                    var input = dialog.GetNode<LineEdit>(Form + "CustomSection/Name");
                    input.Text = "secret";
                    input.EmitSignal(LineEdit.SignalName.TextChanged, input.Text);
                    if (!dialog.GetNode<Button>(Form + "Actions/Publish").Disabled)
                        throw new InvalidOperationException("mask permits an unrelated section");
                    input.Text = "admin_mask_created";
                    input.EmitSignal(LineEdit.SignalName.TextChanged, input.Text);
                    if (dialog.GetNode<Button>(Form + "Actions/Publish").Disabled)
                        throw new InvalidOperationException("mask refuses permitted new section");
                    dialog.GetNode<Button>(Form + "Actions/Publish").EmitSignal(BaseButton.SignalName.Pressed);
                    step++;
                    break;
                case 13:
                    if (!published)
                        return;
                    operation = VerifyDownload("1.0.0", "masked release", 1);
                    step++;
                    break;
                case 14:
                    operation = InstallStorageMod();
                    step++;
                    break;
                case 15:
                    Finish(null);
                    break;
            }
        }

        private void Prepare()
        {
            url = System.Environment.GetEnvironmentVariable("CTHANGOVER_PRIVATE_CATALOG_URL");
            tokenFile = System.Environment.GetEnvironmentVariable("CTHANGOVER_PUBLISH_TOKEN_FILE");
            if (string.IsNullOrEmpty(url) || string.IsNullOrEmpty(tokenFile))
                throw new InvalidOperationException("use tests/integration_windows.py --suite publishing");
            serversPath = GamePaths.Globalize("user://catalog_servers.json");
            cachePath = GamePaths.Globalize("user://catalog_cache.json");
            savedServers = File.Exists(serversPath) ? File.ReadAllBytes(serversPath) : null;
            savedCache = File.Exists(cachePath) ? File.ReadAllBytes(cachePath) : null;
            savedUrls = ModConfig.Instance.CatalogUrls;
            savedTokens = ModConfig.Instance.CatalogTokenFiles;
            File.Delete(serversPath);
            ModConfig.Instance.CatalogUrls = new List<string> { url };
            ModConfig.Instance.CatalogTokenFiles = new Dictionary<string, string> { [url] = tokenFile };
            storageInstalledPath = GamePaths.Globalize("user://mods/test_storage_pack");
            if (Directory.Exists(storageInstalledPath))
            {
                storageInstalledPath = null;
                throw new InvalidOperationException("test_storage_pack already installed; refusing to replace it");
            }
            folder = Path.Combine(Path.GetTempPath(), "cthangover-publishing-probe-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            Directory.CreateDirectory(Path.Combine(folder, ".git"));
            File.WriteAllText(Path.Combine(folder, ".git", "should-not-publish"), "private fixture");
            File.WriteAllText(Path.Combine(folder, "content.txt"), "first release");
            mod = new ModInfo
            {
                Id = Id, Name = "Publishing probe", Author = "Forged manifest credit",
                Manifest = new ModManifest { Id = Id, Name = "Publishing probe", Version = "1.0.0", Author = "Forged manifest credit" },
                FileProvider = new FolderModFileProvider(folder, Id)
            };
            WriteManifest();
        }

        private async Task InstallStorageMod()
        {
            if (!await ModDownloader.InstallAsync("test_storage_pack"))
                throw new InvalidOperationException("filesystem-discovered mod failed installation");
            if (File.ReadAllText(Path.Combine(storageInstalledPath, "content.txt")) != "Storage discovered automatically.")
                throw new InvalidOperationException("filesystem-discovered package bytes mismatch");
        }

        private void WriteManifest()
            => File.WriteAllText(Path.Combine(folder, "manifest.json"), JsonSerializer.Serialize(mod.Manifest, JsonOptionsProvider.WriteIndentedIgnoreNullOptions));

        private void OpenDialog()
        {
            published = false;
            var instance = TscnScenes.LoadAndBuild("scenes/components/mod_publish_dialog.tscn");
            try
            {
                dialog = instance as ModPublishDialog ?? throw new InvalidOperationException("publishing scene not built");
                manager.AddChild(dialog);
                dialog.Published += OnPublished;
                dialog.Open(mod);
            }
            finally
            {
                if (GodotObject.IsInstanceValid(instance) && instance.GetParent() == null)
                    instance.Free();
            }
        }

        private void OnPublished()
        {
            published = true;
            operation = CatalogService.RefreshAsync(forceRefresh: true);
        }

        private async Task VerifyDownload(string version, string expectedContent, int releases)
        {
            var entry = CatalogService.CachedEntries?.FirstOrDefault(IsProbe)
                ?? throw new InvalidOperationException("published mod missing from catalog");
            if (entry.Version != version || entry.Downloads.Count != releases || entry.Author == mod.Author)
                throw new InvalidOperationException("published version/history/verified author mismatch");
            using var client = new System.Net.Http.HttpClient();
            var bytes = await client.GetByteArrayAsync(entry.Downloads[0].Urls[0]);
            var archive = Path.Combine(folder, "download.zip");
            File.WriteAllBytes(archive, bytes);
            try
            {
                using var package = new ZipModFileProvider(archive, Id);
                if (package.ReadFileText("content.txt") != expectedContent || package.FileExists(".git/should-not-publish"))
                    throw new InvalidOperationException("published bytes mismatch or hidden files leaked");
                var installed = ModHasher.ComputeInstallSha256(package, ModConfig.Instance.CatalogIgnorePatterns);
                if (installed != entry.Downloads[0].InstallSha256 || ModHasher.ComputeArchiveSha256(archive) != entry.Downloads[0].ArchiveSha256)
                    throw new InvalidOperationException("published checksums do not match archive");
            }
            finally { File.Delete(archive); }
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

        private bool IsProbe(CatalogExportEntry entry) => entry.Id == mod.Id;

        private void Finish(string failure)
        {
            if (finished)
                return;
            finished = true;
            if (GodotObject.IsInstanceValid(dialog))
                dialog.QueueFree();
            if (GodotObject.IsInstanceValid(manager))
                manager.QueueFree();
            mod?.FileProvider?.Dispose();
            if (storageInstalledPath != null && Directory.Exists(storageInstalledPath))
                Directory.Delete(storageInstalledPath, true);
            if (folder != null)
                Directory.Delete(folder, true);
            if (savedUrls != null)
            {
                ModConfig.Instance.CatalogUrls = savedUrls;
                ModConfig.Instance.CatalogTokenFiles = savedTokens;
                CatalogService.ClearCache();
                if (savedCache != null)
                    File.WriteAllBytes(cachePath, savedCache);
                else
                    File.Delete(cachePath);
                if (savedServers != null)
                    File.WriteAllBytes(serversPath, savedServers);
                else
                    File.Delete(serversPath);
            }
            if (failure != null)
                GameLogger.Log("TEST", failure, LogLevel.Error);
            GameLogger.Log("TEST", $"[TEST_RESULT] name=publishing passed={(failure == null ? 1 : 0)} failed={(failure == null ? 0 : 1)}");
            ApplicationExit.Quit(GetTree(), failure == null ? 0 : 1);
        }
    }
}
