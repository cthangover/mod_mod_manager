namespace Mods.ModManager
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Cthangover.Core.Mods;
    using Cthangover.Core.UI.Theming;
    using Godot;

    /// <summary>
    /// The new-mod wizard's form: a title, an id (suggested from the title until the author types an id), a
    /// description, the base mod the mod depends on, and the game it joins. Create calls back with the typed
    /// values; the caller writes the files and the collection.
    /// </summary>
    public static class NewModDialog
    {
        /// <summary>Builds the (not yet shown) dialog. <paramref name="games"/> lists the game ids and titles.</summary>
        public static AcceptDialog Build(IReadOnlyList<(string Id, string Title)> games, string preselectId, Action<string, string, string, string, string> create)
        {
            var nameEdit = new LineEdit { PlaceholderText = TranslationServer.Translate("mod_manager/newmod_name_tip"), CustomMinimumSize = new Vector2(420, 0) };
            var idEdit = new LineEdit { PlaceholderText = "my_first_mod", CustomMinimumSize = new Vector2(420, 0) };
            var descEdit = new LineEdit { PlaceholderText = TranslationServer.Translate("mod_manager/newmod_desc_tip"), CustomMinimumSize = new Vector2(420, 0) };
            var baseSelect = new OptionButton { CustomMinimumSize = new Vector2(420, 0) };
            var gameSelect = new OptionButton { CustomMinimumSize = new Vector2(420, 0) };
            var selected = 0;
            for (var i = 0; i < games.Count; i++)
            {
                gameSelect.AddItem(games[i].Title);
                gameSelect.SetItemMetadata(i, games[i].Id);
                if (games[i].Id == preselectId)
                    selected = i;
            }
            if (games.Count > 0)
                gameSelect.Select(selected);
            // The bases follow the game: its content mods come first (plan 14.G16), the engine bases last.
            FillBases(baseSelect, games.Count == 0 ? string.Empty : gameSelect.GetItemMetadata(gameSelect.Selected).AsString());
            gameSelect.ItemSelected += index => FillBases(baseSelect, gameSelect.GetItemMetadata((int)index).AsString());

            var box = new VBoxContainer();
            box.AddThemeConstantOverride("separation", UiTokens.Space2);
            box.AddChild(Row("mod_manager/newmod_name", nameEdit));
            box.AddChild(Row("mod_manager/newmod_id", idEdit));
            box.AddChild(Row("mod_manager/newmod_desc", descEdit));
            box.AddChild(Row("mod_manager/newmod_base", baseSelect));
            box.AddChild(Row("mod_manager/newmod_collection", gameSelect));

            // The id follows the title until the author types an id; the suggestion itself is not a typed id.
            var idTouched = false;
            var suggesting = false;
            nameEdit.TextChanged += text =>
            {
                if (idTouched)
                    return;
                suggesting = true;
                idEdit.Text = NewModTemplate.SuggestId(text);
                suggesting = false;
            };
            idEdit.TextChanged += _ =>
            {
                if (!suggesting)
                    idTouched = true;
            };

            var dialog = new AcceptDialog
            {
                Title = TranslationServer.Translate("mod_manager/newmod_title"),
                OkButtonText = TranslationServer.Translate("mod_manager/newmod_create"),
                Exclusive = true,
                DialogText = TranslationServer.Translate("mod_manager/newmod_hint"),
            };
            dialog.AddChild(box);
            dialog.Confirmed += () =>
            {
                var dependency = baseSelect.ItemCount == 0 ? NewModTemplate.Bases[0] : baseSelect.GetItemMetadata(baseSelect.Selected).AsString();
                var game = gameSelect.ItemCount == 0 ? string.Empty : gameSelect.GetItemMetadata(gameSelect.Selected).AsString();
                create(nameEdit.Text.Trim(), idEdit.Text.Trim(), descEdit.Text.Trim(), dependency, game);
            };
            return dialog;
        }

        /// <summary>
        /// Lists the base options of a game (<see cref="NewModTemplate.OptionsFor"/>): the game's content mods (the
        /// dependencies of its story and content mechanics) and the two engine bases. Engine bases keep their titles.
        /// </summary>
        private static void FillBases(OptionButton baseSelect, string gameId)
        {
            baseSelect.Clear();
            var content = new List<string>();
            var game = string.IsNullOrEmpty(gameId) ? null : ModCollectionStore.LoadAll().FirstOrDefault(c => string.Equals(c.Id, gameId, StringComparison.OrdinalIgnoreCase));
            foreach (var mechanic in game?.Mechanics ?? new List<ModCollectionMechanic>())
            {
                if (mechanic.Role is "story" or "content" && mechanic.Mods != null)
                    content.AddRange(mechanic.Mods);
            }
            foreach (var option in NewModTemplate.OptionsFor(content))
            {
                var label = option == NewModTemplate.Bases[0] ? TranslationServer.Translate("mod_manager/newmod_base_rpg")
                    : option == NewModTemplate.Bases[1] ? TranslationServer.Translate("mod_manager/newmod_base_dialog")
                    : ModDependency.Parse(option).ModId;
                baseSelect.AddItem(label);
                baseSelect.SetItemMetadata(baseSelect.ItemCount - 1, option);
            }
            baseSelect.Select(0);
        }

        private static Control Row(string labelKey, Control field)
        {
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", UiTokens.Space3);
            var label = new Label
            {
                Text = TranslationServer.Translate(labelKey),
                CustomMinimumSize = new Vector2(150, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };
            label.AddThemeColorOverride("font_color", UiTokens.Muted);
            row.AddChild(label);
            field.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            row.AddChild(field);
            return row;
        }
    }
}
