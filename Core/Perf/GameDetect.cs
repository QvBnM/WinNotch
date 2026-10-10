using System;
using System.Collections.Generic;
using System.Linq;
using WinNotch.Core.Context;

namespace WinNotch.Core.Perf
{
    /// <summary>Where the detector is between two snapshots.</summary>
    public enum GameState
    {
        /// <summary>No game session.</summary>
        None,
        /// <summary>A game is in front and filling a screen.</summary>
        Running,
        /// <summary>It was running and is not in front now: the session is kept for <see cref="GameDetect.Grace"/>.</summary>
        Grace,
    }

    /// <summary>What one snapshot changed. Both flags can be set at once: one game replaced by another.</summary>
    public readonly struct GameChange
    {
        public GameChange(GameState state, string process, bool started, bool ended, string endedProcess)
        {
            State = state; Process = process ?? ""; Started = started; Ended = ended; EndedProcess = endedProcess ?? "";
        }

        public GameState State { get; }
        /// <summary>The game of the session that is now open ("" when none).</summary>
        public string Process { get; }
        /// <summary>A session just began.</summary>
        public bool Started { get; }
        /// <summary>A session just finished (the grace period ran out, or another game took over).</summary>
        public bool Ended { get; }
        /// <summary>The game of the session that just finished ("" when none ended).</summary>
        public string EndedProcess { get; }
    }

    /// <summary>
    /// P61: when a game session starts and when it ends, from the context engine's snapshots alone. Pure: no Windows,
    /// no WPF, no clock of its own (the caller passes the time), so a two-hour session is tested in a millisecond.
    /// <para><b>What counts as playing.</b> A game must both be the app in front <em>and</em> be filling a screen.
    /// Requiring the screen is what keeps the launchers out: Steam, Battle.net and the rest are in the Game category
    /// (<see cref="AppCategories"/>) because that is what they are about, but sitting in a Steam window is not
    /// playing. An exclusive-fullscreen game the table has never heard of still counts, because Windows reports it as
    /// <see cref="FullscreenKind.Game"/>.</para>
    /// <para><b>Alt-tab is not quitting.</b> Leaving the game for Discord, a browser or the desktop keeps the session
    /// open for <see cref="Grace"/>. Without that, every alt-tab would close a session and open a new one, and the
    /// report would be a pile of three-minute fragments instead of one evening.</para>
    /// <para><b>A game in a window is not detected.</b> That is a deliberate limit, not an oversight: there is no
    /// honest way to tell a windowed game from its launcher or from a wiki page about it, and a session that starts by
    /// mistake would poison the numbers it is supposed to explain.</para>
    /// </summary>
    public sealed class GameDetect
    {
        /// <summary>Same id as the entry in FeatureCatalog (the tests check they match).</summary>
        public const string FeatureId = "game-session";

        /// <summary>How long a session survives with the game out of front. An alt-tab, not a quit.</summary>
        public static readonly TimeSpan Grace = TimeSpan.FromSeconds(90);

        /// <summary>Shorter than this and no report is written: starting a game and closing it is not a session.</summary>
        public static readonly TimeSpan MinSession = TimeSpan.FromMinutes(2);

        /// <summary>
        /// Shops and launchers: in the Game category, but being in front of one is not playing. Only consulted for a
        /// window that fills the screen (a maximised Steam window is the one case where the screen rule is not enough).
        /// </summary>
        public static readonly IReadOnlyList<string> Launchers = new[]
        {
            "steam", "steamwebhelper", "epicgameslauncher", "battle.net", "riotclientservices", "eadesktop",
            "ubisoftconnect", "upc", "galaxyclient", "xboxapp", "gamebar", "leagueclient",
        };

        public static bool IsLauncher(string process) =>
            !string.IsNullOrEmpty(process) && Launchers.Contains(process.Trim().ToLowerInvariant(), StringComparer.Ordinal);

        /// <summary>Is this snapshot someone playing? See the note on the class for why the screen is required.</summary>
        public static bool Playing(ContextSnapshot s)
        {
            if (s == null) return false;
            if (s.Fullscreen == FullscreenKind.Game) return true;       // Windows itself says so: an exclusive game
            if (s.ForegroundCategory != AppCategory.Game) return false;
            if (s.Fullscreen == FullscreenKind.None) return false;      // in a window: not counted, on purpose
            return !IsLauncher(s.ForegroundProcess);
        }

        private string _process = "";
        private DateTime _leftAt;

        public GameState State { get; private set; } = GameState.None;
        /// <summary>The game of the open session, or "" when there is none.</summary>
        public string Process => State == GameState.None ? "" : _process;

        /// <summary>
        /// Feeds one snapshot. <paramref name="nowUtc"/> is the caller's clock, so nothing here reads the time.
        /// Call it on every context change <em>and</em> on a tick, or a session whose game simply disappeared would
        /// stay in <see cref="GameState.Grace"/> until the next change.
        /// </summary>
        public GameChange Update(ContextSnapshot snapshot, DateTime nowUtc)
        {
            bool playing = Playing(snapshot);
            string process = playing ? Normalize(snapshot) : "";

            // A clock that jumped backwards (an NTP correction, the user changing the time) would otherwise leave the
            // grace period never able to expire: the session would stay open for the rest of the run, which means the
            // one-second sampling and the lowered priority would stay on with no way out but the switch.
            if (nowUtc < _leftAt) _leftAt = nowUtc;

            if (playing)
            {
                if (State == GameState.None)
                {
                    _process = process;
                    State = GameState.Running;
                    return new GameChange(State, _process, started: true, ended: false, endedProcess: null);
                }
                // Same game back from an alt-tab, or a game whose name we only learn now (exclusive fullscreen first).
                if (string.Equals(_process, process, StringComparison.Ordinal) || process.Length == 0 || _process.Length == 0)
                {
                    if (process.Length > 0) _process = process;
                    State = GameState.Running;
                    return new GameChange(State, _process, false, false, null);
                }
                // Another game took over: close the old session and open a new one in the same step.
                string old = _process;
                _process = process;
                State = GameState.Running;
                return new GameChange(State, _process, started: true, ended: true, endedProcess: old);
            }

            if (State == GameState.Running)
            {
                State = GameState.Grace;
                _leftAt = nowUtc;
                return new GameChange(State, _process, false, false, null);
            }

            if (State == GameState.Grace && nowUtc - _leftAt >= Grace)
            {
                string old = _process;
                _process = "";
                State = GameState.None;
                return new GameChange(State, "", false, ended: true, endedProcess: old);
            }

            return new GameChange(State, Process, false, false, null);
        }

        /// <summary>Forgets the open session without reporting it (the feature was switched off, the app is closing).</summary>
        public void Reset()
        {
            State = GameState.None;
            _process = "";
        }

        /// <summary>
        /// The game's name. An exclusive-fullscreen game Windows reports without a readable process name gives "",
        /// and the session keeps whatever name it had.
        /// </summary>
        private static string Normalize(ContextSnapshot s) => RawProc.Normalize(s?.ForegroundProcess);
    }
}
