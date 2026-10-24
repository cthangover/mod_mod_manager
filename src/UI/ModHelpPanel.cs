namespace Mods.ModManager
{
    using Godot;
    /// <summary>
    /// Full-screen help overlay of the mods hub: one page per hub level — the mods
    /// grid, a mod's page, the games gallery and the scheme editor. The layout is the
    /// <c>mod_help_panel.tscn</c> prefab (dim backdrop, centred frame, title, body,
    /// close button); this class only binds the nodes and fills the texts, so a new
    /// page is a locale pair <c>mod_manager/help_&lt;level&gt;_title/_body</c> — no code.
    /// Opened by the hub header's help button, closed by the button, a click on the
    /// dim or Escape (the hub closes the panel first in its own Escape chain).
    /// </summary>
    public partial class ModHelpPanel : Cthangover.Mods.Interface.Base.Lists.ModWidget
    {
        private Label _title;
        private RichTextLabel _body;

        protected override void Construct()
        {
            MouseFilter = MouseFilterEnum.Stop;

            _title = GetNode<Label>("Center/Frame/Margin/Column/Header/Title");
            _body = GetNode<RichTextLabel>("Center/Frame/Margin/Column/Body");

            var close = GetNode<Button>("Center/Frame/Margin/Column/Header/Close");
            close.Text = TranslationServer.Translate("mod_manager/help_close");
            close.TooltipText = TranslationServer.Translate("mod_manager/help_close");
            close.Pressed += Close;

            var dim = GetNode<ColorRect>("Dim");
            dim.GuiInput += OnDimGuiInput;
        }

        /// <summary>Shows the page of one hub level; the level names match the test driver's: mods, mod_page, collections, scheme.</summary>
        public void Open(string level)
        {
            _title.Text = TranslationServer.Translate($"mod_manager/help_{level}_title");
            _body.Text = TranslationServer.Translate($"mod_manager/help_{level}_body");
            Visible = true;
        }

        /// <summary>Hides the overlay; the hub calls this from its Escape chain and the driver checks it.</summary>
        public void Close()
        {
            Visible = false;
        }

        private void OnDimGuiInput(InputEvent @event)
        {
            if (@event is InputEventMouseButton mouse && mouse.Pressed)
                Close();
        }
    }
}
