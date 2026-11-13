using System;
using Cthangover.Core.Mods;
using Cthangover.Core.Utils;
using Godot;

namespace Mods.ModManager.PrivateRealModsTest
{
    /// <summary>Only the isolated real-server harness mounts this GUI driver.</summary>
    public sealed class PrivateRealModsInitializer : IModInitializer
    {
        private static bool queued;
        public void OnModLoaded(string modId) { }
        public void OnModResourcesReady()
        {
            if (!GameEnvironment.IsRuntime || queued || Array.IndexOf(OS.GetCmdlineUserArgs(), "--itest=private_real_mods") < 0)
                return;
            queued = true;
            if (System.Environment.GetEnvironmentVariable("CTHANGOVER_E2E_PHASE") == "publish_collection")
                ModRoot.AddDeferred(new CollectionPublishDriver());
            else ModRoot.AddDeferred(new PrivateRealModsDriver());
        }
    }
}
