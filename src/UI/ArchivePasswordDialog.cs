namespace Mods.ModManager
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using Cthangover.Core.Games;
    using Cthangover.Core.Mods;
    using Cthangover.Core.Mods.Providers;
    using Cthangover.Mods.Interface.Base.Lists;
    using Godot;

    /// <summary>
    /// Opens an identified installed package, then rediscovers the current selection without a restart.
    /// Passwords remain in memory by default. The optional checkbox explicitly discloses plaintext persistence.
    /// Closing cancels validation; no background worker accesses Godot controls.
    /// </summary>
    public partial class ArchivePasswordDialog : ModWidget
    {
        private OptionButton archives;
        private Label archivePath;
        private LineEdit password;
        private CheckBox remember;
        private Label status;
        private Button save;
        private Button forget;
        private readonly CancellationTokenSource cancellation = new();
        private TaskCompletionSource<string> downloadPassword;

        /// <summary>Raised after the validated package is visible in the live discovered-mod registry.</summary>
        public event Action<string> Unlocked;

        protected override void Construct()
        {
            archives = GetNode<OptionButton>("Center/Panel/Form/Archives");
            archivePath = GetNode<Label>("Center/Panel/Form/Path");
            password = GetNode<LineEdit>("Center/Panel/Form/Password");
            remember = GetNode<CheckBox>("Center/Panel/Form/Remember");
            status = GetNode<Label>("Center/Panel/Form/Status");
            save = GetNode<Button>("Center/Panel/Form/Buttons/Save");
            forget = GetNode<Button>("Center/Panel/Form/Buttons/Forget");
            GetNode<Label>("Center/Panel/Form/Title").Text = TranslationServer.Translate("mod_manager/archive_password");
            GetNode<Label>("Center/Panel/Form/Hint").Text = TranslationServer.Translate("mod_manager/archive_password_hint");
            password.PlaceholderText = TranslationServer.Translate("mod_manager/archive_secret");
            remember.Text = TranslationServer.Translate("mod_manager/archive_remember");
            remember.TooltipText = TranslationServer.Translate("mod_manager/archive_remember_tip");
            save.Text = TranslationServer.Translate("mod_manager/archive_save");
            save.Pressed += OnSave;
            forget.Text = TranslationServer.Translate("mod_manager/archive_forget");
            forget.Pressed += OnForget;
            archives.ItemSelected += OnArchiveSelected;
            var close = GetNode<Button>("Center/Panel/Form/Buttons/Close");
            close.Text = TranslationServer.Translate("mod_manager/back");
            close.Pressed += QueueFree;
            FillArchives();
        }

        protected override void Destruct()
        {
            downloadPassword?.TrySetCanceled();
            cancellation.Cancel();
            cancellation.Dispose();
            Unlocked = null;
        }

        /// <summary>Selects the package from a card action; the header button instead offers known local archives.</summary>
        public void SelectArchive(string path)
        {
            if (path == null)
                return;
            for (var index = 0; index < archives.ItemCount; index++)
            {
                if (archives.GetItemMetadata(index).AsString() != path)
                    continue;
                archives.Select(index);
                OnArchiveSelected(index);
                return;
            }
        }

        /// <summary>Prompts for an encrypted catalog installation; the downloader validates it and can request another attempt.</summary>
        public Task<string> RequestDownloadPassword(string modId)
        {
            downloadPassword = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            archives.Visible = false;
            archivePath.Text = modId;
            remember.Visible = false;
            forget.Visible = false;
            status.Text = "";
            GetNode<Label>("Center/Panel/Form/Hint").Text = TranslationServer.Translate("mod_manager/archive_download_hint");
            save.Disabled = false;
            return downloadPassword.Task;
        }

        private void FillArchives()
        {
            var seen = new HashSet<string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
            foreach (var entry in ArchiveUnlockService.Default.GetUnavailable())
                AddArchive(entry.Path, seen);
            foreach (var mod in ModRegistry.Instance.AllDiscoveredMods.Values)
                if (mod.FileProvider is ArchiveModFileProvider)
                    AddArchive(mod.FileProvider.RootPath, seen);
            save.Disabled = archives.ItemCount == 0;
            forget.Disabled = save.Disabled;
            if (archives.ItemCount > 0)
                OnArchiveSelected(0);
            else
                status.Text = TranslationServer.Translate("mod_manager/archive_none");
        }

        private void AddArchive(string path, HashSet<string> seen)
        {
            if (!seen.Add(path))
                return;
            archives.AddItem(System.IO.Path.GetFileName(path));
            archives.SetItemMetadata(archives.ItemCount - 1, path);
        }

        private void OnArchiveSelected(long index)
        {
            archivePath.Text = archives.GetItemMetadata((int)index).AsString();
            password.Text = "";
            status.Text = "";
        }

        private async void OnSave()
        {
            if (save.Disabled)
                return;
            if (downloadPassword != null)
            {
                downloadPassword.TrySetResult(password.Text);
                password.Text = "";
                QueueFree();
                return;
            }
            var path = archivePath.Text;
            var secret = password.Text;
            var persist = remember.ButtonPressed;
            var token = cancellation.Token;
            save.Disabled = true;
            forget.Disabled = true;
            archives.Disabled = true;
            remember.Disabled = true;
            status.Text = TranslationServer.Translate("mod_manager/archive_checking");
            try
            {
                var id = await Task.Factory.StartNew(Unlock, (path, secret, persist, token), token,
                    TaskCreationOptions.DenyChildAttach, TaskScheduler.Default);
                if (!GodotObject.IsInstanceValid(this))
                    return;
                var manager = Cthangover.Core.Mods.ModManager.Instance;
                var preparation = await manager.PrepareReloadAsync(GameLaunchArgs.Current, cancellationToken: token);
                if (!GodotObject.IsInstanceValid(this))
                {
                    ModRegistry.Instance.Reloader.Discard(preparation);
                    return;
                }
                var result = manager.ApplyReload(preparation, GetTree());
                await manager.VerifyReloadAsync(result, GetTree());
                if (!GodotObject.IsInstanceValid(this))
                    return;
                if (!result.Applied)
                {
                    status.Text = TranslationServer.Translate("mod_manager/archive_reload_failed");
                    return;
                }
                status.Text = TranslationServer.Translate("mod_manager/archive_unlocked");
                Unlocked?.Invoke(id);
            }
            catch (OperationCanceledException) { }
            catch
            {
                if (GodotObject.IsInstanceValid(this))
                    status.Text = TranslationServer.Translate("mod_manager/archive_failed");
            }
            finally
            {
                if (GodotObject.IsInstanceValid(this))
                {
                    password.Text = "";
                    save.Disabled = false;
                    forget.Disabled = false;
                    archives.Disabled = false;
                    remember.Disabled = false;
                }
            }
        }

        private static string Unlock(object state)
        {
            var (path, secret, persist, token) = ((string, string, bool, CancellationToken))state;
            return ArchiveUnlockService.Default.Unlock(path, secret, persist, token);
        }

        private void OnForget()
        {
            try
            {
                ArchivePasswordStore.Default.Set(archivePath.Text, null);
                password.Text = "";
                status.Text = TranslationServer.Translate("mod_manager/archive_forgotten");
            }
            catch
            {
                status.Text = TranslationServer.Translate("mod_manager/archive_failed");
            }
        }
    }
}
