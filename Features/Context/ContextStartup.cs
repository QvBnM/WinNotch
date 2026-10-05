using System;
using System.Windows;
using System.Windows.Threading;
using WinNotch.Core.Context;
using WinNotch.Core.Flags;

namespace WinNotch.Features.Context
{
    /// <summary>Builds the app's context engine from the Windows sources and the notch's existing services.</summary>
    internal static class ContextStartup
    {
        /// <summary>Called once from App.StartApp, after the feature flags and the notch. Never throws.</summary>
        public static ContextEngine Start(NotchWindow notch, Action<string> log)
        {
            try
            {
                var ui = Application.Current.Dispatcher;
                void OnUi(Action a)
                {
                    if (ui.CheckAccess()) a();
                    else ui.InvokeAsync(a, DispatcherPriority.Normal);      // in order, never blocking the caller
                }
                var sources = new ContextSources
                {
                    Foreground = new ForegroundSource(OnUi),
                    Media = new MediaSource(notch?.Now),
                    Privacy = new PrivacySource(() => !(notch?.IsOpen ?? false)),      // standby = notch closed (a plain read of its mode)
                    Audio = new AudioOutputSource(),
                    Network = new NetworkSource(),
                    Power = new PowerSource(),
                    Display = new DisplaySource(),
                    UsbDrive = new UsbDriveSource(),
                    Idle = new IdleSource(),
                };
                var engine = new ContextEngine(sources, FeatureFlags.Current, log: log);
                ContextEngine.Current = engine;
                engine.Start();
                return engine;
            }
            catch (Exception ex)
            {
                log?.Invoke("Context: pornirea a eșuat: " + ex.GetType().Name);
                return null;
            }
        }
    }

    /// <summary>"context.show" in the notch: a live alert, like the other tool alerts.</summary>
    internal sealed class NotchContextHost : IContextShowHost
    {
        private readonly NotchWindow _n;
        public NotchContextHost(NotchWindow notch) { _n = notch; }
        public void ShowContext(string title, string detail) => _n?.ShowContextAlert(title, detail);
    }
}
