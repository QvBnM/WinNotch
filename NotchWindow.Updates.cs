using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WinNotch.Core.Update;
using WinNotch.Features.Activity;
using WinNotch.Services;

namespace WinNotch
{
    /// <summary>
    /// Automatic updates (see Services/Updater): a quiet check after start and every 6 hours; a new version is offered
    /// in the notch ("Actualizează" / "Mai târziu"); the download is verified against the release key before anything
    /// is replaced. Also the follow-ups after an update: the temperature helper and an outdated browser extension.
    /// </summary>
    public partial class NotchWindow
    {
        private UpdateInfo _update;
        private DateTime _nextUpdateCheck = DateTime.Now.AddSeconds(60);
        private bool _updating, _updateOffered, _afterUpdateShown, _oldExtShown;

        /// <summary>Called every second.</summary>
        private void UpdateTick()
        {
            if (App.JustUpdated && !_afterUpdateShown && _tick > 3 && _mode == Mode.Idle && !_hidden) { _afterUpdateShown = true; AfterUpdate(); return; }
            if (App.RollbackMessage != null && _tick > 3 && _mode == Mode.Idle)      // once, after an automatic rollback to this version
            {
                string m = App.RollbackMessage;
                App.RollbackMessage = null;
                ToolAlert(LegacyAlerts.Rollback, Ui.GWarn, CWarn, m, "Versiunea refuzată nu îți mai e propusă; o versiune mai nouă, da.", 560);
                return;
            }
            if (Bridge.OldExtensionSeen && !_oldExtShown && _mode == Mode.Idle && !_hidden) { _oldExtShown = true; ShowOldExtension(); return; }

            if (!Updater.Configured || _updating) return;
            // automatic checks only if wanted; a version found by "Caută actualizări" is offered either way
            if (S.AutoUpdate && DateTime.Now >= _nextUpdateCheck)
            {
                _nextUpdateCheck = DateTime.Now.AddHours(6);
                _ = CheckUpdate();
            }
            if (_update != null && _update.PreRelease && !S.BetaChannel) _update = null;     // beta channel switched off
            if (_update != null && !_updateOffered && _mode == Mode.Idle && !_hidden && DateTime.Now >= S.UpdateSnoozeUntil && !_liveInteractive)
                if (ShowUpdateOffer(_update)) _updateOffered = true;
        }

        private async Task CheckUpdate()
        {
            var u = await Updater.CheckAsync(S.BetaChannel, App.IsRefusedVersion);
            if (u == null) return;
            if (_update == null || u.Version > _update.Version) { _update = u; _updateOffered = false; }
        }

        /// <summary>"Caută acum" in Settings: the result as a sentence.</summary>
        internal async Task<string> CheckUpdateNow()
        {
            if (!Updater.Configured) return "Actualizările nu sunt configurate încă în această versiune.";
            var u = await Updater.CheckAsync(S.BetaChannel, App.IsRefusedVersion);
            if (u == null) { _update = null; return "Ai ultima versiune (" + Updater.Current + ")."; }     // e.g. beta channel switched off since
            _update = u;
            _updateOffered = false;
            S.UpdateSnoozeUntil = DateTime.MinValue;
            return "Versiunea " + u.Version + " e disponibilă: o vezi în notch.";
        }

        /// <summary>"Caută actualizări" in the tray menu: checks now and answers in the notch (the offer, or "you're up to date").</summary>
        internal async Task CheckUpdateFromMenu()
        {
            if (_updating) return;
            string result;
            try { result = await CheckUpdateNow(); }
            catch (Exception ex) { App.Log("Căutarea actualizărilor: " + ex.GetType().Name); result = "Nu am putut căuta acum. Încearcă mai târziu."; }
            if (_update != null && _update.Version > Updater.Current)
            {
                if (ShowUpdateOffer(_update)) _updateOffered = true;     // otherwise as soon as the notch is free
            }
            else ToolAlert(LegacyAlerts.UpdateCheck, "\uE73E", COk, result, null, 440);
        }

        /// <summary>What a version brought, one line per change, each with its kind (Nou / Îmbunătățit / Modificat / Reparat).</summary>
        internal static FrameworkElement NotesList(string md, int max, out int rows)
        {
            var items = Updater.ParseNotes(md);
            var list = new StackPanel { Margin = new Thickness(42, 8, 6, 0) };
            rows = 0;
            int shown = 0;
            foreach (var (kind, text) in items)
            {
                if (shown == max) { list.Children.Add(Ui.T("+ încă " + (items.Count - max) + " în fereastra WinNotch › Noutăți", 11.5, "DimBrush")); rows++; break; }
                string k = kind.ToLowerInvariant();
                string brush = k.StartsWith("nou") ? "OkBrush" : k.StartsWith("îmbun") || k.StartsWith("imbun") ? "InfoBrush" : k.StartsWith("repar") ? "WarnBrush" : "MutedBrush";
                var chip = new Border { CornerRadius = new CornerRadius(6), Padding = new Thickness(6, 1, 6, 1), Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Top, Width = 86 };
                chip.SetResourceReference(Border.BackgroundProperty, "ChipHoverBrush");
                var kt = Ui.T(kind, 10.5, brush, true);
                kt.HorizontalAlignment = HorizontalAlignment.Center;
                chip.Child = kt;
                var line = Ui.T(text, 12, "InkBrush");
                line.TextWrapping = TextWrapping.Wrap;
                line.TextTrimming = TextTrimming.CharacterEllipsis;
                line.MaxHeight = 34;
                var g = Ui.Cols(Ui.Auto, Ui.Star());
                g.Put(chip); g.Put(line, 1);
                g.Margin = new Thickness(0, 0, 0, 6);
                list.Children.Add(g);
                rows += text.Length > 62 ? 2 : 1;
                shown++;
            }
            return list;
        }

        private bool ShowUpdateOffer(UpdateInfo u)
        {
            var later = Ui.PillBtn("Mai târziu", null);
            var go = Ui.PillBtn("Actualizează", null, true);
            later.Margin = new Thickness(0, 0, 6, 0);
            var head = LiveRow(LiveIcon("\uE896", COk), "WinNotch " + u.Version + " e gata", "Ce aduce versiunea nouă:", Ui.H(0, later, go));
            var notes = NotesList(u.Notes, 6, out int rows);
            if (!Alert(LegacyAlerts.UpdateOffer, Ui.V(0, head, notes), 580, 64 + rows * 22, 45000)) return false;
            _liveInteractive = true;
            LiveLayer.IsHitTestVisible = true;
            SetClickThrough(false);
            later.Click += (o, e) => { S.UpdateSnoozeUntil = DateTime.Now.AddHours(24); S.Save(); EndLive(); };
            go.Click += (o, e) => { EndLive(); _ = InstallUpdate(u); };
            return true;
        }

        private async Task InstallUpdate(UpdateInfo u)
        {
            if (_updating) return;
            _updating = true;
            try
            {
                var bar = Ui.Bar(out var fill, Ui.B("InfoBrush"), 5);
                bar.Width = 120;
                var sub = Ui.T("pornesc…", 12, "MutedBrush");
                var content = LiveRow(Spinner(), "Descarc WinNotch " + u.Version + "…", null, bar);
                ((StackPanel)content.Children[1]).Children.Add(sub);
                Alert(LegacyAlerts.UpdateDownload, content, 470, 60, 600000, true);
                var progress = new Progress<double>(p => { fill.ScaleX = p; sub.Text = Math.Round(p * 100) + "%"; });
                string file = await Task.Run(() => Updater.DownloadAsync(u, progress, CancellationToken.None));
                if (file == null)
                {
                    ToolAlert(LegacyAlerts.UpdateRefused, Ui.GWarn, CHot, "Actualizarea a fost refuzată", "Fișierul descărcat nu are semnătura WinNotch: nu am instalat nimic.", 520);
                    return;
                }
                Alert(LegacyAlerts.UpdateInstalling, LiveRow(Spinner(), "Instalez WinNotch " + u.Version + "…", "Repornesc în câteva secunde", null), 420, 58, 60000, true);
                await Task.Delay(600);
                S.Save();
                // the restart for the update is a clean exit (not counted as a crash), marked before the new version starts
                if (Updater.Apply(file, () => { App.Guard?.MarkCleanExit(); App.ReleaseSingleInstance(); })) ((App)Application.Current).ExitApp();
                else
                {
                    App.Guard?.Resume();          // this run goes on: protected again
                    ToolAlert(LegacyAlerts.UpdateReplaceFailed, Ui.GWarn, CHot, "Nu am putut înlocui WinNotch.exe", "Detalii în log; versiunea de acum merge mai departe.", 480);
                }
            }
            catch (Exception ex)
            {
                App.Log("Actualizare: " + ex.Message);
                ToolAlert(LegacyAlerts.UpdateFailed, Ui.GWarn, CWarn, "Actualizarea nu a reușit", "Probabil fără internet; încerc din nou mai târziu.", 460);
            }
            finally { _updating = false; }
        }

        /// <summary>First start after an update: what's new; then, if needed, the temperature helper (it needs a UAC prompt).</summary>
        private void AfterUpdate()
        {
            if (TempHelper.Installed && TempHelper.Outdated)
                ShowWhatsNew(() => Dispatcher.InvokeAsync(OfferHelperUpdate, System.Windows.Threading.DispatcherPriority.Background));
            else ShowWhatsNew();
        }

        /// <summary>Right after an update: what this version brought (from the notes built into it).</summary>
        private void ShowWhatsNew(Action then = null)
        {
            var ok = Ui.PillBtn("Am înțeles", null, true);
            var head = LiveRow(LiveIcon("\uE73E", COk), "Actualizat la WinNotch " + Updater.Current, "Ce e nou:", ok);
            var notes = NotesList(Updater.OwnNotes(), 6, out int rows);
            if (!Alert(LegacyAlerts.WhatsNew, Ui.V(0, head, notes), 580, 64 + rows * 22, 25000, true)) { then?.Invoke(); return; }
            _liveInteractive = true;
            LiveLayer.IsHitTestVisible = true;
            SetClickThrough(false);
            ok.Click += (o, e) => { EndLive(); then?.Invoke(); };
        }

        private void OfferHelperUpdate()
        {
            var later = Ui.PillBtn("Mai târziu", null);
            var go = Ui.PillBtn("Actualizează", null, true);
            later.Margin = new Thickness(0, 0, 6, 0);
            var content = LiveRow(LiveIcon("\uE7BA", CWarn), "Serviciul de temperatură e de la versiunea veche", "Se actualizează cu o confirmare Windows", Ui.H(0, later, go));
            if (!Alert(LegacyAlerts.HelperUpdate, content, 560, 58, 30000, true)) return;
            _liveInteractive = true;
            LiveLayer.IsHitTestVisible = true;
            SetClickThrough(false);
            later.Click += (o, e) => EndLive();
            go.Click += (o, e) => { EndLive(); ((App)Application.Current).InstallTempHelper(); };
        }

        /// <summary>The browser still runs the extension from before the last update: it must be reloaded once (Chrome's rule).</summary>
        private void ShowOldExtension()
        {
            var copy = Ui.PillBtn("Copiază adresa", null, true);
            var content = LiveRow(LiveIcon("", CWarn), "Extensia din browser e veche", "Deschide chrome://extensions și apasă ↻ la WinNotch", copy);
            if (!Alert(LegacyAlerts.OldExtension, content, 540, 58, 20000)) { _oldExtShown = false; return; }
            _liveInteractive = true;
            LiveLayer.IsHitTestVisible = true;
            SetClickThrough(false);
            copy.Click += (o, e) => { try { Clipboard.SetText("chrome://extensions"); } catch { } EndLive(); };
        }
    }
}
