namespace Mods.ModManager.Tests
{
    using Cthangover.Core.Mods;
    using Cthangover.Core.Mods.Testing;
    using Cthangover.Core.Scenes;
    using Godot;

    /// <summary>Local art follows the installed version; catalog-only art follows the selected download.</summary>
    public class ModCardDataTests
    {
        [ModTest("cards/development_version_selection")]
        static void DevelopmentVersionSelection(ModTestContext ctx)
        {
            var card = new ModCardData { LocalVersion = "0.1.0" };
            ModAssert.True(card.IsInDevelopment, "installed development mod");
            card.SelectedDownload = new CatalogEntryDownload { Version = "1.0.0" };
            ModAssert.True(card.IsInDevelopment, "published release cannot hide the installed development frame");
            card.LocalVersion = null;
            ModAssert.False(card.IsInDevelopment, "catalog-only release has no frame");
            card.SelectedDownload = new CatalogEntryDownload { Version = "alpha.2" };
            ModAssert.True(card.IsInDevelopment, "selected lettered major shows frame");
            card.LocalVersion = null;
            card.SelectedDownload = null;
            card.Catalog = new CatalogExportEntry { Version = "0.9.0" };
            ModAssert.True(card.IsInDevelopment, "catalog-only development mod");
            card.Catalog.Version = "2.0.0-beta";
            ModAssert.False(card.IsInDevelopment, "letters outside the major do not mark development");
            card.Catalog.Version = null;
            ModAssert.False(card.IsInDevelopment, "unknown version has no development badge");
        }

        [ModTest("cards/development_views_refresh")]
        static void DevelopmentViewsRefresh(ModTestContext ctx)
        {
            var card = (ModCard)TscnScenes.LoadAndBuild("scenes/components/mod_card.tscn");
            var detail = (ModDetailView)TscnScenes.LoadAndBuild("scenes/components/mod_detail_view.tscn");
            try
            {
                var data = new ModCardData { Id = "development_preview", Title = "Preview", LocalVersion = "0.1.0" };
                card.Init(data);
                detail.Open(data);
                var tileFrame = card.GetNode<TextureRect>("VBox/Cover/DevelopmentFrame");
                var detailFrame = detail.GetNode<TextureRect>("Scroll/Content/Hero/HeroMargin/HeroRow/Cover/DevelopmentFrame");
                ModAssert.True(tileFrame.Visible && detailFrame.Visible, "local development is framed in both views");
                ModAssert.True(tileFrame.Texture != null && detailFrame.Texture != null, "both views resolve the generated art");
                data.SelectedDownload = new CatalogEntryDownload { Version = "1.0.0" };
                card.RefreshState();
                detail.Open(data);
                ModAssert.True(tileFrame.Visible && detailFrame.Visible, "published release does not hide local development");
                data.LocalVersion = null;
                card.RefreshState();
                detail.Open(data);
                ModAssert.False(tileFrame.Visible || detailFrame.Visible, "selecting a release hides both frames");
                data.SelectedDownload.Version = "alpha.1";
                card.RefreshState();
                detail.Open(data);
                ModAssert.True(tileFrame.Visible && detailFrame.Visible, "selecting a lettered major restores both frames");
            }
            finally
            {
                card.Free();
                detail.Free();
            }
        }
    }
}
