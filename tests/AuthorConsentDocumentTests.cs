namespace Mods.ModManager.Tests
{
    using Cthangover.Core.Mods.Testing;

    /// <summary>Covers the language choice of the bundled author consent: a bundled translation wins,
    /// the Russian original is the binding fallback, and the native flag follows the document actually shown.</summary>
    public class AuthorConsentDocumentTests
    {
        [ModTest("publishing/consent_document_language")]
        static void ConsentDocumentLanguage(ModTestContext context)
        {
            AuthorConsentDocument.Resolve("ru", _ => false, out var path, out var native);
            ModAssert.Equal("legal/consent_author_2026-11-09.txt", path, "Russian always reads the original document");
            ModAssert.True(native, "the original is native for a Russian reader");

            AuthorConsentDocument.Resolve("en", _ => true, out path, out native);
            ModAssert.Equal("legal/consent_author_2026-11-09_en.txt", path, "a bundled translation wins for its language");
            ModAssert.True(native, "the translated document is native for its reader");

            AuthorConsentDocument.Resolve("en_US", _ => false, out path, out native);
            ModAssert.Equal("legal/consent_author_2026-11-09.txt", path, "a language without a bundled file falls back to Russian");
            ModAssert.False(native, "the fallback document is not native");

            AuthorConsentDocument.Resolve("de", _ => true, out path, out native);
            ModAssert.Equal("legal/consent_author_2026-11-09.txt", path, "an unsupported language falls back to Russian");
            ModAssert.False(native, "an unsupported language never claims a native document");

            AuthorConsentDocument.Resolve("", _ => true, out path, out native);
            ModAssert.Equal("legal/consent_author_2026-11-09.txt", path, "an empty locale falls back to Russian");
            ModAssert.False(native, "an empty locale is not native");

            AuthorConsentDocument.Resolve("ru_RU", _ => true, out path, out native);
            ModAssert.Equal("legal/consent_author_2026-11-09.txt", path, "a regional variant of Russian reads the original");
            ModAssert.True(native, "a regional variant of Russian is still native");
        }
    }
}
