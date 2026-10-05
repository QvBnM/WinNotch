using System;
using System.Collections.Generic;
using System.Linq;
using WinNotch.Core.Actions;
using WinNotch.Core.Context;

namespace WinNotch.Features.Context
{
    /// <summary>Shows a short text in the notch (the real one in NotchContextHost, a fake in the tests).</summary>
    public interface IContextShowHost
    {
        void ShowContext(string title, string detail);
    }

    /// <summary>The context engine's actions: "context.show" (debugging: what the engine sees right now).</summary>
    public static class ContextActions
    {
        public const string ShowId = "context.show";
        internal const string GInfo = "";

        public static ActionDescriptor CreateShow(Func<ContextEngine> engine, IContextShowHost host) =>
            new ActionDescriptor(ShowId, "Arată contextul curent",
                (args, ct) =>
                {
                    var (title, detail) = Describe(engine()?.Snapshot ?? ContextSnapshot.Empty);
                    host.ShowContext(title, detail);
                    return ActionResult.OkTask(title + " · " + detail);
                },
                () => engine()?.Running ?? false)
            {
                Aliases = new[] { "context", "ce fac acum", "context curent", "depanare context", "show context", "debug context", "current context" },
                Category = "WinNotch",
                Icon = GInfo,
                FeatureId = ContextEngine.FeatureId,
                RequiresUiThread = true,
                UnavailableMessage = "Motorul de context e oprit (Setări › Funcții noi).",
            };

        /// <summary>Registered once at startup, from App.RegisterActions.</summary>
        public static void Register(ActionRegistry registry, Func<ContextEngine> engine, IContextShowHost host) =>
            registry.Register(CreateShow(engine, host));

        public static string CategoryName(AppCategory c) => c switch
        {
            AppCategory.Dev => "Programare",
            AppCategory.Browser => "Browser",
            AppCategory.Meeting => "Întâlnire",
            AppCategory.Game => "Joc",
            AppCategory.Media => "Media",
            AppCategory.Office => "Birou",
            AppCategory.Creator => "Creație",
            _ => "Altele",
        };

        /// <summary>
        /// A short Romanian summary for the notch: category and process of the app in front (never its window title),
        /// then only what is going on (fullscreen, meeting, media, microphone, camera…).
        /// </summary>
        public static (string Title, string Detail) Describe(ContextSnapshot s)
        {
            s ??= ContextSnapshot.Empty;
            string title = s.ForegroundProcess.Length == 0 ? "Context: nicio aplicație în față"
                : "Context: " + CategoryName(s.ForegroundCategory) + " · " + s.ForegroundProcess;
            var parts = new List<string>();
            if (s.Fullscreen != FullscreenKind.None)
                parts.Add("ecran complet (" + (s.Fullscreen == FullscreenKind.Game ? "joc" : s.Fullscreen == FullscreenKind.Video ? "video" : "altceva") + ")");
            if (s.MeetingActive) parts.Add("întâlnire " + s.MeetingApp);
            if (s.MediaPlaying) parts.Add("redă " + (s.MediaApp.Length > 0 ? s.MediaApp : "ceva"));
            if (s.MicrophoneInUse) parts.Add("microfon" + (s.MicrophoneApps.Count > 0 ? ": " + string.Join(", ", s.MicrophoneApps) : ""));
            if (s.CameraInUse) parts.Add("cameră" + (s.CameraApps.Count > 0 ? ": " + string.Join(", ", s.CameraApps) : ""));
            parts.Add(s.AudioOutput switch
            {
                AudioOutputKind.Headphones => "căști",
                AudioOutputKind.Speakers => "boxe",
                AudioOutputKind.Bluetooth => "Bluetooth",
                _ => "ieșire audio necunoscută",
            });
            parts.Add(s.Network switch
            {
                NetworkKind.WiFi => "Wi-Fi",
                NetworkKind.Ethernet => "Ethernet",
                NetworkKind.Offline => "offline",
                _ => "rețea necunoscută",
            });
            parts.Add(s.OnBattery ? "baterie" + (s.BatteryPercent >= 0 ? " " + s.BatteryPercent + "%" : "") : "la priză");
            if (s.MonitorCount > 0) parts.Add(s.MonitorCount == 1 ? "1 monitor" : s.MonitorCount + " monitoare");
            if (s.UsbDriveConnected) parts.Add("stick USB");
            parts.Add(s.Idle ? "inactiv" : "activ");
            return (title, string.Join(" · ", parts));
        }
    }
}
