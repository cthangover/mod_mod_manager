namespace Mods.ModManager
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text.Json;
    using System.Text.RegularExpressions;
    using Cthangover.Core.Localization;
    using Cthangover.Core.Mods;
    using Cthangover.Core.Utils;

    /// <summary>
    /// Reads declarations, translations and resource choices from discovered folder/ZIP mods without loading
    /// assemblies. Callers supply dependency order and the edited collection's available mod ids, never the
    /// currently running game's registries. No static cache retains providers across reloads.
    /// </summary>
    public class CollectionSettingsCatalog
    {
        private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower, PropertyNameCaseInsensitive = true };
        private readonly Dictionary<string, string> translations = new(StringComparer.Ordinal);
        private readonly Dictionary<string, CollectionSettingDefinition> definitions = new(StringComparer.Ordinal);
        private readonly List<CollectionSettingOption> contributions = new();
        private readonly List<IModInfo> mods;
        private readonly HashSet<string> available;

        public IReadOnlyCollection<CollectionSettingDefinition> Definitions => definitions.Values;
        public List<string> Errors { get; } = new();

        /// <summary>Later mods override resource ids and translations, matching the supplied dependency order.</summary>
        public CollectionSettingsCatalog(IEnumerable<IModInfo> orderedMods, IEnumerable<string> availableModIds, string language)
        {
            mods = orderedMods.ToList();
            available = new HashSet<string>(availableModIds, StringComparer.OrdinalIgnoreCase);
            // Read inactive owners first, so their fields still explain saved values after removal.
            foreach (var mod in mods.OrderBy(m => available.Contains(m.Id)))
                ReadDeclarations(mod);
            foreach (var mod in mods)
                ReadTranslations(mod.FileProvider, "en");
            foreach (var mod in mods)
                ReadTranslations(mod.FileProvider, LanguageCode.Primary(language));
        }

        /// <summary>Mod-local locale files are read even in frontend-only mode, with an English fallback.</summary>
        public string Translate(string key) => key != null && translations.TryGetValue(key.TrimStart('@'), out var text) ? text : key ?? string.Empty;

        /// <summary>Choices from declarations plus the selected collection's resources; unavailable contributions stay visible.</summary>
        public IReadOnlyList<CollectionSettingOption> Choices(CollectionSettingDefinition definition)
        {
            var result = new Dictionary<string, CollectionSettingOption>(StringComparer.Ordinal);
            foreach (var option in definition.Options ?? new List<CollectionSettingOption>())
            {
                option.ModId = definition.ModId;
                option.Available = definition.Available;
                if (!string.IsNullOrEmpty(option.Value))
                    result[option.Value] = option;
            }
            foreach (var option in contributions.Where(o => o.Key == definition.Key).OrderBy(o => o.Available))
                result[option.Value] = option;
            if (definition.Type is "resource" or "character" && !string.IsNullOrEmpty(definition.Source))
                foreach (var mod in mods.Where(m => available.Contains(m.Id)))
                    ReadChoices(mod, definition, result);
            return result.Values.OrderBy(o => o.Value, StringComparer.Ordinal).ToList();
        }

        /// <summary>Unknown/unavailable saved values may be retained; newly typed values must obey the owner's syntax.</summary>
        public bool IsValidCustom(CollectionSettingDefinition definition, string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;
            if (string.IsNullOrEmpty(definition.Pattern))
                return true;
            try { return Regex.IsMatch(value, definition.Pattern, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)); }
            catch (ArgumentException) { return false; }
            catch (RegexMatchTimeoutException) { return false; }
        }

        private void ReadDeclarations(IModInfo mod)
        {
            try
            {
                var text = mod.FileProvider?.ReadFileText("collection_settings.json");
                if (string.IsNullOrWhiteSpace(text))
                    return;
                var file = JsonSerializer.Deserialize<CollectionSettingsFile>(text, Json);
                foreach (var definition in file?.Settings ?? new List<CollectionSettingDefinition>())
                {
                    if (definition == null || string.IsNullOrWhiteSpace(definition.Key))
                        continue;
                    definition.ModId = mod.Id;
                    definition.Available = available.Contains(mod.Id);
                    definitions[definition.Key] = definition;
                }
                foreach (var option in file?.Options ?? new List<CollectionSettingOption>())
                {
                    if (option == null || string.IsNullOrEmpty(option.Key) || string.IsNullOrEmpty(option.Value))
                        continue;
                    option.ModId = mod.Id;
                    option.Available = available.Contains(mod.Id);
                    contributions.Add(option);
                }
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
            {
                Errors.Add(mod.Id);
                GameLogger.Log("MOD_COLLECTIONS", $"Invalid collection settings in '{mod.Id}': {ex.Message}", LogLevel.Warning);
            }
        }

        private void ReadTranslations(IModFileProvider provider, string language)
        {
            if (provider == null || string.IsNullOrEmpty(language))
                return;
            foreach (var path in provider.ListFiles("locale").Order(StringComparer.Ordinal))
            {
                var name = Path.GetFileName(path);
                if (name != language + ".properties" && !name.EndsWith("_" + language + ".properties", StringComparison.Ordinal))
                    continue;
                foreach (var line in (provider.ReadFileText(path) ?? "").Split('\n'))
                {
                    var trimmed = line.Trim();
                    var split = trimmed.IndexOf('=');
                    if (!trimmed.StartsWith('#') && split > 0)
                        translations[trimmed[..split].Trim()] = trimmed[(split + 1)..].Trim();
                }
            }
        }

        private void ReadChoices(IModInfo mod, CollectionSettingDefinition definition, Dictionary<string, CollectionSettingOption> result)
        {
            foreach (var path in Walk(mod.FileProvider, definition.Source))
            {
                var extension = Path.GetExtension(path).ToLowerInvariant();
                if (definition.Type == "resource" && extension is ".png" or ".jpg" or ".jpeg" or ".webp")
                {
                    var value = path[(definition.Source.Length + 1)..^extension.Length];
                    result[value] = new CollectionSettingOption { Value = value, Name = value, ModId = mod.Id, Available = true, Provider = mod.FileProvider, PreviewPath = path };
                }
                else if (definition.Type == "character" && extension == ".json")
                    ReadCharacters(mod, path, result);
            }
        }

        private void ReadCharacters(IModInfo mod, string path, Dictionary<string, CollectionSettingOption> result)
        {
            try
            {
                using var document = JsonDocument.Parse(mod.FileProvider.ReadFileText(path));
                var root = document.RootElement;
                if (!root.TryGetProperty("Kind", out var kind) || kind.GetString() != "character"
                    || !root.TryGetProperty("Items", out var items) || items.ValueKind != JsonValueKind.Array)
                    return;
                foreach (var item in items.EnumerateArray())
                {
                    if (!item.TryGetProperty("Id", out var id) || string.IsNullOrEmpty(id.GetString()))
                        continue;
                    var value = id.GetString();
                    var name = item.TryGetProperty("Name", out var title) ? title.GetString() : value;
                    result[value] = new CollectionSettingOption { Value = value, Name = name, ModId = mod.Id, Available = true };
                }
            }
            catch (JsonException ex)
            {
                GameLogger.Log("MOD_COLLECTIONS", $"Cannot index '{mod.Id}/{path}': {ex.Message}", LogLevel.Warning);
            }
        }

        private static IEnumerable<string> Walk(IModFileProvider provider, string folder)
        {
            foreach (var path in provider.ListFiles(folder).Order(StringComparer.Ordinal))
            {
                if (path.EndsWith('/'))
                {
                    foreach (var child in Walk(provider, path.TrimEnd('/')))
                        yield return child;
                }
                else
                    yield return path;
            }
        }
    }
}
