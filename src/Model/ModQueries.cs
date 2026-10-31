using System.Collections.Generic;
using Cthangover.Core.Mods;

namespace Mods.ModManager
{
    /// <summary>
    /// Read-only cross-mod lookups used by the detail page: direct dependencies,
    /// reverse dependencies (which mods depend on this one) and declared
    /// conflicts. Prefers local manifest data and falls back to the catalog entry
    /// for mods that are not installed.
    /// </summary>
    public static class ModQueries
    {
        /// <summary>Dependency strings (<c>core&gt;=1</c> format) of a mod, whatever source knows it.</summary>
        public static List<string> GetDependencies(ModCardData data)
        {
            var manifestDeps = data?.LocalInfo?.Manifest?.DependencyIds;
            if (manifestDeps != null && manifestDeps.Count > 0)
                return manifestDeps;

            return data?.Catalog?.Depends ?? new List<string>();
        }

        /// <summary>Ids of installed mods whose manifests depend on <paramref name="modId"/>.</summary>
        public static List<string> GetDependents(string modId)
        {
            var result = new List<string>();
            if (string.IsNullOrEmpty(modId))
                return result;

            var allMods = ModRegistry.Instance.AllDiscoveredMods;
            if (allMods == null)
                return result;

            foreach (var pair in allMods)
            {
                if (pair.Key == modId || pair.Value is not ModInfo info)
                    continue;

                var deps = info.Manifest?.DependencyIds;
                if (deps != null && deps.Contains(modId))
                    result.Add(pair.Key);
            }

            return result;
        }

        /// <summary>
        /// The host's integrations (<see cref="ModManifest.Integrations"/>) for its card, one line each: the
        /// partner mods, followed by <paramref name="activeMark"/> when the integration is loaded now. An
        /// integration has no card of its own — the player sees it here, on the mod whose feature it is.
        /// </summary>
        public static List<string> GetIntegrations(ModCardData data, string activeMark)
        {
            var result = new List<string>();
            var host = data?.LocalInfo;
            foreach (var integration in host?.Manifest?.Integrations ?? new List<ModIntegration>())
            {
                if (integration?.With == null || integration.With.Count == 0)
                    continue;
                var line = string.Join(", ", integration.With);
                if (ModRegistry.Instance.GetMod(ModTestSuites.ModIdOf(host.Id, integration.Id)) != null)
                    line += " — " + activeMark;
                result.Add(line);
            }
            return result;
        }

        /// <summary>Declared mutually exclusive mod ids (manifest first, catalog fallback).</summary>
        public static List<string> GetConflicts(ModCardData data)
        {
            var manifestConflicts = data?.LocalInfo?.Manifest?.ConflictsWith;
            if (manifestConflicts != null && manifestConflicts.Count > 0)
                return manifestConflicts;

            return data?.Catalog?.ConflictsWith ?? new List<string>();
        }
    }
}
