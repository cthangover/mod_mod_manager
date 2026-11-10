using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Mods.ModManager
{
    /// <summary>Ephemeral server-verified identity and scopes. Masks grant sections; only IsAdmin grants administrative ownership.</summary>
    public sealed class PublishingPermissions
    {
        [JsonPropertyName("author_id")]
        public string AuthorId { get; set; }
        [JsonPropertyName("author")]
        public string Author { get; set; }
        [JsonPropertyName("sections")]
        public List<string> Sections { get; set; }
        [JsonPropertyName("section_patterns")]
        public List<string> SectionPatterns { get; set; }
        [JsonPropertyName("is_admin")]
        public bool IsAdmin { get; set; }
        [JsonPropertyName("max_archive_bytes")]
        public long MaxArchiveBytes { get; set; }

        /// <summary>Absent on servers that do not require a separate publication consent.</summary>
        [JsonPropertyName("author_consent")]
        public AuthorConsentPermissions AuthorConsent { get; set; }

        /// <summary>Whether the form may offer a concrete name beyond its current section list.</summary>
        [JsonIgnore]
        public bool AllowsCustomSection
        {
            get
            {
                if (IsAdmin) return true;
                if (SectionPatterns != null)
                    foreach (var pattern in SectionPatterns)
                        if (pattern != null && (pattern.Contains('*') || pattern.Contains('?'))) return true;
                return false;
            }
        }

        /// <summary>Client guidance only; the server repeats the authorization at upload and commit.</summary>
        public bool AllowsSection(string section)
        {
            if (!IsIdentifier(section, false)) return false;
            if (IsAdmin || Sections?.Contains(section) == true) return true;
            if (SectionPatterns != null)
                foreach (var pattern in SectionPatterns)
                    if (IsIdentifier(pattern, true) && Matches(pattern, section)) return true;
            return false;
        }

        private static bool IsIdentifier(string value, bool mask)
        {
            if (string.IsNullOrEmpty(value) || value.Length > 64) return false;
            // Same rule as the server: an ID never starts with '.', '-' or '_'.
            if (!(value[0] is >= 'a' and <= 'z' or >= '0' and <= '9' || mask && (value[0] == '*' || value[0] == '?'))) return false;
            foreach (var ch in value)
                if (!(ch is >= 'a' and <= 'z' or >= '0' and <= '9' or '_' or '.' or '-'
                    || mask && (ch == '*' || ch == '?'))) return false;
            return true;
        }

        private static bool Matches(string pattern, string value)
        {
            var p = 0; var v = 0; var star = -1; var retry = 0;
            while (v < value.Length)
            {
                if (p < pattern.Length && (pattern[p] == '?' || pattern[p] == value[v])) { p++; v++; continue; }
                if (p < pattern.Length && pattern[p] == '*') { star = p++; retry = v; continue; }
                if (star < 0) return false;
                v = ++retry; p = star + 1;
            }
            while (p < pattern.Length && pattern[p] == '*') p++;
            return p == pattern.Length;
        }
    }
}
