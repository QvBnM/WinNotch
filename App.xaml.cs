using System;
using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Threading;
using System.Windows;

namespace WinNotch
{
    public partial class App : Application
    {
        private static Mutex _single;
        private NotchWindow _notch;
        private TrayIcon _tray;
        private EditorWindow _editor;

        public AppSettings Settings { get; private set; }

        /// <summary>
        /// WinNotch keeps its own exe open (read-only, no rename/delete allowed) while it runs, so the file can't be
        /// swapped underneath it: what "Restart as administrator" and the temperature-helper install use is the real one.
        /// </summary>
        internal static FileStream OwnExe { get; private set; }

        internal static void LockOwnExe()
        {
            if (OwnExe != null) return;
            try { OwnExe = new FileStream(Environment.ProcessPath, FileMode.Open, FileAccess.Read, FileShare.Read); }
            catch (Exception ex) { Log("Blocarea exe-ului propriu: " + ex.Message); }
        }

        /// <summary>Only for an update: the running exe is renamed and replaced by the verified new one.</summary>
        internal static void ReleaseOwnExe()
        {
            try { OwnExe?.Dispose(); } catch { }
            OwnExe = null;
        }

        /// <summary>Started by the previous version right after an update.</summary>
        internal static bool JustUpdated { get; private set; }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            Features.Smoke.SmokeMode.Init(e.Args);            // first: in smoke mode the data folder is a separate one
            DispatcherUnhandledException += (s, ex) => { Log("Eroare neprevăzută: " + ex.Exception); ex.Handled = true; Guard?.NoteError(); };
            string mode = e.Args.Length > 0 ? e.Args[0] : "";

            // the CPU-temperature helper (SYSTEM task, no window), and its one-off install / removal (elevated)
            if (mode == Services.TempHelper.Arg)
            {
                var t = new Thread(() => { Services.TempHelper.RunServer(); Dispatcher.Invoke(Shutdown); }) { IsBackground = true };
                t.Start();
                return;
            }
            if (mode == Services.TempHelper.InstallArg || mode == Services.TempHelper.UninstallArg)
            {
                if (!IsAdmin) { Shutdown(5); return; }
                LockOwnExe();
                int rc = mode == Services.TempHelper.InstallArg
                    ? (OwnExe == null ? 2 : Services.TempHelper.Install(OwnExe))
                    : Services.TempHelper.Uninstall();
                Shutdown(rc);
                return;
            }
            LockOwnExe();

            JustUpdated = mode == Services.Updater.UpdatedArg;
            _single = new Mutex(false, "WinNotch.SingleInstance");
            bool first;
            // after an update the old version is still closing: wait for it a few seconds
            try { first = _single.WaitOne(JustUpdated ? 15000 : 0); }
            catch (AbandonedMutexException) { first = true; }
            if (!first)
            {
                if (Features.Smoke.SmokeMode.On) { Log("Test de fum: WinNotch rulează deja."); Shutdown(3); return; }
                MessageBox.Show("WinNotch rulează deja. Îl găsești în zona de notificări, lângă ceas.", "WinNotch",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                Shutdown();
                return;
            }

            AppDomain.CurrentDomain.UnhandledException += (s, ex) => Log("Eroare fatală: " + ex.ExceptionObject);

            // before any service: record this start; after repeated crashes, safe mode, then the previous version
            bool safeMode = StartGuarded(Array.IndexOf(e.Args, Core.Flags.FeatureFlags.SafeModeArg) >= 0);
            if (_handedOver) return;
            SessionEnding += (s, ev) => Guard?.MarkCleanExit(sessionEnding: true);       // Windows shutting down / signing out: not a crash

            // a start that fails halfway must not leave a process without notch or icon (holding the single-instance
            // lock and later declared healthy): it is logged and ends as a crash, so the guard counts it
            try { StartApp(safeMode); }
            catch (Exception ex)
            {
                Log("Pornirea a eșuat: " + ex);
                Environment.Exit(1);
            }
        }

        private void StartApp(bool safeMode)
        {
            Settings = AppSettings.Load();
            if (Features.Smoke.SmokeMode.On)
            {
                // smoke tests (own folder, see SmokeMode): no update checks, no temperature service
                Settings.AutoUpdate = false;
                Settings.Temperatures = false;
                Log("Test de fum: pornit (fără actualizări, fără serviciul de temperatură).");
            }
            // feature switches; --safe-mode runs without the Experimental and Beta ones (nothing saved changes)
            if (safeMode) Log("Pornit în mod sigur: funcțiile Experimental și Beta sunt oprite.");
            Core.Flags.FeatureFlags.Current = new Core.Flags.FeatureFlags(Settings.Features, safeMode: safeMode,
                save: () => Dispatcher.BeginInvoke(new Action(() => Settings.Save())), log: Log);
            Core.Diagnostics.HealthLog.Start(Log);
            Core.Diagnostics.PerfProbe.Init(AppSettings.Folder);
            CrashTestIfAsked();
            try { ThemeManager.Apply(Settings); } catch (Exception ex) { Log("Tema: " + ex.Message); }
            if (JustUpdated) Services.Updater.CleanUp();
            _notch = new NotchWindow(Settings);
            _notch.Show();
            _notch.StartActivities();                                  // P13: the alerts' manager; idle with its switch off
            if (Features.Smoke.SmokeMode.On) _notch.StartSmoke();
            _tray = new TrayIcon(this);
            RegisterActions();
            _notch.StartCommandBar();                                  // P14: the shortcut, only with its switch on
            Features.Context.ContextStartup.Start(_notch, Log);       // what the user is doing now (P12); off with its switch
            _notch.StartQuickActions();                                // P20: buttons under the pill by context; idle with its switch off
            _notch.StartSmartClipboard();                              // P21: chips in the Clipboard widget; idle with its switch off
            StartHealthTimer();
        }

        /// <summary>Watches the starts of this version (crashes → safe mode → previous version). Null in the helper modes.</summary>
        internal static Core.Update.StartupGuard Guard { get; private set; }

        /// <summary>Set once after an automatic rollback to this version; the notch shows it and clears it.</summary>
        internal static string RollbackMessage { get; set; }

        /// <summary>A version this PC rolled back from is not offered again.</summary>
        internal static bool IsRefusedVersion(Core.Update.AppVersion v) => Guard?.IsRefused(v) ?? false;

        private bool _handedOver;
        private Timer _healthTimer;

        /// <summary>
        /// Records the start and acts on the decision (StartupGuard; the order of the steps is in StartupCoordinator).
        /// True when this run is in safe mode. When it restarts in safe mode or rolls back, <see cref="_handedOver"/> is
        /// set and the app is shutting down.
        /// </summary>
        private bool StartGuarded(bool safeModeArg)
        {
            var guard = new Core.Update.StartupGuard(new Core.Update.FileStartupStore(AppSettings.Folder), Services.Updater.Current, log: Log);
            Guard = guard;
            var outcome = Core.Update.StartupCoordinator.Run(guard, new StartupHost(), safeModeArg, Core.Flags.FeatureFlags.SafeModeArg);
            RollbackMessage = guard.RollbackMessage;
            if (outcome == Core.Update.StartupOutcome.HandedOver || outcome == Core.Update.StartupOutcome.Quit)
            {
                _handedOver = true;
                Shutdown();
                return false;
            }
            return outcome == Core.Update.StartupOutcome.ContinueSafe;
        }

        /// <summary>The real side of the startup steps: own exe lock, single-instance mutex, process start by full path.</summary>
        private sealed class StartupHost : Core.Update.IStartupHost
        {
            public string ExePath => Environment.ProcessPath;

            public bool PreviousExeValid() => Core.Update.StartupGuard.IsValidPrevious(Services.Updater.PreviousVersion(), Services.Updater.Current);

            public void ReleaseExeLock() => ReleaseOwnExe();
            public void LockExe() => LockOwnExe();
            public void ReleaseMutex() => ReleaseSingleInstance();

            public bool TakeMutex()
            {
                try
                {
                    var m = new Mutex(true, "WinNotch.SingleInstance", out bool created);
                    if (!created) { m.Dispose(); return false; }
                    _single = m;
                    return true;
                }
                catch { return false; }
            }

            public bool Start(string exe, string args)
            {
                try
                {
                    // a restart of a smoke-test run (safe mode, rollback) stays in smoke mode: its own folder, no updates
                    if (Features.Smoke.SmokeMode.On) args = string.IsNullOrEmpty(args) ? Features.Smoke.SmokeMode.Arg : args + " " + Features.Smoke.SmokeMode.Arg;
                    var psi = args == null ? new ProcessStartInfo(exe) : new ProcessStartInfo(exe, args);
                    psi.UseShellExecute = false;
                    psi.WorkingDirectory = Path.GetDirectoryName(exe);
                    Process.Start(psi);
                    return true;
                }
                catch (Exception ex) { Log("Pornire: " + ex.GetType().Name); return false; }
            }

            public void Log(string message) => App.Log(message);
        }

        /// <summary>
        /// Every minute: 10 minutes without unhandled errors make this version healthy, and only then the previous exe
        /// goes. Started once the notch and the tray icon exist (a start that failed halfway is never "healthy").
        /// </summary>
        private void StartHealthTimer()
        {
            var guard = Guard;
            if (guard == null) return;
            _healthTimer = new Timer(_ =>
            {
                try { if (guard.CheckHealthy()) Services.Updater.DeletePrevious(); }
                catch (Exception ex) { Log("Pornire, verificarea sănătății: " + ex.GetType().Name); }
            }, null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
        }

        /// <summary>
        /// Every capability as an action (Core/Actions), registered once, after the feature flags; the Command Bar (P14)
        /// searches and starts them. A problem here is logged and never stops the app.
        /// </summary>
        private void RegisterActions()
        {
            try
            {
                var registry = new Core.Actions.ActionRegistry(Core.Flags.FeatureFlags.Current, new Features.Actions.WpfUiDispatcher(), Log);
                Features.Actions.BuiltInActions.Register(registry, new Features.Actions.AppActionHost(this, _notch));
                Features.Context.ContextActions.Register(registry, () => Core.Context.ContextEngine.Current, new Features.Context.NotchContextHost(_notch));
                Features.Activity.ActivityActions.Register(registry, new NotchActivityHost());
                Features.CommandBar.SettingsActions.Register(registry, new Features.CommandBar.AppSettingsHost(this));
                Features.QuickActions.QuickActionsActions.Register(registry, new NotchQuickActionsHost(_notch));
                Features.SmartClipboard.SmartClipboardActions.Register(registry, new Features.SmartClipboard.NotchSmartClipboardHost(_notch), _notch.SmartClipboardCache);
                Core.Actions.ActionRegistry.Current = registry;
            }
            catch (Exception ex) { Log("Acțiuni: înregistrarea a eșuat: " + ex.GetType().Name); }
        }

        /// <summary>Manual test of the crash protection: with crash-test.flag in the settings folder, a crash 5 s after start.</summary>
        private static void CrashTestIfAsked()
        {
            try { if (!File.Exists(Path.Combine(AppSettings.Folder, "crash-test.flag"))) return; } catch { return; }
            new Thread(() => { Thread.Sleep(5000); throw new InvalidOperationException("Închidere bruscă de test"); }) { IsBackground = true }.Start();
        }

        /// <summary>Elevation can't change while the process runs, so it's read once.</summary>
        public static bool IsAdmin { get; } = ReadIsAdmin();

        private static bool ReadIsAdmin()
        {
            try { using var id = WindowsIdentity.GetCurrent(); return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator); }
            catch { return false; }
        }

        /// <summary>Settings are a page of the one WinNotch window (with the pages and the themes).</summary>
        public void OpenSettings() => OpenEditor("settings");

        /// <summary>P14, the "settings.*" actions: Settings, scrolled to one option (x:Name in SettingsWindow.xaml) and focused.</summary>
        public void OpenSettingsAt(string target)
        {
            OpenEditor("settings");
            _editor?.RevealSetting(target);
        }

        /// <summary>
        /// The WinNotch window: a page (a page id, "themes" or "settings"), optionally with a widget selected. Brought
        /// to the front even though the click came from the notch, which never takes the focus.
        /// </summary>
        public void OpenEditor(string pageId = null, string slotId = null)
        {
            if (_notch == null) return;
            if (_editor == null)
            {
                _editor = new EditorWindow(Settings, _notch);
                _editor.Closed += (s, e) => _editor = null;
                _editor.Open(pageId, slotId);
                _editor.Show();
            }
            else _editor.Open(pageId, slotId);
            if (!_editor.IsVisible) _editor.Show();
            if (_editor.WindowState == WindowState.Minimized) _editor.WindowState = WindowState.Normal;
            var hwnd = new System.Windows.Interop.WindowInteropHelper(_editor).Handle;
            _editor.Topmost = true;                    // over everything for a moment, so it can't open hidden behind the app you were in
            Services.Native.ForceForeground(hwnd);
            _editor.Activate();
            _editor.Topmost = false;
        }

        /// <summary>A page was edited in the notch: the WinNotch window, if open on it, shows the new layout.</summary>
        public void PageChangedInNotch(string pageId) => _editor?.PageChangedElsewhere(pageId);

        /// <summary>Pages were shown, hidden or added in the notch: the WinNotch window's page list follows.</summary>
        public void PagesChangedInNotch() => _editor?.PagesChangedElsewhere();

        public void ToggleNotch() => _notch?.ToggleByHotkey();

        /// <summary>Tray menu: look for a new version now (also when automatic checks are off).</summary>
        public void CheckUpdates() { if (_notch != null) _ = _notch.CheckUpdateFromMenu(); }

        /// <summary>
        /// One UAC prompt: installs the temperature helper (SYSTEM task from Program Files). WinNotch keeps running
        /// with normal rights and reads the CPU temperature from it.
        /// </summary>
        public void InstallTempHelper(bool remove = false, Action done = null)
        {
            var t = new Thread(() =>
            {
                bool ok = Services.TempHelper.RunElevated(remove ? Services.TempHelper.UninstallArg : Services.TempHelper.InstallArg);
                Dispatcher.Invoke(() =>
                {
                    if (!remove && ok) { Settings.Temperatures = true; Settings.Save(); _notch?.ApplySettings(); }
                    done?.Invoke();
                    MessageBox.Show(ok ? (remove ? "Serviciul de temperatură a fost dezinstalat." :
                                                   "Gata. Temperatura procesorului apare în câteva secunde (dacă driverul PawnIO e instalat).")
                                       : "Nu s-a putut face (ai refuzat confirmarea sau a apărut o eroare; detalii în log).", "WinNotch");
                });
            }) { IsBackground = true };
            t.Start();
        }

        public void RestartAsAdmin()
        {
            try
            {
                var psi = new ProcessStartInfo(Environment.ProcessPath) { UseShellExecute = true, Verb = "runas" };
                Guard?.MarkCleanExit();           // before the new process records its own start
                ReleaseSingleInstance();
                Process.Start(psi);
                ExitApp();
            }
            catch (Exception ex)
            {
                // User pressed "No" on the UAC prompt: keep running as we are.
                Log("Repornire ca administrator anulată: " + ex.Message);
                _single = new Mutex(true, "WinNotch.SingleInstance", out _);
                Guard?.Resume();
            }
        }

        internal static void ReleaseSingleInstance()
        {
            try { _single?.ReleaseMutex(); _single?.Dispose(); _single = null; } catch { }
        }

        public void ExitApp()
        {
            Guard?.MarkCleanExit();
            _healthTimer?.Dispose();
            try { Core.Context.ContextEngine.Current?.Dispose(); } catch { }
            try { _notch?.Cleanup(); } catch { }
            try { _tray?.Dispose(); } catch { }
            Core.Diagnostics.HealthLog.Stop();
            Settings?.Save();
            ReleaseSingleInstance();
            Shutdown();
        }

        private static readonly object LogLock = new object();

        public static void Log(string msg)
        {
            try
            {
                lock (LogLock)
                {
                    Directory.CreateDirectory(AppSettings.Folder);
                    using var hold = AppSettings.HoldFolder();
                    string path = Path.Combine(AppSettings.Folder, "log.txt");
                    if (!AppSettings.SafeToWrite(path)) return;
                    // Too big: empty it in place (never delete, which a junction could redirect to another file).
                    if (File.Exists(path) && new FileInfo(path).Length > 512 * 1024) using (new FileStream(path, FileMode.Truncate)) { }
                    File.AppendAllText(path, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + msg + Environment.NewLine);
                }
            }
            catch { }
        }
    }
}
