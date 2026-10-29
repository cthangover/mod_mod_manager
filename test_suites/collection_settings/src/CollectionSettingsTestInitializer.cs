namespace Mods.ModManager.SettingsTest
{
    using Cthangover.Core.Mods;

    /// <summary>Explicit frontend-only UI test, leaving game assemblies unloaded.</summary>
    public class CollectionSettingsTestInitializer : IModInitializer
    {
        private static bool queued;
        public void OnModLoaded(string modId) { }
        public void OnModResourcesReady()
        {
            if (queued)
                return;
            queued = true;
            ModRoot.AddDeferred(new CollectionSettingsTestDriver());
        }
    }
}
