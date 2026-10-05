using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using WinNotch.Core.Actions;
using WinNotch.Services;

namespace WinNotch.Features.Actions
{
    /// <summary>The real <see cref="IBuiltInHost"/>: the same calls the notch's buttons make. UI thread only (the registry sees to it).</summary>
    internal sealed class AppActionHost : IBuiltInHost
    {
        private readonly App _app;
        private readonly NotchWindow _n;

        public AppActionHost(App app, NotchWindow notch) { _app = app; _n = notch; }

        public int Volume => _n.Audio.Volume;
        public void SetVolume(int percent) => _n.SetMasterVolume(percent);
        public bool Muted => _n.Audio.Muted;
        public void ToggleMute() => _n.ToggleMasterMute();
        public bool MicMuted { get => _n.Audio.MicMuted; set => _n.Audio.MicMuted = value; }

        public bool HasMedia { get { var m = _n.Now?.Info; return m != null && (m.Tab != null || m.HasSession); } }
        public void PlayPause() => _n.Now?.PlayPause();
        public void Next() => _n.Now?.Next();
        public void Previous() => _n.Now?.Previous();

        public void ScreenshotFull() => _n.ScreenshotFull();
        public void ScreenshotArea() => _n.ScreenshotArea();
        public void TextFromScreen() => _n.TextFromScreen();
        public void FreeMemory() => _n.OptimizeMemory();

        public bool HasTargetWindow => _n.LastForeground != IntPtr.Zero;
        public int MonitorCount { get { try { return System.Windows.Forms.Screen.AllScreens.Length; } catch { return 1; } } }

        public void Window(WindowCommand command)
        {
            var h = _n.LastForeground;
            if (h == IntPtr.Zero) return;
            switch (command)
            {
                case WindowCommand.Topmost: WindowTools.ToggleTopmost(h); break;
                case WindowCommand.NextMonitor: WindowTools.MoveToNextMonitor(h); break;
                case WindowCommand.Half: WindowTools.SnapHalf(h); break;
                case WindowCommand.Mini: WindowTools.Mini(h); break;
            }
        }

        public IReadOnlyList<string> WorkspaceNames() => _n.S.Workspaces.Select(w => w.Name).ToList();

        public Task OpenWorkspaceAsync(string name)
        {
            var ws = _n.S.Workspaces.FirstOrDefault(w => w.Name == name);
            return ws == null ? Task.CompletedTask : WindowTools.RestoreAsync(ws, _n.Hwnd);
        }

        public IReadOnlyList<DriveItem> RemovableDrives()
        {
            try
            {
                return DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Removable && d.IsReady)
                    .Select(d => new DriveItem { Root = d.RootDirectory.FullName, Name = string.IsNullOrWhiteSpace(d.VolumeLabel) ? "stick-ul USB" : d.VolumeLabel })
                    .ToList();
            }
            catch { return new List<DriveItem>(); }
        }

        public bool Eject(string root) => DeviceService.Eject(root);

        public void OpenUri(string uri)
        {
            try { Shell.Open(uri); }
            catch (Exception ex) { App.Log("Acțiune, setări Windows: " + ex.GetType().Name); }
        }

        public void OpenNotch() { if (!_n.IsOpen) _n.ToggleByHotkey(); }
        public void OpenSettings() => _app.OpenSettings();

        public void StartSpeedTest()
        {
            if (!_n.IsOpen) _n.ToggleByHotkey();
            _n.OpenSpeedTest();
        }
    }

    /// <summary>Runs action work on the WPF UI thread.</summary>
    internal sealed class WpfUiDispatcher : IUiDispatcher
    {
        public Task<ActionResult> InvokeAsync(Func<Task<ActionResult>> work)
        {
            var d = Application.Current?.Dispatcher;
            if (d == null || d.CheckAccess()) return work();
            return d.InvokeAsync(work).Task.Unwrap();
        }
    }
}
