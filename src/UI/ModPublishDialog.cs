using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Cthangover.Core.Mods;
using Cthangover.Mods.Interface.Base.Lists;
using Godot;
using Cthangover.Mods.Interface.View;

namespace Mods.ModManager
{
    /// <summary>Scene-backed publishing form. Identity and sections come from the selected server, never from editable manifest credits.</summary>
    public partial class ModPublishDialog : ModWidget
    {
        private const string Form = "Center/Panel/Margin/Form/";
        private Control consentBlock;
        private RichTextLabel consentText;
        private Label consentLanguage;
        private Button consentWebsite;
        private CheckBox consentCheck;
        private readonly List<CatalogServer> servers = new();
        private readonly CancellationTokenSource lifetime = new();
        private ModCategoryEditor categories;
        private ModInfo mod;
        private ModCollection collection;
        private string EntityKind => collection == null ? "mod" : "collection";
        private string EntityId => collection?.Id ?? mod.Id;
        private OptionButton serverChoice;
        private OptionButton sectionChoice;
        private LineEdit sectionInput;
        private Button createSection;
        private Control customSection;
        private PublishingPermissions permissions;
        private Label identity;
        private Label status;
        private Button publish;
        private Button close;
        private bool busy;
        private int selectionVersion;

        /// <summary>Raised after the server has committed the archive and catalog entry.</summary>
        public event Action Published;

        protected override void Construct()
        {
            Resized += FitForm;
            FitForm();
            consentBlock = GetNode<Control>(Form + "Consent");
            consentText = GetNode<RichTextLabel>(Form + "Consent/Text");
            consentLanguage = GetNode<Label>(Form + "Consent/Language");
            consentWebsite = GetNode<Button>(Form + "Consent/Website");
            consentCheck = GetNode<CheckBox>(Form + "Consent/Accept");
            consentWebsite.Pressed += OnConsentWebsite;
            consentCheck.Toggled += OnConsentToggled;
            serverChoice = GetNode<OptionButton>(Form + "Server");
            sectionChoice = GetNode<OptionButton>(Form + "Section");
            sectionInput = GetNode<LineEdit>(Form + "CustomSection/Name");
            createSection = GetNode<Button>(Form + "CustomSection/Create");
            customSection = GetNode<Control>(Form + "CustomSection");
            sectionInput.TextChanged += OnSectionTextChanged;
            sectionChoice.ItemSelected += OnSectionSelected;
            createSection.Pressed += OnCreateSection;
            identity = GetNode<Label>(Form + "Identity");
            status = GetNode<Label>(Form + "Status");
            publish = GetNode<Button>(Form + "Actions/Publish");
            close = GetNode<Button>(Form + "Actions/Close");
            serverChoice.ItemSelected += OnServerSelected;
            publish.Pressed += OnPublish;
            close.Pressed += OnClose;
            publish.Disabled = true;
        }

        private void FitForm()
        {
            GetNode<Control>("Center/Panel/Margin").CustomMinimumSize = new Vector2(760, Math.Max(300, Math.Min(860, GetViewportRect().Size.Y - 48)));
        }

        /// <summary>Opens for an installed mod; every configured source remains visible with a reason if it cannot publish.</summary>
        public void Open(ModInfo selectedMod)
        {
            mod = selectedMod;
            BeginOpen($"{mod.DisplayTitle} · {mod.Id} · {mod.Version}");
        }

        /// <summary>Uses the same identity, consent and transfer form for a locally editable collection.</summary>
        public void OpenCollection(ModCollection selected)
        {
            if (selected.Origin == ModCollectionOrigin.Catalog) throw new InvalidOperationException("mod_manager/publish_denied");
            collection = selected;
            BeginOpen($"{selected.DisplayTitle} · {selected.Id} · {selected.Version}");
            GetNode<Label>(Form + "Title").Text = Tr("mod_manager/publish_collection");
            publish.Text = Tr("mod_manager/publish_collection");
            GetNode<Label>(Form + "Requirements").Text = Tr("mod_manager/publish_requirements") + "\n" + CollectionPublishRequirements.Describe(selected);
            GetNode<Control>(Form + "Requirements").Visible = true;
        }

        private void BeginOpen(string title)
        {
            EnsureConstructed();
            GetNode<Label>(Form + "Mod").Text = title;
            var host = GetNode<Control>(Form + "Categories");
            host.Visible = collection == null;
            if (collection == null)
            {
                foreach (var child in host.GetChildren()) { host.RemoveChild(child); child.QueueFree(); }
                categories = ModCategoryEditor.Create(mod.Manifest?.Categories);
                host.AddChild(categories);
                categories.SetEditable(ModCategoryManifestStore.CanEdit(mod, ModCategoryEditor.ProjectRoot));
                categories.Changed += UpdateActions;
            }
            servers.Clear();
            serverChoice.Clear();
            sectionChoice.Clear();
            publish.Disabled = true;
            try
            {
                servers.AddRange(CatalogServerStore.Default.Load(ModConfig.Instance));
                foreach (var server in servers)
                    serverChoice.AddItem(PublishDestination.Label(server));
                serverChoice.Disabled = servers.Count == 0;
                var index = PublishDestination.Select(servers, PublishDestination.Previous(EntityKind, EntityId));
                serverChoice.Select(index);
                if (index >= 0) OnServerSelected(index);
                else
                {
                    status.Text = Tr(servers.Count == 0 ? "mod_manager/publish_no_servers" : "mod_manager/publish_choose_server");
                    serverChoice.Text = status.Text;
                    sectionChoice.Disabled = true;
                }
            }
            catch (Exception error) { ShowError(error); }
        }

        protected override void Destruct()
        {
            lifetime.Cancel();
            Published = null;
        }

        private async void OnServerSelected(long index)
        {
            if (busy || index < 0 || index >= servers.Count)
                return;
            var current = ++selectionVersion;
            permissions = null;
            ConfigureConsent();
            sectionChoice.Clear();
            sectionChoice.Disabled = true;
            sectionInput.Text = "";
            customSection.Visible = false;
            identity.Text = "";
            publish.Disabled = true;
            status.Text = Tr("mod_manager/publish_checking");
            try
            {
                var server = servers[(int)index];
                var availability = await PublishAvailability.CheckAsync(server, LoadPermissions, lifetime.Token);
                if (!GodotObject.IsInstanceValid(this) || current != selectionVersion)
                    return;
                if (availability.ErrorKey != null)
                {
                    status.Text = PublishDestination.Label(server) + "\n" + Tr(availability.ErrorKey);
                    if (!string.IsNullOrEmpty(availability.ErrorDetail)) status.Text += "\n" + availability.ErrorDetail;
                    sectionChoice.Disabled = true;
                    return;
                }
                permissions = availability.Permissions;
                ConfigureConsent();
                customSection.Visible = permissions.AllowsCustomSection;
                createSection.Visible = permissions.IsAdmin;
                foreach (var section in permissions.AvailableSections)
                    sectionChoice.AddItem(section);
                identity.Text = string.Format(Tr("mod_manager/publish_identity"), permissions.Author, permissions.AuthorId);
                if (sectionChoice.ItemCount > 0)
                {
                    sectionChoice.Select(0);
                    sectionInput.Text = sectionChoice.GetItemText(0);
                }
                status.Text = Tr(permissions.IsAdmin ? "mod_manager/publish_admin_ready" : "mod_manager/publish_ready");
                UpdateActions();
            }
            catch (OperationCanceledException) { }
            catch (Exception error)
            {
                if (GodotObject.IsInstanceValid(this) && current == selectionVersion)
                    ShowError(error);
            }
        }

        private async void OnPublish()
        {
            if (busy || publish.Disabled || permissions?.AllowsSection(SelectedSection()) != true)
                return;
            if (categories?.HasPendingInput == true) return;
            SetBusy(true);
            status.Text = Tr("mod_manager/publish_uploading");
            try
            {
                var server = servers[serverChoice.Selected];
                if (!permissions.IsAdmin && permissions.AuthorConsent is { Accepted: false } consent)
                {
                    await CatalogPublisher.AcceptAuthorConsentAsync(server.Url, server.TokenFile, consent.Version, lifetime.Token);
                    if (!GodotObject.IsInstanceValid(this)) return;
                    consent.Accepted = true;
                    ConfigureConsent();
                }
                // Progress<T> posts to Godot's synchronization context (main thread).
                var progress = new Progress<PublishProgress>(state =>
                {
                    if (!GodotObject.IsInstanceValid(this) || state.Total <= 0)
                        return;
                    var percent = (int)(state.Sent * 100 / state.Total);
                    status.Text = state.Retry > 0
                        ? string.Format(Tr("mod_manager/publish_reconnecting"), percent, state.Retry)
                        : string.Format(Tr("mod_manager/publish_progress"), percent);
                });
                if (collection == null)
                {
                    if (ModCategoryManifestStore.CanEdit(mod, ModCategoryEditor.ProjectRoot))
                        ModCategoryManifestStore.Save(mod, ModCategoryEditor.ProjectRoot, categories.Categories);
                    await CatalogPublisher.PublishAsync(mod, server.Url, server.TokenFile, SelectedSection(), lifetime.Token, progress);
                }
                else
                    await CatalogPublisher.PublishCollectionAsync(collection, server.Url, server.TokenFile, SelectedSection(), lifetime.Token, progress);
                if (!GodotObject.IsInstanceValid(this))
                    return;
                PublishDestination.Remember(EntityKind, EntityId, server.Url);
                status.Text = Tr("mod_manager/publish_success") + "\n" + PublishDestination.Label(server);
                Published?.Invoke();
            }
            catch (OperationCanceledException) { }
            catch (Exception error)
            {
                if (GodotObject.IsInstanceValid(this))
                {
                    if (error.Message == "mod_manager/publish_consent_required")
                        await RefreshConsentAsync();
                    if (GodotObject.IsInstanceValid(this)) ShowError(error);
                }
            }
            finally
            {
                if (GodotObject.IsInstanceValid(this))
                {
                    SetBusy(false);
                }
            }
        }

        private void ConfigureConsent()
        {
            consentCheck.SetPressedNoSignal(false);
            consentText.Text = "";
            consentBlock.Visible = permissions?.IsAdmin == false && permissions.AuthorConsent is { Accepted: false };
            if (!consentBlock.Visible) return;
            var consent = permissions.AuthorConsent;
            var bundled = consent.Version == AuthorConsentDocument.Version;
            consentText.Visible = bundled;
            consentLanguage.Visible = bundled;
            consentLanguage.Text = Tr("mod_manager/publish_consent_language");
            consentCheck.Text = bundled ? Tr("mod_manager/publish_consent_accept")
                : string.Format(Tr("mod_manager/publish_consent_accept_link"), consent.Version);
            consentWebsite.Disabled = !TryConsentUrl(out _);
            if (bundled && ModRegistry.Instance.AllDiscoveredMods.TryGetValue("mod_manager", out var owner))
            {
                // The provider also reads packaged mods; the document is shown verbatim in the reader's
                // language when a translation is bundled, and is never parsed as BBCode or auto-translated.
                AuthorConsentDocument.Resolve(TranslationServer.GetLocale(), owner.FileProvider.FileExists,
                    out var document, out var native);
                if (native)
                    consentLanguage.Text = Tr("mod_manager/publish_consent_language_native");
                consentText.Text = owner.FileProvider.ReadFileText(document);
                consentText.ScrollToLine(0);
            }
        }

        private async Task RefreshConsentAsync()
        {
            if (permissions?.AuthorConsent != null) permissions.AuthorConsent.Accepted = false;
            ConfigureConsent();
            try
            {
                var server = servers[serverChoice.Selected];
                var received = await CatalogPublisher.GetPermissionsAsync(server.Url, server.TokenFile, lifetime.Token);
                if (!GodotObject.IsInstanceValid(this)) return;
                permissions = received;
                ConfigureConsent();
            }
            catch (OperationCanceledException) { }
            catch
            {
                // No stale permission may enable publication after a rejected consent.
                if (GodotObject.IsInstanceValid(this)) permissions = null;
            }
        }

        private bool TryConsentUrl(out Uri url)
            => Uri.TryCreate(permissions?.AuthorConsent?.Url, UriKind.Absolute, out url)
                && url.Scheme == Uri.UriSchemeHttps && !string.IsNullOrEmpty(url.Host) && string.IsNullOrEmpty(url.UserInfo);

        private void OnConsentWebsite()
        {
            if (TryConsentUrl(out var url)) OS.ShellOpen(url.AbsoluteUri);
        }

        private void OnConsentToggled(bool value) => UpdateActions();

        private string SelectedSection()
            => customSection.Visible ? sectionInput.Text.Trim()
                : sectionChoice.Selected >= 0 ? sectionChoice.GetItemText(sectionChoice.Selected) : "";

        private void OnSectionTextChanged(string text) => UpdateActions();
        private void OnSectionSelected(long index)
        {
            sectionInput.Text = sectionChoice.GetItemText((int)index);
            UpdateActions();
        }

        private void UpdateActions()
        {
            if (publish == null) return;
            publish.Disabled = busy || categories?.HasPendingInput == true || permissions?.AllowsSection(SelectedSection()) != true
                || !permissions.IsAdmin && permissions.AuthorConsent is { Accepted: false } && !consentCheck.ButtonPressed;
            consentCheck.Disabled = busy;
            createSection.Disabled = busy || permissions?.IsAdmin != true || !permissions.AllowsSection(sectionInput.Text.Trim());
            sectionChoice.Disabled = busy || sectionChoice.ItemCount == 0;
        }

        private void SetBusy(bool value)
        {
            busy = value;
            if (categories != null) categories.SetEditable(!value && ModCategoryManifestStore.CanEdit(mod, ModCategoryEditor.ProjectRoot));
            serverChoice.Disabled = value;
            sectionInput.Editable = !value;
            close.Disabled = value;
            UpdateActions();
        }

        private async void OnCreateSection()
        {
            if (busy || createSection.Disabled) return;
            var section = sectionInput.Text.Trim();
            SetBusy(true);
            try
            {
                var server = servers[serverChoice.Selected];
                await CatalogPublisher.CreateSectionAsync(server.Url, server.TokenFile, section, lifetime.Token);
                if (!GodotObject.IsInstanceValid(this)) return;
                if (!permissions.Sections.Contains(section))
                {
                    permissions.Sections.Add(section);
                    sectionChoice.AddItem(section);
                }
                sectionChoice.Select(permissions.Sections.IndexOf(section));
                status.Text = Tr("mod_manager/publish_section_created");
            }
            catch (OperationCanceledException) { }
            catch (Exception error) { if (GodotObject.IsInstanceValid(this)) ShowError(error); }
            finally { if (GodotObject.IsInstanceValid(this)) SetBusy(false); }
        }

        private static Task<PublishingPermissions> LoadPermissions(CatalogServer server, CancellationToken cancellation)
            => CatalogPublisher.GetPermissionsAsync(server.Url, server.TokenFile, cancellation);

        private void ShowError(Exception error)
        {
            status.Text = Tr(PublishAvailability.ErrorMessageKey(error));
            var detail = PublishAvailability.ErrorMessageDetail(error);
            if (!string.IsNullOrEmpty(detail)) status.Text += "\n" + detail;
        }

        private void OnClose()
        {
            if (!busy)
                QueueFree();
        }

        public override void _UnhandledInput(InputEvent @event)
        {
            if (!@event.IsActionPressed("ui_cancel"))
                return;
            GetViewport().SetInputAsHandled();
            OnClose();
        }
    }
}
