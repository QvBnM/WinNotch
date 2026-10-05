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

        /// <summary>
        /// Mode, pill size, what the Activity Manager shows (split, group, peek), the open Command Bar, the page shown (P27) and
        /// the category in the context engine's snapshot (P27), read by the smoke test from the window's ItemStatus.
        /// </summary>
        private void UpdateSmokeStatus()
        {
            var (split, group, peek) = ActivitySmokeFields();
            var snap = Core.Context.ContextEngine.Current?.Snapshot;      // the category the page choice actually uses (meeting/game win)
            string ctx = snap == null ? null : Features.ContextPages.ContextPageRules.EffectiveCategory(snap).ToString();
            AutomationProperties.SetItemStatus(this, SmokeMode.Status(_mode.ToString(), Pill.ActualWidth, Pill.ActualHeight, split, group, peek, CommandBarOpen ? 1 : 0,
                                                                      CurrentPageId(), ctx));
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
                case SmokeCommandKind.FakeContext:
                    SmokeFakeContext(c.Argument);
                    break;
                case SmokeCommandKind.SetContextPage:
                    SmokeSetContextPage(c.Argument, c.Page);
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

        /// <summary>
        /// P27 "fake-context": the category goes into the context engine (its snapshot, through the normal flush), so the
        /// notch reads it on open exactly like a real one. Nothing else can call this: only smoke mode runs these commands.
        /// </summary>
        private void SmokeFakeContext(string category)
        {
            if (!SmokeMode.On) return;                     // test mode only, whoever calls it
            Core.Context.AppCategory? forced = null;
            if (!string.IsNullOrEmpty(category))
            {
                if (!Enum.TryParse(category, false, out Core.Context.AppCategory parsed)) { App.Log("Test de fum: categorie necunoscută."); return; }
                forced = parsed;
            }
            var engine = Core.Context.ContextEngine.Current;
            if (engine == null) { App.Log("Test de fum: motorul de context lipsește; contextul fals e ignorat."); return; }
            bool now = engine.ForceCategoryForSmoke(forced);
            App.Log("Test de fum: context fals „" + (forced?.ToString() ?? "niciunul") + "”" + (now ? "." : " (motorul de context e oprit; se aplică la pornirea lui)."));
        }

        /// <summary>P27 "set-context-page": the mapping category → page, as Settings would save it (only in the smoke folder).</summary>
        private void SmokeSetContextPage(string category, string page)
        {
            if (!SmokeMode.On) return;                     // test mode only, whoever calls it
            var cat = Features.ContextPages.ContextPageRules.ParseKey(category);
            if (cat == null) { App.Log("Test de fum: categorie necunoscută."); return; }
            S.ContextPages = S.WithContextPage(cat.Value, page);
            S.Save();
            App.Log("Test de fum: pagina pentru „" + cat.Value + "”: " + (string.IsNullOrEmpty(page) ? "—" : page) + ".");
        }

        private void StopSmoke()
        {
            _smokeTimer?.Stop();
            _smokeTimer = null;
        }
    }
}
