using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace WinNotch.Services
{
    /// <summary>
    /// CPU temperature without running WinNotch as administrator.
    ///
    /// Reading the CPU sensors needs admin rights. Instead of running the whole app elevated (which made it a target:
    /// anything it reads from your user folders could steer an admin process), a tiny copy of WinNotch runs as a
    /// Windows task under the SYSTEM account, from Program Files, with `--temps`. It has no window, reads nothing from
    /// your profile, takes no input and only answers one question over a local pipe: the current temperatures.
    /// The app itself always runs with normal rights.
    /// </summary>
    public static class TempHelper
    {
        public const string PipeName = "WinNotch.Temps.v1";
        public const string TaskName = "WinNotchTemps";
        public const string Arg = "--temps", InstallArg = "--install-temps", UninstallArg = "--uninstall-temps";

        public static string ProtectedDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "WinNotch");
        public static string ProtectedExe => Path.Combine(ProtectedDir, "WinNotch.exe");
        private static string RuntimeDir => Path.Combine(ProtectedDir, "runtime");
        private static string SystemExe(string name) => Path.Combine(Environment.SystemDirectory, name);

        // ------------------------------------------------------------------ the helper (SYSTEM, --temps)

        /// <summary>Runs forever: reads the sensors every 2 s and answers each pipe connection with one line of JSON.</summary>
        public static void RunServer()
        {
            var temps = new TempService();
            temps.Start();
            var t = new Thread(() =>
            {
                while (true) { temps.RefreshAsync(); Thread.Sleep(2000); }
            }) { IsBackground = true };
            t.Start();

            var security = new PipeSecurity();
            security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null), PipeAccessRights.FullControl, AccessControlType.Allow));
            security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null), PipeAccessRights.FullControl, AccessControlType.Allow));
            // signed-in users may only read the answer (and can't create other instances of this pipe)
            security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null), PipeAccessRights.Read | PipeAccessRights.Synchronize, AccessControlType.Allow));

            // One pipe instance for the whole life of the helper (created once, so the name is never free for someone
            // else to take; FirstPipeInstance: if the name is already taken, we don't share it). It also guarantees a
            // single helper: a second one can't create the pipe and keeps retrying quietly.
            NamedPipeServerStream pipe = null;
            while (pipe == null)
            {
                try { pipe = NamedPipeServerStreamAcl.Create(PipeName, PipeDirection.Out, 1, PipeTransmissionMode.Byte, PipeOptions.FirstPipeInstance | PipeOptions.Asynchronous, 0, 0, security); }
                catch (Exception ex) { App.Log("Serviciu temperaturi, pipe: " + ex.Message); Thread.Sleep(5000); }
            }
            while (true)
            {
                try
                {
                    pipe.WaitForConnection();
                    string json = string.Format(CultureInfo.InvariantCulture, "{{\"cpu\":{0},\"gpu\":{1},\"ssd\":{2},\"load\":{3}}}\n",
                        J(temps.Cpu), J(temps.Gpu), J(temps.Ssd), J(temps.GpuLoad));
                    var bytes = Encoding.ASCII.GetBytes(json);
                    // a client that connects and never reads can't hold the pipe: everything is bounded to half a second
                    if (pipe.WriteAsync(bytes, 0, bytes.Length).Wait(500))
                        System.Threading.Tasks.Task.Run(() => { try { pipe.WaitForPipeDrain(); } catch { } }).Wait(500);
                }
                catch (Exception ex) { App.Log("Serviciu temperaturi: " + ex.Message); Thread.Sleep(200); }
                finally { try { if (pipe.IsConnected) pipe.Disconnect(); } catch { } }
            }
        }

        private static string J(float? v) => v.HasValue ? v.Value.ToString("0.0", CultureInfo.InvariantCulture) : "null";

        // ------------------------------------------------------------------ the app (normal rights) reading it

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetNamedPipeServerSessionId(IntPtr pipe, out uint sessionId);

        /// <summary>
        /// Asks the helper for the temperatures (≈1 ms). Only an answer from session 0 counts: that's where SYSTEM
        /// tasks run and a normal program can't start anything there, so a fake pipe made by another program is ignored.
        /// </summary>
        public static bool TryRead(out float? cpu, out float? gpu, out float? ssd, out float? load)
        {
            cpu = gpu = ssd = load = null;
            try
            {
                using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.In);
                pipe.Connect(150);
                if (!GetNamedPipeServerSessionId(pipe.SafePipeHandle.DangerousGetHandle(), out uint session) || session != 0) return false;
                var buf = new byte[256];
                int n = 0, r;
                while (n < buf.Length && (r = pipe.Read(buf, n, buf.Length - n)) > 0) n += r;
                using var doc = JsonDocument.Parse(Encoding.ASCII.GetString(buf, 0, n).Trim());
                float? F(string k) => doc.RootElement.TryGetProperty(k, out var e) && e.ValueKind == JsonValueKind.Number && e.TryGetSingle(out var f) && f > 0 && f < 150 ? f : (float?)null;
                cpu = F("cpu"); gpu = F("gpu"); ssd = F("ssd"); load = F("load");
                return true;
            }
            catch { return false; }
        }

        /// <summary>Is the helper installed (its copy in Program Files exists)?</summary>
        public static bool Installed => File.Exists(ProtectedExe);

        /// <summary>The installed helper is an older build than the WinNotch running now.</summary>
        public static bool Outdated
        {
            get
            {
                try
                {
                    if (!Installed) return false;
                    var a = new FileInfo(Environment.ProcessPath);
                    var b = new FileInfo(ProtectedExe);
                    return a.Length != b.Length || a.LastWriteTimeUtc != b.LastWriteTimeUtc;
                }
                catch { return false; }
            }
        }

        /// <summary>Starts this WinNotch.exe elevated (one UAC prompt) to install or remove the helper; waits for it.</summary>
        public static bool RunElevated(string arg)
        {
            try
            {
                using var p = Process.Start(new ProcessStartInfo(Environment.ProcessPath, arg) { UseShellExecute = true, Verb = "runas" });
                p?.WaitForExit(60000);
                return p != null && p.HasExited && p.ExitCode == 0;
            }
            catch (Exception ex) { App.Log("Serviciu temperaturi, pornire ca admin anulată: " + ex.Message); return false; }
        }

        // ------------------------------------------------------------------ install / remove (elevated, one-off)

        /// <summary>
        /// Elevated run (--install-temps): copies THIS exe into Program Files and registers the SYSTEM task.
        /// The copy is read from the handle WinNotch holds on its own exe since start (see App.LockOwnExe), so the
        /// file can't be renamed or swapped between the UAC prompt and the copy.
        /// </summary>
        public static int Install(FileStream ownExe)
        {
            try
            {
                Schtasks("/End /TN " + TaskName);
                Schtasks("/Delete /TN WinNotch /F");            // the old elevated-app logon task (0.6.4 and earlier)
                StopCopiesInProgramFiles();                     // the old helper or an old elevated WinNotch still running from there
                Directory.CreateDirectory(ProtectedDir);
                Directory.CreateDirectory(RuntimeDir);
                string tmp = ProtectedExe + ".new";
                using (var dst = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    ownExe.Position = 0;
                    ownExe.CopyTo(dst);
                }
                File.SetLastWriteTimeUtc(tmp, File.GetLastWriteTimeUtc(Environment.ProcessPath));
                // the old helper may still be closing
                for (int i = 0; ; i++)
                {
                    try { File.Move(tmp, ProtectedExe, true); break; }
                    catch (Exception ex) when ((ex is IOException || ex is UnauthorizedAccessException) && i < 20) { Thread.Sleep(250); }
                }

                string xml = Path.Combine(ProtectedDir, "task.xml");
                File.WriteAllText(xml, TaskXml(), Encoding.Unicode);
                int rc = Schtasks("/Create /TN " + TaskName + " /XML \"" + xml + "\" /F");
                try { File.Delete(xml); } catch { }
                if (rc != 0) return rc;
                Schtasks("/Run /TN " + TaskName);
                return 0;
            }
            catch (Exception ex) { App.Log("Instalare serviciu temperaturi: " + ex.Message); return 1; }
        }

        private static void StopCopiesInProgramFiles()
        {
            foreach (var p in Process.GetProcessesByName("WinNotch"))
            {
                try
                {
                    if (p.Id != Environment.ProcessId && string.Equals(Native.ProcessPathFromPid((uint)p.Id), ProtectedExe, StringComparison.OrdinalIgnoreCase))
                    { p.Kill(); p.WaitForExit(3000); }
                }
                catch { }
                finally { p.Dispose(); }
            }
        }

        /// <summary>The logon task of 0.6.4 and earlier (whole app elevated) still exists.</summary>
        public static bool OldTaskExists => Schtasks("/Query /TN WinNotch") == 0;

        public static int Uninstall()
        {
            Schtasks("/End /TN " + TaskName);
            StopCopiesInProgramFiles();
            int rc = Schtasks("/Delete /TN " + TaskName + " /F");
            Schtasks("/Delete /TN WinNotch /F");
            for (int i = 0; i < 20; i++)
            {
                try { if (File.Exists(ProtectedExe)) File.Delete(ProtectedExe); break; }
                catch (IOException) { Thread.Sleep(250); }
                catch (UnauthorizedAccessException) { Thread.Sleep(250); }
            }
            return rc;
        }

        /// <summary>At boot, as SYSTEM, also on battery, never stopped for running long.</summary>
        private static string TaskXml() => @"<?xml version=""1.0"" encoding=""UTF-16""?>
<Task version=""1.2"" xmlns=""http://schemas.microsoft.com/windows/2004/02/mit/task"">
  <RegistrationInfo><Description>WinNotch: citește temperatura procesorului pentru notch (fără fereastră, doar citire).</Description></RegistrationInfo>
  <Triggers><BootTrigger><Enabled>true</Enabled></BootTrigger></Triggers>
  <Principals><Principal id=""Author""><UserId>S-1-5-18</UserId><RunLevel>HighestAvailable</RunLevel></Principal></Principals>
  <Settings>
    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
    <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
    <AllowHardTerminate>true</AllowHardTerminate>
    <StartWhenAvailable>true</StartWhenAvailable>
    <Hidden>true</Hidden>
    <Priority>7</Priority>
    <RestartOnFailure><Interval>PT1M</Interval><Count>3</Count></RestartOnFailure>
  </Settings>
  <Actions Context=""Author""><Exec><Command>" + System.Security.SecurityElement.Escape(SystemExe("cmd.exe")) + @"</Command><Arguments>" + System.Security.SecurityElement.Escape(
      // The single-file exe unpacks its native DLLs at start. For SYSTEM they go into a folder only administrators can
      // write (not a temp folder where someone could plant a DLL first). /d: no AutoRun commands.
      "/d /c \"set \"DOTNET_BUNDLE_EXTRACT_BASE_DIR=" + RuntimeDir + "\" && \"" + ProtectedExe + "\" " + Arg + "\"") + @"</Arguments></Exec></Actions>
</Task>";

        /// <summary>schtasks.exe from System32 by full path (never looked up next to WinNotch.exe).</summary>
        internal static int Schtasks(string args)
        {
            try
            {
                var psi = new ProcessStartInfo(SystemExe("schtasks.exe"), args)
                {
                    CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true
                };
                using var p = Process.Start(psi);
                p.WaitForExit(10000);
                return p.HasExited ? p.ExitCode : -1;
            }
            catch { return -1; }
        }
    }
}
