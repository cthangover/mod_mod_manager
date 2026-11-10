using System;
using Cthangover.Core.Localization;

namespace Mods.ModManager
{
    /// <summary>
    /// Picks the bundled author-consent document for the reader's language. The Russian text is
    /// the binding original; the consent_author_{Version}_{language}.txt resources are convenience
    /// translations shown when they exist, so a reader reads the consent in their own language
    /// whenever one is bundled. The choice is by file, not by Godot translation, because the
    /// document is legal text that must be shown verbatim.
    /// </summary>
    public static class AuthorConsentDocument
    {
        /// <summary>The consent revision bundled with the mod; the resource names and the server contract share it.</summary>
        public const string Version = "2026-11-09";

        /// <summary>The binding Russian document, also the fallback for languages without a translation.</summary>
        public const string RussianPath = "legal/consent_author_" + Version + ".txt";

        /// <summary>
        /// Resolves what to display: the translation for the UI language when the mod bundles it,
        /// otherwise the Russian original. <paramref name="native"/> reports whether the shown
        /// document is written in the reader's own language, which selects the consent-language note.
        /// </summary>
        public static void Resolve(string uiLocale, Func<string, bool> fileExists, out string path, out bool native)
        {
            var language = LanguageCode.Normalize(uiLocale);
            if (language == null || language == "ru")
            {
                path = RussianPath;
                native = language == "ru";
                return;
            }
            var translated = "legal/consent_author_" + Version + "_" + language + ".txt";
            if (fileExists(translated))
            {
                path = translated;
                native = true;
                return;
            }
            path = RussianPath;
            native = false;
        }
    }
}
