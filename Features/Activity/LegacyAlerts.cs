using System;
using System.Collections.Generic;
using System.Linq;

namespace WinNotch.Features.Activity
{
    /// <summary>
    /// One of the notch's existing alerts as the legacy path shows it (NotchWindow.ShowLive, 0.6.13): where it is called,
    /// its size, how long it stays, whether it is "important" (shown over a fullscreen app) and whether it takes clicks.
    /// <para>Pinned to the source by the characterization tests (tests/AlertCharacterizationTests.cs): the call line found
    /// by <see cref="Anchor"/> in <see cref="File"/> must still contain <see cref="Tail"/> (the literal width, height,
    /// duration and "important" argument). No WPF here: the tests compile this file.</para>
    /// </summary>
    public sealed class LegacyAlert
    {
        public LegacyAlert(string id, string file, string anchor, string tail, double width, double height, int durationMs,
                           bool important, bool interactive = false, double heightPerRow = 0, string key = null)
        {
            Id = id; File = file; Anchor = anchor; Tail = tail; Width = width; Height = height; DurationMs = durationMs;
            Important = important; Interactive = interactive; HeightPerRow = heightPerRow; Key = key ?? id;
        }

        /// <summary>Stable content id ("temp-hot"); never shown, never renamed.</summary>
        public string Id { get; }
        /// <summary>Repo-relative source file of the call.</summary>
        public string File { get; }
        /// <summary>Text that finds the call line (exactly one line of <see cref="File"/> contains it).</summary>
        public string Anchor { get; }
        /// <summary>Literal arguments the call line must contain (width, height, duration, important).</summary>
        public string Tail { get; }
        public double Width { get; }
        /// <summary>Height, or the base height when it grows with rows (<see cref="HeightPerRow"/>).</summary>
        public double Height { get; }
        public double HeightPerRow { get; }
        public int DurationMs { get; }
        /// <summary>ShowLive(…, important: true): shown over a fullscreen app.</summary>
        public bool Important { get; }
        /// <summary>Has buttons: the caller turns click-through off after showing it.</summary>
        public bool Interactive { get; }
        /// <summary>
        /// Alerts of one flow share a key: the next step replaces the previous one (OCR "reading…" → "copied", RAM progress
        /// → result, update download → installing / refused). Used by the Activity Manager path (same key = update in place).
        /// </summary>
        public string Key { get; }

        public double HeightFor(int rows) => Height + HeightPerRow * rows;
    }

    /// <summary>Every alert the notch had before the Activity Manager (P13), in one table.</summary>
    public static class LegacyAlerts
    {
        public const string Notch = "NotchWindow.xaml.cs", Updates = "NotchWindow.Updates.cs", Context = "Features/Context/NotchWindow.Context.cs";

        public const string Volume = "volume", Track = "track", Power = "power", BatteryLow = "battery-low", TempHot = "temp-hot",
            Ram = "ram", RamProgress = "ram-progress", RamDone = "ram-done", CaptureResult = "capture-result", CaptureError = "capture-error",
            OcrReading = "ocr-reading", OcrDone = "ocr-done", OcrNoEngine = "ocr-no-engine", OcrNoText = "ocr-no-text", EyeBreak = "eye-break",
            ContextShow = "context", Rollback = "rollback", UpdateCheck = "update-check", UpdateOffer = "update-offer",
            UpdateDownload = "update-download", UpdateInstalling = "update-installing", UpdateRefused = "update-refused",
            UpdateReplaceFailed = "update-replace-failed", UpdateFailed = "update-failed", WhatsNew = "whats-new",
            HelperUpdate = "helper-update", OldExtension = "old-extension";

        /// <summary>Shared keys of the multi-step flows (see <see cref="LegacyAlert.Key"/>).</summary>
        public const string OcrKey = "ocr", RamOptimizeKey = "ram-optimize", UpdateInstallKey = "update-install";

        /// <summary>ToolAlert's fixed part: height 58, 4.2 s, important (the width is its last argument, 440 by default).</summary>
        public const double ToolHeight = 58;
        public const int ToolMs = 4200;

        private static LegacyAlert Tool(string id, string file, string anchor, string tail, double width, string key = null) =>
            new LegacyAlert(id, file, anchor, tail, width, ToolHeight, ToolMs, true, key: key);

        public static readonly IReadOnlyList<LegacyAlert> All = new[]
        {
            new LegacyAlert(Volume, Notch, "g, 270, 40, 1600)", "g, 270, 40, 1600)", 270, 40, 1600, false),
            new LegacyAlert(Track, Notch, "mi.Title, mi.Artist, Equalizer()", "Equalizer()), 360, 54, 3200)", 360, 54, 3200, false),
            new LegacyAlert(Power, Notch, "\"Se încarcă\" : \"Pe baterie\"", "pct), 260, 40, 2600)", 260, 40, 2600, false),
            new LegacyAlert(BatteryLow, Notch, "\"Baterie descărcată: \"", "null), 330, 54, 5000, true)", 330, 54, 5000, true),
            new LegacyAlert(TempHot, Notch, "\"Temperatură ridicată\"", "null), 360, 54, 5000, true)", 360, 54, 5000, true),
            new LegacyAlert(Ram, Notch, "66 + top.Count * 23", "content, 470, 66 + top.Count * 23, 15000)", 470, 66, 15000, false, true, 23),
            new LegacyAlert(RamProgress, Notch, "content, 430, 60, 60000, true)", "content, 430, 60, 60000, true)", 430, 60, 60000, true, key: RamOptimizeKey),
            new LegacyAlert(RamDone, Notch, "done, 520, 60, 4500, true)", "done, 520, 60, 4500, true)", 520, 60, 4500, true, key: RamOptimizeKey),
            new LegacyAlert(CaptureResult, Notch, "row, 540, 100, 5000)", "row, 540, 100, 5000)", 540, 100, 5000, true, true),
            Tool(CaptureError, Notch, "\"Captura nu a putut fi salvată\"", "\"Captura nu a putut fi salvată\", ex.Message); return;", 440),
            new LegacyAlert(OcrReading, Notch, "\"Text din ecran\"", "Spinner()), 470, 100, 30000, true)", 470, 100, 30000, true, key: OcrKey),
            new LegacyAlert(OcrDone, Notch, "\"Text copiat · \"", "COk)), 470, 100, 5000, true)", 470, 100, 5000, true, key: OcrKey),
            Tool(OcrNoEngine, Notch, "\"Windows nu are recunoaștere de text\"", "Recunoaștere optică\", 560); return;", 560, OcrKey),
            Tool(OcrNoText, Notch, "\"Nu am găsit text în zona aleasă\"", "\"Încearcă o zonă mai mare sau mai clară\"); return;", 440, OcrKey),
            new LegacyAlert(EyeBreak, Notch, "content, 360, 48, 21000, true)", "content, 360, 48, 21000, true)", 360, 48, 21000, true, true),
            new LegacyAlert(ContextShow, Context, "ContextActions.GInfo", "detail, null), 620, 58, 6000, true)", 620, 58, 6000, true),
            Tool(Rollback, Updates, "\"Versiunea refuzată nu îți mai e propusă; o versiune mai nouă, da.\"", "o versiune mai nouă, da.\", 560);", 560),
            Tool(UpdateCheck, Updates, "COk, result, null, 440)", "COk, result, null, 440)", 440),
            new LegacyAlert(UpdateOffer, Updates, "580, 64 + rows * 22, 45000)", "notes), 580, 64 + rows * 22, 45000)", 580, 64, 45000, false, true, 22),
            new LegacyAlert(UpdateDownload, Updates, "content, 470, 60, 600000, true)", "content, 470, 60, 600000, true)", 470, 60, 600000, true, key: UpdateInstallKey),
            new LegacyAlert(UpdateInstalling, Updates, "\"Repornesc în câteva secunde\"", "null), 420, 58, 60000, true)", 420, 58, 60000, true, key: UpdateInstallKey),
            Tool(UpdateRefused, Updates, "\"Actualizarea a fost refuzată\"", "nu am instalat nimic.\", 520);", 520, UpdateInstallKey),
            Tool(UpdateReplaceFailed, Updates, "\"Nu am putut înlocui WinNotch.exe\"", "merge mai departe.\", 480);", 480, UpdateInstallKey),
            Tool(UpdateFailed, Updates, "\"Actualizarea nu a reușit\"", "încerc din nou mai târziu.\", 460);", 460, UpdateInstallKey),
            new LegacyAlert(WhatsNew, Updates, "580, 64 + rows * 22, 25000, true)", "notes), 580, 64 + rows * 22, 25000, true)", 580, 64, 25000, true, true, 22),
            new LegacyAlert(HelperUpdate, Updates, "content, 560, 58, 30000, true)", "content, 560, 58, 30000, true)", 560, 58, 30000, true, true),
            new LegacyAlert(OldExtension, Updates, "content, 540, 58, 20000)", "content, 540, 58, 20000)", 540, 58, 20000, false, true),
        };

        public static LegacyAlert Find(string id) => All.FirstOrDefault(a => string.Equals(a.Id, id, StringComparison.Ordinal));
    }

    /// <summary>
    /// The legacy decisions around those alerts, as pure functions with the same semantics as the code in NotchWindow*.cs
    /// and Services/NowPlaying.cs (the characterization tests pin each expression in the source). The legacy code keeps
    /// its own copy and is not changed; these are for the tests and the Activity Manager path.
    /// </summary>
    public static class LegacyAlertRules
    {
        public static readonly TimeSpan RamCooldown = TimeSpan.FromMinutes(15), RamSustain = TimeSpan.FromSeconds(20),
            HotCooldown = TimeSpan.FromMinutes(5), TrackRepeat = TimeSpan.FromMinutes(15), TrackMinGap = TimeSpan.FromSeconds(10),
            VolumeOwnChange = TimeSpan.FromMilliseconds(600), UpdateSnooze = TimeSpan.FromHours(24);
        public const float HotThreshold = 88;
        /// <summary>The first seconds after a start: no "after update" / "rollback" alert before tick 4.</summary>
        public const int StartupTicks = 3;

        /// <summary>ShowLive's gate: never while the notch is open; over a fullscreen app (pill hidden) only if important.</summary>
        public static bool ShowLiveGate(bool notchOpen, bool hidden, bool important) => !notchOpen && !(hidden && !important);

        /// <summary>CheckRam: whether to try the RAM alert now (the cooldown starts only when it was really shown).</summary>
        public static bool RamShouldTry(bool enabled, double usedGb, double totalGb, int percentSetting, DateTime now,
                                        ref DateTime? highSince, DateTime lastAlert, bool idle, bool hidden, bool toolBusy, int topCount)
        {
            if (!enabled || totalGb <= 0) { highSince = null; return false; }
            double pct = usedGb * 100 / totalGb;
            if (pct < Math.Clamp(percentSetting, 50, 98)) { highSince = null; return false; }
            highSince ??= now;
            if ((now - lastAlert).TotalMinutes < 15) return false;
            if ((now - highSince.Value).TotalSeconds < 20 || !idle || hidden || toolBusy || topCount == 0) return false;
            return true;
        }

        /// <summary>SecondTick, heat: CPU or GPU ≥ 88 °C, more than 5 minutes after the last one (counted even if it was dropped).</summary>
        public static bool HotShouldShow(bool temperatures, float? cpu, float? gpu, DateTime now, ref DateTime lastHot)
        {
            float hot = Math.Max(cpu ?? 0, gpu ?? 0);
            if (temperatures && hot >= HotThreshold && (now - lastHot).TotalMinutes > 5) { lastHot = now; return true; }
            return false;
        }

        /// <summary>SecondTick, battery: once at 20% and once at 10%; charging resets both.</summary>
        public static bool BatteryLowStep(bool charging, int percent, ref int lastBatAlert)
        {
            if (charging) { lastBatAlert = 101; return false; }
            if (percent >= 0 && ((percent <= 10 && lastBatAlert > 10) || (percent <= 20 && lastBatAlert > 20)))
            {
                lastBatAlert = percent <= 10 ? 10 : 20;
                return true;
            }
            return false;
        }

        /// <summary>SecondTick, charger plugged in / out (not on the first reading).</summary>
        public static bool PowerChanged(bool? lastCharging, bool charging) => lastCharging != null && lastCharging.Value != charging;

        /// <summary>SecondTick, 20-20-20: active seconds (reset after 5 min idle), only in standby and not over fullscreen.</summary>
        public static bool EyeBreakStep(double idleSeconds, int minutes, bool idleMode, bool hidden, ref int activeSeconds)
        {
            if (idleSeconds > 300) activeSeconds = 0;
            else if (idleSeconds < 60) activeSeconds++;
            if (activeSeconds >= Math.Max(5, minutes) * 60 && idleMode && !hidden) { activeSeconds = 0; return true; }
            return false;
        }

        /// <summary>NowPlaying: a song is announced at most once in 15 minutes, and nothing more often than every 10 s.</summary>
        public static bool TrackAnnounce(string key, DateTime now, IDictionary<string, DateTime> announced, ref DateTime lastAnnounce)
        {
            foreach (var old in announced.Where(k => (now - k.Value).TotalMinutes > 15).Select(k => k.Key).ToList()) announced.Remove(old);
            if (announced.ContainsKey(key) || (now - lastAnnounce).TotalSeconds < 10) return false;
            announced[key] = now;
            lastAnnounce = now;
            return true;
        }

        /// <summary>OnMedia: the track alert only for a new, playing song, notch closed, source not the window in front.</summary>
        public static bool TrackGate(bool trackChanged, bool playing, bool notchOpen, bool sourceInFront) =>
            trackChanged && playing && !notchOpen && !sourceInFront;

        /// <summary>OnSystemVolume: only real changes, not while open, not right after WinNotch set the volume itself.</summary>
        public static bool VolumeShould(int v, bool muted, ref int lastVol, ref bool lastMuted, bool notchOpen, DateTime now, DateTime volSetByUs)
        {
            bool same = lastVol < 0 || (v == lastVol && muted == lastMuted);
            lastVol = v; lastMuted = muted;
            return !same && !notchOpen && (now - volSetByUs).TotalMilliseconds > 600;
        }

        /// <summary>UpdateTick: the update offer, once, in standby, not over fullscreen, not snoozed, not over another alert with buttons.</summary>
        public static bool UpdateOfferGate(bool hasUpdate, bool offered, bool idle, bool hidden, DateTime now, DateTime snoozeUntil, bool liveInteractive) =>
            hasUpdate && !offered && idle && !hidden && now >= snoozeUntil && !liveInteractive;

        /// <summary>UpdateTick: the old-extension alert, once (tried again if it could not be shown).</summary>
        public static bool OldExtensionGate(bool seen, bool shown, bool idle, bool hidden) => seen && !shown && idle && !hidden;

        /// <summary>UpdateTick: "what's new" once after an update, from the 4th second, in standby.</summary>
        public static bool AfterUpdateGate(bool justUpdated, bool shown, int tick, bool idle) => justUpdated && !shown && tick > StartupTicks && idle;

        /// <summary>UpdateTick: the rollback message once, from the 4th second, in standby.</summary>
        public static bool RollbackGate(bool hasMessage, int tick, bool idle) => hasMessage && tick > StartupTicks && idle;
    }
}
