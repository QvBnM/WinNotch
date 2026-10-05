using System;
using System.Collections.Generic;
using System.Linq;

namespace WinNotch.Core.Context
{
    /// <summary>What kind of app is in front (see <see cref="AppCategories"/>). Other = not in the table.</summary>
    public enum AppCategory { Other, Dev, Browser, Meeting, Game, Media, Office, Creator }

    /// <summary>The app in front covers its whole monitor: a game, a video, or something else (a presentation, F11).</summary>
    public enum FullscreenKind { None, Game, Video, Other }

    /// <summary>The default playback device.</summary>
    public enum AudioOutputKind { Unknown, Speakers, Headphones, Bluetooth }

    public enum NetworkKind { Unknown, Offline, WiFi, Ethernet }

    /// <summary>Which parts of the context changed (<see cref="ContextChangedEventArgs.Fields"/>).</summary>
    [Flags]
    public enum ContextField
    {
        None = 0,
        Foreground = 1,
        Fullscreen = 2,
        Media = 4,
        Microphone = 8,
        Camera = 16,
        AudioOutput = 32,
        Meeting = 64,
        Network = 128,
        Power = 256,
        Monitors = 512,
        UsbDrive = 1024,
        Idle = 2048,
    }

    /// <summary>
    /// What the user is doing right now, as one immutable value (change it with <c>with { … }</c>). Built by
    /// <see cref="ContextEngine"/> from its sources. <see cref="ForegroundTitle"/> is personal (document names, chats,
    /// pages): it never goes to the log; <see cref="ToLogString"/> leaves it out.
    /// </summary>
    public sealed record ContextSnapshot
    {
        public static readonly ContextSnapshot Empty = new ContextSnapshot();

        /// <summary>Process name of the app in front, lowercase, without ".exe" ("chrome", "ms-teams"); "" = none.</summary>
        public string ForegroundProcess { get; init; } = "";
        /// <summary>Its window title. Personal: shown nowhere outside the app, never logged.</summary>
        public string ForegroundTitle { get; init; } = "";
        public AppCategory ForegroundCategory { get; init; }
        public FullscreenKind Fullscreen { get; init; }

        public bool MediaPlaying { get; init; }
        /// <summary>The app or site that plays ("Spotify", "YouTube"); "" when nothing plays.</summary>
        public string MediaApp { get; init; } = "";

        public bool MicrophoneInUse { get; init; }
        public IReadOnlyList<string> MicrophoneApps { get => _micApps; init => _micApps = value ?? Array.Empty<string>(); }
        private readonly IReadOnlyList<string> _micApps = Array.Empty<string>();
        public bool CameraInUse { get; init; }
        public IReadOnlyList<string> CameraApps { get => _camApps; init => _camApps = value ?? Array.Empty<string>(); }
        private readonly IReadOnlyList<string> _camApps = Array.Empty<string>();

        public AudioOutputKind AudioOutput { get; init; }

        public bool MeetingActive { get; init; }
        /// <summary>"Teams", "Zoom", "Meet"…; "" when no meeting.</summary>
        public string MeetingApp { get; init; } = "";

        public bool Online { get; init; }
        public NetworkKind Network { get; init; }

        public bool HasBattery { get; init; }
        /// <summary>Running on battery (unplugged). False on a desktop PC.</summary>
        public bool OnBattery { get; init; }
        /// <summary>0..100, -1 = unknown or no battery.</summary>
        public int BatteryPercent { get; init; } = -1;

        public int MonitorCount { get; init; }
        public bool UsbDriveConnected { get; init; }
        /// <summary>No keyboard or mouse input for at least <see cref="ContextEngine.IdleAfter"/>.</summary>
        public bool Idle { get; init; }

        /// <summary>The parts that differ between two snapshots (titles count as Foreground).</summary>
        public static ContextField Diff(ContextSnapshot a, ContextSnapshot b)
        {
            a ??= Empty; b ??= Empty;
            var f = ContextField.None;
            if (a.ForegroundProcess != b.ForegroundProcess || a.ForegroundTitle != b.ForegroundTitle || a.ForegroundCategory != b.ForegroundCategory) f |= ContextField.Foreground;
            if (a.Fullscreen != b.Fullscreen) f |= ContextField.Fullscreen;
            if (a.MediaPlaying != b.MediaPlaying || a.MediaApp != b.MediaApp) f |= ContextField.Media;
            if (a.MicrophoneInUse != b.MicrophoneInUse || !a.MicrophoneApps.SequenceEqual(b.MicrophoneApps)) f |= ContextField.Microphone;
            if (a.CameraInUse != b.CameraInUse || !a.CameraApps.SequenceEqual(b.CameraApps)) f |= ContextField.Camera;
            if (a.AudioOutput != b.AudioOutput) f |= ContextField.AudioOutput;
            if (a.MeetingActive != b.MeetingActive || a.MeetingApp != b.MeetingApp) f |= ContextField.Meeting;
            if (a.Online != b.Online || a.Network != b.Network) f |= ContextField.Network;
            if (a.HasBattery != b.HasBattery || a.OnBattery != b.OnBattery || a.BatteryPercent != b.BatteryPercent) f |= ContextField.Power;
            if (a.MonitorCount != b.MonitorCount) f |= ContextField.Monitors;
            if (a.UsbDriveConnected != b.UsbDriveConnected) f |= ContextField.UsbDrive;
            if (a.Idle != b.Idle) f |= ContextField.Idle;
            return f;
        }

        /// <summary>For the log: category and process name, never the window title or media titles.</summary>
        public string ToLogString() =>
            "aplicație " + (ForegroundProcess.Length > 0 ? ForegroundProcess : "-") + " (" + ForegroundCategory + ")" +
            ", ecran complet " + Fullscreen +
            ", întâlnire " + (MeetingActive ? MeetingApp : "nu") +
            ", ieșire " + AudioOutput +
            ", rețea " + Network +
            ", " + (OnBattery ? "baterie" : "priză") +
            ", monitoare " + MonitorCount +
            (UsbDriveConnected ? ", stick USB" : "") +
            (Idle ? ", inactiv" : "");
    }

    /// <summary>One change of the context, after the debounce.</summary>
    public sealed class ContextChangedEventArgs : EventArgs
    {
        public ContextChangedEventArgs(ContextSnapshot old, ContextSnapshot @new, ContextField fields)
        {
            Old = old; New = @new; Fields = fields;
        }

        public ContextSnapshot Old { get; }
        public ContextSnapshot New { get; }
        /// <summary>Exactly the parts that differ between <see cref="Old"/> and <see cref="New"/>.</summary>
        public ContextField Fields { get; }

        public bool Has(ContextField f) => (Fields & f) != 0;
    }
}
