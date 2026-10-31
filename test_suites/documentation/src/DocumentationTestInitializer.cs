namespace Mods.ModManager.DocumentationTest
{
    using System.Linq;
    using Cthangover.Core.Mods;
    using Godot;

    /// <summary>Mounting this suite explicitly enables the documentation UI walk-through.</summary>
    public class DocumentationTestInitializer : IModInitializer
    {
        private static bool queued;
        public void OnModLoaded(string modId) { }
        public void OnModResourcesReady()
        {
            // Only its own flag: --with-test-suites (the atlases) mounts every suite, and this driver quits the process.
            if (queued || !OS.GetCmdlineUserArgs().Contains("--itest=mod_documentation"))
                return;
            queued = true;
            ModRoot.AddDeferred(new DocumentationTestDriver());
        }
    }
}
