namespace Mods.ModManager
{
    using System;
    using System.Text.Json;

    /// <summary>
    /// Reads the logical scene name of a <c>scenes/*.json</c> file: the <c>name</c> field, which is the name a game
    /// switches to and starts from. Null for a file that is not a scene definition.
    /// </summary>
    public static class SceneNameReader
    {
        /// <summary>The scene's name, or null when the text is not JSON with a non-empty <c>name</c>.</summary>
        public static string Read(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return null;
            try
            {
                using var document = JsonDocument.Parse(json);
                if (document.RootElement.ValueKind == JsonValueKind.Object
                    && document.RootElement.TryGetProperty("name", out var name)
                    && name.ValueKind == JsonValueKind.String
                    && !string.IsNullOrWhiteSpace(name.GetString()))
                    return name.GetString().Trim();
            }
            catch (JsonException)
            {
                return null;
            }
            return null;
        }
    }
}
