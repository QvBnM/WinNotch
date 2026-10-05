using System;
using System.Linq;

namespace WinNotch.Core.Context
{
    /// <summary>
    /// The parts of the snapshot that are worked out from the raw sources: fullscreen kind, meeting, audio output kind.
    /// Pure functions (no Windows calls), so they are tested directly.
    /// </summary>
    public static class ContextRules
    {
        /// <summary>
        /// Game: exclusive Direct3D fullscreen or a known game. Video: a media app, or a browser while something plays.
        /// Other: anything else that covers the monitor (presentation, F11 on a page).
        /// </summary>
        public static FullscreenKind Fullscreen(ForegroundInfo fg, AppCategory category, MediaState media)
        {
            if (fg == null || (!fg.CoversMonitor && !fg.ExclusiveFullscreen)) return FullscreenKind.None;
            if (fg.ExclusiveFullscreen || category == AppCategory.Game) return FullscreenKind.Game;
            if (category == AppCategory.Media || (category == AppCategory.Browser && (media?.Playing ?? false))) return FullscreenKind.Video;
            return FullscreenKind.Other;
        }

        /// <summary>Words in a meeting app's window title that mean a call is on (the main window has none of them).</summary>
        private static readonly string[] MeetingWords =
            { "meeting", "ședință", "ședinta", "sedinta", "întâlnire", "intalnire", "call", "apel", "webinar", "huddle" };

        /// <summary>
        /// The meeting going on now, or null. In this order: an app from the Meeting category uses the microphone or the
        /// camera; a meeting app is in front with a meeting window ("Zoom Meeting", "Ședință | Microsoft Teams"); a
        /// browser is in front on Google Meet / Teams / Zoom on the web. Just having Teams open is not a meeting.
        /// </summary>
        public static string Meeting(ForegroundInfo fg, CaptureState capture, AppCategories categories)
        {
            categories ??= AppCategories.Default;
            capture ??= CaptureState.None;
            var users = (capture.MicrophoneInUse ? capture.MicrophoneApps : Enumerable.Empty<string>())
                        .Concat(capture.CameraInUse ? capture.CameraApps : Enumerable.Empty<string>());
            foreach (var app in users)
                if (categories.Categorize(app) == AppCategory.Meeting) return categories.DisplayName(app);

            if (fg == null) return null;
            string title = (fg.Title ?? "").ToLowerInvariant();
            if (title.Length == 0) return null;
            var cat = categories.Categorize(fg.Process);
            if (cat == AppCategory.Meeting && MeetingWords.Any(w => title.Contains(w, StringComparison.Ordinal)))
                return categories.DisplayName(fg.Process);
            if (cat == AppCategory.Browser)
            {
                if (title.StartsWith("meet - ", StringComparison.Ordinal) || title.StartsWith("meet – ", StringComparison.Ordinal) ||
                    title.Contains("google meet", StringComparison.Ordinal) || title.Contains("meet.google.com", StringComparison.Ordinal))
                    return "Meet";
                if (title.Contains("microsoft teams", StringComparison.Ordinal) && MeetingWords.Any(w => title.Contains(w, StringComparison.Ordinal)))
                    return "Teams";
                if (title.Contains("zoom meeting", StringComparison.Ordinal) || title.Contains("zoom webinar", StringComparison.Ordinal))
                    return "Zoom";
            }
            return null;
        }

        /// <summary>
        /// Kind of playback device from what Windows says about it: its form factor (PKEY_AudioEndpoint_FormFactor:
        /// 1 speakers, 2 line level, 3 headphones, 5 headset, 6 handset, 8 S/PDIF, 9 HDMI display), the id of the device
        /// behind it (Bluetooth ones come from BTHENUM / BTHHFENUM / BTHLEDevice) and, as a last hint, its name.
        /// </summary>
        public static AudioOutputKind AudioOutput(int? formFactor, string deviceInstance, string name)
        {
            string inst = (deviceInstance ?? "").ToUpperInvariant();
            string n = (name ?? "").ToLowerInvariant();
            if (inst.Contains("BTHENUM") || inst.Contains("BTHHFENUM") || inst.Contains("BTHLEDEVICE") || n.Contains("bluetooth") || n.Contains("hands-free"))
                return AudioOutputKind.Bluetooth;
            switch (formFactor)
            {
                case 3: case 5: case 6: return AudioOutputKind.Headphones;
                case 1: case 2: case 8: case 9: return AudioOutputKind.Speakers;
            }
            if (n.Contains("headphone") || n.Contains("headset") || n.Contains("căști") || n.Contains("casti")) return AudioOutputKind.Headphones;
            if (n.Contains("speaker") || n.Contains("difuzor") || n.Contains("boxe")) return AudioOutputKind.Speakers;
            return AudioOutputKind.Unknown;
        }
    }
}
