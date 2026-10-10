using System;
using System.Linq;
using System.Text.RegularExpressions;
using WinNotch.Core.Flags;
using WinNotch.Features.WindowV2;

namespace WinNotch
{
    public static partial class T
    {
        /// <summary>
        /// P52: the WinNotch window. Only the pure layout rules are tested here (the window itself is WPF): the four
        /// tabs and where an old entry point lands, which tab brings a column of its own, the sections of Sistem, and
        /// the source pins that keep the window from growing a second navigation or a second copy of a page.
        /// </summary>
        static void WindowV2Tests()
        {
            V2Tabs();
            V2Sections();
            V2Preview();
            V2SourcePins();
        }

        /// <summary>
        /// P70: the window's signature — the little monitor in the bezel with the real notch on it — and the footer
        /// that says what was just written. Only the maths is here; the drawing is WPF.
        /// </summary>
        static void V2Preview()
        {
            bool Same(PillBox b, double x, double w, double h) =>
                Math.Abs(b.X - x) < 0.01 && Math.Abs(b.Width - w) < 0.01 && Math.Abs(b.Height - h) < 0.01;

            var centre = PreviewModel.Pill("center", false);
            var left = PreviewModel.Pill("left", false);
            var right = PreviewModel.Pill("right", false);
            Check("PV01", "Pastila stă unde spune setarea: la mijloc, lipită de marginea din stânga sau de cea din dreapta",
                  Same(centre, (PreviewModel.FrameWidth - PreviewModel.FullWidth) / 2, PreviewModel.FullWidth, PreviewModel.FullHeight) &&
                  Same(left, PreviewModel.Margin, PreviewModel.FullWidth, PreviewModel.FullHeight) &&
                  Same(right, PreviewModel.FrameWidth - PreviewModel.FullWidth - PreviewModel.Margin, PreviewModel.FullWidth, PreviewModel.FullHeight) &&
                  left.X < centre.X && centre.X < right.X);

            var mini = PreviewModel.Pill("right", true);
            Check("PV02", "Forma mică e mai mică și rămâne lipită de aceeași margine",
                  Same(mini, PreviewModel.FrameWidth - PreviewModel.MiniWidth - PreviewModel.Margin, PreviewModel.MiniWidth, PreviewModel.MiniHeight) &&
                  mini.Width < PreviewModel.FullWidth && mini.Height < PreviewModel.FullHeight);

            var tight = PreviewModel.Pill("left", false, 200);
            Check("PV03", "Pe un cadru îngust pastila se strânge și nu trece niciodată de margini",
                  tight.Width <= 200 - 2 * PreviewModel.Margin + 0.01 &&
                  tight.X >= PreviewModel.Margin - 0.01 &&
                  tight.X + tight.Width <= 200 - PreviewModel.Margin + 0.01);

            Check("PV04", "O poziție necunoscută sau lipsă e citită ca „centru”, nu aruncă",
                  PreviewModel.Normalize(null) == "center" && PreviewModel.Normalize("") == "center" &&
                  PreviewModel.Normalize("CENTRU") == "center" && PreviewModel.Normalize(" Left ") == "left" &&
                  Same(PreviewModel.Pill(null, false), centre.X, centre.Width, centre.Height) &&
                  PreviewModel.PositionName("left") == "stânga" && PreviewModel.PositionName("right") == "dreapta" &&
                  PreviewModel.PositionName("nu.exista") == "centru");

            Check("PV05", "Rândul din subsol spune ce s-a scris, iar fără o schimbare nu spune nimic",
                  PreviewModel.LastChange("Poziție", "Centru") == "Poziție → Centru" &&
                  PreviewModel.LastChange("Poziție", "") == "Poziție" &&
                  PreviewModel.LastChange("", "Centru") == "" && PreviewModel.LastChange(null, null) == "");

            Check("PV06", "Indicatorul de rânduri numără corect, la singular și la plural",
                  PreviewModel.RowsLine(1, 4) == "1 rând din 4 folosit" &&
                  PreviewModel.RowsLine(3, 4) == "3 rânduri din 4 folosite" &&
                  PreviewModel.RowsLine(0, 4) == "0 rânduri din 4 folosite" &&
                  PreviewModel.RowsLine(9, 4) == "4 rânduri din 4 folosite" &&
                  PreviewModel.RowsLine(1, 0) == "");

            Check("PV07", "Numerele se scriu la fel peste tot: cu unitatea lor, fără zecimale inutile",
                  PreviewModel.Value(400, "ms") == "400 ms" && PreviewModel.Value(10, "s") == "10 s" &&
                  PreviewModel.Value(80, "%") == "80%" && PreviewModel.Value(1.15, "") == "1.15");
        }

        static void V2Tabs()
        {
            Check("WV1", "O singură navigare: patru file, cu id-uri unice, titluri în română și iconițe",
                  LayoutRules.Tabs.Count == 4 &&
                  LayoutRules.Tabs.Select(t => t.Id).SequenceEqual(new[] { "workspace", "widgeturi", "teme", "setari" }) &&
                  LayoutRules.Tabs.All(t => t.Id == t.Id.ToLowerInvariant() && !string.IsNullOrWhiteSpace(t.Title) && !string.IsNullOrEmpty(t.Glyph)) &&
                  LayoutRules.Tabs.All(t => !t.Title.Contains("curând", StringComparison.OrdinalIgnoreCase)) &&
                  LayoutRules.FindTab("teme") != null && LayoutRules.FindTab("nu.exista") == null &&
                  LayoutRules.DefaultTab == LayoutRules.Workspace);

            Check("WV2", "Intrările vechi în fereastră duc unde trebuie: teme, setări, noutăți, o pagină, nimic",
                  LayoutRules.TabFor("themes") == LayoutRules.Themes &&
                  LayoutRules.TabFor("settings") == LayoutRules.Settings && LayoutRules.TabFor("news") == LayoutRules.Settings &&
                  LayoutRules.TabFor("pagina-mea") == LayoutRules.Workspace &&
                  LayoutRules.TabFor(null) == LayoutRules.DefaultTab && LayoutRules.TabFor("") == LayoutRules.DefaultTab &&
                  LayoutRules.SectionFor("news") == "noutati" &&
                  LayoutRules.SectionFor(null) == LayoutRules.Sections[0].Id);

            // Problema raportată de autor pe 0.6.22: trei coloane de navigare una lângă alta.
            Check("WV3", "Coloana din stânga aparține filei, nu ferestrei: doar Workspace și Setări au una",
                  LayoutRules.HasLeftPanel(LayoutRules.Workspace) && LayoutRules.HasLeftPanel(LayoutRules.Settings) &&
                  !LayoutRules.HasLeftPanel(LayoutRules.Widgets) && !LayoutRules.HasLeftPanel(LayoutRules.Themes));

            Check("WV4", "Inspectorul e doar unde ai ce inspecta (widget-ul ales, în Workspace)",
                  LayoutRules.HasInspector(LayoutRules.Workspace) &&
                  !LayoutRules.HasInspector(LayoutRules.Widgets) && !LayoutRules.HasInspector(LayoutRules.Themes) &&
                  !LayoutRules.HasInspector(LayoutRules.Settings));

            Check("WV5", "Pe o fereastră îngustă se strâng pe rând: întâi inspectorul, apoi coloana din stânga",
                  LayoutRules.ShowInspector(LayoutRules.Workspace, 1120) && !LayoutRules.ShowInspector(LayoutRules.Workspace, 1119) &&
                  !LayoutRules.ShowInspector(LayoutRules.Themes, 1600) &&
                  LayoutRules.ShowLeftPanel(LayoutRules.Workspace, 900) && !LayoutRules.ShowLeftPanel(LayoutRules.Workspace, 899) &&
                  !LayoutRules.ShowLeftPanel(LayoutRules.Widgets, 1600));

            Check("WV6", "Mărimile și geometria: scara din brief (raza 18, 12 pentru controale mici, spațieri de 4)",
                  LayoutRules.CardRadius == 18 && LayoutRules.ChipRadius == 12 &&
                  LayoutRules.Gap == 12 && LayoutRules.Pad == 24 && LayoutRules.HeaderHeight == 56 &&
                  LayoutRules.BezelHeight == 132 && LayoutRules.BezelHeight > PreviewModel.FrameHeight &&
                  LayoutRules.MinWidth == 900 && LayoutRules.MinHeight == 600 &&
                  LayoutRules.LeftWidth == 212 && LayoutRules.InspectorWidth == 300);

            Check("WV7", "Cardurile din Sistem: 3 peste 1280, 2 peste 900, 1 mai jos",
                  LayoutRules.Columns(1600) == 3 && LayoutRules.Columns(1280) == 3 && LayoutRules.Columns(1279) == 2 &&
                  LayoutRules.Columns(900) == 2 && LayoutRules.Columns(899) == 1);

            Check("WV8", "Sugestia din bara de jos spune ce să faci când Command Bar-ul e oprit",
                  LayoutRules.Hint(true).Length > 10 && LayoutRules.Hint(false).Contains("Setări") &&
                  LayoutRules.Hint(true) != LayoutRules.Hint(false));
        }

        static void V2Sections()
        {
            Check("WV9", "Secțiunile din Setări au id-uri unice, titluri în română și iconițe",
                  LayoutRules.Sections.Count >= 8 &&
                  LayoutRules.Sections.Select(s => s.Id).Distinct().Count() == LayoutRules.Sections.Count &&
                  LayoutRules.Sections.All(s => s.Id == s.Id.ToLowerInvariant() && !string.IsNullOrWhiteSpace(s.Title) && !string.IsNullOrEmpty(s.Glyph)) &&
                  LayoutRules.FindSection("notch") != null && LayoutRules.FindSection("nu.exista") == null &&
                  LayoutRules.Sections[0].Id == "notch");

            // P14: fiecare acțiune „settings.*” numește o opțiune prin controlul ei vechi; toate trebuie să aibă unde să cadă.
            Check("WV10", "Fiecare opțiune a paginii vechi de setări are o secțiune în pagina nouă",
                  SettingsMap.Targets.Count >= 20 &&
                  SettingsMap.Targets.Distinct().Count() == SettingsMap.Targets.Count &&
                  SettingsMap.Targets.All(t => LayoutRules.FindSection(SettingsMap.SectionFor(t)) != null) &&
                  SettingsMap.Targets.All(t => !string.IsNullOrWhiteSpace(SettingsMap.NameFor(t))) &&
                  SettingsMap.SectionFor("nu.exista") == null && SettingsMap.NameFor("nu.exista") == null,
                  string.Join(", ", SettingsMap.Targets.Where(t => LayoutRules.FindSection(SettingsMap.SectionFor(t)) == null)));

            Check("WV11", "Harta acoperă exact opțiunile pe care le numesc acțiunile „settings.*”",
                  Features.CommandBar.SettingsActions.All.Select(o => o.Target).Distinct()
                      .All(t => SettingsMap.SectionFor(t) != null),
                  string.Join(", ", Features.CommandBar.SettingsActions.All.Select(o => o.Target).Distinct()
                      .Where(t => SettingsMap.SectionFor(t) == null)));

            Check("WV12", "Comutatorul din catalog are id-ul regulilor, e Experimental și oprit implicit",
                  LayoutRules.FeatureId == FeatureCatalog.WindowV2 &&
                  FeatureCatalog.Find(LayoutRules.FeatureId) is FeatureInfo fi &&
                  fi.Stage == FeatureStage.Experimental && !fi.DefaultOn);
        }

        static void V2SourcePins()
        {
            string app = Src("App.xaml.cs"), win = Src("Features/WindowV2/WindowV2.cs"),
                   work = Src("Features/WindowV2/WorkspaceView.cs"), insp = Src("Features/WindowV2/WidgetInspector.cs"),
                   set = Src("Features/WindowV2/SettingsView.cs"), wid = Src("Features/WindowV2/WidgetsView.cs"),
                   ctl = Src("Features/WindowV2/V2Controls.cs"), lib = Src("Features/WindowV2/WidgetLibrary.cs"),
                   emb = Src("Features/WindowV2/EmbeddedPages.cs"), ed = Src("EditorWindow.cs"),
                   prev = Src("Features/WindowV2/NotchPreview.cs");

            Check("WV13", "Non-regresie: cu comutatorul oprit se deschide fereastra veche, iar o eroare în v2 cade pe ea",
                  app.Contains("if (OpenWindowV2(pageId, slotId)) return;") &&
                  app.Contains("if (!(Core.Flags.FeatureFlags.Current?.IsEnabled(Features.WindowV2.LayoutRules.FeatureId) ?? false)) return false;") &&
                  app.Contains("ReportError(Features.WindowV2.LayoutRules.FeatureId, ex)"));

            // Raportat de autor: fereastra veche era lipită înăuntrul celei noi, cu paleta ei fixă deschisă.
            Check("WV14", "Fereastra nouă nu mai găzduiește fereastra clasică: editorul de pagini e scris aici, pe jetoanele temei",
                  !win.Contains("EditorWindow") && !work.Contains("EditorWindow") && !emb.Contains("EditorWindow") &&
                  !ed.Contains("internal FrameworkElement TakeContent(Window host)") &&
                  !ed.Contains("internal void DetachContent()") &&
                  work.Contains("new WidgetPage(") && work.Contains("new WidgetLibrary("),
                  "a rămas o legătură cu EditorWindow");

            Check("WV15", "Niciun card „se deschide în fereastra clasică”, nicio funcție desenată degeaba",
                  new[] { win, work, insp, set, wid, emb, ctl, lib }.All(f =>
                      !f.Contains("fereastra clasică") && !f.Contains("în curând") && !f.Contains("Focus Mode") &&
                      !f.Contains("Window Wizard") && !f.Contains("Dev Tools")) &&
                  !app.Contains("OpenClassicEditor"));

            Check("WV16", "Tema aplicației peste tot: pensule prin DynamicResource, nicio culoare scrisă în cod în afara paletei temei",
                  new[] { win, work, insp, set, wid, ctl, lib, prev }.All(f =>
                      !Regex.IsMatch(f, @"Color\.From|Brushes\.(?!Transparent)") &&
                      Count(f, "new SolidColorBrush") == Count(f, "new SolidColorBrush(ThemeManager.Parse")) &&
                  Count(emb, "new SolidColorBrush") == Count(emb, "new SolidColorBrush(ThemeManager.Parse") &&
                  win.Contains("SetResourceReference") && work.Contains("SetResourceReference") && insp.Contains("SetResourceReference"));

            // P70: antetul nu mai e o siluetă desenată degeaba, ci monitorul cu notch-ul pe el — dar tot prin
            // traducătorul lui P50: forma din fereastră și forma din marginea ecranului nu pot să se despartă.
            Check("WV17", "Previzualizarea din ramă refolosește geometria notch-ului ancorat (P50) prin același traducător, nu una nouă",
                  prev.Contains("AnchoredGeometry.Radius(") && prev.Contains("AnchoredGeometry.Ear(") &&
                  prev.Contains("AnchoredShape.Silhouette(") && !prev.Contains("new StreamGeometry()") &&
                  !win.Contains("new StreamGeometry()") &&
                  // unde stă pastila e o regulă pură, testată, nu un număr scris în desen
                  prev.Contains("PreviewModel.Pill(") && win.Contains("_preview.Render("));

            Check("WV18", "Acțiunile pornesc doar prin registru, cu confirmare pentru ce nu e sigur, și în bara de comandă",
                  set.Contains("ActionRegistry.Current?.InvokeAsync(") && set.Contains("ActionInvoker.UI, default, confirmed") &&
                  win.Contains("reg.InvokeAsync(hit.Id, null, ActionInvoker.UI, default, confirmed)") &&
                  new[] { win, set }.All(f => f.Contains("Safety == ActionSafety.Safe")));

            Check("WV19", "Fereastra respectă protocolul comutatorului (abonare, dezabonare, se închide când se oprește) și raportează erorile",
                  win.Contains("FeatureFlags.Current.Changed += _flagHandler;") &&
                  win.Contains("FeatureFlags.Current.Changed -= _flagHandler;") &&
                  win.Contains("ReportError(LayoutRules.FeatureId, ex)"));

            Check("WV20", "Tastatura: filele, rândurile și comutatoarele sunt butoane cu nume, iar Esc închide întâi panoul de peste pagină",
                  new[] { win, work, set, ctl }.All(f => f.Contains("Ui.S(\"NavButton\")") && f.Contains("AutomationProperties.SetName")) &&
                  !new[] { win, work, set }.Any(f => f.Contains("Ui.S(\"IconButton\")")) &&
                  win.Contains("if (e.Key == Key.Escape) { if (!CloseOpenPopup()) Close(); e.Handled = true; }") &&
                  work.Contains("internal bool CloseOpenPopup()"));

            Check("WV21", "Cronometre: ceasul și pagina vie se opresc cu fereastra minimizată, iar ce e în așteptare se salvează la închidere",
                  win.Contains("if (!IsVisible || WindowState == WindowState.Minimized) return;") &&
                  win.Contains("StateChanged +=") && win.Contains("_live.Stop();") &&
                  Regex.IsMatch(win, @"Closed \+=[\s\S]{0,400}?Flush\(\);"));

            Check("WV22", "Inspectorul nu rescrie modelul: mărimile vin din galerie, opțiunile din definiția widget-ului",
                  insp.Contains("Gallery.SizePreviews(") && insp.Contains("def.Options") && insp.Contains("page.Commit()") &&
                  !insp.Contains("new OptionDef") && !insp.Contains("new WidgetDef"));

            // Raportat de autor pe 0.6.23: pagina de setări era foaia albă a ferestrei vechi, lipită în fereastra pe temă întunecată.
            Check("WV23", "Setările sunt desenate aici, pe jetoanele temei; nicio bucată din fereastra clasică nu mai e găzduită",
                  !emb.Contains("SettingsWindow") && !set.Contains("SettingsWindow") && !win.Contains("SettingsWindow") &&
                  !new[] { win, work, set, emb }.Any(f => f.Contains("TakeContent")) &&
                  set.Contains("V2Controls.Toggle(") && set.Contains("V2Controls.Choice(") && set.Contains("V2Controls.Card("),
                  "a rămas conținut găzduit din fereastra clasică");

            Check("WV27", "Setările se aplică pe loc: niciun buton de salvare sau de renunțare, fiecare schimbare scrie și anunță notch-ul",
                  set.Contains("_s.Save();") && set.Contains("_notch?.ApplySettings();") &&
                  // nu în comentarii: un buton chiar etichetat „Salvează” sau „Renunță”
                  !Regex.IsMatch(set, @"(PillBtn|Pill|Content\s*=)\s*\(?\s*""(Salvează|Renunță)"));

            Check("WV28", "Noutățile se citesc cu codul existent, temele trec prin ThemeEdits",
                  emb.Contains("Services.Updater.ParseNotes(Services.Updater.OwnNotes())") &&
                  new[] { "SetOverride", "ResetOverride", "ResetAll", "IsChanged", "Choose", "SaveAs", "Delete", "PickColor" }
                      .All(m => emb.Contains("Core.Ui.ThemeEdits." + m + "(")));

            Check("WV24", "O schimbare făcută în notch ajunge la fereastră, iar redeschiderea pe aceeași pagină nu o reconstruiește",
                  app.Contains("_windowV2?.PageChangedElsewhere(pageId);") &&
                  app.Contains("_windowV2?.PagesChangedElsewhere();") &&
                  win.Contains("if (tab == _tab &&"));

            // Cerut de autor pe 0.6.24: ecranul împărțit în două — sus editezi, jos e ce poți adăuga.
            Check("WV29", "Workspace e împărțit în două jumătăți cu un separator tras de utilizator, nu un singur derulaj lung",
                  work.Contains("private void BuildSplit()") && work.Contains("new GridSplitter()") &&
                  work.Contains("GridResizeDirection.Rows") &&
                  work.Contains("_editorHost") && work.Contains("_libraryHost") &&
                  // pagina crește în jumătatea ei, nu mai e un dreptunghi fix
                  work.Contains("StretchDirection.Both") && work.Contains("PageWidth * MaxZoom"));

            Check("WV30", "Biblioteca de widget-uri e o bandă largă: categoriile pe orizontală, cardurile într-un rând",
                  lib.Contains("Orientation = Orientation.Horizontal") &&
                  lib.Contains("HorizontalScrollBarVisibility = ScrollBarVisibility.Auto") &&
                  lib.Contains("VerticalScrollBarVisibility = ScrollBarVisibility.Disabled") &&
                  // nimic despre un widget nu e rescris: previzualizarea, mărimile și tragerea sunt cele existente
                  lib.Contains("Gallery.LivePreview(") && lib.Contains("Gallery.SizePreviews(") &&
                  lib.Contains("Core.Ui.WidgetDrag.Bind(") && !lib.Contains("DoDragDrop"));

            Check("WV31", "Gestul de tragere e unul singur, folosit și de galerie și de bibliotecă",
                  Src("Widgets/Gallery.cs").Contains("Core.Ui.WidgetDrag.Bind(") &&
                  Count(Src("Widgets/Gallery.cs"), "DoDragDrop") == 0 &&
                  Count(Src("Core/Ui/WidgetDrag.cs"), "DoDragDrop") == 1);

            Check("WV32", "Esc închide întâi fereastra de mărimi a bibliotecii, apoi pe cea a paginii, apoi fereastra",
                  work.Contains("if (_library != null && _library.CloseOpenPopup()) return true;") &&
                  lib.Contains("internal bool CloseOpenPopup()"));

            // App-wide, learned the hard way: a repeated x:Key makes WPF throw while loading the resources, so the app
            // dies before its first window. Nicio probă unitară nu prindea asta — doar testul de fum, la pornire.
            string theme = Src("Theme.xaml");
            var dup = Regex.Matches(theme, "x:Key=\"([^\"]+)\"").Select(m => m.Groups[1].Value)
                           .GroupBy(k => k, StringComparer.Ordinal).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
            Check("WV25", "Theme.xaml nu are două resurse cu aceeași cheie (WPF aruncă la pornire, înainte de orice fereastră)",
                  dup.Count == 0, string.Join(", ", dup));

            int nbStart = theme.IndexOf("x:Key=\"NavButton\"", StringComparison.Ordinal);
            int nbEnd = nbStart < 0 ? -1 : theme.IndexOf("</Style>", nbStart, StringComparison.Ordinal);
            string navButton = nbStart < 0 || nbEnd < 0 ? "" : Norm(theme.Substring(nbStart, nbEnd - nbStart));
            Check("WV26", "Stilul „NavButton” nu fixează mărimea, e focusabil și arată focusul cu contur în AccentBrush",
                  navButton.Length > 0 &&
                  !navButton.Contains("Property=\"Width\"", StringComparison.Ordinal) &&
                  !navButton.Contains("Property=\"Height\"", StringComparison.Ordinal) &&
                  navButton.Contains("<Setter Property=\"Focusable\" Value=\"True\"/>", StringComparison.Ordinal) &&
                  navButton.Contains("<Trigger Property=\"IsKeyboardFocused\" Value=\"True\">", StringComparison.Ordinal) &&
                  navButton.Contains("{DynamicResource AccentBrush}", StringComparison.Ordinal),
                  navButton.Length == 0 ? "stilul lipsește din Theme.xaml" : "");

            // P70: fereastra a fost redesenată ca un aparat — etichete gravate peste rânduri despărțite de o linie de
            // un pixel și numere monospațiate, în locul cardurilor rotunjite unul într-altul.
            Check("WV33", "Desenul nou: grupurile sunt etichete peste rânduri cu linie de 1 px, fără carduri",
                  ctl.Contains("internal static TextBlock Eyebrow(") && ctl.Contains("internal static Border Rule(") &&
                  ctl.Contains("internal static TextBlock Mono(") && ctl.Contains("internal static TextBlock Title(") &&
                  // „Card” a rămas numele din API, ca paginile să nu fie rescrise, dar nu mai desenează un card
                  !ctl.Contains("Ui.Card(") && !ctl.Contains("Ui.Chip(") &&
                  new[] { win, work, insp, set }.All(f => !f.Contains("Ui.Cap(")) &&
                  Src("Theme.xaml").Contains("x:Key=\"LabelFont\"") &&
                  Src("Themes.cs").Contains("P(\"Aparat\", false,"));

            Check("WV34", "Se aplică pe loc și se vede: controalele spun ce au schimbat, iar fereastra taie legătura la închidere",
                  ctl.Contains("internal static Action<string, string> Report;") && ctl.Contains("Report?.Invoke(") &&
                  win.Contains("V2Controls.Report = Changed;") && win.Contains("V2Controls.Report = null;") &&
                  win.Contains("PreviewModel.LastChange("));

            Check("WV35", "Workspace spune cât de înalt va crește notch-ul: un indicator de rânduri sub pagină",
                  work.Contains("PreviewModel.RowsLine(") && work.Contains("Layout.UsedRows(") && work.Contains("Layout.MaxRows"));
        }
    }
}
