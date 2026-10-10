using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using WinNotch.Core.Actions;
using WinNotch.Core.Context;
using WinNotch.Core.Flags;
using WinNotch.Features.ContextPages;
using WinNotch.Widgets;

namespace WinNotch.Features.WindowV2
{
    /// <summary>
    /// P52, the Setări tab: every option the app has, drawn with the theme's own tokens. It does <b>not</b> host the
    /// classic settings window's content any more — that is a fixed light sheet and looked grafted on wherever the
    /// theme was not light. The options themselves are the same ones, read from and written to the same
    /// <see cref="AppSettings"/> fields and the same services; only the controls are this window's.
    /// <para>There is no "Salvează" button: a change applies the moment you make it, like everywhere else in this
    /// window. What you type is written shortly after you stop typing.</para>
    /// </summary>
    internal sealed class SettingsView : Grid
    {
        private readonly AppSettings _s;
        private readonly NotchWindow _notch;
        private readonly Func<Window> _owner;
        private readonly Action<string> _say;
        private readonly Action<Action> _soon;
        private readonly EmbeddedPages _pages = new EmbeddedPages();

        private readonly StackPanel _left = new StackPanel();
        private readonly StackPanel _body = new StackPanel();
        private readonly Border _leftHost;
        private readonly ScrollViewer _bodyScroll;
        private readonly DispatcherTimer _extTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        private TextBlock _extStatus;
        private string _section = LayoutRules.Sections[0].Id;
        private double _width = LayoutRules.MinWidth;
        private int _cols = -1;

        internal SettingsView(AppSettings s, NotchWindow notch, Func<Window> owner, Action<Action> soon, Action<string> say)
        {
            _s = s; _notch = notch; _owner = owner; _soon = soon; _say = say;
            ColumnDefinitions.Add(new ColumnDefinition { Width = Ui.Px(LayoutRules.LeftWidth) });
            ColumnDefinitions.Add(new ColumnDefinition { Width = Ui.Star() });

            _leftHost = new Border
            {
                Padding = new Thickness(LayoutRules.Gap, LayoutRules.Gap, LayoutRules.Gap, LayoutRules.Pad),
                Child = new ScrollViewer { Style = Ui.S("SlimScroll"), Content = _left },
            };
            _leftHost.SetResourceReference(Border.BackgroundProperty, "ChipBrush");
            this.Put(_leftHost);

            _bodyScroll = new ScrollViewer
            {
                Style = Ui.S("SlimScroll"), Content = _body,
                Padding = new Thickness(LayoutRules.Pad, LayoutRules.Pad, LayoutRules.Pad, LayoutRules.Pad),
            };
            this.Put(_bodyScroll, 1);
            _extTimer.Tick += (o, e) => ShowExtStatus();
        }

        /// <summary>Stops what this tab keeps running (the browser-extension poll) and lets go of the news page.</summary>
        internal void Detach()
        {
            _extTimer.Stop();
            _pages.Detach();
        }

        /// <summary>P14 ("settings.*" actions): opens the section an option lives in and says which one it is.</summary>
        internal void Reveal(string target)
        {
            string section = SettingsMap.SectionFor(target);
            if (section == null) return;
            Open(section, _width);
            string name = SettingsMap.NameFor(target);
            if (name != null) _say("Setarea „" + name + "” e în această secțiune.");
        }

        internal void Open(string sectionId, double width)
        {
            _section = LayoutRules.FindSection(sectionId) != null ? sectionId : LayoutRules.Sections[0].Id;
            _width = width;
            BuildLeft();
            BuildBody();
        }

        internal void Relayout(double width)
        {
            _width = width;
            bool left = LayoutRules.ShowLeftPanel(LayoutRules.Settings, width);
            ColumnDefinitions[0].Width = left ? Ui.Px(LayoutRules.LeftWidth) : new GridLength(0);
            _leftHost.Visibility = left ? Visibility.Visible : Visibility.Collapsed;
            if (_section == "actiuni" && LayoutRules.Columns(width) != _cols) BuildBody();
        }

        // ------------------------------------------------------------------ left: this tab's sections

        private void BuildLeft()
        {
            _left.Children.Clear();
            _left.Children.Add(V2Controls.Eyebrow("SETĂRI"));
            foreach (var sec in LayoutRules.Sections)
            {
                var s = sec;
                bool on = string.Equals(sec.Id, _section, StringComparison.Ordinal);
                var mark = new Border { Width = 3, CornerRadius = new CornerRadius(2), Margin = new Thickness(0, 2, 6, 2) };
                if (on) mark.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
                var row = Ui.Cols(Ui.Px(3), Ui.Auto, Ui.Star());
                row.Put(mark);
                row.Put(Ui.Icon(sec.Glyph, 13, Ui.B(on ? "InkBrush" : "MutedBrush")), 1);
                var label = Ui.T(sec.Title, 13, on ? "InkBrush" : "MutedBrush", on);
                label.Margin = new Thickness(8, 0, 0, 0);
                label.VerticalAlignment = VerticalAlignment.Center;
                row.Put(label, 2);
                var host = new Button
                {
                    Style = Ui.S("NavButton"), Content = row, Padding = new Thickness(4, 7, 8, 7),
                    Cursor = Cursors.Hand, Margin = new Thickness(0, 1, 0, 1),
                    HorizontalContentAlignment = HorizontalAlignment.Stretch,
                };
                System.Windows.Automation.AutomationProperties.SetName(host, sec.Title);
                if (on) host.SetResourceReference(Control.BackgroundProperty, "ChipHoverBrush");
                host.Click += (o, e) => { if (!on) { _section = s.Id; BuildLeft(); BuildBody(); } };
                _left.Children.Add(host);
            }
        }

        // ------------------------------------------------------------------ the page

        /// <summary>Writes the settings and tells the notch; every control calls this the moment it changes.</summary>
        private void Save()
        {
            _s.Save();
            _notch?.ApplySettings();
        }

        private void BuildBody()
        {
            _pages.Detach();
            _extTimer.Stop();
            _extStatus = null;
            _body.Children.Clear();
            var sec = LayoutRules.FindSection(_section);
            _body.Children.Add(V2Controls.Title(sec?.Title ?? "Setări"));
            var intro = Ui.T(SectionHint(_section), 13, "MutedBrush");
            intro.TextWrapping = TextWrapping.Wrap;
            intro.MaxWidth = 760;
            intro.HorizontalAlignment = HorizontalAlignment.Left;
            intro.Margin = new Thickness(0, 4, 0, LayoutRules.Pad);
            _body.Children.Add(intro);

            var box = new StackPanel { MaxWidth = 820, HorizontalAlignment = HorizontalAlignment.Left };
            switch (_section)
            {
                case "notch": BuildNotch(box); break;
                case "standby": BuildStandby(box); break;
                case "acasa": BuildHome(box); break;
                case "browser": BuildBrowser(box); break;
                case "sistem": BuildSystem(box); break;
                case "spatii": BuildWorkspaces(box); break;
                case "context": BuildContextPages(box); break;
                case "functii": BuildFeatures(box); break;
                case "noutati": box.Children.Add(_pages.News()); break;
                default: BuildActions(box); break;
            }
            _body.Children.Add(box);
        }

        private static string SectionHint(string id) => id switch
        {
            "notch" => "Cum se poartă notch-ul: unde stă, cât așteaptă, cât de mare e.",
            "standby" => "Ce scrie pe pastilă când nu faci nimic cu ea.",
            "acasa" => "Pagina Acasă și lucrurile care au grijă de tine.",
            "browser" => "Fiecare tab care redă sunet, separat — cu extensia WinNotch.",
            "sistem" => "Pornirea cu Windows, temperaturile și actualizările.",
            "spatii" => "Aranjările de ferestre salvate din notch (Unelte).",
            "context" => "Pe ce pagină se deschide notch-ul, după ce faci în acel moment.",
            "functii" => "Funcții încă în lucru. Se pornesc și se opresc pe loc, fără repornire.",
            "noutati" => "Ce a adus versiunea pe care o folosești.",
            _ => "Tot ce poate face aplicația acum. Butonul pornește acțiunea pe loc.",
        };

        // ---- Notch ----

        private void BuildNotch(StackPanel box)
        {
            box.Children.Add(V2Controls.Card("Poziție și mărime", null, V2Controls.Stack(
                V2Controls.Choice("Poziție", new[] { ("left", "Stânga"), ("center", "Centru"), ("right", "Dreapta") },
                                  _s.Position, v => { _s.Position = v; Save(); }),
                V2Controls.Choice("Mărimea notch-ului deschis",
                                  new[] { ("0", "Automat (după monitor)"), ("1", "100%"), ("1.1", "110%"), ("1.2", "120%"), ("1.3", "130%"), ("1.45", "145%"), ("1.6", "160%") },
                                  _s.UiScale.ToString(CultureInfo.InvariantCulture),
                                  v => { _s.UiScale = double.Parse(v, CultureInfo.InvariantCulture); Save(); }),
                V2Controls.SliderRow("Se deschide la hover după", 0, 1000, 50, _s.DwellMs, v => PreviewModel.Value(v, "ms"),
                                     v => { _s.DwellMs = (int)Math.Round(v); _soon(Save); },
                                     "0 = se deschide imediat ce treci cu mouse-ul peste pastilă."),
                V2Controls.Choice("Se micșorează singur după",
                                  new[] { ("0", "Niciodată"), ("5", "5 secunde"), ("10", "10 secunde"), ("30", "30 de secunde"), ("60", "1 minut") },
                                  _s.MiniAfterSec.ToString(CultureInfo.InvariantCulture),
                                  v => { _s.MiniAfterSec = int.Parse(v, CultureInfo.InvariantCulture); Save(); }))));

            box.Children.Add(V2Controls.Card("Peste alte ferestre", null, V2Controls.Stack(
                V2Controls.Toggle("Mic peste ferestre maximizate", _s.SlimOverMaximized, v => { _s.SlimOverMaximized = v; Save(); },
                                  "Peste tab-urile sau bara de titlu a unei ferestre maximizate, pastila trece la forma mică și se dă la o parte."),
                V2Controls.Choice("Peste jocuri / fullscreen",
                                  new[] { ("alerts", "Se ascunde, apare doar pentru alerte"), ("visible", "Rămâne mereu vizibil") },
                                  _s.Fullscreen, v => { _s.Fullscreen = v; Save(); }))));

            box.Children.Add(V2Controls.Card("Scurtături", null, V2Controls.Stack(
                V2Controls.Choice("Scurtătura Command Bar", new[] { ("space", "Win + Alt + Space"), ("k", "Win + Alt + K") },
                                  _s.CommandBarKey, v => { _s.CommandBarKey = v; Save(); },
                                  NotchWindow.Current?.CommandBarHotkeyProblem()
                                  ?? "Pentru Command Bar (îl pornești din „Funcții noi”). Dacă scurtătura e luată de altă aplicație, alege-o pe cealaltă."))));

            box.Children.Add(V2Controls.Card("Culoare accent", "Se vede pe barele, butoanele și marcajele notch-ului.", Accents()));
        }

        private static readonly (string Hex, string Name)[] AccentChoices =
        {
            ("", "Culoarea temei"), ("#F5A524", "Chihlimbar"), ("#3DDC84", "Verde"), ("#5AA9FF", "Albastru"),
            ("#FF6B8A", "Roz"), ("#A78BFA", "Mov"), ("#E8E8E8", "Alb"),
        };

        private UIElement Accents()
        {
            var wrap = new WrapPanel();
            void Build()
            {
                wrap.Children.Clear();
                foreach (var (hex, name) in AccentChoices)
                {
                    var h = hex;
                    bool on = string.Equals(hex, _s.Accent ?? "", StringComparison.OrdinalIgnoreCase);
                    var dot = new Border { Width = 26, Height = 26, CornerRadius = new CornerRadius(13) };
                    if (h.Length == 0) dot.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
                    else dot.Background = new System.Windows.Media.SolidColorBrush(ThemeManager.Parse(h, "#5AA9FF"));
                    var b = new Button
                    {
                        Style = Ui.S("NavButton"), Content = dot, Padding = new Thickness(4), Margin = new Thickness(0, 0, 6, 6),
                        Cursor = Cursors.Hand, BorderThickness = new Thickness(2), ToolTip = name,
                    };
                    if (on) b.SetResourceReference(Control.BorderBrushProperty, "InkBrush");
                    else b.BorderBrush = System.Windows.Media.Brushes.Transparent;
                    System.Windows.Automation.AutomationProperties.SetName(b, name);
                    b.Click += (o, e) => { _s.Accent = h; Save(); Build(); };
                    wrap.Children.Add(b);
                }
            }
            Build();
            return wrap;
        }

        // ---- Standby ----

        private void BuildStandby(StackPanel box)
        {
            var list = new StackPanel();
            void Build()
            {
                list.Children.Clear();
                var all = AppSettings.Widgets.Select(w => w.Id).ToList();
                var chosen = _s.Standby.Where(all.Contains).ToList();
                var order = chosen.Concat(all.Where(id => !chosen.Contains(id))).ToList();
                int pos = 0;
                foreach (var id in order)
                {
                    var wid = id;
                    string name = AppSettings.Widgets.First(w => w.Id == id).Name;
                    bool on = chosen.Contains(id);
                    int index = chosen.IndexOf(id);
                    var number = Ui.T(on ? (++pos).ToString(CultureInfo.InvariantCulture) : "", 12, "DimBrush", false, true);
                    number.Width = 18;
                    number.VerticalAlignment = VerticalAlignment.Center;

                    var toggle = new CheckBox { IsChecked = on, VerticalAlignment = VerticalAlignment.Center, Content = Ui.T(name, 13, on ? "InkBrush" : "MutedBrush") };
                    toggle.Click += (o, e) =>
                    {
                        var now = _s.Standby.Where(all.Contains).ToList();
                        if (toggle.IsChecked == true)
                        {
                            if (now.Count >= AppSettings.MaxStandby) { _say("Cel mult " + AppSettings.MaxStandby + " elemente în standby."); Build(); return; }
                            now.Add(wid);
                        }
                        else now.Remove(wid);
                        _s.Standby = now;
                        Save();
                        Build();
                    };

                    StackPanel arrows = null;
                    if (on)
                    {
                        arrows = Ui.H(0);
                        int i = index;
                        arrows.Children.Add(V2Controls.Mini("", () => Move(i, -1, Build), "Mai sus"));
                        arrows.Children.Add(V2Controls.Mini("", () => Move(i, 1, Build), "Mai jos"));
                    }
                    list.Children.Add(V2Controls.ListRow(number, toggle, arrows));
                }
            }
            Build();
            box.Children.Add(V2Controls.Card("Ce apare în standby",
                "Bifează până la " + AppSettings.MaxStandby + ". Ordinea de sus în jos e ordinea de pe notch: prima jumătate stă în stânga camerei, restul în dreapta. " +
                "Timer-ul Pomodoro se adaugă singur cât rulează.", list));

            box.Children.Add(V2Controls.Card("Pastila mică", null, V2Controls.Stack(
                V2Controls.Toggle("Linia de progres a piesei", _s.MiniProgress, v => { _s.MiniProgress = v; Save(); },
                                  "Dunga subțire de jos, care urmărește cât a trecut din piesă. Oprită, pastila mică rămâne doar cu ora și data."))));
        }

        private void Move(int index, int delta, Action rebuild)
        {
            var all = AppSettings.Widgets.Select(w => w.Id).ToList();
            var chosen = _s.Standby.Where(all.Contains).ToList();
            int j = index + delta;
            if (index < 0 || index >= chosen.Count || j < 0 || j >= chosen.Count) return;
            (chosen[index], chosen[j]) = (chosen[j], chosen[index]);
            _s.Standby = chosen;
            Save();
            rebuild();
        }

        // ---- Acasă și sănătate ----

        private void BuildHome(StackPanel box)
        {
            var ram = Ui.Cols(Ui.Auto, Ui.Px(8), Ui.Auto);
            ram.Put(V2Controls.ChoiceBox(new[] { ("75", "75%"), ("80", "80%"), ("85", "85%"), ("90", "90%") },
                                         _s.RamAlertPercent.ToString(CultureInfo.InvariantCulture),
                                         v => { _s.RamAlertPercent = int.Parse(v, CultureInfo.InvariantCulture); Save(); }));
            box.Children.Add(V2Controls.Card("Acasă și sănătate", null, V2Controls.Stack(
                V2Controls.Toggle("Versurile piesei", _s.Lyrics, v => { _s.Lyrics = v; Save(); }, "De la LRCLIB, gratuit."),
                V2Controls.Toggle("Pauză pentru ochi", _s.EyeBreak, v => { _s.EyeBreak = v; Save(); },
                                  "La fiecare 20 de minute la PC, 20 de secunde de privit în depărtare."),
                V2Controls.Toggle("Alertă când memoria RAM se umple", _s.RamAlert, v => { _s.RamAlert = v; Save(); },
                                  "Îți arată cine consumă cel mai mult și îți propune eliberarea memoriei. Cel mult o dată la 15 minute, niciodată peste un joc sau un film pe tot ecranul."),
                V2Controls.Row("Pragul alertei", ram))));

            box.Children.Add(V2Controls.Card("Calendar",
                "Lipește link-ul secret iCal al calendarului tău. Google Calendar: Setări → calendarul tău → „Adresa secretă în format iCal”. " +
                "Outlook: Setări → Calendare partajate → Publică un calendar → link ICS.",
                V2Controls.TextRow("Link iCal (.ics)", _s.CalendarIcs ?? "", v => { _s.CalendarIcs = (v ?? "").Trim(); Save(); }, _soon)));

            var coords = Ui.Cols(Ui.Star(), Ui.Px(8), Ui.Px(110), Ui.Px(8), Ui.Px(110));
            var city = V2Controls.Field(_s.City);
            city.TextChanged += (o, e) => { var v = city.Text; _soon(() => { _s.City = string.IsNullOrWhiteSpace(v) ? "Orașul meu" : v.Trim(); Save(); }); };
            var lat = V2Controls.Field(_s.Lat.ToString(CultureInfo.InvariantCulture));
            lat.TextChanged += (o, e) => { var v = lat.Text; _soon(() => { if (double.TryParse(v.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double d)) { _s.Lat = d; Save(); } }); };
            var lon = V2Controls.Field(_s.Lon.ToString(CultureInfo.InvariantCulture));
            lon.TextChanged += (o, e) => { var v = lon.Text; _soon(() => { if (double.TryParse(v.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double d)) { _s.Lon = d; Save(); } }); };
            coords.Put(city); coords.Put(lat, 2); coords.Put(lon, 4);
            var heads = Ui.Cols(Ui.Star(), Ui.Px(8), Ui.Px(110), Ui.Px(8), Ui.Px(110));
            heads.Put(Ui.T("Oraș", 11.5, "DimBrush"));
            heads.Put(Ui.T("Latitudine", 11.5, "DimBrush"), 2);
            heads.Put(Ui.T("Longitudine", 11.5, "DimBrush"), 4);
            heads.Margin = new Thickness(0, 0, 0, 3);
            box.Children.Add(V2Controls.Card("Vremea", "Orașul și coordonatele lui (le găsești pe Google Maps: click dreapta pe hartă).",
                                             V2Controls.Stack(heads, coords)));
        }

        // ---- Browser ----

        private void BuildBrowser(StackPanel box)
        {
            _extStatus = Ui.T("", 12.5, "MutedBrush", true);
            _extStatus.TextWrapping = TextWrapping.Wrap;
            _extStatus.Margin = new Thickness(0, 0, 0, LayoutRules.Gap);
            ShowExtStatus();
            _extTimer.Start();

            var buttons = new WrapPanel();
            buttons.Children.Add(Pill("Deschide extensiile (Chrome)", () => OpenExtensions("chrome.exe", "chrome://extensions/")));
            buttons.Children.Add(Pill("Deschide extensiile (Edge)", () => OpenExtensions("msedge.exe", "edge://extensions/")));
            buttons.Children.Add(Pill("Arată folderul extensiei", () =>
            {
                CopyExtensionPath();
                Services.Shell.Open(Services.BrowserBridge.ExtensionFolder);
            }));

            box.Children.Add(V2Controls.Card("Tab-uri din browser",
                "Windows vede tot browserul ca o singură aplicație. Cu extensia WinNotch, fiecare tab care redă sunet (YouTube, Reels, YouTube Music…) " +
                "apare separat și îl poți opri, pune pe pauză sau da mai încet. Merge în Chrome, Edge, Brave, Opera și Vivaldi. Nimic nu iese din calculator.",
                V2Controls.Stack(
                    V2Controls.Toggle("Arată fiecare tab separat", _s.BrowserTabs, v => { _s.BrowserTabs = v; Save(); }),
                    _extStatus,
                    V2Controls.Hint("Instalare, o singură dată pentru fiecare browser:\n" +
                                    "1. Apasă „Deschide extensiile” (sau scrie chrome://extensions în bara de adrese).\n" +
                                    "2. Pornește „Modul dezvoltator” (Developer mode), dreapta sus.\n" +
                                    "3. Apasă „Încarcă extensia neîmpachetată” (Load unpacked) și alege folderul extensiei. Calea e deja copiată: o lipești cu Ctrl+V."),
                    buttons)));
        }

        private void ShowExtStatus()
        {
            if (_extStatus == null) return;
            var list = NotchWindow.Current?.Bridge?.ConnectedBrowsers() ?? new List<string>();
            if (list.Count == 0)
            {
                _extStatus.Text = "Extensia nu e conectată în niciun browser.";
                _extStatus.SetResourceReference(TextBlock.ForegroundProperty, "WarnBrush");
            }
            else
            {
                _extStatus.Text = "✓ Conectată: " + string.Join(", ", list.Select(Services.AudioSessionsService.Friendly));
                _extStatus.SetResourceReference(TextBlock.ForegroundProperty, "OkBrush");
            }
        }

        private static void CopyExtensionPath()
        {
            Services.BrowserBridge.WriteExtension();
            try { Clipboard.SetText(Services.BrowserBridge.ExtensionFolder); } catch { }
        }

        private void OpenExtensions(string exe, string address)
        {
            CopyExtensionPath();
            // as administrator a browser would start elevated too: copy the address instead
            if (App.IsAdmin) { try { Clipboard.SetText(address); } catch { } _say("Am copiat „" + address + "”: lipește-l în bara de adrese a browserului."); return; }
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe, address) { UseShellExecute = true }); }
            catch (Exception ex) { _say("Nu am putut porni " + exe + ": " + ex.Message); }
        }

        // ---- Sistem ----

        private void BuildSystem(StackPanel box)
        {
            box.Children.Add(V2Controls.Card("Pornire", null, V2Controls.Stack(
                V2Controls.Toggle("Pornește odată cu Windows", AppSettings.StartWithWindows,
                                  v => { try { AppSettings.StartWithWindows = v; } catch (Exception ex) { _say("Nu am putut schimba pornirea cu Windows: " + ex.Message); } }))));

            var temps = V2Controls.Stack(
                V2Controls.Toggle("Citește temperaturile PC-ului", _s.Temperatures, v => { _s.Temperatures = v; Save(); },
                                  "Pentru temperatura procesorului e nevoie și de driverul gratuit PawnIO (pawnio.eu)."));
            var helperHint = V2Controls.Hint("");
            var helperBtn = Pill("", null);
            temps.Children.Add(helperHint);
            temps.Children.Add(helperBtn);
            RefreshTempHelper(helperBtn, helperHint);
            box.Children.Add(V2Controls.Card("Temperaturi", null, temps));

            var status = Ui.T("Versiunea ta: " + Services.Updater.Current +
                              (Services.Updater.Configured ? "" : " · actualizările automate nu sunt încă configurate"), 12.5, "MutedBrush");
            status.TextWrapping = TextWrapping.Wrap;
            var check = Pill("Caută acum", null);
            check.Click += async (o, e) =>
            {
                check.IsEnabled = false;
                status.Text = "Caut…";
                try { status.Text = NotchWindow.Current != null ? await NotchWindow.Current.CheckUpdateNow() : ""; }
                catch (Exception ex) { status.Text = "Nu am putut căuta: " + ex.Message; }
                finally { check.IsEnabled = true; }
            };
            box.Children.Add(V2Controls.Card("Actualizări", null, V2Controls.Stack(
                V2Controls.Toggle("Caută singur versiuni noi", _s.AutoUpdate, v => { _s.AutoUpdate = v; Save(); },
                                  "Te întreabă în notch înainte să instaleze."),
                V2Controls.Toggle("Canal beta (versiuni de test)", _s.BetaChannel, v => { _s.BetaChannel = v; Save(); },
                                  "Primești și versiunile de test, înaintea celor finale. Pot avea probleme; dacă una se închide des, WinNotch revine singur la versiunea anterioară."),
                status, check)));
        }

        /// <summary>The temperature service: whether it must be installed, replaced or updated (asked in the background).</summary>
        private void RefreshTempHelper(Button btn, TextBlock hint)
        {
            void Show(bool oldTask)
            {
                bool installed = Services.TempHelper.Installed, outdated = Services.TempHelper.Outdated;
                bool needed = !installed || outdated || oldTask;
                btn.Visibility = needed ? Visibility.Visible : Visibility.Collapsed;
                hint.Visibility = needed ? Visibility.Visible : Visibility.Collapsed;
                if (!needed) return;
                btn.Content = oldTask ? "Înlocuiește cu serviciul de temperatură"
                            : installed ? "Actualizează serviciul de temperatură"
                            : "Activează temperatura procesorului";
                hint.Text = oldTask
                    ? "Pornirea veche „ca administrator” (până la 0.6.4) e încă activă. Înlocuiește-o cu serviciul de temperatură: WinNotch va rula cu drepturi normale, iar temperatura procesorului se citește în continuare."
                    : installed
                    ? "Serviciul de temperatură e de la o versiune mai veche a WinNotch."
                    : "Temperatura procesorului se citește cu drepturi de administrator. WinNotch nu mai rulează ca administrator: un mic serviciu fără fereastră (contul SYSTEM, din Program Files) citește doar temperaturile și i le dă notch-ului. Se activează o singură dată, cu o confirmare Windows.";
            }
            Show(false);
            // the scheduled-task query takes a moment: asked off the UI thread, then the row is refreshed
            System.Threading.Tasks.Task.Run(() => Services.TempHelper.OldTaskExists).ContinueWith(t =>
            {
                if (t.Status == System.Threading.Tasks.TaskStatus.RanToCompletion) Show(t.Result);
            }, System.Threading.Tasks.TaskScheduler.FromCurrentSynchronizationContext());
            btn.Click += (o, e) =>
            {
                btn.IsEnabled = false;            // until the install (with its Windows prompt) is over
                ((App)Application.Current).InstallTempHelper(done: () => { btn.IsEnabled = true; RefreshTempHelper(btn, hint); });
            };
        }

        // ---- Spații de lucru ----

        private void BuildWorkspaces(StackPanel box)
        {
            var list = new StackPanel();
            void Build()
            {
                list.Children.Clear();
                var all = _s.Workspaces ?? new List<Services.Workspace>();
                if (all.Count == 0)
                {
                    list.Children.Add(Ui.T("Niciun spațiu salvat încă.", 12.5, "MutedBrush"));
                    return;
                }
                foreach (var w in all.ToList())
                {
                    var ws = w;
                    var name = V2Controls.Field(ws.Name);
                    name.TextChanged += (o, e) => { var v = name.Text; _soon(() => { if (!string.IsNullOrWhiteSpace(v)) { ws.Name = v.Trim(); Save(); } }); };
                    var count = Ui.T(ws.Windows.Count + (ws.Windows.Count == 1 ? " aplicație" : " aplicații"), 12, "DimBrush");
                    count.VerticalAlignment = VerticalAlignment.Center;
                    count.Margin = new Thickness(10, 0, 10, 0);
                    var right = Ui.H(0, count, V2Controls.Mini("", () => { _s.Workspaces.Remove(ws); Save(); Build(); }, "Șterge spațiul"));
                    right.VerticalAlignment = VerticalAlignment.Center;
                    list.Children.Add(V2Controls.ListRow(null, name, right));
                }
            }
            Build();
            box.Children.Add(V2Controls.Card("Spații de lucru",
                "Le creezi din notch: Unelte → „+ salvează aranjarea de acum”. Aici le redenumești sau le ștergi.", list));
        }

        // ---- Pagina după context ----

        private void BuildContextPages(StackPanel box)
        {
            var pages = Catalog.Standard.Select(p => (p.Id, p.Name, Hidden: _s.HiddenPages.Contains(p.Id)))
                               .Concat(_s.Pages.Select(p => (p.Id, p.Name, Hidden: p.Hidden))).ToList();
            var options = new List<(string, string)> { ("", "—") };
            options.AddRange(pages.Select(p => (p.Id, p.Hidden ? p.Name + " (ascunsă)" : p.Name)));
            var map = _s.ContextPages ?? AppSettings.NewContextPages();

            var rows = new StackPanel();
            foreach (var cat in ContextPageRules.Categories)
            {
                var c = cat;
                map.TryGetValue(ContextPageRules.Key(cat), out var chosen);
                rows.Children.Add(V2Controls.Choice(Features.Context.ContextActions.CategoryName(cat), options, chosen ?? "",
                                                    v => { _s.ContextPages = _s.WithContextPage(c, v); Save(); }));
            }
            bool on = FeatureFlags.Current?.IsEnabled(ContextPageRules.FeatureId) ?? false;
            box.Children.Add(V2Controls.Card("Pagina după context",
                "„—” = nicio schimbare (rămâne pagina de data trecută). O pagină aleasă de tine în notch e păstrată 10 minute; o pagină ascunsă sau ștearsă e sărită." +
                (on ? "" : " Funcția „Pagina după context” e oprită acum: pornește-o din „Funcții noi”."), rows));
        }

        // ---- Funcții noi ----

        private void BuildFeatures(StackPanel box)
        {
            var flags = FeatureFlags.Current;
            if (flags == null) { box.Children.Add(Ui.T("Funcțiile nu sunt pornite în acest mod.", 12.5, "MutedBrush")); return; }
            if (flags.SafeMode)
                box.Children.Add(V2Controls.Card("Mod sigur",
                    "WinNotch a pornit cu --safe-mode, așa că funcțiile Experimental și Beta sunt oprite până la următoarea pornire normală. Alegerile de aici se păstrează.",
                    new StackPanel()));

            var rows = new StackPanel();
            foreach (var f in flags.Catalog)
            {
                var info = f;
                var head = Ui.H(8, Ui.T(f.Name, 13.5, "InkBrush", true),
                                   Ui.Chip(Ui.T(FeatureCatalog.StageName(f.Stage), 11, "MutedBrush"), 8, 2, LayoutRules.ChipRadius));
                string note = FeatureCatalog.RowNote(f);
                if (note != null) head.Children.Add(Ui.T(note, 11, "DimBrush"));
                head.VerticalAlignment = VerticalAlignment.Center;

                var text = new StackPanel();
                text.Children.Add(head);
                var desc = Ui.T(f.Description, 12, "MutedBrush");
                desc.TextWrapping = TextWrapping.Wrap;
                desc.Margin = new Thickness(0, 3, LayoutRules.Gap, 0);
                text.Children.Add(desc);
                string why = flags.DisabledReason(f.Id);
                if (why != null)
                {
                    var w = Ui.T("Oprită automat: " + why, 12, "WarnBrush");
                    w.TextWrapping = TextWrapping.Wrap;
                    w.Margin = new Thickness(0, 3, LayoutRules.Gap, 0);
                    text.Children.Add(w);
                }

                var sw = V2Controls.Switch(flags.IsSaved(f.Id), v => { flags.Set(info.Id, v); _s.Save(); BuildBody(); }, f.Name);
                sw.VerticalAlignment = VerticalAlignment.Top;
                var row = Ui.Cols(Ui.Star(), Ui.Auto);
                row.Put(text);
                row.Put(sw, 1);
                row.Margin = new Thickness(0, 0, 0, LayoutRules.Gap);
                rows.Children.Add(row);
            }
            box.Children.Add(V2Controls.Card("Funcții noi (experimental)",
                "Se pornesc și se opresc pe loc, fără repornire și fără buton de salvare. Una care dă erori repetate se oprește singură.", rows));

            box.Children.Add(V2Controls.Card("Smart Clipboard", null, V2Controls.Stack(
                V2Controls.Toggle("Mesaj scurt în pastilă la copiere", _s.SmartClipboardPeek, v => { _s.SmartClipboardPeek = v; Save(); },
                                  "Când copiezi un JSON, un link cu urmărire sau un token JWT. Doar cu „Manager de activități” pornit."))));
        }

        // ---- Acțiuni ----

        private void BuildActions(StackPanel box)
        {
            var reg = ActionRegistry.Current;
            IReadOnlyList<ActionDescriptor> actions = Array.Empty<ActionDescriptor>();
            // every registered action, in the registry's own order; it refuses the ones that are off at invoke time
            try { actions = reg?.All.Where(a => (a.AllowedInvokers & ActionInvoker.UI) == ActionInvoker.UI).ToList() ?? actions; }
            catch { }
            if (actions.Count == 0)
            {
                box.Children.Add(Ui.T("Nimic deocamdată: pornește funcțiile din „Funcții noi”.", 12.5, "MutedBrush"));
                return;
            }
            _cols = LayoutRules.Columns(_width);
            foreach (var group in actions.GroupBy(a => string.IsNullOrEmpty(a.Category) ? "Acțiuni" : a.Category))
            {
                var grid = new Grid();
                for (int i = 0; i < _cols; i++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = Ui.Star() });
                var inGroup = group.ToList();
                for (int i = 0; i < inGroup.Count; i++)
                {
                    int row = i / _cols;
                    if (grid.RowDefinitions.Count <= row) grid.RowDefinitions.Add(new RowDefinition { Height = Ui.Auto });
                    var card = ActionCard(inGroup[i]);
                    SetColumn(card, i % _cols);
                    SetRow(card, row);
                    grid.Children.Add(card);
                }
                box.Children.Add(V2Controls.Eyebrow(group.Key.ToUpperInvariant()));
                box.Children.Add(grid);
            }
        }

        private UIElement ActionCard(ActionDescriptor a)
        {
            var icon = new Border { Width = 38, Height = 38, CornerRadius = new CornerRadius(LayoutRules.ChipRadius), Child = Ui.Icon(string.IsNullOrEmpty(a.Icon) ? "" : a.Icon, 16) };
            icon.SetResourceReference(Border.BackgroundProperty, "TrackBrush");
            icon.HorizontalAlignment = HorizontalAlignment.Left;
            var title = Ui.T(a.Title, 13.5, "InkBrush", true);
            title.TextWrapping = TextWrapping.Wrap;
            var run = Ui.PillBtn(a.Safety == ActionSafety.Safe ? "Pornește" : "Pornește…", () => Run(a), true);
            run.HorizontalAlignment = HorizontalAlignment.Left;
            run.Margin = new Thickness(0, LayoutRules.Gap, 0, 0);
            var card = Ui.Card(Ui.V(8, icon, title, run), 16, 14, LayoutRules.CardRadius);
            card.Margin = new Thickness(0, 0, LayoutRules.Gap, LayoutRules.Gap);
            return card;
        }

        private void Run(ActionDescriptor a)
        {
            // The registry does the checking: a Confirm/Dangerous action is asked about first, and only then confirmed.
            bool confirmed = a.Safety == ActionSafety.Safe ||
                             MessageBox.Show(_owner(), a.Title + "?", "WinNotch", MessageBoxButton.OKCancel, MessageBoxImage.Question) == MessageBoxResult.OK;
            if (!confirmed) return;
            var task = ActionRegistry.Current?.InvokeAsync(a.Id, null, ActionInvoker.UI, default, confirmed);
            if (task == null) return;
            // the registry says why it refused (a switch off, a missing parameter, nothing available): the user sees it
            _ = task.ContinueWith(t =>
            {
                string msg = t.IsCompletedSuccessfully ? t.Result?.Message : null;
                if (!string.IsNullOrEmpty(msg)) Dispatcher.InvokeAsync(() => _say(msg));
            }, System.Threading.Tasks.TaskScheduler.Default);
        }

        private Button Pill(string text, Action click)
        {
            var b = Ui.PillBtn(text, click ?? (() => { }));
            b.HorizontalAlignment = HorizontalAlignment.Left;
            b.Margin = new Thickness(0, 0, 8, 6);
            return b;
        }
    }
}
