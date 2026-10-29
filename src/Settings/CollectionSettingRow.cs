namespace Mods.ModManager
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Cthangover.Core.UI.Theming;
    using Cthangover.Mods.Interface.Theming;
    using Godot;

    /// <summary>One draft field. Invalid saved values are retained until the player explicitly replaces them.</summary>
    public partial class CollectionSettingRow : VBoxContainer
    {
        private CollectionSettingsCatalog catalog;
        private CollectionSettingDefinition definition;
        private IReadOnlyList<CollectionSettingOption> choices;
        private OptionButton select;
        private LineEdit custom;
        private Label feedback;
        private TextureRect preview;
        private ImageTexture texture;
        private string original;
        private string value;
        private bool changing;
        private bool customMode;
        private int customIndex = -1;

        public string Key => definition.Key;
        public string Value => customMode ? custom.Text.Trim() : value;
        public bool Valid => Value == original || Value == null || (definition.AllowCustom && catalog.IsValidCustom(definition, Value))
            || choices.Any(o => o.Available && o.Value == Value);

        /// <summary>Builds controls from the owner's descriptor; opening a form never creates a collection override.</summary>
        public void Initialize(CollectionSettingsCatalog source, CollectionSettingDefinition field, string saved)
        {
            catalog = source;
            definition = field;
            original = value = saved;
            Name = field.Key;
            choices = catalog.Choices(field);
            customMode = saved != null && field.AllowCustom && !choices.Any(o => o.Value == saved) && catalog.IsValidCustom(field, saved);
            AddThemeConstantOverride("separation", UiTokens.Space2);
            var title = Wrapped(catalog.Translate(field.Name), UiTokens.InkBright);
            title.TooltipText = field.Key + " · " + field.ModId;
            title.MouseFilter = MouseFilterEnum.Pass;
            AddChild(title);
            AddChild(Wrapped(catalog.Translate(field.Description), UiTokens.Muted));
            var controls = UiKit.Row(UiTokens.Space2);
            select = new OptionButton { SizeFlagsHorizontal = SizeFlags.ExpandFill, FitToLongestItem = false, TooltipText = catalog.Translate(field.Description) };
            select.AddItem(Tr("mod_manager/game_settings_automatic"));
            // Renderer fallback is valid only when exactly one implementation is present.
            select.SetItemDisabled(0, field.Type == "choice" && !field.AllowCustom && string.IsNullOrEmpty(field.Automatic) && choices.Count(o => o.Available) != 1);
            foreach (var option in choices)
            {
                select.AddItem(OptionTitle(option));
                select.SetItemDisabled(select.ItemCount - 1, !option.Available);
            }
            if (saved != null && !customMode && !choices.Any(o => o.Value == saved))
            {
                select.AddItem(saved + " · " + Tr("mod_manager/game_settings_unavailable"));
                select.SetItemDisabled(select.ItemCount - 1, true);
            }
            if (field.AllowCustom)
            {
                customIndex = select.ItemCount;
                select.AddItem(Tr("mod_manager/game_settings_custom"));
            }
            controls.AddChild(select);
            if (field.Type is "resource" or "character")
            {
                var browse = UiKit.Button(Tr("mod_manager/game_settings_browse"));
                browse.TooltipText = Tr("mod_manager/game_settings_search_hint");
                browse.Pressed += OpenPicker;
                controls.AddChild(browse);
            }
            AddChild(controls);
            custom = new LineEdit { Text = saved ?? "", PlaceholderText = Tr("mod_manager/game_settings_custom"), TooltipText = field.Key, Visible = false };
            custom.TextChanged += OnCustomChanged;
            AddChild(custom);
            if (field.Type == "resource")
            {
                preview = new TextureRect { CustomMinimumSize = new Vector2(0, 112), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, MouseFilter = MouseFilterEnum.Ignore };
                AddChild(preview);
            }
            feedback = Wrapped("", UiTokens.Warning);
            AddChild(feedback);
            select.ItemSelected += OnSelected;
            Refresh();
        }

        public override void _ExitTree() => ReleaseTexture();

        private static string Tr(string suffix) => TranslationServer.Translate(suffix);
        private static Label Wrapped(string text, Color color)
            => new Label { Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart, Modulate = color, SizeFlagsHorizontal = SizeFlags.ExpandFill };

        private string OptionTitle(CollectionSettingOption option)
            => catalog.Translate(option.Name ?? option.Value) + (option.Name == option.Value ? "" : " (" + option.Value + ")")
                + (option.Available ? "" : " · " + Tr("mod_manager/game_settings_unavailable"));

        private void OnSelected(long index)
        {
            if (changing)
                return;
            customMode = index == customIndex;
            if (customMode)
                value = custom.Text.Trim();
            else if (index == 0)
                value = null;
            else if (index <= choices.Count)
                value = choices[(int)index - 1].Value;
            Refresh();
        }

        private void OnCustomChanged(string text)
        {
            if (changing)
                return;
            value = text.Trim();
            UpdateFeedback();
        }

        private void Refresh()
        {
            changing = true;
            var index = choices.ToList().FindIndex(o => o.Value == value);
            var typed = customMode;
            select.Select(typed ? customIndex : value == null ? 0 : index >= 0 ? index + 1 : choices.Count + 1);
            custom.Visible = select.Selected == customIndex;
            if (custom.Visible && custom.Text != value)
                custom.Text = value ?? "";
            changing = false;
            UpdateFeedback();
            UpdatePreview(index >= 0 ? choices[index] : null);
        }

        private void UpdateFeedback()
        {
            if (!definition.Available)
                feedback.Text = Tr("mod_manager/game_settings_owner_missing");
            else if (!Valid)
                feedback.Text = Tr("mod_manager/game_settings_invalid");
            else if (value == null)
                feedback.Text = string.IsNullOrEmpty(definition.Automatic) ? Tr("mod_manager/game_settings_single_choice") : catalog.Translate(definition.Automatic);
            else if (!choices.Any(o => o.Available && o.Value == value) && !(definition.AllowCustom && catalog.IsValidCustom(definition, value)))
                feedback.Text = Tr("mod_manager/game_settings_missing");
            else
                feedback.Text = "";
        }

        private void ReleaseTexture()
        {
            if (preview != null)
                preview.Texture = null;
            texture?.Dispose();
            texture = null;
        }

        private void UpdatePreview(CollectionSettingOption option)
        {
            if (preview == null)
                return;
            ReleaseTexture();
            preview.Visible = option?.PreviewPath != null;
            if (!preview.Visible)
                return;
            var data = option.Provider.ReadFileBinary(option.PreviewPath);
            if (data == null)
                return;
            using var image = new Image();
            var extension = System.IO.Path.GetExtension(option.PreviewPath).ToLowerInvariant();
            var error = extension == ".webp" ? image.LoadWebpFromBuffer(data) : extension is ".jpg" or ".jpeg" ? image.LoadJpgFromBuffer(data) : image.LoadPngFromBuffer(data);
            if (error == Error.Ok)
                preview.Texture = texture = ImageTexture.CreateFromImage(image);
        }

        private void OpenPicker()
        {
            var dialog = new ConfirmationDialog { Title = catalog.Translate(definition.Name), OkButtonText = Tr("mod_manager/game_settings_choose"), CancelButtonText = TranslationServer.Translate("mod_manager/cancel") };
            var column = UiKit.Column(UiTokens.Space2);
            var search = new LineEdit { PlaceholderText = Tr("mod_manager/game_settings_search_hint"), TooltipText = Tr("mod_manager/game_settings_search_hint") };
            var list = new ItemList { CustomMinimumSize = new Vector2(0, 80), SizeFlagsVertical = SizeFlags.ExpandFill, TooltipText = catalog.Translate(definition.Description) };
            column.AddChild(search);
            column.AddChild(list);
            dialog.AddChild(column);
            var filtered = new List<CollectionSettingOption>();
            search.TextChanged += text => FillPicker(list, filtered, text);
            dialog.Confirmed += () => ApplyPicker(dialog, list, filtered);
            dialog.Canceled += dialog.QueueFree;
            list.ItemActivated += _ => ApplyPicker(dialog, list, filtered);
            AddChild(dialog);
            FillPicker(list, filtered, "");
            dialog.PopupCenteredClamped(new Vector2I(540, 440), 0.9f);
            search.GrabFocus();
        }

        private void FillPicker(ItemList list, List<CollectionSettingOption> filtered, string search)
        {
            list.Clear();
            filtered.Clear();
            foreach (var option in choices.Where(o => OptionTitle(o).Contains(search, StringComparison.OrdinalIgnoreCase)))
            {
                filtered.Add(option);
                var index = list.AddItem(OptionTitle(option));
                list.SetItemDisabled(index, !option.Available);
                list.SetItemTooltip(index, option.Value + " · " + option.ModId);
                if (option.Value == value)
                    list.Select(index);
            }
        }

        private void ApplyPicker(ConfirmationDialog dialog, ItemList list, List<CollectionSettingOption> filtered)
        {
            var selected = list.GetSelectedItems();
            if (selected.Length > 0 && filtered[selected[0]].Available)
            {
                customMode = false;
                value = filtered[selected[0]].Value;
                Refresh();
            }
            dialog.QueueFree();
        }
    }
}
