using System;
using System.Collections.Generic;

namespace WinNotch.Core.Context
{
    /// <summary>The window in front, as the foreground source reads it.</summary>
    public sealed class ForegroundInfo
    {
        public static readonly ForegroundInfo None = new ForegroundInfo();

        /// <summary>Process file name, any case, with or without path and ".exe" (normalized by the engine).</summary>
        public string Process { get; init; } = "";
        /// <summary>Window title (personal: never logged).</summary>
        public string Title { get; init; } = "";
        /// <summary>The window covers its whole monitor (borderless or exclusive fullscreen; maximized windows don't).</summary>
        public bool CoversMonitor { get; init; }
        /// <summary>Windows reports an exclusive Direct3D fullscreen app (almost always a game).</summary>
        public bool ExclusiveFullscreen { get; init; }
    }

    public sealed class MediaState
    {
        public static readonly MediaState None = new MediaState();
        public bool Playing { get; init; }
        /// <summary>App or site name ("Spotify", "YouTube"), never the song or video title.</summary>
        public string App { get; init; } = "";
    }

    /// <summary>Microphone and camera use (the same data as Windows' privacy indicator).</summary>
    public sealed class CaptureState
    {
        public static readonly CaptureState None = new CaptureState();
        public bool MicrophoneInUse { get; init; }
        public IReadOnlyList<string> MicrophoneApps { get; init; } = Array.Empty<string>();
        public bool CameraInUse { get; init; }
        public IReadOnlyList<string> CameraApps { get; init; } = Array.Empty<string>();
    }

    public sealed class NetworkState
    {
        public static readonly NetworkState Unknown = new NetworkState();
        public bool Online { get; init; }
        public NetworkKind Kind { get; init; }

        /// <summary>From NetService.ConnectionType(): "Wi-Fi", "Ethernet", "Offline" or "" (unknown).</summary>
        public static NetworkState FromConnectionType(string type) => type switch
        {
            "Wi-Fi" => new NetworkState { Online = true, Kind = NetworkKind.WiFi },
            "Ethernet" => new NetworkState { Online = true, Kind = NetworkKind.Ethernet },
            "Offline" => new NetworkState { Online = false, Kind = NetworkKind.Offline },
            _ => Unknown,
        };
    }

    public sealed class PowerState
    {
        public static readonly PowerState Unknown = new PowerState();
        public bool HasBattery { get; init; }
        public bool OnBattery { get; init; }
        public int Percent { get; init; } = -1;
    }

    /// <summary>
    /// A piece of the context. <see cref="Changed"/> says "read me again" (any thread); the engine reads the source
    /// after its debounce, off the UI thread. Sources without a Windows event set <see cref="Polled"/> and are read
    /// every <see cref="ContextEngine.PollInterval"/> (never faster than 2 s). Start/Stop are called by the engine only,
    /// and Stop must remove every hook and subscription the source made.
    /// </summary>
    public interface IContextSource
    {
        /// <summary>Short and fixed, for the log ("foreground", "audio").</summary>
        string Name { get; }
        /// <summary>No event for this data: the engine reads it on its timer.</summary>
        bool Polled { get; }
        event Action Changed;
        void Start();
        void Stop();
    }

    /// <summary>A source of one typed piece. <see cref="Read"/> may throw: the engine keeps the last good value.</summary>
    public interface IContextSource<out T> : IContextSource
    {
        T Read();
    }

    /// <summary>The app in front: SetWinEventHook(EVENT_SYSTEM_FOREGROUND), plus polling for a window that goes fullscreen.</summary>
    public interface IForegroundSource : IContextSource<ForegroundInfo> { }
    public interface IMediaContextSource : IContextSource<MediaState> { }
    public interface IPrivacyContextSource : IContextSource<CaptureState> { }
    /// <summary>The default playback device (headphones, speakers, Bluetooth).</summary>
    public interface IAudioContextSource : IContextSource<AudioOutputKind> { }
    public interface INetworkContextSource : IContextSource<NetworkState> { }
    public interface IPowerContextSource : IContextSource<PowerState> { }
    /// <summary>Number of monitors connected.</summary>
    public interface IDisplayContextSource : IContextSource<int> { }
    /// <summary>A removable drive (USB stick) is connected.</summary>
    public interface IUsbDriveContextSource : IContextSource<bool> { }
    /// <summary>Time since the last keyboard or mouse input.</summary>
    public interface IIdleContextSource : IContextSource<TimeSpan> { }

    /// <summary>The sources the engine combines. Any can be null: that part of the snapshot keeps its default.</summary>
    public sealed class ContextSources
    {
        public IForegroundSource Foreground { get; init; }
        public IMediaContextSource Media { get; init; }
        public IPrivacyContextSource Privacy { get; init; }
        public IAudioContextSource Audio { get; init; }
        public INetworkContextSource Network { get; init; }
        public IPowerContextSource Power { get; init; }
        public IDisplayContextSource Display { get; init; }
        public IUsbDriveContextSource UsbDrive { get; init; }
        public IIdleContextSource Idle { get; init; }
    }
}
