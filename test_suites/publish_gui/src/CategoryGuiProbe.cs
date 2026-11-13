using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Cthangover.Core.Mods;
using Cthangover.Mods.Interface.View;
using Godot;

namespace Mods.ModManager.PublishGui
{

/// <summary>Exercises the category controls through GUI signals, then verifies the bytes written by the wizard and detail form.</summary>
public static class CategoryGuiProbe
{
    private const string Filter = "Margin/Root/Body/Sidebar/SidebarScroll/FilterPanel/";

    public static async Task CheckCatalog(Node owner, ModManagerScreen manager)
    {
        manager.CloseDetail();
        manager.SetSearch("private_test");
        var boxes=manager.GetNode<Control>(Filter+"CategoryList").GetChildren().OfType<CheckBox>().ToList();
        if (boxes.Count(box=>ModCategories.Comparer.Equals(box.GetMeta("category").AsString(),"rpg"))!=1)
            throw new InvalidOperationException("RPG/rpg became separate filters");
        if (boxes.Count<38 || boxes.Any(box=>!box.Visible) || manager.GetNodeOrNull(Filter+"MoreCategories")!=null)
            throw new InvalidOperationException("actual categories were hidden or collapsed");
        var translated=FilterBox(manager,"rpg");
        if (!translated.Text.Contains(ModCategoryNames.Translate("RPG")) || ModCategoryNames.Translate("RPG")=="RPG")
            throw new InvalidOperationException("custom category translation missing");
        if (!FilterBox(manager,"Weap ons! 世界").Text.Contains("Weap ons! 世界"))
            throw new InvalidOperationException("untranslated authored spelling was changed");
        translated.ButtonPressed=true;
        manager.GetNode<CheckButton>(Filter+"GroupToggle").ButtonPressed=true;
        await CatalogService.RefreshAsync(forceRefresh:true);
        if (!FilterBox(manager,"RPG").ButtonPressed) throw new InvalidOperationException("catalog refresh lost category selection");
        var grid=manager.GetNode<Control>("Margin/Root/Body/Content/Grid");
        if (Descendants(grid).OfType<ModCard>().Count()!=2) throw new InvalidOperationException("case-insensitive category filter did not match both mods");
        if (!Descendants(grid).OfType<Label>().Any(label=>label.Text.StartsWith(ModCategoryNames.Translate("RPG")+"  ·  2")))
            throw new InvalidOperationException("case-insensitive primary grouping did not merge both mods");
        await Capture(owner,"categories-filter-translated");
        manager.GetNode<Button>(Filter+"Reset").EmitSignal(BaseButton.SignalName.Pressed);
        manager.SetSearch("private_test");
        FilterBox(manager,"Новая категория").ButtonPressed=true;
        if (Descendants(grid).OfType<ModCard>().Count()!=1) throw new InvalidOperationException("free-text secondary category did not restrict the grid");
        await Capture(owner,"categories-filter-fallback");
        manager.GetNode<Button>(Filter+"Reset").EmitSignal(BaseButton.SignalName.Pressed);
    }

    private static CheckBox FilterBox(ModManagerScreen manager,string category)
        => manager.GetNode<Control>(Filter+"CategoryList").GetChildren().OfType<CheckBox>()
            .First(box=>ModCategories.Comparer.Equals(box.GetMeta("category").AsString(),category));

    public static async Task CheckWizard(Node owner)
    {
        string created = null;
        var wizard = NewModDialog.Build(Array.Empty<(string, string)>(), "", (name, id, desc, dependency, game, categories) =>
        {
            var outcome = NewModCreator.Create(id, name, desc, dependency, "en", out created, categories);
            if (outcome != NewModCreator.Outcome.Created) throw new InvalidOperationException("wizard category fixture failed: " + outcome);
        });
        owner.GetTree().Root.AddChild(wizard);
        try
        {
            (wizard.FindChild("ModName", true, false) as LineEdit).Text = "Category wizard";
            (wizard.FindChild("ModId", true, false) as LineEdit).Text = "test_category_wizard";
            var editor = wizard.FindChild("ModCategoryEditor", true, false) as ModCategoryEditor;
            SelectCustom(editor);
            wizard.PopupCentered(new Vector2I(820, 700));
            await Capture(owner, "categories-wizard", wizard);
            wizard.EmitSignal(AcceptDialog.SignalName.Confirmed);
            var manifest = JsonSerializer.Deserialize<ModManifest>(File.ReadAllText(Path.Combine(created, "manifest.json")));
            if (!manifest.Categories.SequenceEqual(new[] { "RPG", "Weap ons! 世界" })) throw new InvalidOperationException("wizard did not save ordered custom categories");
        }
        finally
        {
            wizard.QueueFree();
            if (created != null) Directory.Delete(created, true);
        }
    }

    public static void SelectCustom(ModCategoryEditor editor)
    {
        foreach (var name in ModCategories.Base) editor.GetNode<CheckBox>("Layout/Base/"+name).ButtonPressed=false;
        var input=editor.GetNode<LineEdit>("Layout/Input/Code");
        var add=editor.GetNode<Button>("Layout/Input/Add");
        input.Text="   ";
        add.EmitSignal(BaseButton.SignalName.Pressed);
        if (editor.Categories.Count!=0) throw new InvalidOperationException("empty names were not ignored");
        input.Text="rp";
        var suggestions=editor.GetNode<OptionButton>("Layout/Suggestions");
        var index=Enumerable.Range(1,suggestions.ItemCount-1).First(i=>ModCategories.Comparer.Equals(suggestions.GetItemMetadata(i).AsString(),"RPG"));
        suggestions.Select(index);
        suggestions.EmitSignal(OptionButton.SignalName.ItemSelected,(long)index);
        if (input.Text!="RPG") throw new InvalidOperationException("completion lost the first authored spelling");
        add.EmitSignal(BaseButton.SignalName.Pressed);
        input.Text="  Weap ons! 世界  ";
        add.EmitSignal(BaseButton.SignalName.Pressed);
        input.Text="rpg";
        add.EmitSignal(BaseButton.SignalName.Pressed);
        if (!editor.Categories.SequenceEqual(new[] { "RPG", "Weap ons! 世界" }))
            throw new InvalidOperationException("free-text input changed case, punctuation or first occurrence order");
        var extras=new[] { "story", "audio", "ui", "visual", "library", "engine" };
        foreach (var name in extras) editor.GetNode<CheckBox>("Layout/Base/"+name).ButtonPressed=true;
        if (editor.Categories.Count!=8) throw new InvalidOperationException("client still limits the number of categories");
        foreach (var name in extras) editor.GetNode<CheckBox>("Layout/Base/"+name).ButtonPressed=false;
        var longName=new string('Ж',100);
        input.Text=longName;
        add.EmitSignal(BaseButton.SignalName.Pressed);
        if (!editor.Categories.Contains(longName)) throw new InvalidOperationException("client still limits category length");
        var chip=editor.GetNode<Control>("Layout/Custom").GetChildren().OfType<Button>()
            .First(button=>button.GetMeta("category").AsString()==longName);
        chip.EmitSignal(BaseButton.SignalName.Pressed);
    }

    public static async Task Capture(Node owner, string name, Viewport viewport = null)
    {
        if (DisplayServer.GetName() == "headless") return;
        await owner.ToSignal(owner.GetTree(), SceneTree.SignalName.ProcessFrame);
        await owner.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        var shots = System.Environment.GetEnvironmentVariable("CTHANGOVER_E2E_SHOTS");
        if (string.IsNullOrEmpty(shots)) return;
        Directory.CreateDirectory(shots);
        if (!ViewportCapture.SavePng(viewport ?? owner.GetViewport(), Path.Combine(shots, name + ".png")))
            throw new InvalidOperationException("category GUI capture failed");
    }

    private static IEnumerable<Node> Descendants(Node node)
    {
        foreach (var child in node.GetChildren())
        {
            if (child.IsQueuedForDeletion()) continue;
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
}
}
