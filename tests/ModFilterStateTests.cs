using System.Collections.Generic;
using Cthangover.Core.Mods;
using Cthangover.Core.Mods.Testing;

namespace Mods.ModManager.Tests
{
    /// <summary>Catalog provenance composes with existing facets and keeps multi-catalog cards unique.</summary>
    public sealed class ModFilterStateTests
    {
        private const string Private = "http://127.0.0.1:18080/v1/catalog";
        private const string Public = "http://127.0.0.1:18081/catalog.json";

        [ModTest("filters/catalog_default_and_provenance")]
        static void DefaultAndProvenance(ModTestContext ctx)
        {
            var local = Card("local");
            var privateCard = Card("private", Private);
            var publicCard = Card("public", Public);
            var mirrored = Card("mirrored", Public, Private);
            var cards = new[] { local, privateCard, publicCard, mirrored };
            var filter = new ModFilterState();
            ModAssert.Null(filter.CatalogUrl, "all catalogs by default");
            ModAssert.Equal(4, filter.Apply(cards).Count, "default includes local and remote cards");
            filter.CatalogUrl = Private;
            var selected = filter.Apply(cards);
            ModAssert.Equal(2, selected.Count, "private and mirrored cards");
            ModAssert.True(selected.Contains(privateCard), "private source matches");
            ModAssert.True(selected.Contains(mirrored), "secondary publishing source matches without duplicating the card");
            ModAssert.False(filter.Matches(publicCard), "other server on same host is excluded");
            filter.CatalogUrl = "http://127.0.0.1:18080/another/catalog";
            ModAssert.Equal(0, filter.Apply(cards).Count, "exact endpoint, not host or path prefix");
            filter.CatalogUrl = string.Empty;
            ModAssert.Equal(1, filter.Apply(cards).Count, "local-only filter");
            ModAssert.True(filter.Matches(local), "source-less local mod stays accessible");
        }

        [ModTest("filters/catalog_composes_with_facets")]
        static void CatalogAndFacets(ModTestContext ctx)
        {
            var card = Card("private", Private);
            card.Title = "Story Pack";
            card.Author = "Test Author";
            card.Status = ModItemStatus.NotInstalled;
            card.Categories = new List<string> { "story" };
            var filter = new ModFilterState { CatalogUrl = Private, Search = "story" };
            filter.Statuses.Add(ModItemStatus.NotInstalled);
            filter.Categories.Add(ModCategories.Story);
            ModAssert.True(filter.Matches(card), "catalog, search, status and category intersect");
            filter.Categories.Clear();
            filter.Categories.Add(ModCategories.Audio);
            ModAssert.False(filter.Matches(card), "category still restricts selected catalog");
            ModAssert.True(filter.MatchesExceptCategory(card), "category counts ignore only category");
            filter.CatalogUrl = Public;
            ModAssert.False(filter.MatchesExceptCategory(card), "category counts respect catalog");
            ModAssert.False(filter.MatchesExceptStatus(card), "status counts respect catalog");
            filter.Categories.Clear();
            ModAssert.True(filter.MatchesExceptCatalog(card), "catalog counts ignore only catalog");
            filter.Search = "missing";
            ModAssert.False(filter.MatchesExceptCatalog(card), "catalog counts respect search");
        }

        [ModTest("filters/reset_restores_all_catalogs")]
        static void Reset(ModTestContext ctx)
        {
            var filter = new ModFilterState { CatalogUrl = Private, Search = "private", Sort = ModSortMode.Author, GroupByCategory = true };
            filter.Statuses.Add(ModItemStatus.NotInstalled);
            filter.Categories.Add(ModCategories.Audio);
            filter.Reset();
            ModAssert.Null(filter.CatalogUrl, "reset returns to all catalogs");
            ModAssert.Equal("", filter.Search, "search cleared");
            ModAssert.Equal(0, filter.Statuses.Count, "statuses cleared");
            ModAssert.Equal(0, filter.Categories.Count, "categories cleared");
            ModAssert.Equal((int)ModSortMode.Author, (int)filter.Sort, "sort preserved");
            ModAssert.True(filter.GroupByCategory, "grouping preserved");
        }

        [ModTest("filters/custom_categories_and_implicit_other")]
        static void CustomCategories(ModTestContext ctx)
        {
            var card = Card("weapon");
            card.Categories = new List<string> { "weapons", "gameplay" };
            var filter = new ModFilterState();
            filter.Categories.Add("weapons");
            ModAssert.True(filter.Matches(card), "custom category matches");
            filter.Categories.Clear();
            filter.Categories.Add("gameplay");
            ModAssert.True(filter.Matches(card), "secondary category matches");
            filter.Categories.Clear();
            filter.Categories.Add("other");
            ModAssert.True(filter.Matches(Card("empty")), "empty categories use Other");
            ModAssert.False(filter.Matches(card), "Other does not match a categorized card");
        }

        private static ModCardData Card(string id, params string[] sources)
        {
            return new ModCardData
            {
                Id = id, Title = id,
                Catalog = sources.Length == 0 ? null : new CatalogExportEntry { Id = id, SourceUrls = new List<string>(sources) },
            };
        }
    }
}
