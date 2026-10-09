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
        /// P52: the WinNotch window, version 2. Only the pure layout rules are tested here (the window itself is WPF):
        /// how many card columns fit a width, when the right column and the sidebar step aside, which category a page or
        /// an action belongs to, and the non-regression — with the switch off the old window opens.
        /// </summary>
        static void WindowV2Tests()
        {
            V2Layout();
            V2CategoriesAndPins();
        }

        static void V2Layout()
        {
            Check("WV1", "Coloane de carduri: 4 peste 1280, 3 peste 900, 2 mai jos",
                  LayoutRules.Columns(1600) == 4 && LayoutRules.Columns(1280) == 4 && LayoutRules.Columns(1279) == 3 &&
                  LayoutRules.Columns(900) == 3 && LayoutRules.Columns(899) == 2 && LayoutRules.Columns(620) == 2 &&
                  LayoutRules.Columns(400) == 1);

            Check("WV2", "Coloana din dreapta se ascunde sub 1100 px",
                  LayoutRules.ShowRightColumn(1100) && LayoutRules.ShowRightColumn(1600) &&
                  !LayoutRules.ShowRightColumn(1099) && !LayoutRules.ShowRightColumn(900));

            Check("WV3", "Sub lățimea minimă totul se așază pe o coloană (bara laterală se strânge)",
                  LayoutRules.SingleColumn(899) && !LayoutRules.SingleColumn(900) &&
                  LayoutRules.MinWidth == 900 && LayoutRules.MinHeight == 600 &&
                  LayoutRules.SidebarWidth == 240 && LayoutRules.RightWidth == 300);

            Check("WV4", "Geometria: aceeași scară ca în brief (raza 18 pentru carduri, 12 pentru controale mici, spațieri de 4)",
                  LayoutRules.CardRadius == 18 && LayoutRules.ChipRadius == 12 &&
                  LayoutRules.Gap == 12 && LayoutRules.Pad == 24 && LayoutRules.HeaderHeight == 52);

            Check("WV5", "Sugestia din bara de jos spune ce să faci când Command Bar-ul e oprit",
                  LayoutRules.Hint(true).Length > 10 && LayoutRules.Hint(false).Contains("Setări") &&
                  LayoutRules.Hint(true) != LayoutRules.Hint(false));
        }

        static void V2CategoriesAndPins()
        {
            Check("WV6", "Bara laterală are doar grupuri care există azi, cu id-uri unice și titluri în română",
                  LayoutRules.Categories.Count >= 6 &&
                  LayoutRules.Categories.Select(c => c.Id).Distinct().Count() == LayoutRules.Categories.Count &&
                  LayoutRules.Categories.All(c => c.Id == c.Id.ToLowerInvariant() && !string.IsNullOrWhiteSpace(c.Title) && !string.IsNullOrEmpty(c.Glyph)) &&
                  LayoutRules.Categories.All(c => !c.Title.Contains("curând", StringComparison.OrdinalIgnoreCase)) &&
                  LayoutRules.Find("setari") != null && LayoutRules.Find("nu.exista") == null);

            Check("WV7", "Intrările vechi în fereastră duc unde trebuie: teme, setări, noutăți, o pagină, nimic",
                  LayoutRules.CategoryFor("themes") == "teme" && LayoutRules.CategoryFor("settings") == "setari" &&
                  LayoutRules.CategoryFor("news") == "noutati" && LayoutRules.CategoryFor("pagina-mea") == "pagini" &&
                  LayoutRules.CategoryFor(null) == LayoutRules.DefaultCategory && LayoutRules.CategoryFor("") == LayoutRules.DefaultCategory);

            Check("WV8", "Acțiunile din registru se împart pe categorii; una necunoscută merge la „Acțiuni”",
                  LayoutRules.CategoryForAction("Clipboard") == "clipboard" && LayoutRules.CategoryForAction("Captură") == "captura" &&
                  LayoutRules.CategoryForAction("Sunet") == "sunet" && LayoutRules.CategoryForAction("Sistem") == "sistem" && LayoutRules.CategoryForAction("Fereastră") == "actiuni" &&
                  LayoutRules.CategoryForAction("ceva nou") == "actiuni" && LayoutRules.CategoryForAction(null) == "actiuni" &&
                  LayoutRules.CategoryForAction("clipboard") == "clipboard" /* fără majuscule */);

            Check("WV9", "Paginile, temele, setările și noutățile sunt pagini întregi, nu carduri",
                  LayoutRules.IsPageContent("pagini") && LayoutRules.IsPageContent("teme") &&
                  LayoutRules.IsPageContent("setari") && LayoutRules.IsPageContent("noutati") &&
                  !LayoutRules.IsPageContent("actiuni") && !LayoutRules.IsPageContent("clipboard"));

            // Fuziunea cerută de autor: v2 nu mai arată un card „se deschide în fereastra clasică” pentru Setări.
            Check("WV24", "Setările sunt în fereastra nouă, nu un card care trimite la cea clasică",
                  LayoutRules.IsEmbeddedContent("setari") && !LayoutRules.IsClassicContent("setari") &&
                  !LayoutRules.IsEmbeddedContent("actiuni") && !LayoutRules.IsEmbeddedContent("clipboard") &&
                  LayoutRules.Categories.Where(c => LayoutRules.IsPageContent(c.Id))
                            .All(c => LayoutRules.IsEmbeddedContent(c.Id) != LayoutRules.IsClassicContent(c.Id)));

            string emb = Src("Features/WindowV2/EmbeddedPages.cs");
            Check("WV25", "Pagina de setări e cea existentă (TakeContent), legată la ScrollViewer-ul centrului, și se desprinde (Detach)",
                  emb.Contains("new SettingsWindow(s)") && emb.Contains("_settings.TakeContent()") &&
                  emb.Contains("new Binding(\"ViewportHeight\") { Source = host }") &&
                  emb.Contains("ScrollBarVisibility.Disabled") && emb.Contains("_settings?.Detach();") &&
                  !emb.Contains("CheckBox") && !emb.Contains("Slider"));

            string v2 = Src("Features/WindowV2/WindowV2.cs");
            Check("WV26", "Fereastra desprinde pagina la schimbarea categoriei și la închidere, și nu o reconstruiește la redimensionare",
                  Count(Norm(v2), "_pages.Detach();") == 2 &&
                  Norm(v2).Contains("!LayoutRules.IsPageContent(_category) && LayoutRules.Columns(CenterWidth()) != _cols"));

            Check("WV28", "Noutățile sunt în fereastra nouă, citite cu codul existent, cu jetoanele temei",
                  LayoutRules.IsEmbeddedContent("noutati") && !LayoutRules.IsClassicContent("noutati") &&
                  emb.Contains("Services.Updater.ParseNotes(Services.Updater.OwnNotes())") &&
                  !System.Text.RegularExpressions.Regex.IsMatch(emb, @"Color\.From|#[0-9A-Fa-f]{6}|Brushes\.(?!Transparent)"));

            // Pe tema întunecată, cardurile albe ale setărilor cu text alb erau ilizibile: pagina își duce cromul cu ea.
            string sw = Src("SettingsWindow.xaml.cs");
            Check("WV27", "Conținutul setărilor își duce fundalul și cerneala proprii oriunde e găzduit",
                  sw.Contains("content.SetValue(TextBlock.ForegroundProperty") && sw.Contains("sheet.Background = new SolidColorBrush"));

            Check("WV10", "Comutatorul din catalog are id-ul regulilor, e Experimental și oprit implicit",
                  LayoutRules.FeatureId == FeatureCatalog.WindowV2 &&
                  FeatureCatalog.Find(LayoutRules.FeatureId) is FeatureInfo fi &&
                  fi.Stage == FeatureStage.Experimental && !fi.DefaultOn);

            string app = Src("App.xaml.cs"), win = Src("Features/WindowV2/WindowV2.cs");
            Check("WV11", "Non-regresie: cu comutatorul oprit se deschide fereastra veche, iar o eroare în v2 cade pe ea",
                  app.Contains("if (OpenWindowV2(pageId)) return;") &&
                  app.Contains("if (!(Core.Flags.FeatureFlags.Current?.IsEnabled(Features.WindowV2.LayoutRules.FeatureId) ?? false)) return false;") &&
                  app.Contains("ReportError(Features.WindowV2.LayoutRules.FeatureId, ex)"));

            Check("WV12", "Fereastra ia tema aplicației (pensule prin DynamicResource), nicio culoare scrisă în cod, iconițe din fontul existent",
                  !System.Text.RegularExpressions.Regex.IsMatch(win, @"Color\.From|#[0-9A-Fa-f]{6}|new SolidColorBrush|Brushes\.(?!Transparent)") &&
                  win.Contains("SetResourceReference") && win.Contains("Ui.Icon(") && !win.Contains(".png"));

            Check("WV13", "Antetul refolosește geometria notch-ului ancorat (P50), nu una nouă, și e înghețat",
                  win.Contains("AnchoredGeometry.Radius(") && win.Contains("AnchoredGeometry.Ear(") && win.Contains("g.Freeze();"));

            Check("WV14", "Acțiunile pornesc doar prin registru, cu confirmare pentru ce nu e sigur; nimic nou nu se inventează",
                  win.Contains("ActionRegistry.Current?.InvokeAsync(") && win.Contains("ActionInvoker.UI, default, confirmed") &&
                  win.Contains("a.Safety == ActionSafety.Safe") && !win.Contains("Process.Start") && !win.Contains("Shell.Open"));

            Check("WV16", "Fereastra respectă protocolul comutatorului (abonare, dezabonare, se închide când comutatorul se oprește) și raportează erorile funcției",
                  win.Contains("FeatureFlags.Current.Changed += _flagHandler;") &&
                  win.Contains("FeatureFlags.Current.Changed -= _flagHandler;") &&
                  System.Text.RegularExpressions.Regex.Matches(win, @"ReportError\(LayoutRules\.FeatureId, ex\)").Count >= 4);

            Check("WV17", "Cardul de căutare se construiește o dată (un element WPF are un singur părinte) și cardurile se refac doar când se schimbă numărul de coloane",
                  win.Contains("if (_searchCard == null)") && win.Contains("_cards.Children.Clear();") &&
                  win.Contains("LayoutRules.Columns(CenterWidth()) != _cols"));

            Check("WV18", "Tastatura: rândurile din bara laterală sunt butoane (Tab, Enter, Space), au nume pentru accesibilitate, iar Esc închide fereastra",
                  win.Contains("new Button") && win.Contains("AutomationProperties.SetName(host, c.Title)") &&
                  win.Contains("if (e.Key == Key.Escape) { Close(); e.Handled = true; }"));

            Check("WV19", "Rezultatul unei acțiuni se arată (nu se înghite), iar ceasul nu bate când fereastra e minimizată",
                  win.Contains("_hint.Text = msg") && win.Contains("if (!IsVisible || WindowState == WindowState.Minimized) return;") &&
                  win.Contains("StateChanged +="));

            // Reparație (0.6.20 testat de autor): filele arătau „Ac”, „Si”, iar bara laterală doar iconițe și „…”.
            // IconButton impune Width=30 / Height=30 și Focusable=False, deci orice buton cu text era tăiat la 30 px.
            string theme = Src("Theme.xaml");
            Check("WV20", "Butoanele cu text (file, bara laterală) nu folosesc stilul de iconiță, care are 30×30 fix",
                  !win.Contains("Ui.S(\"IconButton\")") && Count(Norm(win), "Ui.S(\"NavButton\")") == 2,
                  Count(Norm(win), "Ui.S(\"NavButton\")") + " butoane-rând");

            int nbStart = theme.IndexOf("x:Key=\"NavButton\"", StringComparison.Ordinal);
            int nbEnd = nbStart < 0 ? -1 : theme.IndexOf("</Style>", nbStart, StringComparison.Ordinal);
            string navButton = nbStart < 0 || nbEnd < 0 ? "" : Norm(theme.Substring(nbStart, nbEnd - nbStart));
            Check("WV21", "Stilul „NavButton” nu fixează mărimea, e focusabil și arată focusul cu contur în AccentBrush",
                  navButton.Length > 0 &&
                  !navButton.Contains("Property=\"Width\"", StringComparison.Ordinal) &&
                  !navButton.Contains("Property=\"Height\"", StringComparison.Ordinal) &&
                  navButton.Contains("<Setter Property=\"Focusable\" Value=\"True\"/>", StringComparison.Ordinal) &&
                  navButton.Contains("<Trigger Property=\"IsKeyboardFocused\" Value=\"True\">", StringComparison.Ordinal) &&
                  navButton.Contains("{DynamicResource AccentBrush}", StringComparison.Ordinal),
                  navButton.Length == 0 ? "stilul lipsește din Theme.xaml" : "");

            Check("WV22", "Coloana din dreapta are spațiu la margini și cei 300 px includ marginile, ca lățimea centrului să rămână corectă",
                  win.Contains("Width = LayoutRules.RightWidth,") &&
                  win.Contains("Padding = new Thickness(LayoutRules.Gap, LayoutRules.Pad, LayoutRules.Pad, LayoutRules.Pad)") &&
                  !win.Contains("new StackPanel { Width = LayoutRules.RightWidth }"));

            // App-wide, learned the hard way: a repeated x:Key makes WPF throw while loading the resources, so the app
            // dies before its first window. Nicio probă unitară nu prindea asta — doar testul de fum, la pornire.
            var keys = Regex.Matches(theme, "x:Key=\"([^\"]+)\"").Select(m => m.Groups[1].Value).ToList();
            var dup = keys.GroupBy(k => k, StringComparer.Ordinal).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
            Check("WV23", "Theme.xaml nu are două resurse cu aceeași cheie (WPF aruncă la pornire, înainte de orice fereastră)",
                  dup.Count == 0, string.Join(", ", dup));

            Check("WV15", "Nimic „în curând”: cardurile vin din acțiunile înregistrate, nu dintr-o listă scrisă de mână",
                  win.Contains("reg.All.Where(") && !win.Contains("în curând") && !win.Contains("Focus Mode") &&
                  !win.Contains("Window Wizard") && !win.Contains("Dev Tools"));
        }
    }
}
