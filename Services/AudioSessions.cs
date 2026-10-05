using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace WinNotch.Services
{
    /// <summary>One app that has an audio stream on the default speakers/headphones.</summary>
    public sealed class AudioSource
    {
        public uint Pid;
        public string Process = "";     // e.g. "chrome"
        public string Name = "";        // e.g. "Chrome"
        public float Peak;              // smoothed level 0..1, how loud it is right now
        public float Volume;            // app volume 0..1
        public bool Muted;
        public bool Sounding => Peak > 0.012f && !Muted;
        /// <summary>Last time this app was actually heard (or muted while playing).</summary>
        public DateTime LastSound;
        /// <summary>Making sound now, or a moment ago (quiet parts of a video shouldn't make it vanish).</summary>
        public bool Active => (DateTime.Now - LastSound).TotalSeconds < (Muted ? 600 : 5);
        internal AudioSessionControl Session;                                    // the loudest stream
        internal List<AudioSessionControl> Sessions = new List<AudioSessionControl>(); // every stream of the app
    }

    /// <summary>
    /// Per-app audio from Windows (the same data as the volume mixer): who is making sound, how loud, and the
    /// app's own volume and mute. Must be used from the UI thread.
    /// </summary>
    public sealed class AudioSessionsService : IDisposable
    {
        private MMDeviceEnumerator _enum;
        private MMDevice _dev;
        private readonly Dictionary<uint, string> _names = new Dictionary<uint, string>();
        private Dictionary<string, float> _smooth = new Dictionary<string, float>();      // per stream, not per process
        private readonly Dictionary<string, DateTime> _lastSound = new Dictionary<string, DateTime>();
        private int _checks;

        public List<AudioSource> Sources { get; private set; } = new List<AudioSource>();
        private static readonly uint OwnPid = (uint)Environment.ProcessId;

        public void Scan()
        {
            var list = new List<AudioSource>();
            try
            {
                _enum ??= new MMDeviceEnumerator();
                // Every ~2 s: has the default output changed? Then read the apps on the new device.
                if (_dev != null && ++_checks % 5 == 0)
                {
                    using var cur = _enum.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                    if (cur.ID != _dev.ID) { _dev.Dispose(); _dev = null; }
                }
                _dev ??= _enum.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                var mgr = _dev.AudioSessionManager;
                mgr.RefreshSessions();
                var sessions = mgr.Sessions;
                var smooth = new Dictionary<string, float>();
                for (int i = 0; i < sessions.Count; i++)
                {
                    var s = sessions[i];
                    try
                    {
                        if (s.IsSystemSoundsSession || s.State == AudioSessionState.AudioSessionStateExpired) { s.Dispose(); continue; }
                        uint pid = s.GetProcessID;
                        if (pid == 0 || pid == OwnPid) { s.Dispose(); continue; }     // WinNotch itself (its visualizer listens to the output)
                        float peak = s.AudioMeterInformation.MasterPeakValue;
                        string sid = s.GetSessionInstanceIdentifier ?? pid.ToString();
                        _smooth.TryGetValue(sid, out float prev);
                        float sm = Math.Max(peak, prev * 0.8f);      // fall slowly so short pauses don't flicker
                        smooth[sid] = sm;
                        var proc = ProcessName(pid);
                        // Several streams from one app (Discord voice + notifications, browser tabs): one entry.
                        // It's muted only if every stream is; volume and level come from the loudest; mute/volume act on all.
                        var existing = list.FirstOrDefault(x => x.Process == proc);
                        if (existing != null)
                        {
                            existing.Sessions.Add(s);
                            existing.Muted &= s.SimpleAudioVolume.Mute;
                            if (sm > existing.Peak) { existing.Peak = sm; existing.Session = s; existing.Pid = pid; existing.Volume = s.SimpleAudioVolume.Volume; }
                            continue;
                        }
                        list.Add(new AudioSource
                        {
                            Pid = pid,
                            Process = proc,
                            Name = Friendly(proc),
                            Peak = sm,
                            Volume = s.SimpleAudioVolume.Volume,
                            Muted = s.SimpleAudioVolume.Mute,
                            Session = s,
                            Sessions = new List<AudioSessionControl> { s }
                        });
                    }
                    catch { }
                }
                _smooth = smooth;          // streams that ended are forgotten
            }
            catch (Exception ex)
            {
                App.Log("Sesiuni audio: " + ex.Message);
                _dev = null;          // default device changed or unplugged: pick it up again next time
            }
            var now = DateTime.Now;
            foreach (var a in list)
            {
                if (a.Peak > 0.012f) _lastSound[a.Process] = now;
                _lastSound.TryGetValue(a.Process, out a.LastSound);
            }
            Sources = list.OrderByDescending(x => x.Sounding).ThenByDescending(x => x.Peak).ToList();
        }

        public void SetVolume(AudioSource s, float v)
        {
            v = Math.Clamp(v, 0f, 1f);
            foreach (var x in s.Sessions) try { x.SimpleAudioVolume.Volume = v; } catch { }
            s.Volume = v;
        }

        public void ToggleMute(AudioSource s)
        {
            bool m = !s.Muted;
            foreach (var x in s.Sessions) try { x.SimpleAudioVolume.Mute = m; } catch { }
            s.Muted = m;
            if (m && s.Active) _lastSound[s.Process] = DateTime.Now;    // keep it listed so it can be unmuted
        }

        private string ProcessName(uint pid)
        {
            if (_names.TryGetValue(pid, out var n)) return n;
            try
            {
                string path = Native.ProcessPathFromPid(pid);
                n = path != null ? Path.GetFileNameWithoutExtension(path) : Process.GetProcessById((int)pid).ProcessName;
            }
            catch { n = "pid " + pid; }
            _names[pid] = n.ToLowerInvariant();
            return _names[pid];
        }

        public static string Friendly(string proc)
        {
            switch (proc)
            {
                case "chrome": return "Chrome";
                case "msedge": return "Edge";
                case "firefox": return "Firefox";
                case "opera": case "opera_gx": return "Opera";
                case "brave": return "Brave";
                case "spotify": return "Spotify";
                case "discord": return "Discord";
                case "teams": case "ms-teams": return "Teams";
                case "vlc": return "VLC";
                case "steam": case "steamwebhelper": return "Steam";
                case "zoom": return "Zoom";
                case "whatsapp": return "WhatsApp";
                default: return proc.Length > 0 ? char.ToUpper(proc[0]) + proc.Substring(1) : proc;
            }
        }

        public void Dispose()
        {
            try { _dev?.Dispose(); _enum?.Dispose(); } catch { }
        }
    }
}
