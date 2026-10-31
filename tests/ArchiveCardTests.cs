namespace Mods.ModManager.Tests
{
    using Cthangover.Core.Mods;
    using Cthangover.Core.Mods.Testing;

    /// <summary>Locked packages have their own path identity and offer password entry instead of delete/enable.</summary>
    public class ArchiveCardTests
    {
        [ModTest("archives/locked_card")]
        static void LockedCard(ModTestContext ctx)
        {
            var archive = new UnavailableModArchive(System.IO.Path.Combine(ctx.SandboxDir, "package.7z"), true);
            var card = new ModCardData { Id = "package", IsInstalled = true, UnavailableArchive = archive };
            card.RecomputeStatus();
            ModAssert.Equal((int)ModItemStatus.PasswordRequired, (int)card.Status, "locked status");
            ModAssert.Equal((int)ModActionKind.UnlockArchive, (int)ModActions.GetPrimary(card).Value, "password action");
            ModAssert.False(card.CanToggleEnabled, "locked packages cannot be enabled blindly");
            ModAssert.Equal("archive|" + archive.Path, card.MatchKey, "path-based identity before manifest access");
        }
    }
}
