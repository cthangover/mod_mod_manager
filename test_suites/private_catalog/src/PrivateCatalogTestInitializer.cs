using System.Linq;
using Cthangover.Core.Mods;
using Godot;

namespace Mods.ModManager.PrivateCatalogTest
{
    /// <summary>Requires a running local keyserver fixture; activated explicitly by the host integration script.</summary>
    public class PrivateCatalogTestInitializer : IModInitializer
    {
        private static bool queued;
        public void OnModLoaded(string modId) { }
        public void OnModResourcesReady()
        {
            if (queued || !OS.GetCmdlineUserArgs().Contains("--itest=private_catalog"))
                return;
            queued = true;
            ModRoot.AddDeferred(new PrivateCatalogTestDriver());
        }
    }
}
