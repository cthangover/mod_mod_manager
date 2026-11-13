using System;
using Cthangover.Core.Mods;
using Cthangover.Core.Utils;
using Godot;

namespace Mods.ModManager.PublishGui
{
    /// <summary>Mounts the publishing UI fixture only through the real-server harness.</summary>
    public sealed class PublishGuiInitializer : IModInitializer
    {
        private static bool queued;
        public void OnModLoaded(string modId) { }
        public void OnModResourcesReady()
        {
            if (!GameEnvironment.IsRuntime || queued || Array.IndexOf(OS.GetCmdlineUserArgs(), "--itest=publish_gui") < 0)
                return;
            queued = true;
            ModRoot.AddDeferred(new PublishGuiDriver());
        }
    }
}
