namespace Mods.ModManager
{
    using System.Text.Json.Serialization;
    using Cthangover.Core.Mods;

    /// <summary>An explicit mod contribution or an indexed resource; availability belongs to the edited collection.</summary>
    public class CollectionSettingOption
    {
        public string Key { get; set; }
        public string Value { get; set; }
        public string Name { get; set; }
        [JsonIgnore] public string ModId { get; set; }
        [JsonIgnore] public bool Available { get; set; }
        [JsonIgnore] public IModFileProvider Provider { get; set; }
        [JsonIgnore] public string PreviewPath { get; set; }
    }
}
