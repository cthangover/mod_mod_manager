using System;
using Cthangover.Core.Mods;
using Cthangover.Core.Utils;
using Godot;

namespace Mods.ModManager.CatalogServersTest
{
    /// <summary>Requires the sibling keyserver integration harness; never mounts during ordinary play.</summary>
    public sealed class CatalogServersTestInitializer : IModInitializer
    {
        private static bool queued;
        public void OnModLoaded(string modId) { }
        public void OnModResourcesReady()
        {
            if (!GameEnvironment.IsRuntime || queued || Array.IndexOf(OS.GetCmdlineUserArgs(), "--itest=catalog_servers") < 0)
                return;
            queued = true;
            ModRoot.AddDeferred(new CatalogServersTestDriver());
        }
    }
}
