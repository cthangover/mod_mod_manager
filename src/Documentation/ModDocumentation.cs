using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Cthangover.Core.Localization;
using Cthangover.Core.Mods;
using Cthangover.Core.Utils;

namespace Mods.ModManager
{
    /// <summary>
    /// Reads documentation independently of mod activation and storage format.
    /// Only language-named files and default.md directly under docs participate
    /// in fallback; README/CHANGELOG and nested assets never become translations.
    /// </summary>
    public static class ModDocumentation
    {
        /// <summary>Finds valid documents in stable path order; broken files do not hide other translations.</summary>
        public static IReadOnlyList<ModDocument> Discover(IModFileProvider provider)
        {
            var result = new List<ModDocument>();
            if (provider == null)
                return result;
            try
            {
                foreach (var path in provider.ListFiles("docs").Order(StringComparer.Ordinal))
                {
                    if (!path.EndsWith(".md", StringComparison.Ordinal) || path.EndsWith("/"))
                        continue;
                    var language = System.IO.Path.GetFileNameWithoutExtension(path);
                    if (!IsLanguage(language))
                        continue;
                    var bytes = ReadResource(provider, path, 512 * 1024);
                    if (bytes == null)
                        continue;
                    try
                    {
                        var text = new UTF8Encoding(false, true).GetString(bytes).TrimStart('\uFEFF');
                        if (!string.IsNullOrWhiteSpace(text))
                            result.Add(new ModDocument(language, path, text));
                    }
                    catch (DecoderFallbackException)
                    {
                        GameLogger.Log("MODS", $"Documentation '{provider.Mod}/{path}' is not UTF-8", LogLevel.Warning);
                    }
                    if (result.Count >= 32)
                        break;
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is InvalidDataException || ex is ObjectDisposedException)
            {
                GameLogger.Log("MODS", $"Cannot list documentation for '{provider.Mod}': {ex.Message}", LogLevel.Warning);
            }
            return result;
        }

        /// <summary>Requested tag, primary language, default.md, English, then stable first document.</summary>
        public static ModDocument Select(IReadOnlyList<ModDocument> documents, string language)
        {
            if (documents == null)
                return null;
            var tag = language?.Replace('_', '-');
            foreach (var candidate in new[] { tag, LanguageCode.Primary(tag), "default", "en" })
                foreach (var document in documents)
                    if (string.Equals(document.Language, candidate, StringComparison.OrdinalIgnoreCase))
                        return document;
            return documents.FirstOrDefault();
        }

        /// <summary>Resolves local media relative to a document, rejecting schemes, absolute paths and escapes from docs.</summary>
        public static string ResolveResource(string documentPath, string reference)
        {
            if (string.IsNullOrWhiteSpace(reference) || reference.StartsWith('/') || reference.StartsWith('\\')
                || reference.Contains(':') || reference.Contains('?') || reference.Contains('#'))
                return null;
            var path = ModPath.ResolveRelative(reference, documentPath);
            return path.StartsWith("docs/", StringComparison.Ordinal) ? path : null;
        }

        /// <summary>
        /// Bounded stream reads work for compressed entries too. Folder symlinks
        /// are rejected along the resource path before delegating to the provider.
        /// </summary>
        public static byte[] ReadResource(IModFileProvider provider, string path, int limit)
        {
            if (provider == null || string.IsNullOrEmpty(path) || !path.StartsWith("docs/", StringComparison.Ordinal) || ModPath.Normalize(path) != path)
                return null;
            try
            {
                if (provider.RootPath != null && Directory.Exists(provider.RootPath))
                {
                    var current = provider.RootPath;
                    foreach (var segment in path.Split('/'))
                    {
                        current = System.IO.Path.Combine(current, segment);
                        if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                            return null;
                    }
                }
                using var stream = provider.OpenStream(path);
                if (stream == null)
                    return null;
                using var output = new MemoryStream();
                var buffer = new byte[16384];
                int read;
                while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
                {
                    if (output.Length + read > limit)
                        return null;
                    output.Write(buffer, 0, read);
                }
                return output.ToArray();
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is InvalidDataException || ex is ObjectDisposedException)
            {
                GameLogger.Log("MODS", $"Cannot read documentation resource '{provider.Mod}/{path}': {ex.Message}", LogLevel.Warning);
                return null;
            }
        }

        private static bool IsLanguage(string code)
        {
            if (code == "default")
                return true;
            var parts = code.Split('-');
            if (parts[0].Length < 2 || parts[0].Length > 3 || parts.Length > 3)
                return false;
            foreach (var c in parts[0])
                if (!(c >= 'a' && c <= 'z') && !(c >= 'A' && c <= 'Z'))
                    return false;
            foreach (var part in parts)
            {
                if (part.Length == 0 || part.Length > 8)
                    return false;
                foreach (var c in part)
                    if (!(c >= 'a' && c <= 'z') && !(c >= 'A' && c <= 'Z') && !(c >= '0' && c <= '9'))
                        return false;
            }
            return true;
        }
    }
}
