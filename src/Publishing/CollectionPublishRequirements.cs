using System.Collections.Generic;
using System.Linq;
using Cthangover.Core.Mods;

namespace Mods.ModManager
{
    /// <summary>Lists direct author intent and the resolver's transitive missing dependencies without blocking publication.</summary>
    public static class CollectionPublishRequirements
    {
        public static string Describe(ModCollection collection)
        {
            var lines = new List<string>();
            foreach (var mechanic in collection.Mechanics ?? new())
                lines.AddRange(mechanic.Mods ?? new());
            lines.AddRange((collection.Includes ?? new()).Select(include => include.Ref));
            var index = ModCollectionStore.LoadAll().ToDictionary(c => c.Id, System.StringComparer.OrdinalIgnoreCase);
            var graph = ModCollectionResolver.Resolve(collection, index, ModRegistry.Instance.AllDiscoveredMods,
                DisabledModsStore.Load(), CatalogService.CachedEntries ?? new(), null);
            var missing = graph.Nodes.Where(n => n.State == ModCollectionNodeState.Missing).Select(n => n.ModId).ToList();
            foreach (var include in collection.Includes ?? new())
                if (!index.ContainsKey(ModDependency.Parse(include.Ref).ModId)) missing.Add(include.Ref);
            var text = string.Join(", ", lines);
            return missing.Count == 0 ? text : text + "\n" + string.Format(Godot.TranslationServer.Translate("mod_manager/publish_missing_warning"), string.Join(", ", missing));
        }
    }
}
