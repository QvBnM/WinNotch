using System;
using System.Collections.Generic;
using System.Linq;
using WinNotch.Core.Flags;

namespace WinNotch
{
    public static partial class T
    {
        /// <summary>
        /// Audit of the feature switches (reported by the author: "I turned the games / fullscreen one on and saw no
        /// difference"). Every entry of the catalog must really be read somewhere, must say under what condition it can
        /// be seen at all, and a switch that is already on by default must say so — otherwise ticking its box changes
        /// nothing and the switch looks dead.
        /// </summary>
        static void FeatureAuditTests()
        {
            var all = FeatureCatalog.All;

            Check("FA1", "Fiecare comutator e citit în cod: un IsEnabled pe id-ul lui, nu doar o intrare în catalog",
                  all.Where(f => f.Id != FeatureCatalog.DemoFlag).All(f => FlagReaders(f.Id).Count > 0),
                  string.Join(", ", all.Where(f => f.Id != FeatureCatalog.DemoFlag && FlagReaders(f.Id).Count == 0).Select(f => f.Id)));

            Check("FA2", "Un comutator pornit implicit o spune în Setări (altfel bifarea lui nu schimbă nimic și pare mort)",
                  all.Where(f => f.DefaultOn).All(f => FeatureCatalog.RowNote(f) == "pornită implicit") &&
                  all.Where(f => f.DefaultOn).All(f => Says(f, "pornită implicit")) &&
                  all.Where(f => !f.DefaultOn).All(f => FeatureCatalog.RowNote(f) == null) &&
                  FeatureCatalog.RowNote(null) == null);

            Check("FA3", "Pagina de setări arată marcajul și spune că schimbarea se aplică la „Salvează”",
                  Src("SettingsWindow.xaml.cs").Contains("Core.Flags.FeatureCatalog.RowNote(f)") &&
                  Src("SettingsWindow.xaml").Contains("abia când apeși „Salvează”") &&
                  Src("SettingsWindow.xaml").Contains("pornită implicit"));

            // Raportat de autor: „Ascuns pe tot ecranul” era deja pornită, deci bifarea ei n-a schimbat nimic.
            Check("FA4", "Fiecare descriere spune când se vede funcția (sau că e pornită implicit)",
                  all.All(f => Describes(f)),
                  string.Join(", ", all.Where(f => !Describes(f)).Select(f => f.Id)));

            Check("FA5", "Descrierile rămân o linie citibilă, în română, fără jargon de cod",
                  all.All(f => f.Description.Length is > 40 and <= 450 && f.Description.EndsWith(".", StringComparison.Ordinal) &&
                               !f.Description.Contains("IsEnabled") && !f.Description.Contains("FeatureId")),
                  string.Join(", ", all.Where(f => f.Description.Length > 450).Select(f => f.Id + "=" + f.Description.Length)));

            Check("FA6", "„Ascuns pe tot ecranul” spune cele trei condiții în care se vede (setarea, aplicația fără ramă, un singur monitor)",
                  FeatureCatalog.Find(FeatureCatalog.FullscreenHide) is FeatureInfo fs &&
                  fs.Description.Contains("ascuns") && fs.Description.Contains("fără ramă") &&
                  fs.Description.Contains("monitor") && Says(fs, "pornită implicit"));

            Check("FA7", "Funcțiile clădite pe motorul de context spun că fără el nu pornesc",
                  new[] { FeatureCatalog.ContextPages, FeatureCatalog.QuickActions }
                      .All(id => FeatureCatalog.Find(id).Description.Contains("Motorul de context")));

            // Protocolul comutatorului: oprirea trebuie să fie curată (dezabonare) pentru fiecare funcție care se abonează.
            Check("FA8", "Fiecare funcție care se abonează la Changed se și dezabonează, în același fișier",
                  SubscribingFiles().All(f => Src(f).Contains("Changed -=")),
                  string.Join(", ", SubscribingFiles().Where(f => !Src(f).Contains("Changed -="))));

            Check("FA9", "Comutatorul de test spune pe față că nu face nimic vizibil",
                  FeatureCatalog.Find(FeatureCatalog.DemoFlag).Description.Contains("Doar pentru teste"));
        }

        /// <summary>A description that says when the feature can be seen: a condition, or that it is on by default.</summary>
        static bool Describes(FeatureInfo f)
        {
            string d = f.Description;
            if (f.Id == FeatureCatalog.DemoFlag) return Says(f, "nu face nimic vizibil");
            return Says(f, "pornită implicit") || Says(f, "se vede") || Says(f, "apare") || Says(f, "merge doar");
        }

        /// <summary>The description says this, whatever the case of the first letter (it may start a sentence).</summary>
        static bool Says(FeatureInfo f, string what) =>
            (f?.Description ?? "").IndexOf(what, StringComparison.OrdinalIgnoreCase) >= 0;

        /// <summary>
        /// The app's own files (outside the tests) that read this switch. A feature reads it either through the literal
        /// id, through its own rule class ("ShelfActions.FeatureId"), or — inside that class — as a bare "FeatureId".
        /// </summary>
        static List<string> FlagReaders(string id)
        {
            string type = RuleTypeFor(id);
            string declares = "FeatureId = \"" + id + "\"";
            var hits = new List<string>();
            foreach (var rel in SourceFiles())
            {
                string src = Src(rel);
                if (!src.Contains("IsEnabled(")) continue;
                if (src.Contains("\"" + id + "\"") ||
                    (type != null && src.Contains(type + ".FeatureId")) ||
                    (src.Contains(declares) && src.Contains("IsEnabled(FeatureId)")))
                    hits.Add(rel);
            }
            return hits;
        }

        /// <summary>The rule class that declares this id (its "FeatureId" constant), found by reflection; null if none.</summary>
        static string RuleTypeFor(string id) => typeof(FeatureCatalog).Assembly.GetTypes()
            .Select(t => new { t, f = t.GetField("FeatureId", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static) })
            .Where(x => x.f != null && x.f.IsLiteral && x.f.FieldType == typeof(string) && (string)x.f.GetRawConstantValue() == id)
            .Select(x => x.t.Name).FirstOrDefault();

        /// <summary>Every source file of the app that could read a switch (the test sources and the build folders are out).</summary>
        static IReadOnlyList<string> SourceFiles() => SourceFileCache ??= System.IO.Directory
            .GetFiles(RepoRoot(), "*.cs", System.IO.SearchOption.AllDirectories)
            .Select(p => p.Substring(RepoRoot().Length).TrimStart('\\', '/').Replace('\\', '/'))
            .Where(p => !p.StartsWith("tests/", StringComparison.Ordinal) && !p.Contains("/obj/") && !p.Contains("/bin/"))
            .ToList();

        static IReadOnlyList<string> SourceFileCache;

        /// <summary>The files that subscribe to the switches' Changed event (they must unsubscribe in the same file).</summary>
        static IReadOnlyList<string> SubscribingFiles() => SourceFiles()
            .Where(p => Src(p).Contains("FeatureFlags.Current.Changed +=") || Src(p).Contains("_flags.Changed +="))
            .ToList();

    }
}
