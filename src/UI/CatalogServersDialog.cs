using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Cthangover.Core.Mods;
using Cthangover.Mods.Interface.Base.Lists;
using Godot;

namespace Mods.ModManager
{
    /// <summary>
    /// Edits the player's source list, then refreshes the live manager. Secrets
    /// are accepted in a masked field and never read back into controls. An
    /// existing key is retained only while its exact endpoint remains unchanged.
    /// </summary>
    public partial class CatalogServersDialog : ModWidget
    {
        private const string FormPath = "Center/Panel/Margin/Form/Body/Editor/";
        private CatalogServerStore store;
        private List<CatalogServer> entries;
        private ItemList list;
        private LineEdit serverName;
        private LineEdit url;
        private LineEdit key;
        private CheckBox enabled;
        private CheckBox clearKey;
        private Label status;
        private Label count;
        private string selectedId;
        private bool busy;
        private bool refreshFailed;
        private ConfirmationDialog confirmDelete;

        /// <summary>Raised after persistence and live configuration have both changed.</summary>
        public event Action Changed;

        protected override void Construct()
        {
            store = CatalogServerStore.Default;
            list = GetNode<ItemList>("Center/Panel/Margin/Form/Body/Servers/List");
            serverName = GetNode<LineEdit>(FormPath + "Name");
            url = GetNode<LineEdit>(FormPath + "Url");
            key = GetNode<LineEdit>(FormPath + "Key");
            enabled = GetNode<CheckBox>(FormPath + "Enabled");
            clearKey = GetNode<CheckBox>(FormPath + "ClearKey");
            status = GetNode<Label>("Center/Panel/Margin/Form/Status");
            count = GetNode<Label>("Center/Panel/Margin/Form/Count");
            confirmDelete = GetNode<ConfirmationDialog>("ConfirmDelete");
            confirmDelete.Title = Tr("mod_manager/sources_delete");
            confirmDelete.DialogText = Tr("mod_manager/sources_delete_confirm");
            confirmDelete.GetOkButton().Text = Tr("mod_manager/sources_delete");
            confirmDelete.GetCancelButton().Text = Tr("mod_manager/sources_cancel");
            confirmDelete.Confirmed += OnDeleteConfirmed;
            GetNode<Button>("Center/Panel/Margin/Form/Body/Servers/New").Pressed += OnNew;
            GetNode<Button>(FormPath + "Actions/Save").Pressed += OnSave;
            GetNode<Button>(FormPath + "Actions/Delete").Pressed += OnDelete;
            GetNode<Button>("Center/Panel/Margin/Form/Close").Pressed += OnClose;
            list.ItemSelected += OnSelected;
            clearKey.Toggled += OnClearKeyToggled;
            CatalogService.OnRefreshFailed += OnRefreshFailed;
            try { FillList(null); }
            catch { ShowStorageFailure(); }
        }

        protected override void Destruct()
        {
            CatalogService.OnRefreshFailed -= OnRefreshFailed;
            Changed = null;
            key.Text = "";
        }

        private void FillList(string id)
        {
            entries = store.Load(ModConfig.Instance);
            list.Clear();
            var active = 0;
            var selected = -1;
            for (var index = 0; index < entries.Count; index++)
            {
                var entry = entries[index];
                if (entry.Enabled)
                    active++;
                var state = Tr(entry.Enabled ? "mod_manager/sources_on" : "mod_manager/sources_off");
                list.AddItem($"{entry.Name} · {state}");
                list.SetItemTooltip(index, entry.Url);
                if (entry.Id == id)
                    selected = index;
            }
            count.Text = string.Format(Tr("mod_manager/sources_count"), entries.Count, active);
            if (selected >= 0)
            {
                list.Select(selected);
                FillEditor(entries[selected]);
            }
            else
                OnNew();
        }

        private void FillEditor(CatalogServer entry)
        {
            selectedId = entry?.Id;
            serverName.Text = entry?.Name ?? "";
            url.Text = entry?.Url ?? "";
            key.Text = "";
            key.PlaceholderText = Tr(string.IsNullOrEmpty(entry?.TokenFile)
                ? "mod_manager/sources_key_placeholder" : "mod_manager/sources_key_kept");
            enabled.ButtonPressed = entry?.Enabled ?? true;
            clearKey.ButtonPressed = false;
            clearKey.Visible = !string.IsNullOrEmpty(entry?.TokenFile);
            key.Editable = true;
            GetNode<Button>(FormPath + "Actions/Delete").Disabled = entry == null;
        }

        private void OnNew()
        {
            if (busy)
                return;
            list.DeselectAll();
            FillEditor(null);
            status.Text = "";
        }

        private void OnSelected(long index)
        {
            if (busy)
                return;
            FillEditor(entries[(int)index]);
            status.Text = "";
        }

        private void OnClearKeyToggled(bool clear)
        {
            key.Text = "";
            key.Editable = !clear && !busy;
        }

        private async void OnSave()
        {
            if (busy || entries == null)
                return;
            try
            {
                var entry = new CatalogServer { Name = serverName.Text, Url = url.Text, Enabled = enabled.ButtonPressed };
                if (selectedId != null)
                    entry.Id = selectedId;
                var saved = store.Upsert(ModConfig.Instance, entry, key.Text, clearKey.ButtonPressed);
                key.Text = "";
                store.ApplyTo(ModConfig.Instance);
                FillList(saved.Id);
                Changed?.Invoke();
            }
            catch (ArgumentException)
            {
                status.Text = Tr("mod_manager/sources_invalid");
                return;
            }
            catch (InvalidOperationException)
            {
                status.Text = Tr("mod_manager/sources_duplicate");
                return;
            }
            catch
            {
                status.Text = Tr("mod_manager/sources_storage_failed");
                return;
            }
            await RefreshSaved();
        }

        private void OnDelete()
        {
            if (!busy && selectedId != null)
                confirmDelete.PopupCentered();
        }

        private async void OnDeleteConfirmed()
        {
            if (busy || selectedId == null)
                return;
            try
            {
                store.Remove(ModConfig.Instance, selectedId);
                store.ApplyTo(ModConfig.Instance);
                FillList(null);
                Changed?.Invoke();
            }
            catch
            {
                status.Text = Tr("mod_manager/sources_storage_failed");
                return;
            }
            await RefreshSaved();
        }

        private async Task RefreshSaved()
        {
            SetBusy(true);
            status.Text = Tr("mod_manager/sources_refreshing");
            refreshFailed = false;
            try
            {
                while (CatalogService.IsFetching)
                    await Task.Delay(25);
                await CatalogService.RefreshAsync(forceRefresh: true);
            }
            catch { refreshFailed = true; }
            if (!GodotObject.IsInstanceValid(this))
                return;
            status.Text = Tr(refreshFailed ? "mod_manager/sources_refresh_failed" : "mod_manager/sources_saved");
            SetBusy(false);
        }

        private void SetBusy(bool value)
        {
            busy = value;
            serverName.Editable = !value;
            url.Editable = !value;
            key.Editable = !value && !clearKey.ButtonPressed;
            enabled.Disabled = value;
            clearKey.Disabled = value;
            GetNode<Button>(FormPath + "Actions/Save").Disabled = value;
            GetNode<Button>(FormPath + "Actions/Delete").Disabled = value || selectedId == null;
            GetNode<Button>("Center/Panel/Margin/Form/Body/Servers/New").Disabled = value;
        }

        private void OnRefreshFailed(string message) { if (busy) refreshFailed = true; }
        private void ShowStorageFailure()
        {
            entries = null;
            SetBusy(true);
            status.Text = Tr("mod_manager/sources_storage_failed");
        }
        private void OnClose() => QueueFree();

        public override void _UnhandledInput(InputEvent @event)
        {
            if (!@event.IsActionPressed("ui_cancel"))
                return;
            GetViewport().SetInputAsHandled();
            QueueFree();
        }
    }
}
