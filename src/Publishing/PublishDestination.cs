using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Cthangover.Core.Mods;
using Cthangover.Core.Utils;

namespace Mods.ModManager
{
    /// <summary>Unambiguous source labels and remembered successful destinations, separated by entity kind.</summary>
    public static class PublishDestination
    {
        /// <summary>Ports and paths are part of the visible identity, even when display names are identical.</summary>
        public static string Label(CatalogServer server) => string.IsNullOrWhiteSpace(server.Name)
            ? server.Url : server.Name + " · " + server.Url;

        /// <summary>A previous successful destination wins; otherwise the sole enabled keyed source is preferred, and ambiguous destinations require a choice.</summary>
        public static int Select(IReadOnlyList<CatalogServer> servers, string previous)
        {
            for (var i = 0; i < servers.Count; i++) if (servers[i].Url == previous) return i;
            var candidate = -1;
            var candidates = 0;
            for (var i = 0; i < servers.Count; i++)
                if (servers[i].Enabled && !string.IsNullOrWhiteSpace(servers[i].TokenFile))
                {
                    candidate = i;
                    candidates++;
                }
            return candidates == 1 ? candidate : servers.Count == 1 ? 0 : -1;
        }

        private static string PathName => GameEnvironment.IsRuntime ? GamePaths.Globalize("user://published_destinations.json") : "published_destinations.json";

        private static Dictionary<string, string> Read()
        {
            try { return File.Exists(PathName) ? JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(PathName)) ?? new() : new(); }
            catch { return new(); }
        }

        /// <summary>Reads only the user's own completed publications, never assumes catalog visibility means ownership.</summary>
        public static string Previous(string kind, string id) => Read().GetValueOrDefault(kind + "/" + id);

        /// <summary>Persists after the server has acknowledged completion; remembers no bearer or temporary URLs.</summary>
        public static void Remember(string kind, string id, string url)
        {
            var values = Read(); values[kind + "/" + id] = url;
            var path = PathName;
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path)));
            var temporary = path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(values));
            File.Move(temporary, path, true);
        }
    }
}
