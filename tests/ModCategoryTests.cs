using System.Collections.Generic;
using System.Linq;
using Cthangover.Core.Mods;
using Cthangover.Core.Mods.Testing;
using Cthangover.Mods.Interface.View;

namespace Mods.ModManager.Tests
{
public class ModCategoryTests
{
    [ModTest("categories/counts_merge_case_and_keep_all_actual_categories")]
    static void Counts(ModTestContext context)
    {
        var local = new ModCardData { Id = "one", Categories = new() { "RPG", "Weap ons!" }, Catalog = new() { Categories = new() { "rpg" } } };
        var variant = new ModCardData { Id = "one", Categories = new() { "Rpg" }, Catalog = new() { Categories = new() { "Новая категория" } } };
        var empty = new ModCardData { Id = "two" };
        var filter = new ModFilterState();
        var counts = ModManagerModel.CountCategories(new[] { local, variant, empty },filter);
        ModAssert.Equal(1,counts["rpg"],"case variants of one mod counted once");
        ModAssert.Equal("RPG",counts.Keys.First(name=>ModCategories.Comparer.Equals(name,"rpg")),"first authored spelling is retained");
        ModAssert.Equal(1,counts["Новая категория"],"all catalog variants contribute names");
        ModAssert.Equal(1,counts["other"],"implicit Other");
        filter.Search="missing";
        var zeros=ModManagerModel.CountCategories(new[] { local, variant, empty },filter);
        ModAssert.Equal(counts.Count,zeros.Count,"all actual categories remain visible despite other facets");
        ModAssert.True(zeros.Values.All(count=>count==0),"other facets affect the counts");
        filter.Search="";
        filter.Categories.Add("rpg");
        ModAssert.True(filter.Matches(local) && filter.Matches(variant),"category filtering ignores case");
    }

    [ModTest("categories/alphabetical_display_order_and_authored_fallback")]
    static void Order(ModTestContext context)
    {
        var order=ModCategoryNames.Order(new[] { "OTHER", "Zulu", "alpha", "Beta", "BETA" });
        ModAssert.True(order.SequenceEqual(new[] { "alpha", "Beta", "Zulu", "OTHER" }),"alphabetical display order, first spelling, Other last");
        ModAssert.Equal("Weap ons! 世界",ModCategoryNames.Translate("Weap ons! 世界"),"untranslated names are displayed as authored");
    }
}
}
