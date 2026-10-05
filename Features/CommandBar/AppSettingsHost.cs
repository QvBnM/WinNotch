namespace WinNotch.Features.CommandBar
{
    /// <summary>The real <see cref="ISettingsHost"/>: the WinNotch window, Settings page, at one option. UI thread (the registry sees to it).</summary>
    internal sealed class AppSettingsHost : ISettingsHost
    {
        private readonly App _app;
        public AppSettingsHost(App app) { _app = app; }
        public void OpenSettingsAt(string target) => _app.OpenSettingsAt(target);
    }
}
