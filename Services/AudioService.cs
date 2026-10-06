using System;
using NAudio.CoreAudioApi;

namespace WinNotch.Services
{
    /// <summary>Master volume of the default playback device, with change notifications.</summary>
    public sealed class AudioService : IDisposable
    {
        private MMDeviceEnumerator _enum;
        private MMDevice _dev;

        /// <summary>Raised (on a background thread) with the new volume 0..100 and mute state.</summary>
        public event Action<int, bool> Changed;

        public void Start()
        {
            try
            {
                _enum = new MMDeviceEnumerator();
                _dev = _enum.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                _dev.AudioEndpointVolume.OnVolumeNotification += OnNotify;
            }
            catch (Exception ex) { App.Log("Audio indisponibil: " + ex.Message); }
        }

        /// <summary>
        /// Called every few seconds: when the default output changes (headphones plugged in, Bluetooth connected),
        /// follow it, so the volume controls and the volume alert act on the device you actually hear.
        /// </summary>
        public void CheckDevice()
        {
            try
            {
                _enum ??= new MMDeviceEnumerator();
                using var cur = _enum.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                if (_dev != null && cur.ID == _dev.ID) return;
                if (_dev != null) { try { _dev.AudioEndpointVolume.OnVolumeNotification -= OnNotify; } catch { } _dev.Dispose(); }
                _dev = _enum.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                _dev.AudioEndpointVolume.OnVolumeNotification += OnNotify;
                Changed?.Invoke(Volume, Muted);
            }
            catch { /* no output device right now */ }
        }

        /// <summary>
        /// Windows calls this from its own audio thread, through COM: an exception escaping into unmanaged code ends the
        /// process, so a subscriber that throws is logged and the rest goes on (P51c).
        /// </summary>
        private void OnNotify(AudioVolumeNotificationData d)
        {
            try { Changed?.Invoke((int)Math.Round(d.MasterVolume * 100), d.Muted); }
            catch (Exception ex) { App.Log("Volum, anunțul Windows: " + ex.GetType().Name); }
        }

        public int Volume
        {
            get
            {
                try { return _dev == null ? 0 : (int)Math.Round(_dev.AudioEndpointVolume.MasterVolumeLevelScalar * 100); }
                catch { return 0; }
            }
            set
            {
                try { if (_dev != null) _dev.AudioEndpointVolume.MasterVolumeLevelScalar = Math.Clamp(value, 0, 100) / 100f; }
                catch { }
            }
        }

        public bool Muted
        {
            get { try { return _dev != null && _dev.AudioEndpointVolume.Mute; } catch { return false; } }
            set { try { if (_dev != null) _dev.AudioEndpointVolume.Mute = value; } catch { } }
        }

        /// <summary>Default microphone mute (affects every app).</summary>
        public bool MicMuted
        {
            get
            {
                try { using var mic = (_enum ??= new MMDeviceEnumerator()).GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications); return mic.AudioEndpointVolume.Mute; }
                catch { return false; }
            }
            set
            {
                try { using var mic = (_enum ??= new MMDeviceEnumerator()).GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications); mic.AudioEndpointVolume.Mute = value; }
                catch (Exception ex) { App.Log("Microfon: " + ex.Message); }
            }
        }

        public string OutputName => DeviceName(DataFlow.Render, Role.Multimedia);
        public string InputName => DeviceName(DataFlow.Capture, Role.Communications);

        private string DeviceName(DataFlow flow, Role role)
        {
            try { using var d = (_enum ??= new MMDeviceEnumerator()).GetDefaultAudioEndpoint(flow, role); return d.FriendlyName; }
            catch { return "—"; }
        }

        public void Dispose()
        {
            try
            {
                if (_dev != null) _dev.AudioEndpointVolume.OnVolumeNotification -= OnNotify;
                _dev?.Dispose();
                _enum?.Dispose();
            }
            catch { }
        }
    }
}
