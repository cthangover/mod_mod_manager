namespace Mods.ModManager
{
    using System.Collections.Generic;

    /// <summary>collection_settings.json: owners declare fields, implementation mods contribute choices.</summary>
    public class CollectionSettingsFile
    {
        public List<CollectionSettingDefinition> Settings { get; set; } = new();
        public List<CollectionSettingOption> Options { get; set; } = new();
    }
}
