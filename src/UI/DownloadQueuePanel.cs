namespace Mods.ModManager
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using Cthangover.Core.Mods;
    using Cthangover.Core.UI.Theming;
    using Godot;
    using Cthangover.Mods.Interface.Theming;

    /// <summary>
    /// Hub panel listing every unfinished mod install, one row per mod in the
    /// order of <see cref="ModDownloader.Active"/> (queued installs included).
    /// Each row shows the catalog name (or the mod id), a progress bar, a detail
    /// line that depends on the stage (queued, downloading with speed and time
    /// left, paused, reconnecting, verifying or extracting), and Pause/Resume and
    /// Cancel buttons wired to <see cref="ModDownloader"/>. The panel hides itself
    /// when nothing is in progress. Finished or failed installs drop their row.
    /// <para/>
    /// The panel is code-built and styled like the hub banner, in neutral surface
    /// colours with the accent used for the header. It subscribes to the static
    /// downloader events while it is in the tree.
    /// </summary>
    public partial class DownloadQueuePanel : PanelContainer
    {
        private readonly Label _header;
        private readonly VBoxContainer _rows;
        private readonly Dictionary<string, DownloadRow> _byId = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Builds the empty panel; rows appear when it enters the tree.</summary>
        public DownloadQueuePanel()
        {
            Visible = false;
            AddThemeStyleboxOverride("panel", ModManagerTheme.MakeBox(
                UiTokens.Surface1,
                UiTokens.LineCard,
                UiTokens.RadiusMd, UiTokens.Space4, UiTokens.Space2));

            var column = UiKit.Column(UiTokens.Space2);
            AddChild(column);

            _header = UiKit.Label(string.Empty, UiTokens.FontBodySmall, UiTokens.Accent);
            column.AddChild(_header);

            _rows = UiKit.Column(UiTokens.Space3);
            column.AddChild(_rows);
        }

        /// <summary>Subscribes to the downloader events and shows the current installs.</summary>
        public override void _EnterTree()
        {
            ModDownloader.OnProgress -= OnProgress;
            ModDownloader.OnProgress += OnProgress;
            ModDownloader.OnCompleted -= OnCompleted;
            ModDownloader.OnCompleted += OnCompleted;
            Sync();
        }

        /// <summary>Unsubscribes the static downloader events.</summary>
        public override void _ExitTree()
        {
            ModDownloader.OnProgress -= OnProgress;
            ModDownloader.OnCompleted -= OnCompleted;
        }

        private void OnProgress(InstallProgress progress)
        {
            if (!GodotObject.IsInstanceValid(this) || progress == null)
                return;

            if (progress.Step == InstallStep.Complete || progress.Step == InstallStep.Failed)
            {
                Remove(progress.ModId);
                Refresh();
                return;
            }

            Sync();
        }

        private void OnCompleted(string modId, bool success)
        {
            if (!GodotObject.IsInstanceValid(this))
                return;

            Remove(modId);
            Refresh();
        }

        /// <summary>Makes the rows match <see cref="ModDownloader.Active"/> and their order, skipping finished installs.</summary>
        private void Sync()
        {
            var live = ModDownloader.Active
                .Where(progress => progress != null
                    && !string.IsNullOrEmpty(progress.ModId)
                    && progress.Step != InstallStep.Complete
                    && progress.Step != InstallStep.Failed)
                .ToList();

            var keep = new HashSet<string>(live.Select(progress => progress.ModId), StringComparer.OrdinalIgnoreCase);
            foreach (var id in _byId.Keys.Where(id => !keep.Contains(id)).ToList())
                Remove(id);

            for (var index = 0; index < live.Count; index++)
            {
                var progress = live[index];
                if (!_byId.TryGetValue(progress.ModId, out var row))
                {
                    row = CreateRow(progress.ModId);
                    _byId[progress.ModId] = row;
                    _rows.AddChild(row.Root);
                }

                row.Apply(progress);
                _rows.MoveChild(row.Root, index);
            }

            Refresh();
        }

        private void Remove(string modId)
        {
            if (string.IsNullOrEmpty(modId) || !_byId.Remove(modId, out var row))
                return;

            _rows.RemoveChild(row.Root);
            row.Root.QueueFree();
        }

        /// <summary>Updates the header count and the visibility after rows changed.</summary>
        private void Refresh()
        {
            _header.Text = string.Format(
                TranslationServer.Translate("mod_manager/download_header"),
                _byId.Count.ToString(CultureInfo.InvariantCulture));
            Visible = _byId.Count > 0;
        }

        private static DownloadRow CreateRow(string modId)
        {
            var row = new DownloadRow { ModId = modId };

            var entry = CatalogMerger.FindEntry(CatalogService.CachedEntries, modId, null);
            var title = string.IsNullOrEmpty(entry?.Name) ? modId : entry.Name;

            row.Root = UiKit.Column(UiTokens.Space1);

            var top = UiKit.Row(UiTokens.Space3);
            row.Root.AddChild(top);

            row.TitleLabel = UiKit.Label(title, UiTokens.FontBody, UiTokens.Ink);
            row.TitleLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            row.TitleLabel.ClipText = true;
            top.AddChild(row.TitleLabel);

            row.PauseButton = UiKit.Button(TranslationServer.Translate("mod_manager/download_pause"));
            row.PauseButton.TooltipText = TranslationServer.Translate("mod_manager/download_pause_tip");
            row.PauseButton.Pressed += () => TogglePause(row);
            top.AddChild(row.PauseButton);

            row.CancelButton = UiKit.Button(TranslationServer.Translate("mod_manager/download_cancel"));
            row.CancelButton.TooltipText = TranslationServer.Translate("mod_manager/download_cancel_tip");
            row.CancelButton.Pressed += () => ModDownloader.Cancel(row.ModId);
            top.AddChild(row.CancelButton);

            row.Bar = new ProgressBar
            {
                MinValue = 0,
                MaxValue = 1,
                Step = 0.001,
                ShowPercentage = true,
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            };
            row.Root.AddChild(row.Bar);

            row.DetailLabel = UiKit.Label(string.Empty, UiTokens.FontSmall, UiTokens.Muted);
            row.Root.AddChild(row.DetailLabel);

            return row;
        }

        private static void TogglePause(DownloadRow row)
        {
            if (row.Last == null)
                return;

            if (row.Last.IsPaused)
                ModDownloader.Resume(row.ModId);
            else
                ModDownloader.Pause(row.ModId);
        }

        /// <summary>Builds the detail line for one stage of an install.</summary>
        private static string Describe(InstallProgress progress)
        {
            switch (progress.Step)
            {
                case InstallStep.Queued:
                    return TranslationServer.Translate("mod_manager/download_queued");
                case InstallStep.Downloading:
                    var sizes = Sizes(progress);
                    if (progress.IsPaused)
                        return string.Format(TranslationServer.Translate("mod_manager/download_paused"), sizes);
                    if (progress.Retry > 0)
                        return string.Format(
                            TranslationServer.Translate("mod_manager/download_reconnecting"),
                            sizes,
                            progress.Retry.ToString(CultureInfo.InvariantCulture));
                    return string.Format(
                        TranslationServer.Translate("mod_manager/download_running"),
                        sizes,
                        Speed(progress),
                        Eta(progress));
                case InstallStep.VerifyingArchive:
                case InstallStep.Extracting:
                case InstallStep.VerifyingInstall:
                    return TranslationServer.Translate("mod_manager/download_installing");
                default:
                    return progress.Message ?? string.Empty;
            }
        }

        /// <summary>"12.4 MB of 180.0 MB", or only the received part when the total is unknown.</summary>
        private static string Sizes(InstallProgress progress)
        {
            if (progress.TotalBytes > 0)
                return string.Format(
                    TranslationServer.Translate("mod_manager/download_size_of"),
                    Size(progress.ReceivedBytes),
                    Size(progress.TotalBytes));
            return Size(progress.ReceivedBytes);
        }

        private static string Speed(InstallProgress progress)
            => string.Format(
                TranslationServer.Translate("mod_manager/download_speed"),
                Size((long)progress.BytesPerSecond));

        /// <summary>Time left, or the "estimating" text while the total or the speed is unknown.</summary>
        private static string Eta(InstallProgress progress)
        {
            if (progress.TotalBytes <= 0 || progress.BytesPerSecond <= 0)
                return TranslationServer.Translate("mod_manager/download_eta_unknown");

            var seconds = Math.Max(0d, (progress.TotalBytes - progress.ReceivedBytes) / progress.BytesPerSecond);
            if (seconds < 60)
                return string.Format(
                    TranslationServer.Translate("mod_manager/download_eta_seconds"),
                    ((long)Math.Ceiling(seconds)).ToString(CultureInfo.InvariantCulture));

            var minutes = (long)Math.Ceiling(seconds / 60d);
            if (minutes < 60)
                return string.Format(
                    TranslationServer.Translate("mod_manager/download_eta_minutes"),
                    minutes.ToString(CultureInfo.InvariantCulture));

            return string.Format(
                TranslationServer.Translate("mod_manager/download_eta_hours"),
                (minutes / 60).ToString(CultureInfo.InvariantCulture),
                (minutes % 60).ToString(CultureInfo.InvariantCulture));
        }

        /// <summary>Formats a byte count as KB, MB or GB with one decimal.</summary>
        private static string Size(long bytes)
        {
            const double kb = 1024d;
            const double mb = kb * 1024d;
            const double gb = mb * 1024d;

            if (bytes >= gb)
                return string.Format(
                    TranslationServer.Translate("mod_manager/download_unit_gb"),
                    (bytes / gb).ToString("0.0", CultureInfo.InvariantCulture));
            if (bytes >= mb)
                return string.Format(
                    TranslationServer.Translate("mod_manager/download_unit_mb"),
                    (bytes / mb).ToString("0.0", CultureInfo.InvariantCulture));
            return string.Format(
                TranslationServer.Translate("mod_manager/download_unit_kb"),
                (Math.Max(0L, bytes) / kb).ToString("0.0", CultureInfo.InvariantCulture));
        }

        /// <summary>One install row; the controls are kept so they can be updated in place.</summary>
        private sealed class DownloadRow
        {
            /// <summary>Mod being installed.</summary>
            public string ModId;
            /// <summary>Latest progress applied to this row.</summary>
            public InstallProgress Last;
            /// <summary>Row container.</summary>
            public VBoxContainer Root;
            /// <summary>Mod name or id.</summary>
            public Label TitleLabel;
            /// <summary>Completion bar.</summary>
            public ProgressBar Bar;
            /// <summary>Stage-dependent detail line.</summary>
            public Label DetailLabel;
            /// <summary>Pause or resume the download.</summary>
            public Button PauseButton;
            /// <summary>Cancel the install.</summary>
            public Button CancelButton;

            /// <summary>Shows the newest progress of this install.</summary>
            public void Apply(InstallProgress progress)
            {
                Last = progress;
                Bar.Value = Math.Clamp(progress.Progress, 0f, 1f);
                DetailLabel.Text = Describe(progress);

                var downloading = progress.Step == InstallStep.Downloading;
                PauseButton.Disabled = !downloading;
                PauseButton.Text = TranslationServer.Translate(progress.IsPaused
                    ? "mod_manager/download_resume"
                    : "mod_manager/download_pause");
                CancelButton.Disabled = !(downloading || progress.Step == InstallStep.Queued);
            }
        }
    }
}
