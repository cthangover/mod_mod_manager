using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Cthangover.Core.Mods.Testing;

namespace Mods.ModManager.Tests
{
    /// <summary>Checks the server's JSON contract and preservation of every publishing permission across serialization.</summary>
    public class PublishingPermissionsTests
    {
        [ModTest("publishing/permissions_json")]
        static void PermissionsJson(ModTestContext context)
        {
            const string serverJson = "{\"author_id\":\"permanent-author\",\"author\":\"Автор\",\"sections\":[\"novels\",\"rpg\"],\"max_archive_bytes\":268435456,\"is_admin\":true,\"section_patterns\":[\"*\"]}";
            var original = JsonSerializer.Deserialize<PublishingPermissions>(serverJson);
            ModAssert.Equal("permanent-author", original.AuthorId, "server author ID");
            ModAssert.Equal("Автор", original.Author, "server display name");
            ModAssert.True(original.Sections.SequenceEqual(new[] { "novels", "rpg" }), "server sections");
            ModAssert.Equal(268435456L, original.MaxArchiveBytes, "server archive limit");
            ModAssert.True(original.IsAdmin, "server admin role");
            ModAssert.True(original.SectionPatterns.SequenceEqual(new[] { "*" }), "server mask scopes");
            var json = JsonSerializer.Serialize(original);
            var restored = JsonSerializer.Deserialize<PublishingPermissions>(json);
            ModAssert.Equal(original.AuthorId, restored.AuthorId, "roundtrip author ID");
            ModAssert.Equal(original.Author, restored.Author, "roundtrip display name");
            ModAssert.True(original.Sections.SequenceEqual(restored.Sections), "roundtrip sections");
            ModAssert.Equal(original.MaxArchiveBytes, restored.MaxArchiveBytes, "roundtrip archive limit");
            ModAssert.True(original.IsAdmin == restored.IsAdmin, "roundtrip admin role");
            ModAssert.True(original.SectionPatterns.SequenceEqual(restored.SectionPatterns), "roundtrip masks");
        }
        [ModTest("publishing/masked_sections")]
        static void MaskedSections(ModTestContext context)
        {
            var permissions = new PublishingPermissions { Sections = new List<string>(), SectionPatterns = new List<string> { "rpg_*", "story_?" } };
            ModAssert.True(permissions.AllowsSection("rpg_new"), "future section in namespace");
            ModAssert.True(permissions.AllowsSection("story_a"), "single-character wildcard");
            ModAssert.False(permissions.AllowsSection("story_ab"), "one character is not two");
            ModAssert.False(permissions.AllowsSection("secret"), "scope stays bounded");
            ModAssert.False(permissions.IsAdmin, "mask does not grant admin");
            ModAssert.True(permissions.AllowsCustomSection, "masked author can enter permitted section");
            ModAssert.False(permissions.AllowsSection("rpg_*"), "literal masks cannot be section names");
            permissions.IsAdmin = true;
            ModAssert.True(permissions.AllowsSection("secret"), "admin has all sections");
            ModAssert.False(permissions.AllowsSection("../secret"), "admin still requires concrete valid names");
        }

        [ModTest("publishing/legacy_permissions")]
        static void LegacyPermissions(ModTestContext context)
        {
            var permissions = JsonSerializer.Deserialize<PublishingPermissions>("{\"author_id\":\"old\",\"sections\":[\"story\"],\"max_archive_bytes\":1024}");
            ModAssert.False(permissions.IsAdmin, "old servers never imply admin");
            ModAssert.True(permissions.AllowsSection("story"), "legacy literal grant");
            ModAssert.False(permissions.AllowsSection("other"), "legacy grant stays literal");
            ModAssert.False(permissions.AllowsCustomSection, "old form remains a section picker");
        }
    }
}
