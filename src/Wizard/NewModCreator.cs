namespace Mods.ModManager
{
    using System;
    using System.IO;
    using System.Text;
    using Cthangover.Core.Mods;
    using Cthangover.Core.Utils;

    /// <summary>
    /// Writes a new mod's folder into the writable mods root: the manifest, the folders of
    /// <see cref="NewModTemplate.Folders"/> and the locale stubs (English and the player's language). It writes
    /// plain files only: no repository is created and no <c>.git</c> is read or copied. Joining a collection is the
    /// caller's job, so a refused id leaves every collection as it was.
    /// </summary>
    public static class NewModCreator
    {
        private const string LogCategory = "MOD_MANAGER";
        private static readonly UTF8Encoding Utf8 = new(false);

        /// <summary>The outcome of <see cref="Create"/>.</summary>
        public enum Outcome
        {
            Created,
            InvalidName,
            InvalidId,
            Exists,
            NoModsFolder,
            Failed,
        }

        /// <summary>
        /// Creates <c>&lt;modsRoot&gt;/&lt;id&gt;/</c>. <paramref name="path"/> is the new folder when the outcome is
        /// <see cref="Outcome.Created"/>, else null. <paramref name="language"/> is the player's language code.
        /// </summary>
        public static Outcome Create(string id, string name, string description, string dependency, string language, out string path)
        {
            path = null;
            if (!NewModTemplate.IsValidName(name))
                return Outcome.InvalidName;
            if (!NewModTemplate.IsValidId(id))
                return Outcome.InvalidId;

            var root = ModRoots.ModsPath;
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
                return Outcome.NoModsFolder;
            if (ModRegistry.Instance.AllDiscoveredMods.ContainsKey(id) || ModRegistry.Instance.GetMod(id) != null)
                return Outcome.Exists;

            var folder = Path.Combine(root, id);
            if (Directory.Exists(folder) || File.Exists(folder))
                return Outcome.Exists;

            try
            {
                Directory.CreateDirectory(folder);
                foreach (var sub in NewModTemplate.Folders)
                    Directory.CreateDirectory(Path.Combine(folder, sub));

                File.WriteAllText(Path.Combine(folder, "manifest.json"), NewModTemplate.Manifest(id, name, description, dependency), Utf8);
                var locale = Path.Combine(folder, "locale");
                var stub = NewModTemplate.Locale(id, name, description);
                File.WriteAllText(Path.Combine(locale, "en.properties"), stub, Utf8);
                if (!string.IsNullOrEmpty(language) && language != "en")
                    File.WriteAllText(Path.Combine(locale, language + ".properties"), stub, Utf8);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                GameLogger.Log(LogCategory, $"New mod '{id}' was not written: {ex.Message}", LogLevel.Error);
                return Outcome.Failed;
            }

            GameLogger.Log(LogCategory, $"New mod '{id}' written to '{folder}'");
            path = folder;
            return Outcome.Created;
        }
    }
}
