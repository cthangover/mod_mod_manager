using Cthangover.Core.Mods;

namespace Mods.ModManager.DocumentationTest
{
    /// <summary>Mounting this suite explicitly enables the documentation UI walk-through.</summary>
    public class DocumentationTestInitializer : IModInitializer
    {
        private static bool queued;
        public void OnModLoaded(string modId) { }
        public void OnModResourcesReady()
        {
            if (queued)
                return;
            queued = true;
            ModRoot.AddDeferred(new DocumentationTestDriver());
        }
    }
}
