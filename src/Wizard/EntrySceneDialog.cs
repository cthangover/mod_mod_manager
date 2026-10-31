namespace Mods.ModManager
{
    using System;
    using System.Collections.Generic;
    using Godot;

    /// <summary>
    /// The start-scene picker of a game: the scenes its installed mods define, the current one selected. Apply
    /// hands the chosen name back; the caller stores it in the collection draft (Save writes it, and New game
    /// starts there on the next launch).
    /// </summary>
    public static class EntrySceneDialog
    {
        /// <summary>Builds the (not yet shown) picker; <paramref name="current"/> may be null (the game's default scene).</summary>
        public static AcceptDialog Build(IReadOnlyList<string> scenes, string current, Action<string> apply)
        {
            var select = new OptionButton { CustomMinimumSize = new Vector2(420, 0) };
            var items = new List<string>(scenes);
            if (!string.IsNullOrEmpty(current) && !items.Contains(current))
                items.Insert(0, current);
            for (var i = 0; i < items.Count; i++)
            {
                select.AddItem(items[i]);
                if (items[i] == current)
                    select.Selected = i;
            }

            var box = new VBoxContainer();
            var hint = new Label
            {
                Text = TranslationServer.Translate(items.Count == 0 ? "mod_manager/entry_none" : "mod_manager/entry_text"),
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
                CustomMinimumSize = new Vector2(420, 0),
            };
            box.AddChild(hint);
            box.AddChild(select);

            var dialog = new AcceptDialog
            {
                Title = TranslationServer.Translate("mod_manager/entry_title"),
                OkButtonText = TranslationServer.Translate("mod_manager/entry_apply"),
                Exclusive = true,
            };
            dialog.AddChild(box);
            dialog.Confirmed += () =>
            {
                apply(items.Count == 0 || select.Selected < 0 ? null : items[select.Selected]);
            };
            return dialog;
        }
    }
}
