using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Cthangover.Core.Bootstrap;
using Cthangover.Core.Mods;
using Cthangover.Core.Utils;
using Godot;

namespace Mods.ModManager.PublishGui
{
    /// <summary>Enters both endpoint and bearer through the catalog form, publishes as an author, then checks a reader's diagnostic.</summary>
    public partial class PublishGuiDriver : Node
    {
        private const string Form = "Center/Panel/Margin/Form/";
        private const string Editor = Form + "Body/Editor/";
        private CatalogServersDialog sources;
        private ModPublishDialog dialog;
        private ModHubScreen hub;
        private ModInfo mod;
        private ModManagerScreen manager;
        private string url;
        private string authorToken;
        private string readerToken;
        private string serversPath;
        private string cachePath;
        private byte[] savedServers;
        private byte[] savedCache;
        private List<string> savedUrls;
        private Dictionary<string, string> savedTokens;
        private readonly List<string> createdKeys = new();
        private Task operation;
        private bool published;
        private bool finished;
        private double elapsed;
        private double openedAt;
        private int step;

        public override void _Process(double delta)
        {
            if (finished) return;
            elapsed += delta;
            try
            {
                if (elapsed > 110) throw new InvalidOperationException("publish_gui timeout at step " + step);
                if (!Boot.IsReady || CatalogService.IsFetching || operation != null && !operation.IsCompleted) return;
                operation?.GetAwaiter().GetResult();
                operation = null;
                if (GodotObject.IsInstanceValid(sources) && sources.GetNode<Button>(Editor + "Actions/Save").Disabled) return;
                RunStep();
            }
            catch (Exception error) { Finish(error.ToString()); }
        }

        private void RunStep()
        {
            switch (step)
            {
                case 0:
                    if (GetTree().CurrentScene?.FindChild("LauncherPlay", true, false) is Button play)
                    { play.EmitSignal(BaseButton.SignalName.Pressed); return; }
                    if (GetTree().CurrentScene?.FindChild("play", true, false) is not Button) return;
                    Prepare();
                    (GetTree().CurrentScene.FindChild("mods_hub", true, false) as Button)?.EmitSignal(BaseButton.SignalName.Pressed);
                    hub = GetTree().CurrentScene.FindChild("ModHubScreen", true, false) as ModHubScreen
                        ?? throw new InvalidOperationException("main menu did not open mod hub");
                    OpenSources();
                    sources.GetNode<Button>(Form + "Body/Servers/New").EmitSignal(BaseButton.SignalName.Pressed);
                    sources.GetNode<LineEdit>(Editor + "Name").Text = "Author via GUI";
                    sources.GetNode<LineEdit>(Editor + "Url").Text = url.Replace("/v1/catalog", "");
                    SaveKey(authorToken);
                    step++;
                    break;
                case 1:
                    VerifyStoredKey(authorToken);
                    CloseSources();
                    OpenPublish();
                    step++;
                    break;
                case 2:
                    if (elapsed - openedAt < 2) return;
                    RequireChoices();
                    if (dialog.GetNode<Button>(Form + "Actions/Publish").Disabled)
                        throw new InvalidOperationException("author publication disabled: " + dialog.GetNode<Label>(Form + "Status").Text);
                    dialog.GetNode<Button>(Form + "Actions/Publish").EmitSignal(BaseButton.SignalName.Pressed);
                    step++;
                    break;
                case 3:
                    if (!published) return;
                    operation = CatalogService.RefreshAsync(forceRefresh: true);
                    step++;
                    break;
                case 4:
                    var entry = CatalogService.CachedEntries.FirstOrDefault(value => value.Id == mod.Id)
                        ?? throw new InvalidOperationException("GUI-published mod missing on real server");
                    if (entry.Author != (System.Environment.GetEnvironmentVariable("CTHANGOVER_PUBLISH_EXPECTED_AUTHOR") ?? "Publisher") || entry.Version != "0.1.0")
                        throw new InvalidOperationException("server identity or version mismatch");
                    operation = VerifyArchive(entry.Downloads[0].Urls[0]);
                    GameLogger.Log("TEST", "publish_gui: author configured through GUI published successfully");
                    dialog.QueueFree();
                    OpenSources();
                    var list = sources.GetNode<ItemList>(Form + "Body/Servers/List");
                    list.Select(0);
                    list.EmitSignal(ItemList.SignalName.ItemSelected, 0L);
                    SaveKey(readerToken);
                    step++;
                    break;
                case 5:
                    VerifyStoredKey(readerToken);
                    CloseSources();
                    OpenPublish();
                    step++;
                    break;
                case 6:
                    if (elapsed - openedAt < 2) return;
                    if (dialog.GetNode<OptionButton>(Form + "Server").ItemCount != 1)
                        throw new InvalidOperationException("reader-configured server silently disappeared from publish choices");
                    if (!dialog.GetNode<Button>(Form + "Actions/Publish").Disabled
                        || dialog.GetNode<OptionButton>(Form + "Section").ItemCount != 0)
                        throw new InvalidOperationException("reader gained publishing rights");
                    if (!dialog.GetNode<Label>(Form + "Status").Text.Contains(TranslationServer.Translate("mod_manager/publish_denied").ToString()))
                        throw new InvalidOperationException("reader has no explicit denial diagnostic");
                    Finish(null);
                    break;
            }
        }

        private void Prepare()
        {
            url = System.Environment.GetEnvironmentVariable("CTHANGOVER_PRIVATE_CATALOG_URL");
            authorToken = File.ReadAllText(System.Environment.GetEnvironmentVariable("CTHANGOVER_PUBLISH_TOKEN_FILE")).Trim();
            readerToken = File.ReadAllText(System.Environment.GetEnvironmentVariable("CTHANGOVER_PRIVATE_TOKEN_FILE")).Trim();
            serversPath = GamePaths.Globalize("user://catalog_servers.json");
            cachePath = GamePaths.Globalize("user://catalog_cache.json");
            savedServers = File.Exists(serversPath) ? File.ReadAllBytes(serversPath) : null;
            savedCache = File.Exists(cachePath) ? File.ReadAllBytes(cachePath) : null;
            savedUrls = ModConfig.Instance.CatalogUrls;
            savedTokens = ModConfig.Instance.CatalogTokenFiles;
            if (savedServers != null || savedUrls.Count != 0)
                throw new InvalidOperationException("publish_gui requires an isolated empty profile and catalog configuration");
            mod = (ModInfo)ModRegistry.Instance.AllDiscoveredMods["mod_manager"];

        }

        private void OpenSources()
        {
            (hub.FindChild("CatalogsButton", true, false) as Button)?.EmitSignal(BaseButton.SignalName.Pressed);
            sources = hub.FindChild("CatalogServersDialog", true, false) as CatalogServersDialog
                ?? throw new InvalidOperationException("catalog button did not open its form");
        }

        private void SaveKey(string token)
        {
            var input = sources.GetNode<LineEdit>(Editor + "Key");
            if (!input.Secret) throw new InvalidOperationException("unmasked key input");
            input.Text = token;
            sources.GetNode<Button>(Editor + "Actions/Save").EmitSignal(BaseButton.SignalName.Pressed);
        }

        private void VerifyStoredKey(string expected)
        {
            var stored = CatalogServerStore.Default.Load(ModConfig.Instance).Single();
            createdKeys.Add(stored.TokenFile);
            if (stored.Url != url || !Path.IsPathFullyQualified(stored.TokenFile)
                || File.ReadAllText(stored.TokenFile).Trim() != expected)
                throw new InvalidOperationException("GUI did not persist normalized endpoint and private key");
        }

        private void CloseSources() => sources.GetNode<Button>(Form + "Close").EmitSignal(BaseButton.SignalName.Pressed);

        private void OpenPublish()
        {
            manager = hub.FindChild("ModManagerScreen", true, false) as ModManagerScreen
                ?? throw new InvalidOperationException("mod manager tab missing");
            manager.SetSearch("mod_manager");
            var card = FindCard(manager) ?? throw new InvalidOperationException("installed mod manager card missing");
            using (var click = new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true })
                card.EmitSignal(Control.SignalName.GuiInput, click);
            var detail = manager.GetNode<ModDetailView>("Margin/Root/Body/Content/Detail");
            if (!manager.IsDetailOpen) throw new InvalidOperationException("installed card did not open detail");
            detail.GetNode<Button>("Scroll/Content/ActionsRow/Publish").EmitSignal(BaseButton.SignalName.Pressed);
            dialog = manager.FindChild("ModPublishDialog", true, false) as ModPublishDialog
                ?? throw new InvalidOperationException("publish button did not open publication form");
            dialog.Published += OnPublished;
            openedAt = elapsed;
        }

        private void RequireChoices()
        {
            var server = dialog.GetNode<OptionButton>(Form + "Server");
            var section = dialog.GetNode<OptionButton>(Form + "Section");
            if (server.ItemCount != 1 || server.Selected != 0 || !server.Text.Contains("127.0.0.1:18080"))
                throw new InvalidOperationException("GUI-configured author server missing: " + dialog.GetNode<Label>(Form + "Status").Text);
            if (section.ItemCount != 1 || section.GetItemText(0) != "local_test")
                throw new InvalidOperationException("author literal section missing");
        }

        private static async Task VerifyArchive(string download)
        {
            using var client = new System.Net.Http.HttpClient();
            using var stream = new MemoryStream(await client.GetByteArrayAsync(download));
            using var zip = new System.IO.Compression.ZipArchive(stream);
            using var reader = new StreamReader(zip.GetEntry("manifest.json").Open());
            using var manifest = JsonDocument.Parse(await reader.ReadToEndAsync());
            if (manifest.RootElement.GetProperty("id").GetString() != "mod_manager"
                || manifest.RootElement.GetProperty("version").GetString() != "0.1.0")
                throw new InvalidOperationException("server archive manifest differs from the installed mod");
        }

        private static ModCard FindCard(Node node)
        {
            if (node.IsQueuedForDeletion()) return null;
            if (node is ModCard card) return card;
            foreach (var child in node.GetChildren())
            {
                var found = FindCard(child);
                if (found != null) return found;
            }
            return null;
        }

        private void OnPublished() => published = true;

        private void Finish(string failure)
        {
            if (finished) return;
            finished = true;
            if (GodotObject.IsInstanceValid(hub)) hub.QueueFree();
            if (savedUrls != null)
            {
                ModConfig.Instance.CatalogUrls = savedUrls;
                ModConfig.Instance.CatalogTokenFiles = savedTokens;
                CatalogService.ClearCache();
                if (savedServers == null) File.Delete(serversPath); else File.WriteAllBytes(serversPath, savedServers);
                if (savedCache == null) File.Delete(cachePath); else File.WriteAllBytes(cachePath, savedCache);
            }
            foreach (var path in createdKeys) File.Delete(path);
            if (failure != null) GameLogger.Log("TEST", failure, LogLevel.Error);
            GameLogger.Log("TEST", $"[TEST_RESULT] name=publish_gui passed={(failure == null ? 1 : 0)} failed={(failure == null ? 0 : 1)}");
            ApplicationExit.Quit(GetTree(), failure == null ? 0 : 1);
        }
    }
}
