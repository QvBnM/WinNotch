using System;
using System.Diagnostics;
using System.IO;

namespace WinNotch.Services
{
    /// <summary>Opens apps, files, folders and ms-settings links.</summary>
    public static class Shell
    {
        /// <summary>
        /// When WinNotch runs as administrator (for CPU temperatures), whatever it starts would inherit that.
        /// Going through Explorer starts it with normal rights instead, like a click in the Start menu.
        /// </summary>
        public static void Open(string target, string workingDir = null)
        {
            if (string.IsNullOrWhiteSpace(target)) return;
            if (App.IsAdmin)
            {
                Process.Start(new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"),
                    "\"" + target.Replace("\"", "") + "\"") { UseShellExecute = false });
                return;
            }
            var psi = new ProcessStartInfo(target) { UseShellExecute = true };
            if (!string.IsNullOrEmpty(workingDir)) psi.WorkingDirectory = workingDir;
            Process.Start(psi);
        }
    }
}
