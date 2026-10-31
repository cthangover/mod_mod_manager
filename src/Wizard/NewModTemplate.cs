namespace Mods.ModManager
{
    using System;
    using System.Linq;
    using System.Text.Json;
    using System.Text.Json.Nodes;
    using System.Text.RegularExpressions;

    /// <summary>
    /// The text the new-mod wizard writes: the manifest of a mod that depends on one base mod, and the locale stubs
    /// with the mod's two titles (<c>mod_&lt;id&gt;_name</c> and <c>_desc</c>). Pure rules without Godot or files, so
    /// the wizard's output is pinned by unit tests. The wizard creates a folder of plain files: no repository, no
    /// <c>.git</c>, and it never reads a <c>.git/config</c> (the template's credentials stay where they are).
    /// </summary>
    public static class NewModTemplate
    {
        /// <summary>Folders a new mod starts with; the editors create their own files in them.</summary>
        public static readonly string[] Folders = { "locale", "scenarios", "scenes", "src" };

        /// <summary>The base mods a novice can start from, with their dependency strings.</summary>
        public static readonly string[] Bases = { "rpg>=1", "dialog_base>=1" };

        private static readonly Regex IdPattern = new("^[a-z][a-z0-9_]{2,39}$", RegexOptions.CultureInvariant);

        /// <summary>
        /// True for a mod id the rules allow: lowercase <c>snake_case</c>, 3 to 40 characters, a letter first, and
        /// neither <c>core</c> nor a <c>base</c> word in it (AGENTS: "core" is the C# kernel, <c>base</c> is a suffix only).
        /// </summary>
        public static bool IsValidId(string id)
        {
            if (string.IsNullOrEmpty(id) || !IdPattern.IsMatch(id))
                return false;
            var words = id.Split('_');
            return !words.Contains("core") && !words.Contains("base") && !id.Contains("__");
        }

        /// <summary>A starting id for a typed name: lowercase Latin words joined with _, or <c>my_mod</c> when nothing is left.</summary>
        public static string SuggestId(string name)
        {
            var words = Regex.Replace((name ?? string.Empty).ToLowerInvariant(), "[^a-z0-9]+", "_").Trim('_');
            if (words.Length < 3 || !char.IsLetter(words[0]))
                return "my_mod";
            return words.Length > 40 ? words.Substring(0, 40).TrimEnd('_') : words;
        }

        /// <summary>The manifest text: id, title, description, version 0.1.0 and one dependency; LF line ends, a final newline.</summary>
        public static string Manifest(string id, string name, string description, string dependency)
        {
            var root = new JsonObject
            {
                ["id"] = id,
                ["name"] = Line(name),
                ["version"] = "0.1.0",
                ["author"] = "player",
                ["description"] = Line(description),
                ["category"] = "story",
                ["sources"] = new JsonArray(JsonValue.Create("src/**/*.cs")),
                ["depends"] = new JsonArray(JsonValue.Create(dependency)),
            };
            // The default encoder writes '>' and '&' as \u escapes; a manifest is read by people, so they stay as typed.
            var text = root.ToJsonString(new JsonSerializerOptions { WriteIndented = true })
                .Replace("\\u003E", ">").Replace("\\u003C", "<").Replace("\\u0026", "&");
            return text.Replace("\r\n", "\n") + "\n";
        }

        /// <summary>
        /// The properties text of one language: the mod's two titles. The wizard writes the English file and the
        /// file of the player's language, so the translation window has the gaps to fill next.
        /// </summary>
        public static string Locale(string id, string name, string description)
        {
            return "# Titles of the mod: shown by the mod manager.\n"
                   + $"mod_{id}_name = {Line(name)}\n"
                   + $"mod_{id}_desc = {Line(description)}\n";
        }

        /// <summary>A typed name is usable when it has text and fits one line of a manifest or a properties file.</summary>
        public static bool IsValidName(string name)
        {
            var trimmed = (name ?? string.Empty).Trim();
            return trimmed.Length > 0 && trimmed.Length <= 80;
        }

        /// <summary>Line text for a manifest value or a properties value: no line breaks, trimmed.</summary>
        public static string Line(string text) => (text ?? string.Empty).Replace("\r", " ").Replace("\n", " ").Trim();
    }
}
