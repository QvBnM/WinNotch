using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using WinNotch.Core.Context;
using WinNotch.Services;
using CaptureState = WinNotch.Core.Context.CaptureState;     // NAudio has one too

namespace WinNotch.Features.Context
{
    /// <summary>Common part of the Windows sources: the Changed event, raised safely.</summary>
    internal abstract class SourceBase : IContextSource
    {
        public abstract string Name { get; }
        public virtual bool Polled => false;
        public event Action Changed;
        protected void Raise() { try { Changed?.Invoke(); } catch { /* the engine never throws from its handler */ } }
        public virtual void Start() { }
        public virtual void Stop() { }
    }

    /// <summary>
    /// The app in front. SetWinEventHook(EVENT_SYSTEM_FOREGROUND) tells when it changes (no polling for that); the hook
    /// needs a thread with a message loop, so it is set and removed on the UI thread. Also polled, only to notice a window
    /// that goes fullscreen while it stays in front (one GetWindowRect every few seconds).
    /// </summary>
    internal sealed class ForegroundSource : SourceBase, IForegroundSource
    {
        private const uint EVENT_SYSTEM_FOREGROUND = 0x0003, WINEVENT_OUTOFCONTEXT = 0x0000;
        private const int QUNS_RUNNING_D3D_FULL_SCREEN = 3;

        private delegate void WinEventProc(IntPtr hook, uint ev, IntPtr hwnd, int idObject, int idChild, uint thread, uint time);
        [DllImport("user32.dll")] private static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr module, WinEventProc proc, uint pid, uint thread, uint flags);
        [DllImport("user32.dll")] private static extern bool UnhookWinEvent(IntPtr hook);
        [DllImport("shell32.dll")] private static extern int SHQueryUserNotificationState(out int state);
        private delegate bool EnumChildProc(IntPtr hwnd, IntPtr lParam);
        [DllImport("user32.dll")] private static extern bool EnumChildWindows(IntPtr parent, EnumChildProc proc, IntPtr lParam);

        private static readonly string[] ShellClasses = { "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd" };

        private readonly Action<Action> _onUi;
        private readonly WinEventProc _proc;         // kept in a field: the hook calls it after this method returns
        private IntPtr _hook;

        /// <param name="onUi">Runs work on the UI thread (in order, never blocking the caller).</param>
        public ForegroundSource(Action<Action> onUi)
        {
            _onUi = onUi;
            _proc = (h, ev, hwnd, obj, child, thread, time) => Raise();
        }

        public override string Name => "foreground";
        public override bool Polled => true;

        public override void Start() => _onUi(() =>
        {
            if (_hook != IntPtr.Zero) return;
            _hook = SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, IntPtr.Zero, _proc, 0, 0, WINEVENT_OUTOFCONTEXT);
            if (_hook == IntPtr.Zero) App.Log("Context: hook-ul pentru aplicația din față nu a pornit (rămâne verificarea periodică).");
        });

        public override void Stop() => _onUi(() =>
        {
            if (_hook == IntPtr.Zero) return;
            UnhookWinEvent(_hook);
            _hook = IntPtr.Zero;
        });

        public ForegroundInfo Read()
        {
            var h = Native.GetForegroundWindow();
            if (h == IntPtr.Zero) return ForegroundInfo.None;
            if (ShellClasses.Contains(Native.ClassName(h))) return new ForegroundInfo { Process = "explorer" };    // desktop, taskbar
            string path = Native.ProcessPath(h) ?? "";
            if (Path.GetFileName(path).Equals("ApplicationFrameHost.exe", StringComparison.OrdinalIgnoreCase))
                path = HostedAppPath(h) ?? path;                                     // Store apps: the real app is a child window

            bool covers = false;
            if (Native.GetWindowRect(h, out var r) && !Native.IsZoomed(h))
            {
                var info = new Native.MONITORINFO { cbSize = Marshal.SizeOf(typeof(Native.MONITORINFO)) };
                if (Native.GetMonitorInfo(Native.MonitorFromWindow(h, Native.MONITOR_DEFAULTTONEAREST), ref info))
                {
                    var b = info.rcMonitor;
                    covers = r.Left <= b.Left && r.Top <= b.Top && r.Right >= b.Right && r.Bottom >= b.Bottom;
                }
            }
            bool exclusive = false;
            try { exclusive = SHQueryUserNotificationState(out int st) == 0 && st == QUNS_RUNNING_D3D_FULL_SCREEN; } catch { }
            return new ForegroundInfo { Process = Path.GetFileName(path), Title = Native.Title(h), CoversMonitor = covers, ExclusiveFullscreen = exclusive };
        }

        private static string HostedAppPath(IntPtr frame)
        {
            Native.GetWindowThreadProcessId(frame, out uint framePid);
            string found = null;
            EnumChildWindows(frame, (c, l) =>
            {
                Native.GetWindowThreadProcessId(c, out uint pid);
                if (pid == framePid || pid == 0) return true;
                found = Native.ProcessPathFromPid(pid);
                return found == null;
            }, IntPtr.Zero);
            return found;
        }
    }

    /// <summary>
    /// Microphone and camera use, from PrivacyService. The microphone is polled (its 2 s cache is read every second by the
    /// notch anyway). The camera is not: Windows tells when its registry key changes (RegNotifyChangeKeyValue), and only
    /// then is it read again (see <see cref="CameraRefresh"/>). If that can't be watched, it is read every 10 s while the
    /// notch is open and never in standby.
    /// </summary>
    internal sealed class PrivacySource : SourceBase, IPrivacyContextSource
    {
        private const string WebcamKey = @"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\webcam";
        private const int REG_NOTIFY_CHANGE_NAME = 0x1, REG_NOTIFY_CHANGE_LAST_SET = 0x4, REG_NOTIFY_THREAD_AGNOSTIC = 0x10000000;

        [DllImport("advapi32.dll")]
        private static extern int RegNotifyChangeKeyValue(SafeRegistryHandle key, bool watchSubtree, int filter, SafeWaitHandle evt, bool async);

        private readonly Func<bool> _standby;
        private readonly object _lock = new object();
        private CameraRefresh _camera = new CameraRefresh(watching: false);
        private CapabilityUse _cam;
        private RegistryKey _key;
        private AutoResetEvent _signal;
        private RegisteredWaitHandle _wait;

        /// <param name="standby">The notch is closed.</param>
        public PrivacySource(Func<bool> standby) { _standby = standby ?? (() => false); }

        public override string Name => "privacy";
        public override bool Polled => true;

        public override void Start()
        {
            lock (_lock)
            {
                _cam = null;
                string why = null;
                try
                {
                    _key = Registry.CurrentUser.OpenSubKey(WebcamKey);
                    if (_key == null) why = "nicio cameră folosită până acum";      // nothing to watch yet
                    else
                    {
                        _signal = new AutoResetEvent(false);
                        if (!Arm()) why = "notificarea a fost refuzată";
                        else _wait = ThreadPool.RegisterWaitForSingleObject(_signal, (st, timedOut) => OnCameraKey(), null, Timeout.Infinite, false);
                    }
                }
                catch (Exception ex) { why = ex.GetType().Name; }
                if (why == null) _camera = new CameraRefresh(watching: true);
                else
                {
                    CloseWatch();
                    _camera = new CameraRefresh(watching: false);
                    App.Log("Context: camera nu poate fi urmărită prin notificări (" + why + "); o verific la 10 s, doar cu notch-ul deschis.");
                }
            }
        }

        public override void Stop()
        {
            lock (_lock) CloseWatch();
        }

        /// <summary>Asks Windows for the next change (one notification per call; any thread may call it again).</summary>
        private bool Arm() => _key != null && _signal != null &&
            RegNotifyChangeKeyValue(_key.Handle, true, REG_NOTIFY_CHANGE_NAME | REG_NOTIFY_CHANGE_LAST_SET | REG_NOTIFY_THREAD_AGNOSTIC, _signal.SafeWaitHandle, true) == 0;

        private void OnCameraKey()
        {
            lock (_lock)
            {
                if (_wait == null) return;                 // stopped meanwhile
                _camera.Notified(DateTime.UtcNow);
                if (!Arm()) App.Log("Context: notificarea pentru cameră nu a putut fi reînnoită.");
            }
            Raise();
        }

        private void CloseWatch()
        {
            try { _wait?.Unregister(null); } catch { }
            _wait = null;
            try { _key?.Dispose(); } catch { }             // closing the key also ends the pending notification
            _key = null;
            try { _signal?.Dispose(); } catch { }
            _signal = null;
        }

        public CaptureState Read()
        {
            var mic = PrivacyService.Microphone();
            var now = DateTime.UtcNow;
            bool standby = _standby();
            CapabilityUse cam;
            CameraRefresh policy;
            lock (_lock) { policy = _camera; cam = _cam; }
            if (policy.ShouldRead(now, standby))
            {
                cam = PrivacyService.Camera();
                policy.MarkRead(now);
                lock (_lock) if (ReferenceEquals(policy, _camera)) _cam = cam;
            }
            if (!policy.KnownNow(standby)) cam = null;     // not watched and not read in standby: unknown, not "in use"
            return new CaptureState
            {
                MicrophoneInUse = mic.InUse, MicrophoneApps = mic.Apps.ToList(),
                CameraInUse = cam?.InUse ?? false, CameraApps = cam?.Apps.ToList() ?? new List<string>(),
            };
        }
    }

    /// <summary>
    /// The default playback device. Windows tells when it changes (IMMNotificationClient); only then is it read, with
    /// its form factor and the device behind it (Bluetooth).
    /// </summary>
    internal sealed class AudioOutputSource : SourceBase, IAudioContextSource
    {
        /// <summary>Instance id of the device behind the endpoint ("{1}.BTHENUM\…" for Bluetooth).</summary>
        private static readonly PropertyKey DeviceInstanceKey = new PropertyKey(new Guid("b3f8fa53-0004-438e-9003-51a46e139bfc"), 2);

        private MMDeviceEnumerator _enum;
        private EndpointWatcher _watcher;

        public override string Name => "audio";

        /// <summary>
        /// The enumerator is made and released on a thread-pool (MTA) thread: made on the UI thread (STA), a later Stop from
        /// a background thread would have to wait for the UI thread, which may itself be waiting to switch the engine.
        /// </summary>
        private static void OnMta(Action a)
        {
            if (Thread.CurrentThread.GetApartmentState() == ApartmentState.MTA) a();
            else Task.Run(a).GetAwaiter().GetResult();
        }

        public override void Start() => OnMta(() =>
        {
            _enum = new MMDeviceEnumerator();
            _watcher = new EndpointWatcher(Raise);
            _enum.RegisterEndpointNotificationCallback(_watcher);
        });

        public override void Stop() => OnMta(() =>
        {
            try { if (_watcher != null) _enum?.UnregisterEndpointNotificationCallback(_watcher); } catch { }
            try { _enum?.Dispose(); } catch { }
            _enum = null; _watcher = null;
        });

        public AudioOutputKind Read()
        {
            using var en = new MMDeviceEnumerator();
            if (!en.HasDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia)) return AudioOutputKind.Unknown;
            using var d = en.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            int? formFactor = null;
            string instance = null;
            try
            {
                var props = d.Properties;
                if (props.Contains(PropertyKeys.PKEY_AudioEndpoint_FormFactor)) formFactor = Convert.ToInt32(props[PropertyKeys.PKEY_AudioEndpoint_FormFactor].Value);
                if (props.Contains(DeviceInstanceKey)) instance = props[DeviceInstanceKey].Value as string;
            }
            catch { /* some drivers don't expose these: the name decides */ }
            return ContextRules.AudioOutput(formFactor, instance, d.FriendlyName);
        }
    }

    /// <summary>
    /// Windows' callback for audio device changes (a COM object, so it is public and COM-visible). By default only a new
    /// default output is reported (the context engine); P30's output list also asks for devices added, removed or changing
    /// state. Called on a COM thread: <c>changed</c> must return at once and not call COM back.
    /// </summary>
    [ComVisible(true)]
    public sealed class EndpointWatcher : IMMNotificationClient
    {
        private readonly Action _changed;
        private readonly bool _devices;
        public EndpointWatcher(Action changed, bool includeDeviceEvents = false) { _changed = changed; _devices = includeDeviceEvents; }

        public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId) { if (flow == DataFlow.Render) _changed(); }
        public void OnDeviceStateChanged(string deviceId, DeviceState newState) { if (_devices && !IsCapture(deviceId)) _changed(); }
        public void OnDeviceAdded(string pwstrDeviceId) { if (_devices && !IsCapture(pwstrDeviceId)) _changed(); }
        public void OnDeviceRemoved(string deviceId) { if (_devices && !IsCapture(deviceId)) _changed(); }

        /// <summary>
        /// Capture endpoints have ids "{0.0.1.00000000}.{…}" (render: "{0.0.0.…}"): a microphone coming or going doesn't
        /// re-read the list of outputs. A string test only (no COM in the callback); any other form counts as an output.
        /// Used only with includeDeviceEvents (the context engine's behaviour is unchanged).
        /// </summary>
        public static bool IsCapture(string deviceId) => deviceId != null && deviceId.StartsWith("{0.0.1.", StringComparison.Ordinal);
        public void OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key) { }
    }

    /// <summary>Online / Wi-Fi / Ethernet, read again when Windows reports an address or availability change.</summary>
    internal sealed class NetworkSource : SourceBase, INetworkContextSource
    {
        public override string Name => "network";

        public override void Start()
        {
            NetworkChange.NetworkAddressChanged += OnAddress;
            NetworkChange.NetworkAvailabilityChanged += OnAvailability;
        }

        public override void Stop()
        {
            NetworkChange.NetworkAddressChanged -= OnAddress;
            NetworkChange.NetworkAvailabilityChanged -= OnAvailability;
        }

        private void OnAddress(object s, EventArgs e) => Raise();
        private void OnAvailability(object s, NetworkAvailabilityEventArgs e) => Raise();

        public NetworkState Read() => NetworkState.FromConnectionType(NetService.ConnectionType());
    }

    /// <summary>Battery or plugged in: Windows' power status change (also sent when the percentage changes).</summary>
    internal sealed class PowerSource : SourceBase, IPowerContextSource
    {
        public override string Name => "power";
        public override void Start() => SystemEvents.PowerModeChanged += OnPower;
        public override void Stop() => SystemEvents.PowerModeChanged -= OnPower;
        private void OnPower(object s, PowerModeChangedEventArgs e) { if (e.Mode == PowerModes.StatusChange || e.Mode == PowerModes.Resume) Raise(); }

        public PowerState Read()
        {
            if (!Native.GetSystemPowerStatus(out var ps)) return PowerState.Unknown;
            bool has = ps.BatteryFlag != 128 && ps.BatteryFlag != 255 && ps.BatteryLifePercent != 255;      // same test as SystemStats
            return new PowerState { HasBattery = has, OnBattery = has && ps.ACLineStatus == 0, Percent = has ? ps.BatteryLifePercent : -1 };
        }
    }

    /// <summary>Number of monitors, read again when the display settings change.</summary>
    internal sealed class DisplaySource : SourceBase, IDisplayContextSource
    {
        private const int SM_CMONITORS = 80;
        [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);

        public override string Name => "display";
        public override void Start() => SystemEvents.DisplaySettingsChanged += OnDisplay;
        public override void Stop() => SystemEvents.DisplaySettingsChanged -= OnDisplay;
        private void OnDisplay(object s, EventArgs e) => Raise();
        public int Read() => GetSystemMetrics(SM_CMONITORS);
    }

    /// <summary>
    /// A USB stick is connected. Windows reports volumes coming and going (WMI Win32_VolumeChangeEvent); if that can't be
    /// watched, the drive list is polled instead.
    /// </summary>
    internal sealed class UsbDriveSource : SourceBase, IUsbDriveContextSource
    {
        private ManagementEventWatcher _watcher;
        private bool _polled;

        public override string Name => "usb";
        public override bool Polled => _polled;

        public override void Start()
        {
            try
            {
                _watcher = new ManagementEventWatcher(new WqlEventQuery("SELECT * FROM Win32_VolumeChangeEvent"));
                _watcher.EventArrived += OnVolume;
                _watcher.Start();
                _polled = false;
            }
            catch (Exception ex)
            {
                App.Log("Context: evenimentele pentru stick-uri USB nu merg (" + ex.GetType().Name + "); verific periodic.");
                DisposeWatcher();
                _polled = true;
            }
        }

        public override void Stop() => DisposeWatcher();

        private void DisposeWatcher()
        {
            if (_watcher == null) return;
            try { _watcher.EventArrived -= OnVolume; _watcher.Stop(); } catch { }
            try { _watcher.Dispose(); } catch { }
            _watcher = null;
        }

        private void OnVolume(object s, EventArrivedEventArgs e)
        {
            try { e.NewEvent?.Dispose(); } catch { }
            Raise();
        }

        public bool Read() => DriveInfo.GetDrives().Any(d => d.DriveType == DriveType.Removable && d.IsReady);
    }

    /// <summary>Time since the last input (GetLastInputInfo, a cheap call; there is no event for it).</summary>
    internal sealed class IdleSource : SourceBase, IIdleContextSource
    {
        public override string Name => "idle";
        public override bool Polled => true;
        public TimeSpan Read() => TimeSpan.FromSeconds(Native.IdleSeconds());
    }

    /// <summary>What plays now, from the same NowPlaying the notch shows (app or site name only, never the title).</summary>
    internal sealed class MediaSource : SourceBase, IMediaContextSource
    {
        private readonly NowPlaying _now;
        public MediaSource(NowPlaying now) { _now = now; }

        public override string Name => "media";
        public override void Start() { if (_now != null) _now.Changed += OnMedia; }
        public override void Stop() { if (_now != null) _now.Changed -= OnMedia; }
        private void OnMedia(bool trackChanged) => Raise();

        public MediaState Read()
        {
            var m = _now?.Info;
            if (m == null || !(m.HasSession || m.Tab != null) || !m.Playing) return MediaState.None;
            string app = m.Tab != null ? (string.IsNullOrEmpty(m.Tab.Site) ? AudioSessionsService.Friendly((m.Tab.Browser ?? "").ToLowerInvariant()) : m.Tab.Site) : m.App;
            return new MediaState
            {
                Playing = true, App = app ?? "",
                Process = m.Tab?.Browser ?? "",                // the tab's browser process ("chrome", "msedge")
                AppId = m.Tab == null ? m.AppId ?? "" : "",
            };
        }
    }
}
