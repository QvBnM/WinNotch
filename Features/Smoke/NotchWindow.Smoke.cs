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

        /// <summary>Mode and pill size, read by the smoke test from the window's ItemStatus.</summary>
        private void UpdateSmokeStatus() =>
            AutomationProperties.SetItemStatus(this, SmokeMode.Status(_mode.ToString(), Pill.ActualWidth, Pill.ActualHeight));

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
            }
            UpdateSmokeStatus();
        }

        private void StopSmoke()
        {
            _smokeTimer?.Stop();
            _smokeTimer = null;
        }
    }
}
