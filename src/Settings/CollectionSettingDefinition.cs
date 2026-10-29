namespace Mods.ModManager
{
    using System.Collections.Generic;
    using System.Text.Json.Serialization;

    /// <summary>A mod-owned collection parameter, readable before that mod's assembly is loaded.</summary>
    public class CollectionSettingDefinition
    {
        public string Key { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public string Group { get; set; }
        /// <summary>choice, resource or character. Resource/character sources name a mod-relative folder.</summary>
        public string Type { get; set; } = "choice";
        public string Source { get; set; }
        public bool AllowCustom { get; set; }
        /// <summary>Explanation of the runtime fallback, not a value to persist.</summary>
        public string Automatic { get; set; }
        /// <summary>Optional anchored validation expression for custom input.</summary>
        public string Pattern { get; set; }
        public List<CollectionSettingOption> Options { get; set; } = new();
        [JsonIgnore] public string ModId { get; set; }
        [JsonIgnore] public bool Available { get; set; }
    }
}
