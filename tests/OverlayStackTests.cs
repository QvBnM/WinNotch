using System;
using System.Linq;
using WinNotch.Core.Flags;
using WinNotch.Core.Ui;

namespace WinNotch
{
    public static partial class T
    {
        /// <summary>
        /// P51: „Ieșire audio” (and the shelf, the gallery, the sizes, the note) stayed open on a click elsewhere, and Esc
        /// closed nothing. The rules of <see cref="OverlayStack"/>: what closes what, in which order, and the test that
        /// failed before the repair — a pop-up closed by its backdrop told nobody, so the caller kept a dead reference.
        /// </summary>
        static void OverlayStackTests()
        {
            OverlayStackRules();
            OverlayStackSourcePins();
        }

        /// <summary>A fake panel: remembers whether it was closed and why.</summary>
        sealed class FakeOverlay
        {
            public string Id;
            public int Closed;
            public OverlayClose Why;
            public Action<OverlayClose> Close => why => { Closed++; Why = why; };
        }

        static void OverlayStackRules()
        {
            var st = new OverlayStack();
            var audio = new FakeOverlay { Id = "ieșire-audio" };
            var shelf = new FakeOverlay { Id = "raft" };
            var qa = new FakeOverlay { Id = "quick-actions" };

            st.Register(qa.Id, OverlayLevel.Hint, qa.Close);
            st.Register(audio.Id, OverlayLevel.Panel, audio.Close);
            Check("OS1a", "Un panou nou nu închide un indiciu (quick actions rămân)", qa.Closed == 0 && st.Count == 2);

            st.Register(shelf.Id, OverlayLevel.Panel, shelf.Close);
            Check("OS1b", "Un panou deschis peste altul îl închide pe primul, cu motivul „alt panou”",
                  audio.Closed == 1 && audio.Why == OverlayClose.OtherPanel && qa.Closed == 0 &&
                  st.IsOpen(shelf.Id) && !st.IsOpen(audio.Id) && st.Topmost == shelf.Id);

            Check("OS2", "Click în afară închide doar panoul de deasupra",
                  st.OnOutsideClick(null) == shelf.Id && shelf.Closed == 1 && shelf.Why == OverlayClose.OutsideClick &&
                  qa.Closed == 0 && st.Topmost == qa.Id);

            Check("OS2b", "Click în interiorul panoului de deasupra nu închide nimic",
                  st.OnOutsideClick(qa.Id) == null && qa.Closed == 0);

            Check("OS2c", "Click în afară închide și un indiciu, când e singurul deschis",
                  st.OnOutsideClick(null) == qa.Id && qa.Closed == 1 && st.Count == 0 && st.OnOutsideClick(null) == null);

            // Esc, from the top down, one press at a time
            var a2 = new FakeOverlay { Id = "galerie" };
            var h2 = new FakeOverlay { Id = "notă" };
            st.Register(h2.Id, OverlayLevel.Hint, h2.Close);
            st.Register(a2.Id, OverlayLevel.Panel, a2.Close);
            Check("OS3", "Esc închide de sus în jos, câte unul, apoi nimic",
                  st.OnEscape() == a2.Id && a2.Why == OverlayClose.Escape && st.Count == 1 &&
                  st.OnEscape() == h2.Id && st.Count == 0 && st.OnEscape() == null);

            var p1 = new FakeOverlay { Id = "raft" };
            var p2 = new FakeOverlay { Id = "quick-actions" };
            st.Register(p1.Id, OverlayLevel.Panel, p1.Close);
            st.Register(p2.Id, OverlayLevel.Hint, p2.Close);
            Check("OS4", "Închiderea notch-ului / intrarea în editare închid tot, de sus în jos",
                  st.CloseAll(OverlayClose.NotchClosed) == 2 && p1.Closed == 1 && p2.Closed == 1 &&
                  p1.Why == OverlayClose.NotchClosed && st.Count == 0 && st.CloseAll(OverlayClose.EditMode) == 0);

            var once = new FakeOverlay { Id = "mărimi" };
            st.Register(once.Id, OverlayLevel.Panel, once.Close);
            st.Close(once.Id, OverlayClose.Button);
            Check("OS5", "Close al unui panou deja închis nu face nimic și nu aruncă",
                  once.Closed == 1 && !st.Close(once.Id, OverlayClose.Button) && !st.Close("nu.exista", OverlayClose.Escape) &&
                  !st.Close(null, OverlayClose.Escape) && once.Closed == 1);

            // the bug: the caller's own closing routine calls Close again (its button, or the backdrop) → no second call, no loop
            var re = new FakeOverlay { Id = "ieșire-audio" };
            int depth = 0, max = 0;
            Action<OverlayClose> reentrant = why =>
            {
                depth++;
                max = Math.Max(max, depth);
                re.Closed++;
                st.Close(re.Id, OverlayClose.Button);
                depth--;
            };
            st.Register(re.Id, OverlayLevel.Panel, reentrant);
            st.OnEscape();
            Check("OS6", "Rutina de închidere a apelantului poate apela Close: se închide o singură dată, fără buclă",
                  re.Closed == 1 && max == 1 && st.Count == 0);

            Check("OS7", "Tastatura se citește doar cât teancul nu e gol",
                  !new OverlayStack().NeedsKeyboard && NeedsKeyboardWith(OverlayLevel.Hint) && NeedsKeyboardWith(OverlayLevel.Panel));

            var st2 = new OverlayStack();
            var modal = new FakeOverlay { Id = "command-bar" };
            var under = new FakeOverlay { Id = "raft" };
            st2.Register(modal.Id, OverlayLevel.Modal, modal.Close);
            st2.Register(under.Id, OverlayLevel.Panel, under.Close);
            Check("OS8", "Un modal rămâne deasupra, chiar dacă se deschide un panou după el",
                  st2.Topmost == modal.Id && modal.Closed == 0 && st2.OnEscape() == modal.Id);

            var st3 = new OverlayStack();
            var first = new FakeOverlay { Id = "raft" };
            var again = new FakeOverlay { Id = "raft" };
            st3.Register(first.Id, OverlayLevel.Panel, first.Close);
            st3.Register(again.Id, OverlayLevel.Panel, again.Close);
            Check("OS9", "Același id înregistrat a doua oară înlocuiește intrarea (fără dubluri)",
                  st3.Count == 1 && first.Closed == 1 && again.Closed == 0 && st3.LevelOf("raft") == OverlayLevel.Panel);

            Check("OS10", "Comutatorul din catalog are id-ul regulilor, e Beta și pornit implicit",
                  OverlayStack.FeatureId == FeatureCatalog.OverlayDismiss &&
                  FeatureCatalog.Find(OverlayStack.FeatureId) is FeatureInfo fi && fi.Stage == FeatureStage.Beta && fi.DefaultOn);

            var st4 = new OverlayStack();
            var pa = new FakeOverlay { Id = "raft" };
            var hb = new FakeOverlay { Id = "notă" };
            st4.Register(pa.Id, OverlayLevel.Panel, pa.Close);
            st4.Register(hb.Id, OverlayLevel.Hint, hb.Close);
            Check("OS11", "CloseAll pe un nivel lasă celălalt în pace",
                  st4.CloseAll(OverlayLevel.Panel, OverlayClose.OtherPanel) == 1 && pa.Closed == 1 && hb.Closed == 0 &&
                  st4.Ids.Count == 1 && st4.Ids[0] == hb.Id);
        }

        static bool NeedsKeyboardWith(OverlayLevel level)
        {
            var st = new OverlayStack();
            st.Register("x", level, _ => { });
            return st.NeedsKeyboard;
        }

        /// <summary>The hooks in the old files, and the repair the brief asks for by name: the pop-up tells its caller.</summary>
        static void OverlayStackSourcePins()
        {
            string pages = Src("NotchWindow.Pages.cs"), gallery = Src("Widgets/Gallery.cs"), notch = Src("NotchWindow.xaml.cs"),
                   editor = Src("EditorWindow.cs");

            Check("OS12", "Pop-up-ul închis de fundalul lui anunță apelantul, o singură dată",
                  gallery.Contains("Action close = () => { if (closed) return; closed = true; layer.Children.Remove(dim); onClosed?.Invoke(); };") &&
                  gallery.Contains("dim.MouseLeftButtonUp += (o, e) => { if (e.OriginalSource == dim) close(); };") &&
                  !gallery.Contains("if (e.OriginalSource == dim) layer.Children.Remove(dim);"));

            Check("OS13", "Mărimile nu mai lasă referințe moarte: _sizes și _popup se curăță la click în afară",
                  pages.Contains("onClosed: () => { _sizes = null; _popup = (null, null); OverlayUnregister(OvSizes); RelayoutPanel(); }") &&
                  pages.Contains("onClosed: () => _popup = (null, null)"));

            Check("OS14", "Panourile se înregistrează, fiecare pe nivelul lui",
                  Src("Features/AudioSwitch/NotchWindow.AudioSwitch.cs").Contains("OverlayRegister(OvAudio, Core.Ui.OverlayLevel.Panel") &&
                  Src("Features/Shelf/NotchWindow.Shelf.cs").Contains("OverlayRegister(OvShelf, Core.Ui.OverlayLevel.Panel") &&
                  Src("Features/QuickActions/NotchWindow.QuickActions.cs").Contains("OverlayRegister(OvQuickActions, Core.Ui.OverlayLevel.Hint") &&
                  pages.Contains("OverlayRegister(OvGallery, Core.Ui.OverlayLevel.Panel") &&
                  pages.Contains("OverlayRegister(OvSizes, Core.Ui.OverlayLevel.Panel") &&
                  pages.Contains("OverlayRegister(OvNote, Core.Ui.OverlayLevel.Hint"));

            Check("OS15", "Legăturile din notch: pornire, oprire, Esc/click din PollTick, închiderea notch-ului și editarea",
                  notch.Contains("StartOverlays();") && notch.Contains("StopOverlays();") && notch.Contains("OverlayPollTick();") &&
                  notch.Contains("OverlayCloseAll(Core.Ui.OverlayClose.NotchClosed);") &&
                  pages.Contains("OverlayCloseAll(Core.Ui.OverlayClose.EditMode);"));

            Check("OS16", "În fereastra WinNotch, Esc trece prin PreviewKeyDown (acolo e focus real), nu prin polling",
                  editor.Contains("PreviewKeyDown += (o, e) =>") && editor.Contains("_edPopup.Close();") &&
                  !editor.Contains("GetAsyncKeyState"));

            Check("OS17", "Numele panourilor sunt aceleași cu cele din plasa de siguranță (fără „alte-N”)",
                  new[] { "galerie", "mărimi", "notă", "raft", "ieșire-audio", "quick-actions" }
                      .All(n => Src("Features/NotchGuard/NotchWindow.NotchGuard.cs").Contains("\"" + n + "\"")));
        }
    }
}
