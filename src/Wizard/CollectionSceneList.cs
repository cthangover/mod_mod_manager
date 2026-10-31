namespace Mods.ModManager
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Cthangover.Core.Mods;

    /// <summary>
    /// The logical scenes a game's installed mods define: the <c>name</c> of every <c>scenes/**.json</c> file, read
    /// through each mod's file provider (folders, zips and test folders alike). These are the names a game can start
    /// in, so the start-scene picker offers only those.
    /// </summary>
    public static class CollectionSceneList
    {
        /// <summary>Sorted, distinct scene names of the given installed mods.</summary>
        public static List<string> Names(IEnumerable<string> modIds)
        {
            var names = new SortedSet<string>(StringComparer.Ordinal);
            var discovered = ModRegistry.Instance.AllDiscoveredMods;
            foreach (var id in modIds ?? Enumerable.Empty<string>())
            {
                if (!discovered.TryGetValue(id, out var info) || info?.FileProvider == null)
                    continue;

                var pending = new Stack<string>();
                pending.Push("scenes");
                while (pending.Count > 0)
                {
                    var directory = pending.Pop();
                    foreach (var entry in info.FileProvider.ListFiles(directory))
                    {
                        if (entry.EndsWith("/", StringComparison.Ordinal))
                            pending.Push(entry.TrimEnd('/'));
                        else if (entry.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                        {
                            var name = SceneNameReader.Read(info.FileProvider.ReadFileText(entry));
                            if (name != null)
                                names.Add(name);
                        }
                    }
                }
            }

            return names.ToList();
        }
    }
}
