using System;
using Cthangover.Core.Mods;
using Cthangover.Core.Utils;
using Godot;

namespace Mods.ModManager.PublishingTest
{
    /// <summary>Mounts the publishing UI fixture only through the real-server harness.</summary>
    public sealed class PublishingTestInitializer : IModInitializer
    {
        private static bool queued;
        public void OnModLoaded(string modId) { }
        public void OnModResourcesReady()
        {
            if (!GameEnvironment.IsRuntime || queued || Array.IndexOf(OS.GetCmdlineUserArgs(), "--itest=publishing") < 0)
                return;
            queued = true;
            ModRoot.AddDeferred(new PublishingTestDriver());
        }
    }
}
