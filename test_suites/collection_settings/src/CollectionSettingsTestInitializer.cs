namespace Mods.ModManager.SettingsTest
{
    using System.Linq;
    using Cthangover.Core.Mods;
    using Godot;

    /// <summary>Explicit frontend-only UI test, leaving game assemblies unloaded.</summary>
    public class CollectionSettingsTestInitializer : IModInitializer
    {
        private static bool queued;
        public void OnModLoaded(string modId) { }
        public void OnModResourcesReady()
        {
            // Only its own flag: --with-test-suites (the atlases) mounts every suite, and this driver quits the process.
            if (queued || !OS.GetCmdlineUserArgs().Contains("--itest=collection_settings"))
                return;
            queued = true;
            ModRoot.AddDeferred(new CollectionSettingsTestDriver());
        }
    }
}
