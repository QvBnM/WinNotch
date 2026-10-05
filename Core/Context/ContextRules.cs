using System;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace WinNotch.Core.Context
{
    /// <summary>
    /// The parts of the snapshot that are worked out from the raw sources: fullscreen kind, meeting, audio output kind.
    /// Pure functions (no Windows calls), so they are tested directly.
    /// </summary>
    public static class ContextRules
    {
        /// <summary>
        /// Game: exclusive Direct3D fullscreen or a known game. Video: a media app, or a browser playing something itself
        /// (music from Spotify while a page is on F11 is not a video).
        /// Other: anything else that covers the monitor (presentation, F11 on a page).
        /// </summary>
        public static FullscreenKind Fullscreen(ForegroundInfo fg, AppCategory category, MediaState media)
        {
            if (fg == null || (!fg.CoversMonitor && !fg.ExclusiveFullscreen)) return FullscreenKind.None;
            if (fg.ExclusiveFullscreen || category == AppCategory.Game) return FullscreenKind.Game;
            if (category == AppCategory.Media || (category == AppCategory.Browser && MediaBelongsTo(media, fg.Process))) return FullscreenKind.Video;
            return FullscreenKind.Other;
        }

        /// <summary>
        /// The media plays in the app in front: same process (browser tabs), or the media session's id / app name contains
        /// the process name ("MSEdge" for msedge, "Spotify.exe" for spotify), as the notch's "source in front" check does.
        /// </summary>
        public static bool MediaBelongsTo(MediaState media, string foregroundProcess)
        {
            if (media == null || !media.Playing) return false;
            string exe = AppCategories.Normalize(foregroundProcess);
            if (exe.Length < 3 || exe == "applicationframehost" || exe == "explorer") return false;
            if (AppCategories.Normalize(media.Process) == exe) return true;
            string ids = ((media.AppId ?? "") + " " + (media.App ?? "")).ToLowerInvariant();
            return ids.Contains(exe, StringComparison.Ordinal);
        }

        /// <summary>
        /// Whole words in a meeting app's window title that mean a call is on (compared without diacritics). Whole words
        /// only: Teams' "Calls" / "Apeluri" section is not a call.
        /// </summary>
        private static readonly string[] MeetingWords = { "meeting", "sedinta", "intalnire", "call", "apel", "webinar", "huddle" };

        /// <summary>A Google Meet room code ("abc-defg-hij") right after "Meet - " at the start of the tab title.</summary>
        private static readonly Regex MeetRoom = new Regex(@"^meet\s*[-–—]\s*[a-z]{3}-[a-z]{4}-[a-z]{3}\b", RegexOptions.CultureInvariant);

        /// <summary>"Ședința echipei | Microsoft Teams" → sedinta, echipei, microsoft, teams.</summary>
        private static string[] Words(string title)
        {
            var d = (title ?? "").ToLowerInvariant().Normalize(NormalizationForm.FormD);
            var b = new StringBuilder(d.Length);
            foreach (char c in d)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
                b.Append(char.IsLetterOrDigit(c) ? c : ' ');
            }
            return b.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        }

        private static bool HasMeetingWord(string title) => Words(title).Any(w => MeetingWords.Contains(w));

        /// <summary>
        /// The meeting going on now, or null. In this order: an app from the Meeting category uses the microphone or the
        /// camera; a meeting app is in front with a meeting window ("Zoom Meeting", "Ședință | Microsoft Teams"); a browser
        /// is in front on Google Meet (a room code in the title, or "Google Meet" while a browser uses the microphone), or on
        /// Teams / Zoom on the web. Just having Teams open, or its Calls section, is not a meeting.
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
            string title = (fg.Title ?? "").ToLowerInvariant().Trim();
            if (title.Length == 0) return null;
            var cat = categories.Categorize(fg.Process);
            if (cat == AppCategory.Meeting && HasMeetingWord(title))
                return categories.DisplayName(fg.Process);
            if (cat == AppCategory.Browser)
            {
                bool browserOnMic = capture.MicrophoneInUse && (capture.MicrophoneApps ?? Array.Empty<string>()).Any(a => categories.Categorize(a) == AppCategory.Browser);
                if (MeetRoom.IsMatch(title)) return "Meet";
                if (browserOnMic && (title.StartsWith("meet ", StringComparison.Ordinal) || title.Contains("google meet", StringComparison.Ordinal) ||
                                     title.Contains("meet.google.com", StringComparison.Ordinal)))
                    return "Meet";
                if (title.Contains("microsoft teams", StringComparison.Ordinal) && HasMeetingWord(title))
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
