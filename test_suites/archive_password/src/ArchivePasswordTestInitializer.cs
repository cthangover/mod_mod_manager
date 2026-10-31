using System.Linq;
using Cthangover.Core.Mods;
using Cthangover.Core.Utils;
using Godot;

namespace Mods.ModManager.ArchivePasswordTest
{
    /// <summary>Runs only when explicitly selected, never during normal boot or atlas generation.</summary>
    public class ArchivePasswordTestInitializer : IModInitializer
    {
        private static bool queued;
        public void OnModLoaded(string modId) { }
        public void OnModResourcesReady()
        {
            if (!GameEnvironment.IsRuntime || queued || !OS.GetCmdlineUserArgs().Contains("--itest=archive_password"))
                return;
            queued = true;
            ModRoot.AddDeferred(new ArchivePasswordTestDriver());
        }
    }
}
