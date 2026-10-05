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
            DispatcherUnhandledException += (s, ex) => { Log("Eroare neprevăzută: " + ex.Exception); ex.Handled = true; };
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
                MessageBox.Show("WinNotch rulează deja. Îl găsești în zona de notificări, lângă ceas.", "WinNotch",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                Shutdown();
                return;
            }

            AppDomain.CurrentDomain.UnhandledException += (s, ex) => Log("Eroare fatală: " + ex.ExceptionObject);

            Settings = AppSettings.Load();
            // feature switches; --safe-mode runs without the Experimental and Beta ones (nothing saved changes)
            bool safeMode = Array.IndexOf(e.Args, Core.Flags.FeatureFlags.SafeModeArg) >= 0;
            if (safeMode) Log("Pornit în mod sigur: funcțiile Experimental și Beta sunt oprite.");
            Core.Flags.FeatureFlags.Current = new Core.Flags.FeatureFlags(Settings.Features, safeMode: safeMode,
                save: () => Dispatcher.BeginInvoke(new Action(() => Settings.Save())), log: Log);
            Core.Diagnostics.HealthLog.Start(Log);
            try { ThemeManager.Apply(Settings); } catch (Exception ex) { Log("Tema: " + ex.Message); }
            if (JustUpdated) Services.Updater.CleanUp();
            _notch = new NotchWindow(Settings);
            _notch.Show();
            _tray = new TrayIcon(this);
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
                ReleaseSingleInstance();
                Process.Start(psi);
                ExitApp();
            }
            catch (Exception ex)
            {
                // User pressed "No" on the UAC prompt: keep running as we are.
                Log("Repornire ca administrator anulată: " + ex.Message);
                _single = new Mutex(true, "WinNotch.SingleInstance", out _);
            }
        }

        internal static void ReleaseSingleInstance()
        {
            try { _single?.ReleaseMutex(); _single?.Dispose(); _single = null; } catch { }
        }

        public void ExitApp()
        {
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
