using System;
using System.Linq;
using WinNotch.Core.Flags;
using WinNotch.Features.PagesV2;

namespace WinNotch
{
    /// <summary>
    /// P74: the four standard pages in the notch's own language — engraved group names, every measurement in the
    /// monospaced face, thinner bars and one thread along the bottom edge. The rule that decides what is a
    /// measurement, and the pins that keep the whole look in one place instead of four page files.
    /// </summary>
    public static partial class T
    {
        static void PagesV2Tests()
        {
            MeasurementRule();
            PagesV2Look();
            PagesV2SourcePins();
        }

        static void MeasurementRule()
        {
            string[] numbers =
            {
                "28%", "84%", "13,1 / 32 GB", "54 °C", "48,2 MB/s", "1:12", "09:41", "~22 min", "2560 × 1440",
                "412 / 54", "12 ms", "0", "-3", "1.1.1.1", "6,2 GB", "(4)", "3:28",
            };
            Check("PV1", "Măsurătorile intră în fontul cu cifre de aceeași lățime",
                  numbers.All(PageStyle.IsMeasurement),
                  string.Join(", ", numbers.Where(n => !PageStyle.IsMeasurement(n))));

            string[] words =
            {
                "Dansează pe scrum", "se încarcă", "Memorie", "CPU 28%", "Chrome", "Baterie 10%", "—", "",
                "Procesorul s-a încălzit", "Microsoft Teams · de 12 min", "vineri, 10 octombrie",
            };
            Check("PV2", "Un text cu un cuvânt înaintea cifrei rămâne text: „CPU 28%” e o propoziție, nu un număr",
                  !words.Any(PageStyle.IsMeasurement),
                  string.Join(", ", words.Where(PageStyle.IsMeasurement)));

            Check("PV3", "Fără cifre nu e măsurătoare, oricât de scurt ar fi; null și gol sunt în regulă",
                  !PageStyle.IsMeasurement(null) && !PageStyle.IsMeasurement("") && !PageStyle.IsMeasurement("GB") &&
                  !PageStyle.IsMeasurement("—"));

            Check("PV4", "Unitatea e scurtă: după cifre încap cel mult cinci litere, nu o frază",
                  PageStyle.IsMeasurement("48,2 MB/s") && PageStyle.IsMeasurement("22 min") &&
                  !PageStyle.IsMeasurement("3 aplicații") && !PageStyle.IsMeasurement("20 de secunde") &&
                  PageStyle.MaxUnitLetters == 5);
        }

        static void PagesV2Look()
        {
            Check("PV5", "Mărimile: cardurile la 16, barele la 3, firul de 2 px retras cu 20 de la margini",
                  PageStyle.CardRadius == 16 && PageStyle.BarHeight == 3 &&
                  PageStyle.ThreadThickness == 2 && PageStyle.ThreadInset == 20 &&
                  PageStyle.BarHeight < 4 && PageStyle.EyebrowSize == 11);

            Check("PV6", "Numele unui grup se gravează: majuscule, nimic altceva schimbat",
                  PageStyle.Eyebrow("Consumă acum") == "CONSUMĂ ACUM" && PageStyle.Eyebrow(null) == "" &&
                  PageStyle.Eyebrow("CPU") == "CPU");

            Check("PV7", "Id-ul comutatorului e cel din catalog, funcția e experimentală și oprită implicit",
                  PageStyle.FeatureId == FeatureCatalog.NewPages &&
                  FeatureCatalog.Find(PageStyle.FeatureId) is FeatureInfo f &&
                  f.Stage == FeatureStage.Experimental && !f.DefaultOn);
        }

        static void PagesV2SourcePins()
        {
            string ui = Src("Ui.cs"), part = Src("Features/PagesV2/NotchWindow.PagesV2.cs"),
                   notch = Src("NotchWindow.xaml.cs"), meas = Src("Features/PagesV2/MeasureText.cs");

            Check("PV8", "Noul desen stă într-un singur loc: constructorii din Ui, prin care trec toate cele patru pagini",
                  ui.Contains("internal static bool NewPages") && ui.Contains("PageStyle.FeatureId") &&
                  ui.Contains("new Features.PagesV2.MeasureText(") && ui.Contains("PageStyle.Eyebrow(text)") &&
                  ui.Contains("PageStyle.BarHeight"));

            Check("PV9", "Paginile nu sunt rescrise: niciun fișier de pagină nu citește comutatorul",
                  new[] { "Panes/HomePane.cs", "Panes/SystemPane.cs", "Panes/DevicesPane.cs", "Panes/ToolsPane.cs" }
                      .All(f => !Src(f).Contains("PageStyle") && !Src(f).Contains("pagini-redesenate")));

            // TextBlock.OnPropertyChanged e sealed (prins de CI la 0.6.29), iar un DependencyPropertyDescriptor ar
            // ține fiecare rând în viață pentru totdeauna: cârligul e metadata lui Text, suprascrisă pentru tipul ăsta.
            Check("PV10", "Alegerea fontului se reface la fiecare schimbare de text, nu o dată la construirea paginii",
                  meas.Contains("TextProperty.OverrideMetadata(typeof(MeasureText)") &&
                  meas.Contains("PageStyle.IsMeasurement(Text)") &&
                  !meas.Contains("DependencyPropertyDescriptor") && !meas.Contains("override void OnPropertyChanged"));

            Check("PV11", "Firul: niciun cronometru nou (merge pe tick-ul existent), doar cu notch-ul deschis, fără culori în cod",
                  part.Contains("_mode == Mode.Expanded ? MediaProgress() : -1") &&
                  notch.Contains("PagesV2Tick();") && !part.Contains("DispatcherTimer") &&
                  part.Contains("SetResourceReference(Border.BackgroundProperty, \"AccentBrush\")") &&
                  !System.Text.RegularExpressions.Regex.IsMatch(part, @"Color\.From|#[0-9A-Fa-f]{6}|new SolidColorBrush"));

            Check("PV12", "Protocolul comutatorului: abonare, dezabonare, Dispatcher, erorile prin ReportError",
                  part.Contains("FeatureFlags.Current.Changed += _pagesFlagHandler;") &&
                  part.Contains("FeatureFlags.Current.Changed -= _pagesFlagHandler;") &&
                  part.Contains("Dispatcher.InvokeAsync(ApplyPagesV2Switch)") &&
                  part.Contains("ReportError(PageStyle.FeatureId, ex)") &&
                  notch.Contains("StartPagesV2();") && notch.Contains("StopPagesV2();"));
        }
    }
}
