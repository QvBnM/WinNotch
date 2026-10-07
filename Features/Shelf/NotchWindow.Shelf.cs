using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WinNotch.Core.Actions;
using WinNotch.Core.Flags;
using WinNotch.Features.Shelf;
using WinNotch.Features.Smoke;
using WinNotch.Services;

namespace WinNotch
{
    /// <summary>
    /// The notch's side of the shelf (P23, ADR 0011). With the "shelf" switch off nothing here runs: no AllowDrop, no
    /// drag handlers, no header button, the hover rule exactly as before. With it on: the closed pill still lets clicks
    /// through (it can't take a drop: plan B of the spike), but a drag carried onto it opens the notch by hover; the open
    /// notch takes files dropped anywhere on it (preview events on the pill, so pages and text boxes don't swallow them)
    /// and shows the shelf as an overlay over the page (also from the "Raft" button in the header). The checks run off the
    /// UI thread (<see cref="ShelfPaths.Check"/>); every button goes through <see cref="ActionRegistry.InvokeAsync"/>.
    /// Only counters go to the log: never a path or a file name.
    /// </summary>
    public partial class NotchWindow
    {
        /// <summary>Our own drag (an item dragged out of the shelf): never taken back as a drop.</summary>
        internal const string ShelfDragFormat = "WinNotchShelfItem";
        /// <summary>Room the overlay needs (unscaled px; the list scrolls inside it).</summary>
        private const double ShelfPanelMinHeight = 380;
        private static readonly TimeSpan ShelfMessageTime = TimeSpan.FromSeconds(5);

        private Action<string> _shFlagHandler;
        /// <summary>UI-thread copy of the switch.</summary>
        private bool _shOn;
        private readonly ShelfModel _shModel = new ShelfModel();
        /// <summary>Which rows are ticked (UI thread only, never saved): nothing ticked = the buttons take the whole shelf.</summary>
        private readonly ShelfSelection _shSelection = new ShelfSelection();
        private readonly ShelfDragHover _shDrag = new ShelfDragHover();
        private Button _shToggle;
        private TextBlock _shToggleCount;
        private Border _shPanel;
        private StackPanel _shList;
        private TextBlock _shCount, _shMessage, _shEmpty, _shCopyText;
        private Button _shCopy;
        private DispatcherTimer _shMessageTimer;
        private int _shDrawn = -1;
        private bool _shPruning;
        private Point? _shPressAt;
        private ShelfItem _shPressed;
        private readonly HashSet<string> _shRunning = new HashSet<string>(StringComparer.Ordinal);
        private DragEventHandler _shDragEnter, _shDragOver, _shDrop, _shRefuse;
        /// <summary>The primary (physical) button for GetAsyncKeyState: read when the switch goes on and when the notch closes, not every tick (R1).</summary>
        private int _shPrimaryButton = 0x01;

        [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
        private const int SM_SWAPBUTTON = 23;

        private static bool ShelfEnabled() => FeatureFlags.Current?.IsEnabled(ShelfActions.FeatureId) ?? false;

        /// <summary>Shared with the "shelf.*" actions (thread-safe).</summary>
        internal ShelfModel ShelfItems => _shModel;

        /// <summary>Called once at startup (App.StartApp), after the actions. UI thread.</summary>
        internal void StartShelf()
        {
            if (_shFlagHandler != null) return;
            _shFlagHandler = id =>
            {
                if (id == ShelfActions.FeatureId) Dispatcher.InvokeAsync(ApplyShelfSwitch);       // any thread → UI
            };
            if (FeatureFlags.Current != null) FeatureFlags.Current.Changed += _shFlagHandler;
            ApplyShelfSwitch();
        }

        private void StopShelf()
        {
            _shMessageTimer?.Stop();
            if (_shFlagHandler != null && FeatureFlags.Current != null) FeatureFlags.Current.Changed -= _shFlagHandler;
            _shFlagHandler = null;
            if (_shOn) ShelfOff();
            _shOn = false;
        }

        /// <summary>The switch changed (or startup): read it again (two changes can arrive in any order).</summary>
        private void ApplyShelfSwitch()
        {
            if (_shFlagHandler == null) return;                 // stopped meanwhile
            bool on = ShelfEnabled();
            if (on == _shOn) return;
            _shOn = on;
            try
            {
                if (on)
                {
                    _shModel.Load(S.Shelf);
                    _shDrawn = -1;
                    ShelfReadPrimaryButton();
                    ShelfWireDrop(true);
                    ShelfEnsureToggle();
                    App.Log("Raft: pornit (" + _shModel.Count + " elemente).");
                }
                else
                {
                    ShelfOff();
                    App.Log("Raft: oprit.");
                }
            }
            catch (Exception ex) { FeatureFlags.Current?.ReportError(ShelfActions.FeatureId, ex); }
        }

        /// <summary>Everything the switch added goes (the references stay in settings.json for when it's on again).</summary>
        private void ShelfOff()
        {
            ShelfWireDrop(false);
            ShelfHidePanel();
            _shSelection.Clear();
            if (_shToggle != null) { HeaderRight.Children.Remove(_shToggle); _shToggle = null; _shToggleCount = null; }
            _shDrag.Reset();
            _shPressAt = null;
            _shPressed = null;
        }

        // ------------------------------------------------------------------ hooks

        /// <summary>
        /// Hook in PollTick (idle): true while a drag carried in from outside (the primary button already down when the
        /// mouse came onto the pill) is over it, so the old "a click meant for the window below" rule doesn't keep it shut
        /// and it opens by hover like always. A press on the pill itself stays a click-through. False with the switch off.
        /// </summary>
        private bool ShelfDragHover(bool inside)
        {
            if (!_shOn) return false;
            return _shDrag.Update(inside, Native.GetAsyncKeyState(_shPrimaryButton) < 0);
        }

        /// <summary>
        /// Hook at the end of Expand: the count in the header; gone items checked off the UI thread. The overlay itself comes
        /// only with a drag that carries files (DragEnter with FileDrop), not with any drag that opened the notch (R1).
        /// </summary>
        private void ShelfOnOpen()
        {
            if (!_shOn) return;
            try
            {
                ShelfUpdateToggle();
                _ = ShelfPruneAsync();
            }
            catch (Exception ex) { FeatureFlags.Current?.ReportError(ShelfActions.FeatureId, ex); }
        }

        /// <summary>Hook in Collapse: the overlay goes with the panel.</summary>
        private void ShelfOnClose()
        {
            if (!_shOn && _shPanel == null) return;
            ShelfHidePanel(false);
            _shDrag.Reset();
            _shPressAt = null;
            _shPressed = null;
            ShelfReadPrimaryButton();
        }

        private void ShelfReadPrimaryButton() => _shPrimaryButton = GetSystemMetrics(SM_SWAPBUTTON) != 0 ? 0x02 : 0x01;

        /// <summary>Hook in UpdateHeader: edit mode has its own buttons; the shelf steps aside.</summary>
        private void ShelfHeaderChanged()
        {
            if (_shToggle != null) _shToggle.Visibility = Editing ? Visibility.Collapsed : Visibility.Visible;
            if (Editing && _shPanel != null) ShelfHidePanel(false);
        }

        /// <summary>Hook in PanelH: the overlay's room (0 when it's closed).</summary>
        private double ShelfPanelHeight() => _shPanel != null ? ShelfPanelMinHeight : 0;

        // ------------------------------------------------------------------ dropping files on the open notch

        private void ShelfWireDrop(bool on)
        {
            if (on && _shDrop == null)
            {
                _shDragEnter = ShelfPreviewDragEnter;
                _shDragOver = ShelfPreviewDragOver;
                _shDrop = ShelfPreviewDrop;
                _shRefuse = ShelfRefuseUnhandled;
                Pill.AllowDrop = true;
                Pill.AddHandler(PreviewDragEnterEvent, _shDragEnter);
                Pill.AddHandler(PreviewDragOverEvent, _shDragOver);
                Pill.AddHandler(PreviewDropEvent, _shDrop);
                Pill.AddHandler(DragEnterEvent, _shRefuse);         // R1: bubbling, only what nobody handled
                Pill.AddHandler(DragOverEvent, _shRefuse);
                Pill.AddHandler(DropEvent, _shRefuse);
            }
            else if (!on && _shDrop != null)
            {
                Pill.RemoveHandler(PreviewDragEnterEvent, _shDragEnter);
                Pill.RemoveHandler(PreviewDragOverEvent, _shDragOver);
                Pill.RemoveHandler(PreviewDropEvent, _shDrop);
                Pill.RemoveHandler(DragEnterEvent, _shRefuse);
                Pill.RemoveHandler(DragOverEvent, _shRefuse);
                Pill.RemoveHandler(DropEvent, _shRefuse);
                Pill.ClearValue(AllowDropProperty);
                _shDragEnter = _shDragOver = _shDrop = _shRefuse = null;
            }
        }

        /// <summary>Files from another app onto the open notch (not in edit mode, not an alert, not our own drag).</summary>
        private bool ShelfAccepts(DragEventArgs e)
        {
            if (!_shOn || _mode != Mode.Expanded || Editing || e?.Data == null) return false;
            try { return e.Data.GetDataPresent(DataFormats.FileDrop) && !e.Data.GetDataPresent(ShelfDragFormat); }
            catch (COMException) { return false; }
        }

        /// <summary>A reference, so "link" when the source allows it, else "copy"; never "move" (the source would delete its file).</summary>
        private static DragDropEffects ShelfEffect(DragDropEffects allowed) =>
            (allowed & DragDropEffects.Link) != 0 ? DragDropEffects.Link : (allowed & DragDropEffects.Copy) != 0 ? DragDropEffects.Copy : DragDropEffects.None;

        /// <summary>
        /// R1: AllowDrop on the pill is inherited by everything in it, and a drop target that sets nothing gives the source
        /// its default effect (with Move: text dragged from Word would be cut). Whatever nobody handled on the way up (text,
        /// edit mode, an interactive alert) is refused here; text boxes handle their own text before it gets here.
        /// </summary>
        private void ShelfRefuseUnhandled(object sender, DragEventArgs e)
        {
            if (e.Handled) return;
            e.Effects = DragDropEffects.None;
            e.Handled = true;
        }

        private void ShelfPreviewDragEnter(object sender, DragEventArgs e)
        {
            if (!ShelfAccepts(e)) return;
            e.Effects = ShelfEffect(e.AllowedEffects);
            e.Handled = true;
            if (_shPanel == null) ShelfShowPanel();
        }

        private void ShelfPreviewDragOver(object sender, DragEventArgs e)
        {
            if (!ShelfAccepts(e)) return;
            e.Effects = ShelfEffect(e.AllowedEffects);
            e.Handled = true;
        }

        private void ShelfPreviewDrop(object sender, DragEventArgs e)
        {
            if (!ShelfAccepts(e)) return;
            e.Effects = ShelfEffect(e.AllowedEffects);
            e.Handled = true;
            string[] files = null;
            try { files = e.Data.GetData(DataFormats.FileDrop) as string[]; }
            catch (COMException) { }
            if (files == null || files.Length == 0) return;
            _ = ShelfAddPathsAsync(files);
        }

        /// <summary>
        /// Paths from a drop (or the smoke command): checked off the UI thread (drive, existence, shortcuts; a sleeping stick
        /// can take seconds), added, saved, shown. Counters to the log; the sentence to the shelf's header.
        /// </summary>
        private async Task ShelfAddPathsAsync(string[] raw)
        {
            try
            {
                var list = raw.Take(ShelfPaths.MaxDropped).ToArray();
                int ignored = Math.Max(0, raw.Length - list.Length);
                var report = await Task.Run(() =>
                {
                    var r = new ShelfAddReport { Ignored = ignored };
                    foreach (var p in list)
                    {
                        var check = ShelfPaths.Check(p, LocalShelfFileSystem.Instance, out var path, out bool folder);
                        r.Count(check == ShelfRefusal.None ? _shModel.TryAdd(path, folder) : check);
                    }
                    return r;
                });
                App.Log(report.LogLine());
                if (report.Added > 0) ShelfSave();
                if (_mode == Mode.Expanded) ShelfShowPanel();
                ShelfRedraw();
                ShelfShowMessage(report.Message(), report.Added > 0 && report.Refused + report.Full + report.Ignored == 0);
            }
            catch (Exception ex) { FeatureFlags.Current?.ReportError(ShelfActions.FeatureId, ex); }
        }

        /// <summary>Gone items leave the list quietly (when the notch opens; off the UI thread, no timer).</summary>
        private async Task ShelfPruneAsync()
        {
            if (_shPruning || _shModel.Count == 0) return;
            _shPruning = true;
            try
            {
                var items = _shModel.Items;
                // R1: only when the drive is here and the file isn't; an unplugged stick keeps its items
                var gone = await Task.Run(() => items.Where(i => ShelfPaths.IsGone(i.Path, LocalShelfFileSystem.Instance)).Select(i => i.Id).ToList());
                foreach (var i in items) i.Exists = !gone.Contains(i.Id);
                int n = _shModel.RemoveAll(gone);
                if (n > 0) { App.Log("Raft: " + n + " elemente care nu mai există, scoase."); ShelfSave(); }
                ShelfRedraw();
            }
            catch (Exception ex) { FeatureFlags.Current?.ReportError(ShelfActions.FeatureId, ex); }
            finally { _shPruning = false; }
        }

        private void ShelfSave()
        {
            S.Shelf = _shModel.ToSettings();          // a new list (a save from another thread may read the old one)
            S.Save();
        }

        /// <summary>From the actions (any thread): saved and redrawn on the UI thread.</summary>
        internal void ShelfChanged() => Dispatcher.InvokeAsync(() =>
        {
            try { ShelfSave(); ShelfRedraw(); }
            catch (Exception ex) { FeatureFlags.Current?.ReportError(ShelfActions.FeatureId, ex); }
        });

        /// <summary>For "shelf.copy-path" and "shelf.ocr" (UI thread): WinNotch's own clipboard write (not recorded again); false when busy.</summary>
        internal bool ShelfWriteClipboard(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            try
            {
                _ignoreClip = true;
                Clipboard.SetText(text);
            }
            catch (Exception ex) when (ex is ExternalException || ex is ArgumentException || ex is InvalidOperationException)
            {
                _ignoreClip = false;
                return false;
            }
            SmartClipboardOurs(text);                 // the Clipboard widget's chips follow our own writes (P21)
            return true;
        }

        /// <summary>
        /// For "shelf.copy-files" (UI thread): the files on the clipboard as a copy, exactly like Ctrl+C in Explorer — the
        /// paths as FileDrop plus "Preferred DropEffect" = copy (never move, so nothing of yours is ever taken from where
        /// it is). WinNotch writes no file: the copying is Windows' own, when you press Ctrl+V. The clipboard keeps the
        /// list after WinNotch closes (SetDataObject with copy: true). False when another application holds the clipboard.
        /// </summary>
        internal bool ShelfWriteFiles(IReadOnlyList<string> paths)
        {
            if (paths == null || paths.Count == 0) return false;
            try
            {
                var data = new DataObject();
                data.SetData(DataFormats.FileDrop, paths.ToArray());
                // DROPEFFECT_COPY | DROPEFFECT_LINK (5), what Explorer itself puts there for a copy
                data.SetData("Preferred DropEffect", new System.IO.MemoryStream(BitConverter.GetBytes((int)(DragDropEffects.Copy | DragDropEffects.Link))));
                _ignoreClip = true;
                Clipboard.SetDataObject(data, true);
            }
            catch (Exception ex) when (ex is ExternalException || ex is ArgumentException || ex is InvalidOperationException)
            {
                _ignoreClip = false;
                return false;
            }
            return true;
        }

        // ------------------------------------------------------------------ the header button and the overlay

        private void ShelfEnsureToggle()
        {
            if (_shToggle != null) return;
            var icon = new TextBlock { Text = ShelfActions.GShelf, FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
            icon.SetResourceReference(TextBlock.FontFamilyProperty, "IconFont");
            icon.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
            _shToggleCount = ThemedText("", 11, "MutedBrush");
            _shToggle = new Button { Style = Ui.S("GhostPill"), Margin = new Thickness(0, 0, 6, 0), ToolTip = "Raftul: fișierele trase aici (doar căile, nu copii)", Content = Ui.H(5, icon, _shToggleCount) };
            AutomationProperties.SetAutomationId(_shToggle, SmokeMode.ShelfToggleAutomationId);
            AutomationProperties.SetName(_shToggle, "Raft");
            _shToggle.Click += (o, e) =>
            {
                e.Handled = true;
                if (_shPanel != null) ShelfHidePanel();
                else ShelfShowPanel();
            };
            HeaderRight.Children.Insert(0, _shToggle);
            ShelfHeaderChanged();
            ShelfUpdateToggle();
        }

        private void ShelfUpdateToggle()
        {
            if (_shToggleCount == null) return;
            int n = _shModel.Count;
            _shToggleCount.Text = n > 0 ? "Raft " + n : "Raft";
        }

        /// <summary>The copy button's label: what it would act on now (the ticked rows, or the whole shelf).</summary>
        private void ShelfUpdateCopy(IReadOnlyList<ShelfItem> items)
        {
            if (_shCopy == null || _shCopyText == null) return;
            int chosen = _shSelection.Count > 0 ? _shSelection.Chosen(items).Count : items.Count;
            _shCopyText.Text = _shSelection.Count > 0 ? "Copiază selecția (" + chosen + ")" : "Copiază tot (" + chosen + ")";
            _shCopy.IsEnabled = chosen > 0;
            AutomationProperties.SetName(_shCopy, _shCopyText.Text);
        }

        private void ShelfShowPanel()
        {
            if (_shPanel != null || !_shOn || Editing || _mode != Mode.Expanded) return;
            var title = ThemedText("Raft", 15, "InkBrush", true);
            _shCount = ThemedText("", 11.5, "MutedBrush");
            var copyIcon = new TextBlock { Text = ShelfActions.GCopyFiles, FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
            copyIcon.SetResourceReference(TextBlock.FontFamilyProperty, "IconFont");
            copyIcon.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
            _shCopyText = ThemedText("", 11.5, "InkBrush");
            _shCopy = new Button
            {
                Style = Ui.S("GhostPill"), Margin = new Thickness(0, 0, 6, 0), Content = Ui.H(5, copyIcon, _shCopyText),
                ToolTip = "Pune fișierele pe clipboard (ca un Ctrl+C din Explorer); dă apoi Ctrl+V în folderul în care le vrei",
            };
            AutomationProperties.SetAutomationId(_shCopy, SmokeMode.ShelfCopyFilesAutomationId);
            _shCopy.Click += (o, e) => { e.Handled = true; _ = ShelfRunAsync(ShelfActions.CopyFilesId, _shSelection.Keys(_shModel.Items)); };
            var clear = new Button { Style = Ui.S("GhostPill"), Margin = new Thickness(0, 0, 6, 0), Content = "Golește", ToolTip = "Scoate tot din raft (fișierele rămân pe disc)" };
            AutomationProperties.SetAutomationId(clear, SmokeMode.ShelfClearAutomationId);
            AutomationProperties.SetName(clear, "Golește");
            clear.Click += (o, e) => { e.Handled = true; _ = ShelfRunAsync(ShelfActions.ClearId, null); };
            var close = new Button { Style = Ui.S("AccentPill"), Content = "Închide" };
            close.Click += (o, e) => { e.Handled = true; ShelfHidePanel(); };
            var head = Ui.Cols(Ui.Auto, Ui.Star(), Ui.Auto, Ui.Auto, Ui.Auto);
            head.Put(Ui.H(8, title, _shCount));
            head.Put(_shCopy, 2);
            head.Put(clear, 3);
            head.Put(close, 4);

            _shMessage = ThemedText("", 11.5, "DimBrush");
            _shMessage.Visibility = Visibility.Collapsed;
            _shMessage.Margin = new Thickness(0, 6, 0, 0);
            AutomationProperties.SetAutomationId(_shMessage, SmokeMode.ShelfMessageAutomationId);
            _shEmpty = ThemedText("Trage aici fișiere sau foldere din Explorer. Raftul ține doar căile lor (nu copii), cel mult " + ShelfModel.MaxItems + ".", 12, "DimBrush");
            _shEmpty.TextWrapping = TextWrapping.Wrap;
            _shEmpty.Margin = new Thickness(2, 14, 2, 0);
            _shList = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
            var scroll = new ScrollViewer { Style = Ui.S("SlimScroll"), Content = _shList };
            var body = Ui.Rows(Ui.Auto, Ui.Auto, Ui.Auto, Ui.Star());
            body.Put(head);
            body.Put(_shMessage, 0, 1);
            body.Put(_shEmpty, 0, 2);
            body.Put(scroll, 0, 3);

            _shPanel = new Border { Margin = new Thickness(-6, 36, -6, -4), Padding = new Thickness(14, 12, 14, 10), CornerRadius = new CornerRadius(16), Child = body };
            _shPanel.SetResourceReference(Border.BackgroundProperty, "NotchBrush");
            AutomationProperties.SetAutomationId(_shPanel, SmokeMode.ShelfPanelAutomationId);
            AlertInterrupt(Core.Ui.UserIntent.OpenPanel);      // P51b hook (Features/AlertInterrupt)
            OverlayHost.Children.Add(_shPanel);
            OverlayRegister(OvShelf, Core.Ui.OverlayLevel.Panel, _shPanel, () => ShelfHidePanel());         // P51 hook (Features/Overlays)
            _shDrawn = -1;
            ShelfRedraw();
            RelayoutPanel();
        }

        private void ShelfHidePanel(bool relayout = true)
        {
            _shMessageTimer?.Stop();
            OverlayUnregister(OvShelf);     // P51 hook (Features/Overlays)
            if (_shPanel == null) return;
            OverlayHost.Children.Remove(_shPanel);
            _shPanel = null;
            _shList = null;
            _shCount = _shMessage = _shEmpty = _shCopyText = null;
            _shCopy = null;
            _shPressAt = null;
            _shPressed = null;
            if (relayout) RelayoutPanel();
            else ExpLayer.Height = PanelH();
        }

        /// <summary>The list again, only when the shelf changed since it was drawn (UI thread).</summary>
        private void ShelfRedraw()
        {
            ShelfUpdateToggle();
            if (_shPanel == null || _shList == null) return;
            var items = _shModel.Items;
            _shSelection.Prune(items);
            _shCount.Text = items.Count + " / " + ShelfModel.MaxItems;
            _shEmpty.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            ShelfUpdateCopy(items);
            if (_shDrawn == _shModel.Version) return;
            _shDrawn = _shModel.Version;
            _shList.Children.Clear();
            foreach (var it in items) _shList.Children.Add(ShelfRow(it));
        }

        private FrameworkElement ShelfRow(ShelfItem it)
        {
            var icon = new TextBlock { Text = ShelfActions.GlyphFor(it.Kind), FontSize = 14, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
            icon.SetResourceReference(TextBlock.FontFamilyProperty, "IconFont");
            icon.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
            var name = ThemedText(it.Name, 12.5, "InkBrush");
            name.TextTrimming = TextTrimming.CharacterEllipsis;
            AutomationProperties.SetAutomationId(name, SmokeMode.ShelfItemPrefix + it.Id);
            AutomationProperties.SetName(name, it.Name);
            var where = ThemedText(ShelfPaths.FolderOf(it.Path) ?? "", 11, "DimBrush");
            where.TextTrimming = TextTrimming.CharacterEllipsis;
            var text = Ui.V(0, name, where);
            text.VerticalAlignment = VerticalAlignment.Center;

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            buttons.Children.Add(ShelfButton(ShelfActions.GCopy, "Copiază calea", ShelfActions.CopyPathId, SmokeMode.ShelfCopyPrefix, it));
            buttons.Children.Add(ShelfButton(ShelfActions.GFolder, it.IsFolder ? "Deschide folderul" : "Deschide folderul în care e", ShelfActions.OpenFolderId, "shelf-folder-", it));
            buttons.Children.Add(ShelfButton(ShelfActions.GZip, "Arhivează (zip nou, alături)", ShelfActions.ZipId, "shelf-zip-", it));
            if (it.CanOcr) buttons.Children.Add(ShelfButton(ShelfActions.GText, "Copiază textul din imagine (OCR)", ShelfActions.OcrId, "shelf-ocr-", it));
            if (it.ConvertsTo != ShelfImageFormat.None)
                buttons.Children.Add(ShelfButton(ShelfActions.GConvert, it.ConvertsTo == ShelfImageFormat.Jpeg ? "Fă o copie JPG (alături)" : "Fă o copie PNG (alături)", ShelfActions.ConvertId, "shelf-convert-", it));
            buttons.Children.Add(ShelfButton(ShelfActions.GRemove, "Scoate din raft (fișierul rămâne)", ShelfActions.RemoveId, SmokeMode.ShelfRemovePrefix, it));

            var g = Ui.Cols(Ui.Px(30), Ui.Px(26), Ui.Star(), Ui.Auto);
            g.Put(ShelfTick(it));
            g.Put(icon, 1);
            g.Put(text, 2);
            g.Put(buttons, 3);
            var row = new Border
            {
                CornerRadius = new CornerRadius(12), Padding = new Thickness(8, 5, 6, 5), Margin = new Thickness(0, 0, 0, 4), Child = g,
                ToolTip = it.IsFolder ? "Trage-l într-o aplicație ca folder" : "Trage-l într-o aplicație ca fișier", Opacity = it.Exists == false ? 0.5 : 1,
            };
            row.SetResourceReference(Border.BackgroundProperty, "ChipBrush");
            row.PreviewMouseLeftButtonDown += (o, e) =>
            {
                if (InsideButton(e.OriginalSource as DependencyObject, row)) return;
                _shPressAt = e.GetPosition(row);
                _shPressed = it;
            };
            row.MouseLeftButtonUp += (o, e) => { _shPressAt = null; _shPressed = null; };
            row.MouseMove += (o, e) => ShelfMaybeDragOut(row, it, e);
            return row;
        }

        private static bool InsideButton(DependencyObject d, DependencyObject stop)
        {
            for (; d != null && d != stop; d = d is Visual || d is System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d))
                if (d is ButtonBase) return true;
            return false;
        }

        /// <summary>
        /// The tick at the start of a row: what „Copiază selecția” and a drag out of the shelf take. Only the shown glyph
        /// and the header's label change (no row is rebuilt), so the list doesn't flicker while you tick.
        /// </summary>
        private Button ShelfTick(ShelfItem it)
        {
            var glyph = new TextBlock { FontSize = 14, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
            glyph.SetResourceReference(TextBlock.FontFamilyProperty, "IconFont");
            var b = new Button { Style = Ui.S("IconButton"), Width = 26, Height = 26, Content = glyph, ToolTip = "Alege acest element (pentru „Copiază selecția”)" };
            AutomationProperties.SetAutomationId(b, SmokeMode.ShelfSelectPrefix + it.Id);
            string id = it.Id;
            void Draw()
            {
                bool on = _shSelection.Has(id);
                glyph.Text = on ? ShelfActions.GTicked : ShelfActions.GUnticked;
                glyph.SetResourceReference(TextBlock.ForegroundProperty, on ? "AccentBrush" : "DimBrush");
                AutomationProperties.SetName(b, (on ? "Ales: " : "Alege: ") + it.Name);
            }
            Draw();
            b.Click += (o, e) =>
            {
                e.Handled = true;
                _shSelection.Toggle(id);
                Draw();
                ShelfUpdateCopy(_shModel.Items);
            };
            return b;
        }

        private Button ShelfButton(string glyph, string tip, string actionId, string automationPrefix, ShelfItem it)
        {
            var icon = new TextBlock { Text = glyph, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
            icon.SetResourceReference(TextBlock.FontFamilyProperty, "IconFont");
            icon.SetResourceReference(TextBlock.ForegroundProperty, "MutedBrush");
            var b = new Button { Style = Ui.S("IconButton"), Width = 26, Height = 26, Margin = new Thickness(1, 0, 1, 0), ToolTip = tip, Content = icon };
            AutomationProperties.SetAutomationId(b, automationPrefix + it.Id);
            AutomationProperties.SetName(b, tip);
            string id = it.Id;
            b.Click += (o, e) =>
            {
                e.Handled = true;
                _ = ShelfRunAsync(actionId, id);
            };
            return b;
        }

        /// <summary>
        /// Dragging a row out gives the file (FileDrop) to the app it's dropped on: only a local path whose last check (when
        /// the notch opened) didn't find it gone; "copy" or "link", never "move" (the shelf's file must stay where it is).
        /// </summary>
        private void ShelfMaybeDragOut(FrameworkElement row, ShelfItem it, MouseEventArgs e)
        {
            if (_shPressAt == null || _shPressed != it || e.LeftButton != MouseButtonState.Pressed) return;
            var d = e.GetPosition(row) - _shPressAt.Value;
            if (Math.Abs(d.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(d.Y) < SystemParameters.MinimumVerticalDragDistance) return;
            _shPressAt = null;
            _shPressed = null;
            // the ticked rows travel together when the dragged one is among them (dragging an unticked row takes only it)
            var dragged = _shSelection.Count > 1 && _shSelection.Has(it.Id) ? _shSelection.Chosen(_shModel.Items) : new[] { it };
            var paths = new List<string>();
            foreach (var x in dragged)
                if (x.Exists != false && ShelfPaths.TryNormalize(x.Path, out var p, out _) == ShelfRefusal.None) paths.Add(p);
            if (paths.Count == 0) return;
            try
            {
                var data = new DataObject();
                data.SetData(DataFormats.FileDrop, paths.ToArray());
                data.SetData(ShelfDragFormat, "1");
                DragDrop.DoDragDrop(row, data, DragDropEffects.Copy | DragDropEffects.Link);
            }
            catch (Exception ex) when (ex is COMException || ex is InvalidOperationException) { }      // the target app refused it: nothing to do
        }

        /// <summary>Every button through the registry (its checks, its log without values); the result in the header.</summary>
        private async Task ShelfRunAsync(string actionId, string element)
        {
            string key = actionId + "|" + element;
            if (!_shRunning.Add(key)) return;                   // the same click twice while it runs (a zip, an OCR): once
            try
            {
                var reg = ActionRegistry.Current;
                if (reg == null) return;
                if (actionId == ShelfActions.ZipId) ShelfShowMessage("Arhivez…", true);
                else if (actionId == ShelfActions.OcrId) ShelfShowMessage("Citesc textul…", true);
                else if (actionId == ShelfActions.ConvertId) ShelfShowMessage("Convertesc…", true);
                string param = actionId == ShelfActions.CopyFilesId ? ShelfActions.ElementsParam : ShelfActions.ElementParam;
                var args = element == null ? null : new Dictionary<string, string> { [param] = element };
                var r = await reg.InvokeAsync(actionId, args, ActionInvoker.UI);
                await Dispatcher.InvokeAsync(() =>
                {
                    if (r != null) ShelfShowMessage(r.Message, r.Success);
                    ShelfRedraw();
                });
            }
            catch (Exception ex) { FeatureFlags.Current?.ReportError(ShelfActions.FeatureId, ex); }
            finally { await Dispatcher.InvokeAsync(() => _shRunning.Remove(key)); }
        }

        /// <summary>A short sentence under the shelf's title for a few seconds (never logged).</summary>
        private void ShelfShowMessage(string text, bool ok)
        {
            if (_shMessage == null || string.IsNullOrEmpty(text)) return;
            if (text.Length > 120) text = text.Substring(0, 119) + "…";
            _shMessage.Text = text;
            _shMessage.SetResourceReference(TextBlock.ForegroundProperty, ok ? "DimBrush" : "WarnBrush");
            _shMessage.Visibility = Visibility.Visible;
            AutomationProperties.SetName(_shMessage, text);
            if (_shMessageTimer == null)
            {
                _shMessageTimer = new DispatcherTimer { Interval = ShelfMessageTime };
                _shMessageTimer.Tick += (o, e) =>
                {
                    _shMessageTimer.Stop();                    // one shot
                    if (_shMessage != null) _shMessage.Visibility = Visibility.Collapsed;
                };
            }
            _shMessageTimer.Stop();
            _shMessageTimer.Start();
        }
    }
}

namespace WinNotch.Features.Shelf
{
    /// <summary>The "shelf.*" actions in the app: the notch's shelf, its clipboard writes, Shell.Open and Windows' OCR.</summary>
    internal sealed class NotchShelfHost : IShelfHost
    {
        private readonly NotchWindow _n;
        public NotchShelfHost(NotchWindow notch) { _n = notch ?? throw new ArgumentNullException(nameof(notch)); }

        public ShelfModel Model => _n.ShelfItems;
        public IShelfFileSystem Files => LocalShelfFileSystem.Instance;
        public bool SetText(string text) => _n.ShelfWriteClipboard(text);
        public bool SetFiles(IReadOnlyList<string> paths) => _n.ShelfWriteFiles(paths);
        public void Changed() => _n.ShelfChanged();

        /// <summary>A local folder (checked by the action off the UI thread), never a file: through Shell.Open, with a "\" at the end.</summary>
        public string OpenFolder(string folder)
        {
            if (ShelfPaths.TryNormalize(folder, out var p, out _) != ShelfRefusal.None) return "Folderul nu e pe acest PC.";
            try
            {
                Services.Shell.Open(p.TrimEnd('\\') + "\\");
                return null;
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception || ex is InvalidOperationException || ex is System.IO.IOException)
            {
                return "Folderul nu a putut fi deschis.";
            }
        }

        /// <summary>The image read off the UI thread (limits, scaled for OCR), then Windows' OCR as Win+Alt+T does it.</summary>
        public async Task<string> RecognizeTextAsync(string imagePath, CancellationToken ct)
        {
            BitmapSource img = await ShelfImages.LoadForOcrAsync(imagePath);
            ct.ThrowIfCancellationRequested();
            try { return await Services.ScreenTools.RecognizeAsync(img); }
            catch (Exception ex) when (ex is COMException || ex is System.IO.IOException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is InvalidOperationException)
            {
                throw new ShelfImageException("Recunoașterea textului nu a reușit pentru această imagine.");      // R1: a fixed message, not a feature error
            }
        }

        public Task<ShelfFileResult> ConvertImageAsync(string imagePath, ShelfImageFormat to, CancellationToken ct) => ShelfImages.ConvertAsync(imagePath, to, ct);
    }
}
