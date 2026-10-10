namespace WinNotch.Features.Performance
{
    /// <summary>
    /// The real <see cref="IPerfHost"/>: the WinNotch window, on the Performanță tab. UI thread (the registry sees to
    /// it). The tab is part of the new window, so with that switch off the action says what is missing instead of
    /// opening the old window on a page that does not exist there.
    /// </summary>
    internal sealed class AppPerfHost : IPerfHost
    {
        private readonly App _app;
        public AppPerfHost(App app) { _app = app; }

        public string OpenPerformance()
        {
            if (!(Core.Flags.FeatureFlags.Current?.IsEnabled(WindowV2.LayoutRules.FeatureId) ?? false))
                return "Pornește și „Fereastra WinNotch v2” (Setări › Funcții noi): fila Performanță e acolo.";
            _app.OpenEditor(WindowV2.LayoutRules.Performance);
            return null;
        }
    }
}
