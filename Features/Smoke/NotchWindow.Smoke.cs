using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Threading;
using WinNotch.Features.Smoke;
using WinNotch.Services;

namespace WinNotch
{
    /// <summary>The notch's side of the smoke tests (only with --smoke): a status for UI Automation and the test commands.</summary>
    public partial class NotchWindow
    {
        private DispatcherTimer _smokeTimer;

        /// <summary>Called once at startup in smoke mode. UI thread.</summary>
        internal void StartSmoke()
        {
            if (!SmokeMode.On || _smokeTimer != null) return;
            AutomationProperties.SetAutomationId(this, SmokeMode.NotchAutomationId);
            Pill.SizeChanged += (o, e) => UpdateSmokeStatus();
            UpdateSmokeStatus();
            _smokeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };     // test mode only
            _smokeTimer.Tick += (o, e) => { UpdateSmokeStatus(); RunSmokeCommands(); };
            _smokeTimer.Start();
        }

        /// <summary>Mode, pill size, what the Activity Manager shows (split, group, peek) and the open Command Bar, read by the smoke test from the window's ItemStatus.</summary>
        private void UpdateSmokeStatus()
        {
            var (split, group, peek) = ActivitySmokeFields();
            AutomationProperties.SetItemStatus(this, SmokeMode.Status(_mode.ToString(), Pill.ActualWidth, Pill.ActualHeight, split, group, peek, CommandBarOpen ? 1 : 0));
        }

        /// <summary>Reads smoke-commands.txt from the smoke folder, deletes it, runs the valid lines in order.</summary>
        private void RunSmokeCommands()
        {
            string path = Path.Combine(AppSettings.Folder, SmokeMode.CommandsFile);
            string[] lines;
            try
            {
                if (!File.Exists(path)) return;
                var info = new FileInfo(path);
                lines = info.Length > SmokeMode.MaxFileBytes ? Array.Empty<string>() : File.ReadAllLines(path, Encoding.UTF8);
                File.Delete(path);
            }
            catch (IOException) { return; }                 // still being written: next tick
            catch (UnauthorizedAccessException) { return; }
            foreach (var c in SmokeMode.ParseAll(lines))
            {
                try { RunSmokeCommand(c); }
                catch (Exception ex) { App.Log("Test de fum: comanda a dat eroare: " + ex.GetType().Name); }
            }
        }

        private void RunSmokeCommand(SmokeCommand c)
        {
            switch (c.Kind)
            {
                case SmokeCommandKind.VolumeAlert:
                    App.Log("Test de fum: alertă de volum.");
                    LiveVolume(42, false);
                    break;
                case SmokeCommandKind.TrackAlert:
                    App.Log("Test de fum: alertă de piesă.");
                    ShowTrackAlert(new MediaInfo { HasSession = true, Playing = true, Title = "Piesă de test", Artist = "Test de fum" });
                    break;
                case SmokeCommandKind.ToggleFeature:
                    var flags = Core.Flags.FeatureFlags.Current;
                    if (flags?.Find(c.Argument) == null) { App.Log("Test de fum: funcție necunoscută: " + c.Argument); break; }
                    bool on = !flags.IsSaved(c.Argument);
                    flags.Set(c.Argument, on);
                    App.Log("Test de fum: funcția „" + c.Argument + "” " + (on ? "pornită" : "oprită") + ".");
                    break;
                case SmokeCommandKind.PersistentActivity:
                case SmokeCommandKind.BurstActivity:
                case SmokeCommandKind.LowActivity:
                    if (!_activityOn || _activity == null) { App.Log("Test de fum: managerul de activități e oprit; comanda e ignorată."); break; }
                    var r = SmokeActivity(c);
                    App.Log("Test de fum: activitate de test (" + c.Kind + " " + c.Number + "): " + r + ".");
                    break;
                case SmokeCommandKind.DismissActivities:
                    _ = SmokeDismissActivities();
                    break;
                case SmokeCommandKind.OpenCommandBar:
                    App.Log("Test de fum: Command Bar prin comandă (aceeași cale ca scurtătura).");
                    OnCommandBarShortcut();
                    break;
            }
            UpdateSmokeStatus();
        }

        /// <summary>Test activities: persistent n (split pill with two), a burst of n alerts („N noutăți”), a Low one (peek).</summary>
        private Core.Activity.PostResult SmokeActivity(SmokeCommand c)
        {
            switch (c.Kind)
            {
                case SmokeCommandKind.PersistentActivity:
                    return _activity.Post(new Core.Activity.Activity
                    {
                        Id = "smoke-persistent-" + c.Number, Persistent = true, Title = "Activitate de test " + c.Number, Glyph = c.Number == 1 ? Ui.GMusic : Ui.GClock,
                    });
                case SmokeCommandKind.BurstActivity:
                    var last = Core.Activity.PostResult.Dropped;
                    for (int i = 1; i <= c.Number; i++)
                        last = _activity.Post(new Core.Activity.Activity
                        {
                            Id = "smoke-burst-" + i, Duration = TimeSpan.FromSeconds(3), Width = 300, Height = 40,
                            Payload = LiveRow(LiveIcon(Features.Context.ContextActions.GInfo, CWhite), "Alertă de test " + i, null, null),
                        });
                    return last;
                default:
                    return _activity.Post(new Core.Activity.Activity
                    {
                        Id = "smoke-low", Priority = Core.Activity.ActivityPriority.Low, Title = "Activitate discretă de test", Duration = TimeSpan.FromSeconds(2),
                    });
            }
        }

        /// <summary>"dismiss-activities": through the action, like the Command Bar will (unavailable with the switch off).</summary>
        private async System.Threading.Tasks.Task SmokeDismissActivities()
        {
            var reg = Core.Actions.ActionRegistry.Current;
            if (reg == null) { App.Log("Test de fum: registrul de acțiuni lipsește."); return; }
            try
            {
                var r = await reg.InvokeAsync(Features.Activity.ActivityActions.DismissAllId, null, Core.Actions.ActionInvoker.UI);
                App.Log("Test de fum: " + Features.Activity.ActivityActions.DismissAllId + " → " + (r.Success ? "făcut" : "indisponibil") + ".");
            }
            catch (Exception ex) { App.Log("Test de fum: comanda a dat eroare: " + ex.GetType().Name); }
            UpdateSmokeStatus();
        }

        private void StopSmoke()
        {
            _smokeTimer?.Stop();
            _smokeTimer = null;
        }
    }
}
