using System;
using System.IO;

namespace WinNotch.Core.Update
{
    /// <summary>
    /// Puts the previous version back: WinNotch.exe → WinNotch.rejected.exe, WinNotch.old.exe → WinNotch.exe. Nothing is
    /// deleted before the swap succeeded; if any step fails the files are put back as they were.
    /// </summary>
    public static class Rollback
    {
        public const string OldName = "WinNotch.old.exe";
        public const string RejectedName = "WinNotch.rejected.exe";

        public static string OldPath(string exePath) => Path.Combine(Path.GetDirectoryName(exePath) ?? "", OldName);
        public static string RejectedPath(string exePath) => Path.Combine(Path.GetDirectoryName(exePath) ?? "", RejectedName);

        /// <summary>
        /// Swaps the files. <paramref name="releaseLocks"/> runs first (the app keeps its own exe open). False (and
        /// nothing changed) if WinNotch.old.exe is missing or a step failed.
        /// </summary>
        public static bool Swap(string exePath, Action releaseLocks, Action<string> log)
        {
            string old = OldPath(exePath), rejected = RejectedPath(exePath);
            if (!File.Exists(old)) { log?.Invoke("Revenire: versiunea anterioară lipsește; nu schimb nimic."); return false; }
            try
            {
                releaseLocks?.Invoke();
                if (File.Exists(rejected)) File.Delete(rejected);       // a version refused earlier
                File.Move(exePath, rejected);
                try { File.Move(old, exePath); }
                catch { File.Move(rejected, exePath); throw; }            // put the current one back
                return true;
            }
            catch (Exception ex)
            {
                log?.Invoke("Revenire: nu am putut schimba fișierele: " + ex.GetType().Name);
                // both moves failed: never leave the folder without WinNotch.exe (the Run entry points to it)
                if (!File.Exists(exePath) && File.Exists(rejected))
                {
                    try { File.Copy(rejected, exePath, false); log?.Invoke("Revenire: WinNotch.exe lipsea; l-am refăcut din copia curentă."); }
                    catch (Exception ex2) { log?.Invoke("Revenire: WinNotch.exe lipsește și nu a putut fi refăcut: " + ex2.GetType().Name); }
                }
                return false;
            }
        }
    }
}
