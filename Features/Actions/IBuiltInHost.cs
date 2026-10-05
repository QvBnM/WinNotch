using System.Collections.Generic;
using System.Threading.Tasks;

namespace WinNotch.Features.Actions
{
    public enum WindowCommand { Topmost, NextMonitor, Half, Mini }

    /// <summary>A removable drive that can be ejected.</summary>
    public sealed class DriveItem
    {
        public string Root;     // "E:\"
        public string Name;     // "SanDisk Ultra"
    }

    /// <summary>
    /// What the built-in actions call: the app's existing services and notch commands (the real one in
    /// <see cref="AppActionHost"/>, a fake in the tests). No new logic lives behind it.
    /// </summary>
    public interface IBuiltInHost
    {
        int Volume { get; }
        void SetVolume(int percent);
        bool Muted { get; }
        void ToggleMute();
        bool MicMuted { get; set; }

        bool HasMedia { get; }
        void PlayPause();
        void Next();
        void Previous();

        void ScreenshotFull();
        void ScreenshotArea();
        void TextFromScreen();
        void FreeMemory();

        /// <summary>There is a window to act on (the last app you were in, not the notch).</summary>
        bool HasTargetWindow { get; }
        int MonitorCount { get; }
        void Window(WindowCommand command);

        IReadOnlyList<string> WorkspaceNames();
        Task OpenWorkspaceAsync(string name);

        IReadOnlyList<DriveItem> RemovableDrives();
        bool Eject(string root);

        /// <summary>ms-settings links, through the app's launch path (Services.Shell.Open).</summary>
        void OpenUri(string uri);

        void OpenNotch();
        void OpenSettings();
        void StartSpeedTest();
    }
}
