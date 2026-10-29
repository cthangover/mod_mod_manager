namespace Mods.ModManager
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Cthangover.Core.UI.Theming;
    using Cthangover.Mods.Interface.Theming;
    using Godot;

    /// <summary>Edits a detached copy; Apply replaces the collection draft, Cancel leaves it untouched.</summary>
    public partial class CollectionSettingsDialog : ConfirmationDialog
    {
        private readonly List<CollectionSettingRow> rows = new();
        private readonly List<HBoxContainer> extras = new();
        private CollectionSettingsCatalog catalog;
        private Action<Dictionary<string, string>> apply;
        private VBoxContainer extraFields;
        private Label error;

        /// <summary>Builds a scrollable form from selected mods and existing keys, including undeclared third-party settings.</summary>
        public void Initialize(CollectionSettingsCatalog source, Dictionary<string, string> settings, Action<Dictionary<string, string>> onApply)
        {
            catalog = source;
            apply = onApply;
            Name = "CollectionSettingsDialog";
            Title = Tr("mod_manager/game_settings_title");
            OkButtonText = TranslationServer.Translate("mod_manager/collection_json_apply");
            CancelButtonText = TranslationServer.Translate("mod_manager/cancel");
            DialogHideOnOk = false;
            Exclusive = true;
            // Only the native action bar stays outside the scroll. A large content minimum
            // defeats PopupCenteredClamped: Godot grows the window back past the viewport.
            Unresizable = false;
            var scroll = new ScrollContainer { Name = "FieldsScroll", CustomMinimumSize = new Vector2(0, 80), SizeFlagsVertical = Control.SizeFlags.ExpandFill, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, VerticalScrollMode = ScrollContainer.ScrollMode.Auto, FollowFocus = true };

            var fields = UiKit.Column(UiTokens.Space4);
            fields.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            scroll.AddChild(fields);
            fields.AddChild(Wrapped(Tr("mod_manager/game_settings_hint")));
            string group = null;
            foreach (var definition in catalog.Definitions.Where(d => d.Available || settings?.ContainsKey(d.Key) == true).OrderBy(d => d.Group).ThenBy(d => d.Key))
            {
                if (group != definition.Group)
                {
                    group = definition.Group;
                    fields.AddChild(Wrapped(catalog.Translate(group)));
                }
                var row = new CollectionSettingRow();
                fields.AddChild(row);
                row.Initialize(catalog, definition, settings != null && settings.TryGetValue(definition.Key, out var saved) ? saved : null);
                rows.Add(row);
            }
            var advanced = UiKit.Button(Tr("mod_manager/game_settings_advanced"));
            advanced.ToggleMode = true;
            advanced.TooltipText = Tr("mod_manager/game_settings_advanced_hint");
            fields.AddChild(advanced);
            extraFields = UiKit.Column(UiTokens.Space2);
            extraFields.Visible = false;
            fields.AddChild(extraFields);
            advanced.Toggled += visible => extraFields.Visible = visible;
            var known = new HashSet<string>(rows.Select(r => r.Key), StringComparer.Ordinal);
            foreach (var pair in settings ?? new Dictionary<string, string>())
                if (!known.Contains(pair.Key))
                    AddExtra(pair.Key, pair.Value);
            var add = UiKit.Button(Tr("mod_manager/game_settings_add"));
            add.TooltipText = Tr("mod_manager/game_settings_advanced_hint");
            add.Pressed += () => AddExtra("", "");
            extraFields.AddChild(add);
            error = Wrapped(catalog.Errors.Count == 0 ? "" : Tr("mod_manager/game_settings_metadata_error") + " " + string.Join(", ", catalog.Errors));
            error.Modulate = UiTokens.Warning;
            fields.AddChild(error);
            AddChild(scroll);
            Confirmed += Apply;
            Canceled += QueueFree;
        }

        private static string Tr(string suffix) => TranslationServer.Translate(suffix);
        private static Label Wrapped(string text) => new() { Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };

        private void AddExtra(string key, string value)
        {
            var row = UiKit.Row(UiTokens.Space2);
            var keyEdit = new LineEdit { Name = "Key", Text = key, PlaceholderText = Tr("mod_manager/game_settings_key"), TooltipText = Tr("mod_manager/game_settings_key"), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            var valueEdit = new LineEdit { Name = "Value", Text = value ?? "", PlaceholderText = Tr("mod_manager/game_settings_value"), TooltipText = Tr("mod_manager/game_settings_value"), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            var remove = UiKit.Button(Tr("mod_manager/game_settings_remove"));
            remove.TooltipText = Tr("mod_manager/game_settings_remove_hint");
            row.AddChild(keyEdit);
            row.AddChild(valueEdit);
            row.AddChild(remove);
            remove.Pressed += () => RemoveExtra(row);
            extraFields.AddChild(row);
            extras.Add(row);
        }

        private void RemoveExtra(HBoxContainer row)
        {
            extras.Remove(row);
            row.QueueFree();
        }

        private void Apply()
        {
            var invalid = rows.FirstOrDefault(r => !r.Valid);
            if (invalid != null)
            {
                error.Text = invalid.Key + ": " + Tr("mod_manager/game_settings_invalid");
                return;
            }
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            var reserved = new HashSet<string>(rows.Select(r => r.Key), StringComparer.Ordinal);
            foreach (var row in rows)
                if (row.Value != null)
                    result[row.Key] = row.Value;
            foreach (var row in extras)
            {
                var key = row.GetNode<LineEdit>("Key").Text.Trim();
                var value = row.GetNode<LineEdit>("Value").Text;
                if (key.Length == 0 && value.Length == 0)
                    continue;
                if (key.Length == 0 || reserved.Contains(key) || !result.TryAdd(key, value))
                {
                    error.Text = Tr("mod_manager/game_settings_duplicate");
                    return;
                }
            }
            apply(result);
            QueueFree();
        }
    }
}
