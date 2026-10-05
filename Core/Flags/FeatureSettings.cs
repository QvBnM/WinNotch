using System;
using System.Collections.Concurrent;

namespace WinNotch
{
    public sealed partial class AppSettings
    {
        /// <summary>
        /// Feature switches the user changed (feature id → on/off). A missing key (or a settings.json from before 0.7
        /// without "Features") means the catalog's default. Concurrent: a feature can be switched off automatically from
        /// a background thread while the settings are being saved.
        /// </summary>
        public ConcurrentDictionary<string, bool> Features { get; set; } = NewFeatures();

        internal static ConcurrentDictionary<string, bool> NewFeatures() => new ConcurrentDictionary<string, bool>(StringComparer.Ordinal);

        /// <summary>Called after loading: "Features": null (hand-edited file) becomes empty.</summary>
        internal void NormalizeFeatures() => Features ??= NewFeatures();
    }
}
