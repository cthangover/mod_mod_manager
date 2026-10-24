using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Cthangover.Core.Bootstrap;
using Cthangover.Core.UI.Menu;
using Cthangover.Core.Utils;
using Cthangover.Mods.Interface.Menu;
using Cthangover.Mods.Interface.View;
using Godot;
using Mods.ModManager;

namespace Mods.ModManagerHelpTest
{
    /// <summary>
    /// Walks the Mods &amp; Collections hub like a player who hovers over everything: the mods grid, a mod's
    /// page, the games gallery, the scheme editor (with a mechanic and then a mod selected) and the help
    /// panel of each level. On every level each visible button, toggle, list and input must carry a tooltip,
    /// and no tooltip may sit on a control that ignores the mouse (Godot never shows it there). One step
    /// per tick, so deferred layout settles. <c>--shots=&lt;dir&gt;</c> saves every level.
    /// Prints <c>[TEST_RESULT] name=mod_help</c>.
    /// </summary>
    public partial class ModHelpTestDriverNode : Node
    {
        private const string HubId = "mods_hub";
        private const string GameId = "novel_template";
        private const float TimeoutSeconds = 90f;

        private readonly List<string> failures = new();
        private int passed;
        private int step;
        private double elapsed;
        private double stepAt;
        private bool done;
        private ModHubScreen hub;

        public override void _Process(double delta)
        {
            if (done)
                return;

            elapsed += delta;
            if (elapsed > TimeoutSeconds)
            {
                failures.Add($"timeout at step {step}");
                Finish();
                return;
            }

            if (elapsed - stepAt < 0.5)
                return;
            stepAt = elapsed;

            try
            {
                RunStep();
            }
            catch (Exception ex)
            {
                failures.Add($"step {step}: {ex.GetType().Name}: {ex.Message}");
                Finish();
            }
        }

        private void RunStep()
        {
            switch (step)
            {
                case 0:
                    if (!Boot.IsReady)
                        return;
                    if (GetTree().CurrentScene?.FindChild("LauncherPlay", true, false) is Button launcherPlay)
                    {
                        launcherPlay.EmitSignal(BaseButton.SignalName.Pressed);
                        return;
                    }
                    if (GetTree().CurrentScene?.FindChild("play", true, false) is not Button)
                        return;
                    var entry = MenuRegistry.GetButtonById(HubId);
                    hub = MenuBuilder.OpenScene((Control)GetTree().CurrentScene, entry?.ScenePath, HubId) as ModHubScreen;
                    Check("the hub opens", hub != null);
                    if (hub == null)
                        Finish();
                    break;

                case 1:
                    PressTab("mod_manager/hub_tab_mods");
                    break;

                case 2:
                    CheckLevel("mods");
                    var card = hub.FindChildren("*", "", true, false).OfType<ModCard>().FirstOrDefault(c => c.IsVisibleInTree());
                    Check("the grid shows mod cards", card != null);
                    card?.EmitSignal(Control.SignalName.GuiInput, new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true });
                    break;

                case 3:
                    CheckLevel("mod_page");
                    PressTab("mod_manager/hub_tab_collections");
                    break;

                case 4:
                    CheckLevel("gallery");
                    hub.OpenCollectionEditor(GameId);
                    break;

                case 5:
                {
                    CheckLevel("scheme");
                    var group = hub.FindChildren("*", "", true, false).OfType<CollectionGroupCard>().FirstOrDefault(g => g.IsVisibleInTree());
                    Check("the scheme shows mechanics", group != null);
                    Click(group);
                    break;
                }

                case 6:
                {
                    CheckLevel("scheme_mechanic");
                    var chip = hub.FindChildren("*", "", true, false).OfType<CollectionModChip>().FirstOrDefault(c => c.IsVisibleInTree());
                    Check("the mechanic shows its mods", chip != null);
                    Click(chip);
                    break;
                }

                case 7:
                    CheckLevel("scheme_mod");
                    PressHelp();
                    break;

                case 8:
                    CheckHelp("help_scheme");
                    PressTab("mod_manager/hub_tab_collections");
                    break;

                case 9:
                    PressHelp();
                    break;

                case 10:
                    CheckHelp("help_collections");
                    PressTab("mod_manager/hub_tab_mods");
                    break;

                case 11:
                    PressHelp();
                    break;

                case 12:
                    CheckHelp("help_mods");
                    hub.QueueFree();
                    Finish();
                    break;
            }

            step++;
        }

        /// <summary>A left click on the frame of a scheme mechanic or mod chip (their first panel takes the mouse).</summary>
        private static void Click(Control owner)
        {
            var panel = owner?.GetChildren().OfType<PanelContainer>().FirstOrDefault();
            panel?.EmitSignal(Control.SignalName.GuiInput, new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = true });
            panel?.EmitSignal(Control.SignalName.GuiInput, new InputEventMouseButton { ButtonIndex = MouseButton.Left, Pressed = false });
        }

        private void PressTab(string key)
        {
            var text = TranslationServer.Translate(key).ToString();
            var tab = Buttons(hub).FirstOrDefault(b => TextOf(b) == text && b.IsVisibleInTree() && b.ToggleMode);
            tab ??= Buttons(hub).FirstOrDefault(b => TextOf(b) == text && b.IsVisibleInTree());
            Check($"tab '{text}' shown", tab != null);
            tab?.EmitSignal(BaseButton.SignalName.Pressed);
        }

        private void PressHelp()
        {
            var help = Buttons(hub).FirstOrDefault(b => b.Name == "HelpButton" && b.IsVisibleInTree());
            Check("the header offers help", help != null);
            help?.EmitSignal(BaseButton.SignalName.Pressed);
        }

        private void CheckHelp(string level)
        {
            var panel = hub.FindChildren("*", "", true, false).OfType<ModHelpPanel>().FirstOrDefault(p => p.IsVisibleInTree());
            Check($"{level}: the help panel opens", panel != null);
            var body = panel?.FindChildren("*", "RichTextLabel", true, false).OfType<RichTextLabel>().FirstOrDefault();
            Check($"{level}: the help explains something", body != null && body.GetParsedText().Length > 200);
            Shot(level);
            CheckLevel(level);
            panel?.Close();
        }

        /// <summary>Every visible control the player can press, pick or type into explains itself on hover.</summary>
        private void CheckLevel(string level)
        {
            Shot(level);
            var silent = new List<string>();
            var deaf = new List<string>();
            foreach (var control in hub.FindChildren("*", "", true, false).OfType<Control>())
            {
                if (!control.IsVisibleInTree())
                    continue;
                var tip = control.TooltipText;
                if (Interactive(control) && string.IsNullOrWhiteSpace(tip))
                    silent.Add(Describe(control));
                if (!string.IsNullOrWhiteSpace(tip) && control.MouseFilter == Control.MouseFilterEnum.Ignore)
                    deaf.Add(Describe(control));
            }

            Check($"{level}: every control has a tooltip" + (silent.Count > 0 ? ": missing on " + string.Join(", ", silent) : string.Empty), silent.Count == 0);
            Check($"{level}: no tooltip on a control that ignores the mouse" + (deaf.Count > 0 ? ": " + string.Join(", ", deaf) : string.Empty), deaf.Count == 0);
        }

        private static bool Interactive(Control control)
            => (control is BaseButton || control is LineEdit || control is TextEdit)
               && control.MouseFilter != Control.MouseFilterEnum.Ignore
               && !InsidePopup(control);

        /// <summary>Items inside a popup menu follow the menu itself, not their own tooltips.</summary>
        private static bool InsidePopup(Control control)
        {
            for (Node parent = control.GetParent(); parent != null; parent = parent.GetParent())
            {
                if (parent is PopupMenu)
                    return true;
            }

            return false;
        }

        private static string Describe(Control control)
        {
            var text = control switch
            {
                Button button => button.Text,
                LineEdit edit => edit.PlaceholderText,
                _ => string.Empty,
            };
            return $"{control.GetClass()} '{control.Name}'" + (string.IsNullOrEmpty(text) ? string.Empty : $" \"{text}\"");
        }

        private static IEnumerable<BaseButton> Buttons(Node root)
            => root == null ? Enumerable.Empty<BaseButton>() : root.FindChildren("*", "", true, false).OfType<BaseButton>();

        private static string TextOf(BaseButton button)
            => button is Button clickable ? clickable.Text : string.Empty;

        /// <summary>With <c>--shots=&lt;dir&gt;</c> saves the window as <paramref name="name"/>.png (windowed runs only).</summary>
        private void Shot(string name)
        {
            string dir = null;
            foreach (var arg in OS.GetCmdlineUserArgs())
            {
                if (arg.StartsWith("--shots="))
                    dir = arg.Substring("--shots=".Length);
            }
            if (string.IsNullOrEmpty(dir) || DisplayServer.GetName() == "headless")
                return;
            dir = ProjectSettings.GlobalizePath(dir);
            Directory.CreateDirectory(dir);
            ViewportCapture.SavePng(GetViewport(), Path.Combine(dir, name + ".png"));
        }

        private void Check(string name, bool ok)
        {
            if (ok)
            {
                passed++;
                GameLogger.Log("TEST", $"ModHelpTest: [OK] {name}");
            }
            else
            {
                failures.Add(name);
                GameLogger.Log("TEST", $"ModHelpTest: [FAIL] {name}", LogLevel.Error);
            }
        }

        private void Finish()
        {
            if (done)
                return;
            done = true;
            GameLogger.Log("TEST", $"[TEST_RESULT] name=mod_help passed={passed} failed={failures.Count}");
            foreach (var failure in failures)
                GameLogger.Log("TEST", $"ModHelpTest: failure: {failure}", LogLevel.Error);
            GetTree().Quit(failures.Count == 0 ? 0 : 1);
        }
    }
}
