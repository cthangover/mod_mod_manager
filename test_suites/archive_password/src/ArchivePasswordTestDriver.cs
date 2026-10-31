namespace Mods.ModManager.ArchivePasswordTest
{
    using System;
    using System.IO;
    using Cthangover.Core.Bootstrap;
    using Cthangover.Core.Mods;
    using Cthangover.Core.Scenes;
    using Cthangover.Core.Utils;
    using Godot;

    /// <summary>Exercises a locked card, wrong password, session-only unlock and live rediscovery through the real UI.</summary>
    public partial class ArchivePasswordTestDriver : Node
    {
        private ModManagerScreen manager;
        private ArchivePasswordDialog dialog;
        private string packagePath;
        private string credentialPath;
        private byte[] originalCredentials;
        private double elapsed;
        private double stepAt;
        private int step;
        private bool finished;
        private System.Threading.Tasks.Task<string> downloadPrompt;

        public override void _Process(double delta)
        {
            if (!GameEnvironment.IsRuntime || finished)
                return;
            elapsed += delta;
            if (elapsed > 90)
            {
                Finish("timeout at step " + step);
                return;
            }
            if (!Boot.IsReady || elapsed - stepAt < 0.2)
                return;
            stepAt = elapsed;
            try { RunStep(); }
            catch (Exception ex) { Finish(ex.ToString()); }
        }

        private void RunStep()
        {
            switch (step)
            {
                case 0:
                    if (GetTree().CurrentScene?.FindChild("LauncherPlay", true, false) is Button launcherPlay)
                    {
                        launcherPlay.EmitSignal(BaseButton.SignalName.Pressed);
                        return;
                    }
                    if (GetTree().CurrentScene?.FindChild("play", true, false) is not Button)
                        return;
                    PrepareFixture();
                    OpenManager();
                    manager.EnsureConstructed();
                    manager.SetSearch(Path.GetFileNameWithoutExtension(packagePath));
                    var card = FindArchiveCard(manager);
                    if (card == null)
                        throw new InvalidOperationException("locked archive card is missing");
                    card.GetNode<Button>("VBox/Body/Box/ActionRow/ActionHost/Action").EmitSignal(BaseButton.SignalName.Pressed);
                    dialog = manager.FindChild("ArchivePasswordDialog", true, false) as ArchivePasswordDialog;
                    if (dialog == null)
                        throw new InvalidOperationException("locked card did not open its password dialog");
                    var password = dialog.GetNode<LineEdit>("Center/Panel/Form/Password");
                    if (!password.Secret)
                        throw new InvalidOperationException("password must be masked");
                    if (dialog.GetNode<CheckBox>("Center/Panel/Form/Remember").ButtonPressed)
                        throw new InvalidOperationException("password persistence must be opt-in");
                    if (dialog.GetNode<Label>("Center/Panel/Form/Path").Text != packagePath)
                        throw new InvalidOperationException("dialog did not select the clicked package");
                    password.Text = "wrong-password";
                    dialog.GetNode<Button>("Center/Panel/Form/Buttons/Save").EmitSignal(BaseButton.SignalName.Pressed);
                    step++;
                    break;
                case 1:
                    if (dialog.GetNode<Button>("Center/Panel/Form/Buttons/Save").Disabled)
                        return;
                    var expected = TranslationServer.Translate("mod_manager/archive_failed").ToString();
                    if (dialog.GetNode<Label>("Center/Panel/Form/Status").Text != expected)
                        throw new InvalidOperationException("bad password did not show a failure");
                    if (dialog.GetNode<LineEdit>("Center/Panel/Form/Password").Text.Length != 0)
                        throw new InvalidOperationException("password input was not cleared");
                    dialog.GetNode<LineEdit>("Center/Panel/Form/Password").Text = "fixture-пароль";
                    dialog.GetNode<Button>("Center/Panel/Form/Buttons/Save").EmitSignal(BaseButton.SignalName.Pressed);
                    step++;
                    break;
                case 2:
                    if (dialog.GetNode<Button>("Center/Panel/Form/Buttons/Save").Disabled)
                        return;
                    if (!ModRegistry.Instance.AllDiscoveredMods.TryGetValue("test_sevenzip", out var mod)
                        || mod.FileProvider.RootPath != packagePath)
                        throw new InvalidOperationException("package was not discovered without restarting");
                    if (mod.FileProvider.ReadFileText("Data/привет.txt") != "Привет из 7z!\n")
                        throw new InvalidOperationException("unlocked content differs");
                    if (ArchiveUnlockService.Default.GetUnavailable().Count != 0)
                    {
                        foreach (var item in ArchiveUnlockService.Default.GetUnavailable())
                            if (item.Path == packagePath)
                                throw new InvalidOperationException("unlocked package remains locked");
                    }
                    VerifyCredentialFile();
                    dialog.GetNode<Button>("Center/Panel/Form/Buttons/Close").EmitSignal(BaseButton.SignalName.Pressed);
                    step++;
                    break;
                case 3:
                    if (GodotObject.IsInstanceValid(dialog))
                        throw new InvalidOperationException("dialog did not close");
                    manager.GetNode<Button>("Margin/Root/Header/ArchivePassword").EmitSignal(BaseButton.SignalName.Pressed);
                    dialog = manager.FindChild("ArchivePasswordDialog", true, false) as ArchivePasswordDialog;
                    downloadPrompt = dialog.RequestDownloadPassword("catalog_package");
                    if (dialog.GetNode<CheckBox>("Center/Panel/Form/Remember").Visible)
                        throw new InvalidOperationException("download password must not offer persistence");
                    dialog.GetNode<LineEdit>("Center/Panel/Form/Password").Text = "download-secret";
                    dialog.GetNode<Button>("Center/Panel/Form/Buttons/Save").EmitSignal(BaseButton.SignalName.Pressed);
                    step++;
                    break;
                case 4:
                    if (!downloadPrompt.IsCompletedSuccessfully || downloadPrompt.Result != "download-secret")
                        throw new InvalidOperationException("download password prompt did not return the typed value");
                    VerifyCredentialFile();
                    manager.QueueFree();
                    step++;
                    break;
                case 5:
                    manager = null;
                    dialog = null;
                    Finish(null);
                    break;
            }
        }

        private void PrepareFixture()
        {
            if (ModRegistry.Instance.AllDiscoveredMods.ContainsKey("test_sevenzip"))
                throw new InvalidOperationException("fixture id is already installed");
            var root = GamePaths.Globalize("user://mods/");
            Directory.CreateDirectory(root);
            packagePath = Path.GetFullPath(Path.Combine(root, "archive_password_smoke_" + Guid.NewGuid().ToString("N") + ".7z"));
            credentialPath = GamePaths.Globalize("user://archive_passwords.json");
            originalCredentials = File.Exists(credentialPath) ? File.ReadAllBytes(credentialPath) : null;
            File.Copy(GamePaths.Globalize("res://tests/fixtures/sevenzip/encrypted_headers_solid.7z"), packagePath);
            ArchiveUnlockService.Default.RecordFailure(packagePath, true);
        }

        private static ModCard FindArchiveCard(Node root)
        {
            if (root is ModCard card)
                return card;
            foreach (var child in root.GetChildren())
            {
                var found = FindArchiveCard(child);
                if (found != null)
                    return found;
            }
            return null;
        }

        private void VerifyCredentialFile()
        {
            if (originalCredentials == null)
            {
                if (File.Exists(credentialPath))
                    throw new InvalidOperationException("session unlock wrote a credential file");
            }
            else if (!File.ReadAllBytes(credentialPath).AsSpan().SequenceEqual(originalCredentials))
                throw new InvalidOperationException("session unlock changed remembered credentials");
        }

        private void OpenManager()
        {
            var instance = TscnScenes.LoadAndBuild("scenes/mod_manager_screen.tscn");
            try
            {
                manager = instance as ModManagerScreen;
                if (manager == null)
                    throw new InvalidOperationException("manager scene not built");
                AddChild(manager);
            }
            finally
            {
                if (GodotObject.IsInstanceValid(instance) && instance.GetParent() == null)
                    instance.Free();
            }
        }

        private void Finish(string failure)
        {
            if (finished)
                return;
            finished = true;
            if (packagePath != null)
            {
                ArchivePasswordStore.Default.SetSession(packagePath, null);
                ArchiveUnlockService.Default.Remove(packagePath);
                File.Delete(packagePath);
            }
            if (GodotObject.IsInstanceValid(manager))
                manager.QueueFree();
            if (failure != null)
                GameLogger.Log("TEST", failure, LogLevel.Error);
            GameLogger.Log("TEST", $"[TEST_RESULT] name=archive_password passed={(failure == null ? 1 : 0)} failed={(failure == null ? 0 : 1)}");
            ApplicationExit.Quit(GetTree(), failure == null ? 0 : 1);
        }
    }
}
