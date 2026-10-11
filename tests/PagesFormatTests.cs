using System;
using System.Linq;
using WinNotch.Core.Flags;
using WinNotch.Features.PagesFormat;

namespace WinNotch
{
    /// <summary>
    /// P75: forma celor patru pagini, după planșele din `docs/design`. Regulile pure (scara de text, cele două
    /// accente, coloanele) și pinii care țin forma nouă lângă cea veche, fără s-o rescrie pe aceea.
    /// </summary>
    public static partial class T
    {
        static void PagesFormatTests()
        {
            PageFormatScale();
            PageFormatGeometry();
            PageFormatSourcePins();
        }

        static void PageFormatScale()
        {
            var all = Enum.GetValues(typeof(Ink)).Cast<Ink>().ToList();

            Check("PF1", "Fiecare rol are o mărime pozitivă, iar scara coboară de la numărul mare la eticheta gravată",
                  all.All(i => PageFormat.Size(i) > 0) &&
                  PageFormat.Size(Ink.Big) > PageFormat.Size(Ink.Medium) &&
                  PageFormat.Size(Ink.Medium) > PageFormat.Size(Ink.Value) &&
                  PageFormat.Size(Ink.Value) > PageFormat.Size(Ink.Label) &&
                  PageFormat.Size(Ink.Label) > PageFormat.Size(Ink.Eyebrow));

            Check("PF2", "În fontul cu cifre de aceeași lățime intră toate numerele și etichetele gravate, și numai ele",
                  new[] { Ink.Big, Ink.Medium, Ink.Value, Ink.Note, Ink.Eyebrow, Ink.Unit }.All(PageFormat.Mono) &&
                  new[] { Ink.Title, Ink.Name, Ink.Sub, Ink.Label }.All(i => !PageFormat.Mono(i)));

            Check("PF3", "Cuvintele mărunte nu sunt îngroșate: eticheta, textul secundar și cel gravat rămân subțiri",
                  !PageFormat.Strong(Ink.Label) && !PageFormat.Strong(Ink.Sub) && !PageFormat.Strong(Ink.Eyebrow) &&
                  PageFormat.Strong(Ink.Title) && PageFormat.Strong(Ink.Big));

            Check("PF4", "Culorile vin din temă, pe roluri: titlul și numerele în cerneală, secundarul stins, gravatul și mai stins",
                  PageFormat.BrushKey(Ink.Title) == "InkBrush" && PageFormat.BrushKey(Ink.Big) == "InkBrush" &&
                  PageFormat.BrushKey(Ink.Sub) == "MutedBrush" && PageFormat.BrushKey(Ink.Label) == "MutedBrush" &&
                  PageFormat.BrushKey(Ink.Eyebrow) == "DimBrush" && PageFormat.BrushKey(Ink.Note) == "DimBrush" &&
                  all.All(i => PageFormat.BrushKey(i).EndsWith("Brush", StringComparison.Ordinal)));

            // Planșele folosesc exact două accente, fiecare cu un singur sens: chihlimbar = ce se mișcă acum,
            // ger = date reci. Dacă un număr nu e niciuna, rămâne alb — asta le ține pe amândouă citibile.
            Check("PF5", "Două accente, fiecare cu un sens; restul numerelor rămân albe",
                  PageFormat.ReadingBrush(PageFormat.Reading.Live) == "AccentBrush" &&
                  PageFormat.ReadingBrush(PageFormat.Reading.Cold) == "InfoBrush" &&
                  PageFormat.ReadingBrush(PageFormat.Reading.Alarm) == "HotBrush" &&
                  PageFormat.ReadingBrush(PageFormat.Reading.Plain) == "InkBrush");

            Check("PF6", "Se gravează doar ce trebuie gravat, iar majusculele nu strică diacriticele",
                  PageFormat.Tracking(Ink.Eyebrow) > 0 && PageFormat.Tracking(Ink.Unit) > 0 &&
                  PageFormat.Tracking(Ink.Title) == 0 && PageFormat.Tracking(Ink.Value) == 0 &&
                  PageFormat.Eyebrow("Placă video") == "PLACĂ VIDEO" && PageFormat.Eyebrow("Consumă acum") == "CONSUMĂ ACUM" &&
                  PageFormat.Eyebrow(null) == "");
        }

        static void PageFormatGeometry()
        {
            var (l, r) = PageFormat.Columns(684, PageFormat.SystemSplit);
            Check("PF7", "Coloanele paginii Sistem ies ca în planșă (394 / 280, cu șanț de 10)",
                  l == 394 && r == 280 && Math.Abs(l + r + PageFormat.Gutter - 684) < 0.001);

            var (hl, hr) = PageFormat.Columns(684, PageFormat.HomeSplit);
            var (dl, dr) = PageFormat.Columns(684, PageFormat.DevicesSplit);
            // Proporțiile sunt raportate la ce împart cele două coloane (conținutul fără șanț), fiindcă șanțul e fix:
            // altfel fiecare coloană ieșea cu câțiva pixeli mai scurtă decât în planșă.
            Check("PF8", "Și celelalte: Acasă 440 / 234, Dispozitive 374 / 300",
                  hl == 440 && hr == 234 && dl == 374 && dr == 300);

            // Notch-ul are lățimea aleasă de utilizator, deci împărțirea e o proporție, nu pixelii din planșă.
            Check("PF9", "La orice lățime, cele două coloane plus șanțul dau exact conținutul, și niciuna nu e negativă",
                  new double[] { 300, 480, 684, 900, 1200 }.All(w =>
                  {
                      var (a, b) = PageFormat.Columns(w, PageFormat.SystemSplit);
                      return a >= 0 && b >= 0 && Math.Abs(a + b + PageFormat.Gutter - w) < 0.001;
                  }));

            Check("PF10", "O împărțire absurdă e strânsă între limite, iar o lățime negativă nu produce coloane negative",
                  PageFormat.Columns(684, 5).Left <= 684 && PageFormat.Columns(684, -3).Left >= 0 &&
                  PageFormat.Columns(-50, PageFormat.SystemSplit).Left >= 0 && PageFormat.Columns(-50, PageFormat.SystemSplit).Right >= 0);

            Check("PF11", "Conținutul e panoul fără cele două margini, iar înălțimile sunt cele din planșe",
                  PageFormat.Content(720) == 684 && PageFormat.Content(0) == 0 &&
                  PageFormat.SystemHeight == 320 && PageFormat.HomeHeight == 310 &&
                  PageFormat.DevicesHeight == 330 && PageFormat.ToolsHeight == 360);
        }

        static void PageFormatSourcePins()
        {
            var info = FeatureCatalog.Find(PageFormat.FeatureId);
            // Livrată în 0.6.33, dar oprită: prima rulare cu ea pornită a raportat notch-ul închizându-se singur după
            // un click pe Quick Actions. Până se lămurește, nu devine implicită.
            Check("PF12", "Comutatorul din catalog are id-ul regulilor, e Experimental și oprit implicit",
                  info != null && FeatureCatalog.PageFormat == PageFormat.FeatureId &&
                  info.Stage == FeatureStage.Experimental && !info.DefaultOn);

            // Plasa: nici constructorul, nici treaba lor repetată nu pot dărâma notch-ul — a treia eroare în zece
            // minute oprește forma singură și paginile vechi revin.
            Check("PF21", "Fiecare pagină nouă are plasa funcției sub ea, nu doar constructorul",
                  Src("Features/PagesFormat/NotchWindow.PagesFormat.cs").Contains("internal static void GuardFormat(Action body)") &&
                  new[] { "SystemPage", "DevicesPage", "ToolsPage", "HomePage" }
                      .All(n => Src("Features/PagesFormat/" + n + ".cs").Contains("Refresh() => NotchWindow.GuardFormat(RefreshCore);")) &&
                  Src("Features/PagesFormat/HomePage.cs").Contains("Fast() => NotchWindow.GuardFormat(FastCore);") &&
                  Src("Features/PagesFormat/HomePage.cs").Contains("OnMediaChanged() => NotchWindow.GuardFormat(OnMediaChangedCore);"));

            string hook = Src("Features/PagesFormat/NotchWindow.PagesFormat.cs"), pages = Src("NotchWindow.Pages.cs"),
                   notch = Src("NotchWindow.xaml.cs");

            Check("PF13", "Toate patru au forma nouă, aleasă la fabrica de pagini, cu întoarcere la cea veche",
                  pages.Contains("FormatPane(id) ?? id switch") &&
                  hook.Contains("\"home\" => _fmtHome ??= new HomePage(this),") &&
                  hook.Contains("\"system\" => _fmtSystem ??= new SystemPage(this),") &&
                  hook.Contains("\"devices\" => _fmtDevices ??= new DevicesPage(this),") &&
                  hook.Contains("\"tools\" => _fmtTools ??= new ToolsPage(this),") &&
                  hook.Contains("if (!PageFormatEnabled()) return null;"));

            // Paginile vechi nu știu de funcție: dacă ar ști, n-ar mai fi o întoarcere, ci o a doua cale de întreținut.
            Check("PF14a", "Niciuna dintre cele patru pagini vechi nu citește comutatorul",
                  new[] { "HomePane", "SystemPane", "DevicesPane", "ToolsPane" }
                      .All(n => !Src("Panes/" + n + ".cs").Contains("PageFormat") &&
                                !Src("Panes/" + n + ".cs").Contains("pagini-format")));

            Check("PF14", "Amândouă formele aceleiași pagini răspund la același id (altfel reconstruirea uită pe care erai)",
                  pages.Contains("p == _home || p == _sources || p == _fmtHome ? \"home\"") &&
                  pages.Contains("p == _system || p == _fmtSystem ? \"system\"") &&
                  pages.Contains("p == _devices || p == _fmtDevices ? \"devices\"") &&
                  pages.Contains("p == _tools || p == _fmtTools ? \"tools\"") &&
                  pages.Contains("ForgetFormatPanes();") &&
                  hook.Contains("internal void ForgetFormatPanes()"));

            Check("PF15", "Protocolul comutatorului: pornire, oprire, dezabonare, UI pe Dispatcher, erorile prin ReportError",
                  notch.Contains("StartPagesFormat();") && notch.Contains("StopPagesFormat();") &&
                  hook.Contains("FeatureFlags.Current.Changed += _fmtFlagHandler;") &&
                  hook.Contains("FeatureFlags.Current.Changed -= _fmtFlagHandler;") &&
                  hook.Contains("Dispatcher.InvokeAsync(") &&
                  hook.Contains("ReportError(PageFormat.FeatureId, ex)"));

            string deck = Src("Features/PagesFormat/Deck.cs"), sys = Src("Features/PagesFormat/SystemPage.cs"),
                   home = Src("Features/PagesFormat/HomePage.cs"), dev = Src("Features/PagesFormat/DevicesPage.cs"),
                   tools = Src("Features/PagesFormat/ToolsPage.cs");
            // Culoarea copertei e singura care intră în pagină din afara temei, și vine din aceeași citire ca pe pagina
            // veche (HomePane.Dominant), deci nu e o culoare scrisă în cod.
            Check("PF16", "Nicio culoare scrisă în cod în forma nouă: totul prin jetoanele temei",
                  !System.Text.RegularExpressions.Regex.IsMatch(deck + sys + dev + tools, @"Color\.From|#[0-9A-Fa-f]{6}|new SolidColorBrush") &&
                  !System.Text.RegularExpressions.Regex.IsMatch(home, @"#[0-9A-Fa-f]{6}|new SolidColorBrush") &&
                  home.Contains("HomePane.Dominant(m.Art)") && deck.Contains("SetResourceReference"));

            Check("PF17", "Niciun cronometru nou pe paginile care doar citesc; notița își păstrează salvarea amânată",
                  new[] { deck, sys, home, dev }.All(f => !f.Contains("DispatcherTimer") && !f.Contains("CompositionTarget.Rendering")) &&
                  new[] { sys, home, dev, tools }.All(f => f.Contains("public override void Refresh()")) &&
                  tools.Contains("if (_noteSave.IsEnabled) { _noteSave.Stop(); W.S.Save(); }"));

            Check("PF19", "Niciun sistem dublat: versurile, culoarea copertei și scanarea dispozitivelor vin din serviciile care există",
                  home.Contains("LyricsService.GetAsync(") && home.Contains("LyricsService.IndexAt(") &&
                  dev.Contains("DeviceService.ScanAsync()") && dev.Contains("PrivacyService.Microphone()") &&
                  tools.Contains("Launcher.Search(") && tools.Contains("Launcher.Calculate(") &&
                  tools.Contains("MemoryTools.Status()"));

            Check("PF20", "Nimic nu pornește un proces altfel decât prin Shell.Open (regula de securitate)",
                  new[] { sys, home, dev, tools }.All(f => !f.Contains("Process.Start")) &&
                  dev.Contains("Shell.Open(\"ms-settings:sound\")"));

            Check("PF18", "Cele două fețe ale planșelor vin din temă, nu scrise în cod, și sunt înglobate în exe",
                  deck.Contains("\"PageMono\" : \"PageFont\"") &&
                  Src("Theme.xaml").Contains("x:Key=\"PageFont\"") && Src("Theme.xaml").Contains("x:Key=\"PageMono\"") &&
                  Src("WinNotch.csproj").Contains("<Resource Include=\"Fonts\\*.ttf\" />"));
        }
    }
}
