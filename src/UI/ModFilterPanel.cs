using Cthangover.Mods.Interface.View;
using System;
using System.Collections.Generic;
using System.Linq;
using Cthangover.Core.Mods;
using Cthangover.Core.UI.Theming;
using Godot;

namespace Mods.ModManager
{
    /// <summary>
    /// Left filter sidebar. The frame (title, reset button, section captions and
    /// the grouping toggle) lives in
    /// <c>scenes/components/mod_filter_panel.tscn</c> for visual editing; this
    /// script fills the status/category lists and the catalog selector, keeping
    /// the shared <see cref="ModFilterState"/> in sync.
    /// The panel does not own the state and never rebuilds the grid itself — it
    /// announces every change through <see cref="Changed"/>.
    /// </summary>
    public partial class ModFilterPanel : VBoxContainer
    {
        private ModFilterState _state;
        private Dictionary<string, int> _categoryCounts = new(ModCategories.Comparer);
        private Label _title;
        private Button _reset;
        private Label _statusCaption;
        private Label _categoryCaption;
        private CheckButton _groupToggle;
        private VBoxContainer _statusList;
        private VBoxContainer _categoryList;
        private OptionButton _catalogSelect;
        private readonly Dictionary<string, string> _catalogNames = new(StringComparer.Ordinal);
        private readonly Dictionary<ModItemStatus, CheckBox> _statusBoxes = new();
        private readonly Dictionary<string, CheckBox> _categoryBoxes = new(ModCategories.Comparer);

        /// <summary>Raised after any filter or grouping change.</summary>
        public event Action Changed;

        /// <summary>
        /// Raised after the reset button cleared the state. The panel cannot
        /// touch the header's search box, so the screen clears it in response.
        /// </summary>
        public event Action ResetRequested;

        /// <summary>Binds the sidebar to the shared filter state; call once, after the scene is built.</summary>
        public void Bind(ModFilterState state)
        {
            _state = state;

            _title = GetNode<Label>("Title");
            _reset = GetNode<Button>("Reset");
            _statusCaption = GetNode<Label>("StatusCaption");
            _categoryCaption = GetNode<Label>("CategoryCaption");
            _groupToggle = GetNode<CheckButton>("GroupToggle");
            _statusList = GetNode<VBoxContainer>("StatusList");
            _categoryList = GetNode<VBoxContainer>("CategoryList");
            _catalogSelect = GetNode<OptionButton>("CatalogSelect");
            GetNode<Label>("CatalogCaption").Text = TranslationServer.Translate("mod_manager/filter_catalog");

            _title.Text = TranslationServer.Translate("mod_manager/filters");
            _reset.Text = TranslationServer.Translate("mod_manager/reset_filters");
            _reset.TooltipText = TranslationServer.Translate("mod_manager/filters_reset_tooltip");
            _statusCaption.Text = TranslationServer.Translate("mod_manager/filter_status");
            _categoryCaption.Text = TranslationServer.Translate("mod_manager/filter_categories");
            _groupToggle.Text = TranslationServer.Translate("mod_manager/group_by_category");
            _groupToggle.ButtonPressed = _state.GroupByCategory;
            ModHelpPanel.Attach(_reset, "filters_reset");
            ModHelpPanel.Attach(_groupToggle, "group_categories");

            _statusCaption.AddThemeColorOverride("font_color", UiTokens.Muted);
            _categoryCaption.AddThemeColorOverride("font_color", UiTokens.Muted);

            _reset.Pressed += OnResetPressed;
            _groupToggle.Toggled += OnGroupToggled;
            _catalogSelect.ItemSelected += OnCatalogSelected;

            foreach (var status in Enum.GetValues<ModItemStatus>())
                AddStatusCheck(status);
        }

        /// <summary>Refreshes available source choices after model reload; a removed/disabled selection returns to all catalogs.</summary>
        public void SetCatalogs(IReadOnlyList<CatalogServer> servers)
        {
            _catalogSelect.Clear();
            _catalogNames.Clear();
            _catalogSelect.AddItem(TranslationServer.Translate("mod_manager/filter_all_catalogs"));
            _catalogSelect.AddItem(TranslationServer.Translate("mod_manager/filter_local_only"));
            _catalogSelect.SetItemMetadata(1, string.Empty);
            var selected = _state.CatalogUrl == string.Empty ? 1 : 0;
            foreach (var server in servers)
            {
                if (!server.Enabled || string.IsNullOrEmpty(server.Url) || _catalogNames.ContainsKey(server.Url))
                    continue;
                var caption = server.Name;
                if (Uri.TryCreate(server.Url, UriKind.Absolute, out var uri)
                    && (string.IsNullOrEmpty(caption) || caption == uri.Host))
                    caption = uri.Authority;
                _catalogNames.Add(server.Url, caption ?? server.Url);
                _catalogSelect.AddItem(caption ?? server.Url);
                var index = _catalogSelect.ItemCount - 1;
                _catalogSelect.SetItemMetadata(index, server.Url);
                _catalogSelect.GetPopup().SetItemTooltip(index, server.Url);
                if (_state.CatalogUrl == server.Url)
                    selected = index;
            }
            if (selected == 0)
                _state.CatalogUrl = null;
            _catalogSelect.Select(selected);
            UpdateCatalogTooltip();
        }

        /// <summary>Refreshes the numbers in the checkbox captions.</summary>
        public void RefreshCounts(
            Dictionary<string, int> categoryCounts,
            Dictionary<ModItemStatus, int> statusCounts,
            Dictionary<string, int> catalogCounts)
        {
            for (var index = 1; index < _catalogSelect.ItemCount; index++)
            {
                var source = _catalogSelect.GetItemMetadata(index).AsString();
                var caption = index == 1 ? TranslationServer.Translate("mod_manager/filter_local_only").ToString() : _catalogNames[source];
                _catalogSelect.SetItemText(index, $"{caption} ({catalogCounts.GetValueOrDefault(source)})");
            }
            foreach (var pair in _statusBoxes)
            {
                var count = statusCounts.GetValueOrDefault(pair.Key);
                pair.Value.Text = $"{ModStatusNames.Translate(pair.Key)} ({count})";
            }

            _categoryCounts = categoryCounts;
            RebuildCategories();
        }

        private void RebuildCategories()
        {
            foreach (var child in _categoryList.GetChildren()) { _categoryList.RemoveChild(child); child.QueueFree(); }
            _categoryBoxes.Clear();
            foreach (var category in ModCategoryNames.Order(_categoryCounts.Keys))
            {
                AddCategoryCheck(category);
                _categoryBoxes[category].Text = $"{ModCategoryNames.Translate(category)} ({_categoryCounts[category]})";
            }
        }

        private void AddStatusCheck(ModItemStatus status)
        {
            var box = new CheckBox
            {
                Text = ModStatusNames.Translate(status),
                ButtonPressed = _state.Statuses.Contains(status),
            };
            box.Toggled += pressed => OnStatusToggled(status, pressed);
            _statusBoxes[status] = box;
            _statusList.AddChild(box);
        }

        private void AddCategoryCheck(string category)
        {
            var box = new CheckBox
            {
                Text = ModCategoryNames.Translate(category),
                ClipText = true,
                TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
                TooltipText = TranslationServer.Translate("mod_categories/hint_filter") + " " + category,
                ButtonPressed = _state.Categories.Contains(category),
            };
            box.SetMeta("category", category);
            box.Toggled += pressed => OnCategoryToggled(category, pressed);
            _categoryBoxes[category] = box;
            _categoryList.AddChild(box);
        }

        private void OnStatusToggled(ModItemStatus status, bool pressed)
        {
            if (pressed)
                _state.Statuses.Add(status);
            else
                _state.Statuses.Remove(status);

            Changed?.Invoke();
        }

        private void OnCategoryToggled(string category, bool pressed)
        {
            if (pressed)
                _state.Categories.Add(category);
            else
                _state.Categories.Remove(category);

            Changed?.Invoke();
        }

        private void OnGroupToggled(bool pressed)
        {
            _state.GroupByCategory = pressed;
            Changed?.Invoke();
        }

        private void OnCatalogSelected(long index)
        {
            _state.CatalogUrl = index == 0 ? null : _catalogSelect.GetItemMetadata((int)index).AsString();
            UpdateCatalogTooltip();
            Changed?.Invoke();
        }

        private void UpdateCatalogTooltip()
        {
            // Only a concrete catalog has extra information beyond the selected caption: its URL.
            _catalogSelect.TooltipText = string.IsNullOrEmpty(_state.CatalogUrl) ? string.Empty : _state.CatalogUrl;
        }

        private void OnResetPressed()
        {
            _state.Reset();
            _catalogSelect.Select(0);
            UpdateCatalogTooltip();

            foreach (var box in _statusBoxes.Values)
                box.SetPressedNoSignal(false);
            foreach (var box in _categoryBoxes.Values)
                box.SetPressedNoSignal(false);

            ResetRequested?.Invoke();
            Changed?.Invoke();
        }
    }
}
