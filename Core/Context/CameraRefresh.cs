using System;

namespace WinNotch.Core.Context
{
    /// <summary>
    /// When the privacy source reads the camera's registry state again. With registry notifications (RegNotifyChangeKeyValue):
    /// once at start, then only after a notification, for <see cref="RecheckWindow"/> (PrivacyService caches its reads 2 s,
    /// so the first read after a change can still be old). Without them: every <see cref="FallbackInterval"/> while the notch
    /// is open, never in standby, and then the camera counts as unknown (not in use) instead of keeping an old value.
    /// </summary>
    public sealed class CameraRefresh
    {
        public static readonly TimeSpan FallbackInterval = TimeSpan.FromSeconds(10);
        /// <summary>Longer than one poll + the debounce + PrivacyService's 2 s cache: one read surely sees the change.</summary>
        public static readonly TimeSpan RecheckWindow = TimeSpan.FromSeconds(6.5);

        private readonly object _lock = new object();
        private bool _readOnce;
        private DateTime _lastRead, _until;

        public CameraRefresh(bool watching) { Watching = watching; }

        /// <summary>Registry notifications work.</summary>
        public bool Watching { get; }

        /// <summary>The registry key changed (any thread).</summary>
        public void Notified(DateTime now) { lock (_lock) _until = now + RecheckWindow; }

        public bool ShouldRead(DateTime now, bool standby)
        {
            lock (_lock)
            {
                if (Watching) return !_readOnce || now < _until;
                if (standby) return false;
                return !_readOnce || now - _lastRead >= FallbackInterval;
            }
        }

        public void MarkRead(DateTime now) { lock (_lock) { _readOnce = true; _lastRead = now; } }

        /// <summary>Whether the last read value can be used now (not in standby without notifications).</summary>
        public bool KnownNow(bool standby) => Watching || !standby;
    }
}
