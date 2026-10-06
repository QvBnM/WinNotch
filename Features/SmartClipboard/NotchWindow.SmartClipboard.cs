using System;
using System.IO;
using System.Windows;
using WinNotch.Core.Flags;
using WinNotch.Features.SmartClipboard;

namespace WinNotch
{
    /// <summary>
    /// The notch's side of Smart Clipboard (P21, ADR 0010). With the "smart-clipboard" switch off nothing here runs: the
    /// hooks return at once and the Clipboard widget shows no chips. With it on: the text the clipboard history just
    /// recorded (so never one a password manager marked private, see <see cref="ClipboardPrivacy"/>) is the one the
    /// chips and the "clipboard.*" actions work on; with the peek option and the Activity Manager on, a recognized JSON,
    /// link with tracking or JWT gives one Low peek with a fixed title. No timer, no polling: only the clipboard
    /// notification the notch already listens to. Nothing of the content goes to the log: only on/off and a counter.
    /// </summary>
    public partial class NotchWindow
    {
        private Action<string> _scFlagHandler;
        /// <summary>UI-thread copy of the switch.</summary>
        private volatile bool _scOn;
        /// <summary>The text copied last (recorded, or written by WinNotch); null = none / private / another format. Read from any thread.</summary>
        private volatile string _scLatest;
        private readonly SmartClipCache _scCache = new SmartClipCache();
        /// <summary>Recognized copies since the switch went on: the only thing logged (a number).</summary>
        private int _scRecognized;

        private static bool SmartClipboardEnabled() => FeatureFlags.Current?.IsEnabled(SmartClipboardActions.FeatureId) ?? false;

        /// <summary>For the Clipboard widget's chips (UI thread).</summary>
        internal bool SmartClipboardOn => _scOn;
        internal string SmartClipboardText => _scOn ? _scLatest : null;
        internal SmartClip SmartClipboardCurrent() => _scOn ? _scCache.Get(_scLatest) : SmartClip.None;
        /// <summary>Shared with the "clipboard.*" actions (thread-safe): a text is analyzed once.</summary>
        internal SmartClipCache SmartClipboardCache => _scCache;

        /// <summary>Called once at startup (App.StartApp), after the actions. UI thread.</summary>
        internal void StartSmartClipboard()
        {
            if (_scFlagHandler != null) return;
            _scFlagHandler = id =>
            {
                if (id == SmartClipboardActions.FeatureId) Dispatcher.InvokeAsync(ApplySmartClipboardSwitch);      // any thread → UI
            };
            if (FeatureFlags.Current != null) FeatureFlags.Current.Changed += _scFlagHandler;
            ApplySmartClipboardSwitch();
        }

        private void StopSmartClipboard()
        {
            if (_scFlagHandler != null && FeatureFlags.Current != null) FeatureFlags.Current.Changed -= _scFlagHandler;
            _scFlagHandler = null;
            _scOn = false;
            _scLatest = null;
        }

        /// <summary>The switch changed (or startup): read it again (two changes can arrive in any order).</summary>
        private void ApplySmartClipboardSwitch()
        {
            if (_scFlagHandler == null) return;                 // stopped meanwhile
            bool on = SmartClipboardEnabled();
            if (on == _scOn) return;
            _scOn = on;
            _scLatest = null;                                    // only what is copied from now on
            if (on) { _scRecognized = 0; App.Log("Smart Clipboard: pornit."); }
            else App.Log("Smart Clipboard: oprit (" + _scRecognized + " recunoașteri).");
        }

        /// <summary>
        /// Hook at the start of OnClipboard (after WinNotch's own writes are skipped): until the new text is known, and
        /// unless it is a text that isn't private, no chips (they would act on what was there before).
        /// </summary>
        private void SmartClipboardForget() => _scLatest = null;

        /// <summary>Hook in OnClipboard, right after the history recorded <paramref name="text"/> (not private, not empty).</summary>
        private void SmartClipboardCopied(string text)
        {
            if (!_scOn) return;
            try
            {
                _scLatest = text;
                var clip = _scCache.Get(text);
                if (clip.Kind == SmartClipKind.None) return;
                _scRecognized++;
                if (!S.SmartClipboardPeek || !_activityOn || _activity == null) return;     // the peek: an option, and only through the Activity Manager
                string title = SmartClipboardActions.PeekTitle(clip);
                if (title == null) return;
                _activity.Post(new Core.Activity.Activity
                {
                    Id = SmartClipboardActions.FeatureId, Priority = Core.Activity.ActivityPriority.Low, Duration = Core.Activity.ActivityManager.PeekDuration,
                    Title = title, Glyph = SmartClipboardActions.GClipboard,
                });
            }
            catch (Exception ex) { FeatureFlags.Current?.ReportError(SmartClipboardActions.FeatureId, ex); }
        }

        /// <summary>Hook in CopyToClipboard: WinNotch put this text there itself (it isn't recorded or peeked, but the chips follow it).</summary>
        private void SmartClipboardOurs(string text)
        {
            if (_scOn) _scLatest = text;
        }

        /// <summary>
        /// For the actions (UI thread): the result into the clipboard as WinNotch's own write, so the clipboard
        /// notification that follows is skipped (no loop, no second recognition, no peek). False when it is busy.
        /// </summary>
        internal bool SmartClipboardWrite(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            try
            {
                _ignoreClip = true;
                Clipboard.SetText(text);
            }
            catch (Exception ex) when (ex is System.Runtime.InteropServices.ExternalException || ex is ArgumentException || ex is InvalidOperationException)
            {
                _ignoreClip = false;
                return false;
            }
            SmartClipboardOurs(text);
            return true;
        }
    }
}

namespace WinNotch.Features.SmartClipboard
{
    /// <summary>The "clipboard.*" actions in the app: the notch's last copied text, its clipboard writes and Shell.Open.</summary>
    internal sealed class NotchSmartClipboardHost : ISmartClipboardHost
    {
        private readonly NotchWindow _n;
        public NotchSmartClipboardHost(NotchWindow notch) { _n = notch ?? throw new ArgumentNullException(nameof(notch)); }

        public string CurrentText => _n.SmartClipboardText;

        public bool SetText(string text) => _n.SmartClipboardWrite(text);

        public string OpenUrl(string url)
        {
            if (!SmartClipRecognizer.IsUrl(url)) return "Nu ai copiat un link http sau https.";       // http(s) only, whoever calls it
            try
            {
                Services.Shell.Open(url);
                return null;
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception || ex is InvalidOperationException || ex is FileNotFoundException)
            {
                return "Link-ul nu a putut fi deschis.";             // no browser set (R1): a fixed reason, not counted as a feature error
            }
        }

        /// <summary>
        /// A local folder only: never a network path, never a mapped network drive (it would log in to that server), and
        /// never a file itself (that would run it): a file opens the folder it is in.
        /// </summary>
        public string OpenFolder(string path)
        {
            if (SmartClipRecognizer.IsNetworkPath(path) || !SmartClipRecognizer.TryPath(path, out var p)) return "Nu ai copiat o cale de pe acest PC.";
            try
            {
                var root = Path.GetPathRoot(p);
                if (string.IsNullOrEmpty(root) || new DriveInfo(root).DriveType is DriveType.Network or DriveType.NoRootDirectory or DriveType.Unknown)
                    return "Calea e pe o unitate de rețea sau lipsă; nu o deschid.";
                string folder = Directory.Exists(p) ? p : File.Exists(p) ? Path.GetDirectoryName(p) : null;
                if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return "Calea copiată nu există pe acest PC.";
                Services.Shell.Open(folder.TrimEnd('\\', '/') + "\\");      // R1: a trailing "\" names a folder ("C:\" stays "C:\")
                return null;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is NotSupportedException || ex is System.ComponentModel.Win32Exception)
            {
                return "Folderul nu a putut fi deschis.";
            }
        }
    }
}
