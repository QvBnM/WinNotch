using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using NAudio.CoreAudioApi;
using WinNotch.Features.Context;

namespace WinNotch.Features.AudioSwitch
{
    /// <summary>
    /// The Windows side of P30 (ADR 0012), the only place that touches COM for it (not compiled into the Linux tests).
    /// The list comes from NAudio (MMDeviceEnumerator, documented); the change goes through IPolicyConfig, an undocumented
    /// interface of Windows' own Sound panel (CLSID PolicyConfigClient, the Windows 7 → 11 IID). Every call runs on a
    /// thread-pool (MTA) thread, where the COM objects are made, used and released: no proxy, nothing waits for the UI
    /// thread. Per-application routing (IAudioPolicyConfigFactory, the sessions) is deliberately not used.
    /// </summary>
    internal sealed class PolicyConfigSwitcher : IAudioEndpointSwitcher
    {
        private static readonly Guid PolicyConfigClientClsid = new Guid("870af99c-171d-4f9e-af0d-e63df40c2bc9");

        /// <summary>ERole: eConsole (system sounds, most apps), eMultimedia (music, video), eCommunications (calls).</summary>
        private const int ERoleConsole = 0, ERoleMultimedia = 1, ERoleCommunications = 2;

        private static int _readFailures;

        /// <summary>Runs <paramref name="f"/> on an MTA thread (the caller's if it is one already).</summary>
        internal static T OnMta<T>(Func<T> f)
        {
            if (Thread.CurrentThread.GetApartmentState() == ApartmentState.MTA) return f();
            return Task.Run(f).GetAwaiter().GetResult();
        }

        public IReadOnlyList<AudioEndpointInfo> List() => OnMta<IReadOnlyList<AudioEndpointInfo>>(() =>
        {
            var list = new List<AudioEndpointInfo>();
            try
            {
                using var en = new MMDeviceEnumerator();
                var all = en.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
                for (int i = 0; i < all.Count; i++)
                {
                    using var d = all[i];                  // each MMDevice released right away
                    string name;
                    try { name = d.FriendlyName; } catch (COMException) { name = null; }
                    list.Add(new AudioEndpointInfo(d.ID, name));
                }
            }
            catch (COMException)
            {
                // no audio service (a server, the CI machine): no outputs, said once in the log (type only)
                if (Interlocked.Increment(ref _readFailures) == 1) App.Log("Ieșire audio: dispozitivele nu pot fi citite acum; lista e goală.");
            }
            return list;
        });

        public string GetDefault() => OnMta(() =>
        {
            try
            {
                using var en = new MMDeviceEnumerator();
                if (!en.HasDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia)) return null;
                using var d = en.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                return d.ID;
            }
            catch (COMException) { return null; }
        });

        /// <summary>
        /// All three roles, as the Sound panel's "Set Default" (console + multimedia) plus "Default Communication Device":
        /// switching to the headphones moves the calls too. Any failing HRESULT throws (the service then turns the switch off).
        /// </summary>
        public void SetDefault(string endpointId)
        {
            if (string.IsNullOrEmpty(endpointId)) throw new ArgumentException("endpoint");
            OnMta(() =>
            {
                object client = null;
                try
                {
                    var type = Type.GetTypeFromCLSID(PolicyConfigClientClsid, true);
                    client = Activator.CreateInstance(type);
                    var policy = (IPolicyConfig)client;             // QueryInterface: InvalidCastException if this Windows lacks it
                    foreach (int role in new[] { ERoleConsole, ERoleMultimedia, ERoleCommunications })
                    {
                        int hr = policy.SetDefaultEndpoint(endpointId, role);
                        if (hr != 0) Marshal.ThrowExceptionForHR(hr);
                    }
                }
                finally
                {
                    if (client != null && Marshal.IsComObject(client)) Marshal.FinalReleaseComObject(client);
                }
                return true;
            });
        }

        /// <summary>
        /// IPolicyConfig (Windows 7 → 11, IID f8679f50-…). Only SetDefaultEndpoint is called; the methods before it are
        /// declared (as placeholders) only to keep the vtable order.
        /// </summary>
        [ComImport, Guid("f8679f50-850a-41cf-9c72-430f290290c8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IPolicyConfig
        {
            [PreserveSig] int GetMixFormat(IntPtr deviceId, IntPtr format);
            [PreserveSig] int GetDeviceFormat(IntPtr deviceId, int useDefault, IntPtr format);
            [PreserveSig] int ResetDeviceFormat(IntPtr deviceId);
            [PreserveSig] int SetDeviceFormat(IntPtr deviceId, IntPtr endpointFormat, IntPtr mixFormat);
            [PreserveSig] int GetProcessingPeriod(IntPtr deviceId, int useDefault, IntPtr defaultPeriod, IntPtr minimumPeriod);
            [PreserveSig] int SetProcessingPeriod(IntPtr deviceId, IntPtr period);
            [PreserveSig] int GetShareMode(IntPtr deviceId, IntPtr mode);
            [PreserveSig] int SetShareMode(IntPtr deviceId, IntPtr mode);
            [PreserveSig] int GetPropertyValue(IntPtr deviceId, int fxStore, IntPtr key, IntPtr value);
            [PreserveSig] int SetPropertyValue(IntPtr deviceId, int fxStore, IntPtr key, IntPtr value);
            [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string deviceId, int role);
            [PreserveSig] int SetEndpointVisibility(IntPtr deviceId, int visible);
        }
    }

    /// <summary>
    /// Device notifications for the list (IMMNotificationClient through NAudio). The enumerator that holds the subscription
    /// is made and released on a thread-pool (MTA) thread, as the context engine's audio source does; the callback
    /// (<see cref="EndpointWatcher"/>, shared with it) only re-arms the service's debounce timer.
    /// </summary>
    internal sealed class AudioEndpointEvents : IAudioEndpointEvents
    {
        private MMDeviceEnumerator _enum;
        private EndpointWatcher _watcher;

        public void Start(Action changed) => PolicyConfigSwitcher.OnMta(() =>
        {
            if (_enum != null) return true;
            _enum = new MMDeviceEnumerator();
            _watcher = new EndpointWatcher(changed, includeDeviceEvents: true);
            try { _enum.RegisterEndpointNotificationCallback(_watcher); }
            catch { _enum.Dispose(); _enum = null; _watcher = null; throw; }
            return true;
        });

        public void Stop() => PolicyConfigSwitcher.OnMta(() =>
        {
            try { if (_watcher != null) _enum?.UnregisterEndpointNotificationCallback(_watcher); } catch (COMException) { }
            try { _enum?.Dispose(); } catch (COMException) { }
            _enum = null;
            _watcher = null;
            return true;
        });
    }
}
