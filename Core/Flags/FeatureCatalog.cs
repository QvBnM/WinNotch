using System;
using System.Collections.Generic;
using System.Linq;

namespace WinNotch.Core.Flags
{
    /// <summary>How finished a feature is. Safe mode (--safe-mode) treats Experimental and Beta as off.</summary>
    public enum FeatureStage { Experimental, Beta, Stable }

    /// <summary>One switchable feature: what Settings shows and what it starts as when the user never touched it.</summary>
    public sealed class FeatureInfo
    {
        public FeatureInfo(string id, string name, string description, FeatureStage stage, bool defaultOn)
        {
            Id = id; Name = name; Description = description; Stage = stage; DefaultOn = defaultOn;
        }

        /// <summary>Stable key in settings.json ("Features"): lowercase words with dashes, never renamed once shipped.</summary>
        public string Id { get; }
        /// <summary>Shown in Settings (Romanian).</summary>
        public string Name { get; }
        /// <summary>One line, shown under the name (Romanian).</summary>
        public string Description { get; }
        public FeatureStage Stage { get; }
        /// <summary>Off until the version that announces the feature.</summary>
        public bool DefaultOn { get; }
    }

    /// <summary>
    /// Every switchable feature, declared in one place. A new feature adds one entry here (off by default), then checks
    /// <see cref="FeatureFlags.IsEnabled"/> and listens to <see cref="FeatureFlags.Changed"/> to start or stop itself.
    /// </summary>
    public static class FeatureCatalog
    {
        public const string DemoFlag = "demo-flag";

        public static readonly IReadOnlyList<FeatureInfo> All = new[]
        {
            new FeatureInfo(DemoFlag, "Funcție de test", "Nu face nimic vizibil; verifică faptul că pornirea și oprirea funcțiilor noi merg.",
                            FeatureStage.Experimental, false),
        };

        public static FeatureInfo Find(string id) => All.FirstOrDefault(f => string.Equals(f.Id, id, StringComparison.Ordinal));

        /// <summary>Romanian label for a stage, as shown in Settings.</summary>
        public static string StageName(FeatureStage s) => s switch
        {
            FeatureStage.Experimental => "Experimental",
            FeatureStage.Beta => "Beta",
            _ => "Stabil",
        };
    }
}
