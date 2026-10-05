using System;
using System.Collections.Generic;
using Stopwatch = System.Diagnostics.Stopwatch;
using System.Linq;
using System.Threading;
using WinNotch.Core.Actions;
using WinNotch.Core.Activity;
using WinNotch.Core.Flags;
using WinNotch.Features.Actions;
using WinNotch.Features.Activity;
using WinNotch.Features.Context;

namespace WinNotch
{
    public static partial class T
    {
        sealed class ActEnv : IActivityEnvironment
        {
            public volatile bool Open, Full;
            public bool NotchOpen => Open;
            public bool Fullscreen => Full;
        }

        sealed class CountingPresenter : IActivityPresenter
        {
            public int Calls;
            public Action OnInvalidate;
            public void Invalidate() { Interlocked.Increment(ref Calls); OnInvalidate?.Invoke(); }
        }

        sealed class FakeActivityHost : IActivityHost
        {
            public int Calls;
            public int DismissAll() { Calls++; return 2; }
        }

        static Activity Act(string id, ActivityPriority p = ActivityPriority.Normal, int ms = 3000, string key = null, bool persistent = false,
                            bool interactive = false, string title = "", double w = 300, double h = 40) =>
            new Activity { Id = id, Key = key, Priority = p, Duration = TimeSpan.FromMilliseconds(ms), Persistent = persistent, Interactive = interactive, Title = title, Width = w, Height = h, Payload = id };

        /// <summary>P13: the Activity Manager's rules, its limits and speed, the switch, the action and the routing of the old alerts.</summary>
        static void ActivityTests()
        {
            (ActivityManager M, FakeClock C, ActEnv E, CountingPresenter P) New()
            {
                var c = new FakeClock(); var e = new ActEnv(); var p = new CountingPresenter();
                return (new ActivityManager(c, e, p), c, e, p);
            }

            // ---- one alert: shown, its size and UI, then gone; nothing ticks in standby
            {
                var (m, c, _, p) = New();
                var r = m.Post(Act("a", w: 320, h: 54, ms: 2000));
                var v = m.View;
                bool shown = r == PostResult.Shown && v.Kind == ActivityViewKind.Single && (string)v.Primary.Payload == "a" && v.Primary.Width == 320 && v.Primary.Height == 54 && m.TimerActive;
                c.Ms(1999); bool still = m.View.Kind == ActivityViewKind.Single;
                c.Ms(1); bool gone = m.View.Kind == ActivityViewKind.None && !m.TimerActive && c.Active == 0;
                Check("AM1", "O alertă: apare cu UI-ul și mărimea ei, stă exact durata, apoi standby fără niciun cronometru", shown && still && gone && p.Calls == 2, $"{shown} {still} {gone} calls={p.Calls}");
            }

            // ---- same key: changed in place, no new slot, the duration starts again
            {
                var (m, c, _, _) = New();
                m.Post(Act("vol", ms: 1600)); c.Ms(1000);
                long ver = m.View.Version;
                var r = m.Post(Act("vol", ms: 1600, title: "nou"));
                c.Ms(1500); bool kept = m.View.Primary?.Title == "nou";
                c.Ms(100); bool gone = m.View.Kind == ActivityViewKind.None;
                m.Post(Act("big", ActivityPriority.High, 10000)); m.Post(Act("q", key: "k")); int q1 = m.QueueCount;
                var r2 = m.Post(Act("q2", key: "k", title: "schimbat")); int q2 = m.QueueCount;
                bool upd = m.Update(Act("q3", key: "k")) && !m.Update(Act("x", key: "nimic")) && m.QueueCount == 1;
                Check("AM2", "Aceeași cheie: actualizare pe loc (afișată sau la coadă), fără loc nou; durata repornește; Update fără cheie existentă nu adaugă nimic",
                      r == PostResult.Updated && m.View.Version > ver && kept && gone && q1 == 1 && r2 == PostResult.Updated && q2 == 1 && upd, $"{r} {kept} {gone} {q1} {r2} {q2} {upd}");
            }

            // ---- priorities: same or higher replaces; lower waits (at most 10 s); Critical interrupts anything
            {
                var (m, c, _, _) = New();
                m.Post(Act("n1")); var rep = m.Post(Act("n2")); bool replaced = m.View.Primary.Id == "n2";
                m.Post(Act("h", ActivityPriority.High, 5000));
                var q = m.Post(Act("low-n", ActivityPriority.Normal, 3000));
                bool waits = q == PostResult.Queued && m.View.Primary.Id == "h";
                c.Ms(5000); bool next = m.View.Primary?.Id == "low-n";
                c.Ms(3000);
                m.Post(Act("long", ActivityPriority.High, 30000)); m.Post(Act("stale"));
                c.Ms(30000); bool stale = m.View.Kind == ActivityViewKind.None;        // waited 30 s: too old to be news
                m.Post(Act("btn", ActivityPriority.High, 20000, interactive: true));
                var crit = m.Post(Act("crit", ActivityPriority.Critical, 4000));
                bool interrupts = crit == PostResult.Shown && m.View.Primary.Id == "crit";
                Check("AM3", "Priorități: egală sau mai mare înlocuiește (ca până acum); mai mică așteaptă la coadă și apare după; după 10 s la coadă nu mai apare; Critical întrerupe orice",
                      rep == PostResult.Shown && replaced && waits && next && stale && interrupts, $"{replaced} {waits} {next} {stale} {interrupts}");
            }

            // ---- notch open and fullscreen
            {
                var (m, c, e, _) = New();
                e.Open = true;
                var n = m.Post(Act("n")); var h = m.Post(Act("h", ActivityPriority.High)); var cr = m.Post(Act("c", ActivityPriority.Critical));
                var pe = m.Post(Act("p", persistent: true, title: "P"));
                bool hiddenWhileOpen = m.View.Kind != ActivityViewKind.Single || m.View.Primary.Id != "c";
                e.Open = false; m.NotchClosed();
                bool critAfter = m.View.Primary?.Id == "c";
                c.Ms(4000); bool persAfter = m.View.Kind == ActivityViewKind.Single && m.View.Primary.Persistent;
                m.DismissAll();
                e.Full = true;
                var fl = m.Post(Act("l", ActivityPriority.Low)); var fn = m.Post(Act("n")); var fp = m.Post(Act("p2", persistent: true));
                var fh = m.Post(Act("h", ActivityPriority.High)); bool hShown = m.View.Primary?.Id == "h";
                var fc = m.Post(Act("c", ActivityPriority.Critical));
                Check("AM4", "Notch deschis: Normal/High aruncate (ca până acum), Critical și persistentele așteaptă închiderea; ecran complet: doar alertele High și Critical, persistentele așteaptă",
                      n == PostResult.Dropped && h == PostResult.Dropped && cr == PostResult.Queued && pe == PostResult.Queued && hiddenWhileOpen && critAfter && persAfter &&
                      fl == PostResult.Dropped && fn == PostResult.Dropped && fp == PostResult.Queued /* R1: kept for after */ && fh == PostResult.Shown && hShown && fc == PostResult.Shown,
                      $"{n} {h} {cr} {pe} {critAfter} {persAfter} {fl} {fn} {fp} {fh} {fc}");
            }

            // ---- bursts: more than 3 distinct alerts in 5 s → „N noutăți”
            {
                var (m, c, _, _) = New();
                var rs = new List<PostResult>();
                for (int i = 1; i <= 3; i++) { rs.Add(m.Post(Act("b" + i))); c.Ms(200); }
                bool noGroupAt3 = m.View.Kind == ActivityViewKind.Single && m.View.Primary.Id == "b3";
                rs.Add(m.Post(Act("b4"))); c.Ms(200);
                bool g4 = m.View.Kind == ActivityViewKind.Group && m.View.GroupCount == 4 && m.View.Primary.Title == "4 noutăți";
                rs.Add(m.Post(Act("b5")));
                rs.Add(m.Post(Act("b5")));                                           // the same alert again: not counted twice
                bool g5 = m.View.Kind == ActivityViewKind.Group && m.View.GroupCount == 5 && m.View.Primary.Title == "5 noutăți" && m.QueueCount == 0;
                c.Ms(3999); bool stays = m.View.Kind == ActivityViewKind.Group;
                c.Ms(1); bool ends = m.View.Kind == ActivityViewKind.None;
                Check("AM5", "Peste 3 alerte diferite în 5 s → un singur „N noutăți” (4, apoi 5; aceeași alertă nu e numărată de două ori); expiră după 4 s",
                      noGroupAt3 && g4 && g5 && stays && ends && rs.Skip(3).All(r => r == PostResult.Grouped), string.Join(",", rs) + $" {noGroupAt3} {g4} {g5} {stays} {ends}");

                var (m2, c2, _, _) = New();
                for (int i = 0; i < 50; i++) { m2.Post(Act("volume", ms: 1600)); c2.Ms(30); }               // a volume drag: one key
                bool volOk = m2.View.Kind == ActivityViewKind.Single && m2.View.Primary.Id == "volume";
                for (int i = 0; i < 6; i++) m2.Post(Act("h" + i, ActivityPriority.High));                      // important ones are never grouped
                bool highOk = m2.View.Kind == ActivityViewKind.Single && m2.View.Primary.Id == "h5";
                m2.Post(Act("x1")); m2.Post(Act("x2", ActivityPriority.Low)); m2.Post(Act("x3")); m2.Post(Act("x4"));
                bool groupWaits = m2.View.Primary.Id == "h5" && m2.QueueCount == 1;
                for (int i = 0; i < 4; i++) m2.Post(Act("btn" + i, ActivityPriority.High, interactive: true));     // button alerts neither
                Check("AM6", "Nu se grupează: actualizările aceleiași chei (volum tras), alertele High, cele cu butoane; un grup care nu poate apărea încă așteaptă la coadă",
                      volOk && highOk && groupWaits && m2.View.Primary.Id == "btn3", $"{volOk} {highOk} {groupWaits} {m2.View.Primary.Id}");
            }

            // ---- R1 (P13): after „N noutăți” expires, the burst starts over (it used to recount the summarized alerts)
            {
                var (m, c, _, _) = New();
                for (int i = 1; i <= 5; i++) { m.Post(Act("r" + i)); c.Ms(200); }        // group at the 4th, expires 4 s after the 5th
                bool grouped = m.View.Kind == ActivityViewKind.Group && m.View.GroupCount == 5;
                c.Ms(3800); bool expired = m.View.Kind == ActivityViewKind.None;
                c.Ms(500);
                var r = m.Post(Act("after"));
                Check("AM15", "R1: o alertă la 500 ms după ce „5 noutăți” a expirat apare singură (nu ca „6 noutăți”)",
                      grouped && expired && r == PostResult.Shown && m.View.Kind == ActivityViewKind.Single && m.View.Primary.Id == "after", $"{grouped} {expired} {r} {m.View.Kind}");
            }

            // ---- R1 (P13): persistent ones posted over fullscreen wait for it to end; a button alert is never queued
            {
                var (m, _, e, _) = New();
                e.Full = true;
                var rp = m.Post(Act("p-full", persistent: true, title: "Livrare"));
                bool waits = rp == PostResult.Queued && m.PersistentCount == 1 && m.View.Kind == ActivityViewKind.None;
                e.Full = false; m.Refresh();
                bool shownAfter = m.View.Kind == ActivityViewKind.Single && m.View.Primary.Id == "p-full";
                var (m2, _, e2, _) = New();
                e2.Open = true;
                var ri = m2.Post(Act("btn-crit", ActivityPriority.Critical, interactive: true));
                e2.Open = false; m2.NotchClosed();
                Check("AM16", "R1: o activitate persistentă Normal venită peste ecran complet e păstrată și apare când ecranul complet se termină; o alertă Critical cu butoane cu notch-ul deschis e aruncată, nu pusă la coadă",
                      waits && shownAfter && ri == PostResult.Dropped && m2.View.Kind == ActivityViewKind.None, $"{rp} {waits} {shownAfter} {ri}");
            }

            Check("AM7", "„N noutăți” în română: 1 noutate, 4 noutăți, 19 noutăți, 20 de noutăți, 101 noutăți, 120 de noutăți",
                  ActivityManager.GroupTitle(1) == "1 noutate" && ActivityManager.GroupTitle(4) == "4 noutăți" && ActivityManager.GroupTitle(19) == "19 noutăți" &&
                  ActivityManager.GroupTitle(20) == "20 de noutăți" && ActivityManager.GroupTitle(101) == "101 noutăți" && ActivityManager.GroupTitle(120) == "120 de noutăți");

            // ---- persistent ones: behind the alerts; two → split pill
            {
                var (m, c, _, _) = New();
                var r1 = m.Post(Act("p1", persistent: true, title: "Comandă"));
                bool one = m.View.Kind == ActivityViewKind.Single && m.View.IsPersistent && !m.TimerActive;
                m.Post(Act("p2", persistent: true, title: "Scor"));
                bool split = m.View.Kind == ActivityViewKind.Split && m.View.Primary.Id == "p1" && m.View.Secondary.Id == "p2" && m.View.IsPersistent && !m.TimerActive;
                m.Post(Act("alert", ms: 2000)); bool over = m.View.Kind == ActivityViewKind.Single && m.View.Primary.Id == "alert";
                c.Ms(2000); bool back = m.View.Kind == ActivityViewKind.Split;
                m.Post(Act("p2", persistent: true, title: "Scor 2-1")); bool updated = m.View.Secondary.Title == "Scor 2-1" && m.PersistentCount == 2;
                m.Post(Act("p3", ActivityPriority.High, persistent: true, title: "Important")); bool order = m.View.Primary.Id == "p3" && m.View.Secondary.Id == "p1";
                m.Dismiss("p3"); m.Dismiss("p1"); bool single = m.View.Kind == ActivityViewKind.Single && m.View.Primary.Id == "p2";
                for (int i = 0; i < 20; i++) m.Post(Act("pp" + i, persistent: true));
                bool capped = m.PersistentCount == ActivityManager.MaxPersistent;
                int n = m.DismissAll(); bool none = m.View.Kind == ActivityViewKind.None && !m.TimerActive && c.Active == 0;
                Check("AM8", "Persistente: una → pastila ei, fără cronometru; două → pastilă împărțită (prioritatea mai mare la stânga); o alertă trece peste și apoi revine; aceeași cheie se actualizează; cel mult 8",
                      r1 == PostResult.Shown && one && split && over && back && updated && order && single && capped && n == 8 && none, $"{one} {split} {over} {back} {updated} {order} {single} {capped} {n} {none}");
            }

            // ---- Low → peek (2 s); button alerts → now or never
            {
                var (m, c, _, _) = New();
                m.Post(Act("low", ActivityPriority.Low, 10000, title: "discret"));
                bool peek = m.View.Kind == ActivityViewKind.Peek && m.View.Primary.Title == "discret";
                c.Ms(2000); bool peekGone = m.View.Kind == ActivityViewKind.None;
                m.Post(Act("h", ActivityPriority.High, 5000));
                var lowQ = m.Post(Act("low2", ActivityPriority.Low));
                var btnN = m.Post(Act("btn", ActivityPriority.Normal, 5000, interactive: true));
                var btnH = m.Post(Act("btn2", ActivityPriority.High, 5000, interactive: true));
                bool btnShown = m.View.Primary.Id == "btn2" && m.View.Primary.Interactive;
                Check("AM9", "Low → „peek” de 2 s (orice durată ar cere); alerta cu butoane: acum sau deloc (niciodată la coadă)",
                      peek && peekGone && lowQ == PostResult.Queued && btnN == PostResult.Dropped && btnH == PostResult.Shown && btnShown, $"{peek} {peekGone} {lowQ} {btnN} {btnH}");
            }

            // ---- dismiss, touch, notch closed
            {
                var (m, c, e, _) = New();
                m.Post(Act("a", ms: 1000));
                c.Ms(900); bool touched = m.Touch("a") && !m.Touch("b");
                c.Ms(900); bool alive = m.View.Primary?.Id == "a";
                m.Post(Act("h", ActivityPriority.High, 5000)); m.Post(Act("q"));
                bool dq = m.Dismiss("q") && m.QueueCount == 0 && m.View.Primary.Id == "h";
                bool dc = m.Dismiss("h") && m.View.Kind == ActivityViewKind.None && !m.Dismiss("h") && !m.Dismiss(null);
                m.Post(Act("p", persistent: true)); m.Post(Act("x", ms: 9000)); m.Post(Act("hq", ActivityPriority.High, 9000)); m.Post(Act("lq", ActivityPriority.Normal));
                e.Open = true; e.Open = false; m.NotchClosed();
                bool closed = m.View.Kind == ActivityViewKind.Single && m.View.Primary.Id == "p" && m.QueueCount == 0 && !m.TimerActive;
                Check("AM10", "Touch repornește durata alertei afișate; Dismiss scoate din coadă sau de pe ecran; după închiderea notch-ului alertele acoperite dispar, persistentele revin",
                      touched && alive && dq && dc && closed, $"{touched} {alive} {dq} {dc} {closed}");
            }

            // ---- bad activities, long titles
            {
                var (m, _, _, _) = New();
                bool bad = new[]
                {
                    null, new Activity { Id = "Bad Id" }, new Activity { Id = "" }, new Activity { Id = new string('a', 61) }, new Activity { Id = "ok", Width = -1 },
                    new Activity { Id = "ok", Duration = TimeSpan.Zero }, new Activity { Id = "ok", Duration = TimeSpan.FromHours(2) }, new Activity { Id = "ok", Height = double.NaN },
                }.All(a => m.Post(a) == PostResult.Dropped) && m.View.Kind == ActivityViewKind.None;
                var longT = new Activity { Id = "ok", Title = new string('x', 500) + "\nlinie" };
                bool okPersistent = m.Post(new Activity { Id = "p.ok", Persistent = true, Duration = TimeSpan.Zero }) == PostResult.Shown;
                Check("AM11", "Activități invalide (id, mărime, durată) → aruncate, fără excepție; titlul tăiat la 120 de caractere, fără rânduri noi",
                      bad && longT.Title.Length == Activity.MaxTitle && longT.Title.EndsWith("…") && !longT.Title.Contains('\n') && okPersistent);
            }

            // ---- the queue is bounded: the least important and oldest go first
            {
                var (m, c, _, _) = New();
                m.Post(Act("download", ActivityPriority.High, 600000));
                for (int i = 0; i < 60; i++)
                {
                    m.Post(Act("n" + i));                                            // 2.5 s apart: never more than 3 in 5 s, no burst
                    if (i % 6 == 0) m.Post(Act("l" + i, ActivityPriority.Low));
                    c.Ms(2500);
                }
                int qc = m.QueueCount;
                Check("AM12", "Coada are cel mult 50 de locuri: pleacă întâi cele Low, apoi cele mai vechi", qc == ActivityManager.MaxQueue, qc.ToString());
            }

            // ---- 1000 alerts in 10 s: fast, bounded, nothing left ticking
            {
                var (m, c, _, p) = New();
                var sw = Stopwatch.StartNew();
                int maxQ = 0;
                for (int i = 0; i < 1000; i++)
                {
                    var pr = i % 10 == 0 ? ActivityPriority.High : i % 7 == 0 ? ActivityPriority.Low : i % 97 == 0 ? ActivityPriority.Critical : ActivityPriority.Normal;
                    if (i % 50 == 0) m.Post(Act("p" + (i % 4), persistent: true));
                    else m.Post(Act("a" + (i % 40), pr, 1000 + i % 5000, interactive: i % 31 == 0));
                    if (i % 100 == 0) m.Dismiss("a" + (i % 40));
                    maxQ = Math.Max(maxQ, m.QueueCount);
                    c.Ms(10);
                }
                long ms = sw.ElapsedMilliseconds;
                c.Ms(60000);
                bool end = m.View.Kind == ActivityViewKind.Split && !m.TimerActive && c.Active == 0;
                m.DismissAll();
                Check("AM13", "1000 de alerte în 10 s: sub 1 s de calcul, coada ≤ 50, la final doar persistentele, niciun cronometru rămas",
                      ms < 1000 && maxQ <= ActivityManager.MaxQueue && m.PersistentCount == 0 && end && p.Calls > 0, $"{ms} ms, coada max {maxQ}, {end}");
            }

            // ---- many threads at once, real timers, a presenter that calls back: no deadlock, consistent state
            {
                var presenter = new CountingPresenter();
                var env = new ActEnv();
                var m = new ActivityManager(new ThreadPoolActivityScheduler(), env, presenter);
                presenter.OnInvalidate = () => { var v = m.View; if (v.Kind == ActivityViewKind.Group && v.GroupCount > 30) m.Dismiss(ActivityManager.GroupId); };
                Exception error = null;
                var threads = Enumerable.Range(0, 8).Select(t => new Thread(() =>
                {
                    try
                    {
                        var rnd = new Random(t);
                        for (int i = 0; i < 2000; i++)
                        {
                            int op = rnd.Next(20);
                            var pr = (ActivityPriority)rnd.Next(4);
                            if (op == 0) m.Dismiss("k" + rnd.Next(30));
                            else if (op == 1) m.Touch("k" + rnd.Next(30));
                            else if (op == 2 && rnd.Next(50) == 0) m.DismissAll();
                            else if (op == 3) m.Post(Act("p" + rnd.Next(5), pr, persistent: true));
                            else if (op == 4) { env.Open = rnd.Next(2) == 0; if (!env.Open) m.NotchClosed(); }
                            else if (op == 5) env.Full = rnd.Next(4) == 0;
                            else m.Post(Act("k" + rnd.Next(30), pr, 1 + rnd.Next(30), interactive: op == 6));
                            if (m.QueueCount > ActivityManager.MaxQueue) throw new InvalidOperationException("coada peste limită");
                        }
                    }
                    catch (Exception ex) { error = ex; }
                })).ToList();
                var sw = Stopwatch.StartNew();
                threads.ForEach(th => th.Start());
                bool joined = threads.All(th => th.Join(TimeSpan.FromSeconds(20)));
                env.Open = false; env.Full = false;
                m.DismissAll();
                Thread.Sleep(100);
                bool clean = m.View.Kind == ActivityViewKind.None && !m.TimerActive && !m.HasAny;
                Check("AM14", "8 fire × 2000 de operații în paralel, cronometre reale, prezentator care apelează înapoi: fără blocare, fără excepții, coada ≤ 50, la final curat",
                      joined && error == null && clean, $"{joined} {error?.GetType().Name} {clean} {sw.ElapsedMilliseconds} ms");
            }

            // ---- the switch
            var info = FeatureCatalog.Find(ActivityManager.FeatureId);
            var flagsOn = new FeatureFlags(new Dictionary<string, bool>());
            var safe = new FeatureFlags(new Dictionary<string, bool> { [ActivityManager.FeatureId] = true }, safeMode: true);
            Check("AF1", "Comutatorul „activity-manager”: în catalog, Experimental, oprit implicit, oprit în modul sigur; același id ca în Core/Activity",
                  info != null && info.Stage == FeatureStage.Experimental && !info.DefaultOn && FeatureCatalog.ActivityManager == ActivityManager.FeatureId &&
                  !flagsOn.IsEnabled(ActivityManager.FeatureId) && !safe.IsEnabled(ActivityManager.FeatureId) && info.Name == "Manager de activități");

            // ---- the action
            {
                var host = new FakeActivityHost();
                var on = new FeatureFlags(new Dictionary<string, bool> { [ActivityManager.FeatureId] = true });
                var off = new FeatureFlags(new Dictionary<string, bool>());
                var rOn = new ActionRegistry(on); var rOff = new ActionRegistry(off);
                bool noDup = true;
                try
                {
                    BuiltInActions.Register(rOn, new FakeBuiltInHost());
                    ContextActions.Register(rOn, () => null, null);
                    ActivityActions.Register(rOn, host);
                    ActivityActions.Register(rOff, host);
                }
                catch (InvalidOperationException) { noDup = false; }
                var a = rOn.Get(ActivityActions.DismissAllId);
                Check("AF2", "Acțiunea „activity.dismiss-all”: id valid și unic, titlu în română, aliasuri ro + en, legată de comutator, pe firul UI",
                      noDup && a != null && ActionRegistry.IsValidId(a.Id) && a.Title == "Închide toate activitățile din notch" && a.FeatureId == ActivityManager.FeatureId &&
                      a.Aliases.Contains("dismiss all") && a.Aliases.Contains("închide alertele") && a.RequiresUiThread && a.Safety == ActionSafety.Safe);
                var res = rOn.InvokeAsync(ActivityActions.DismissAllId, null, ActionInvoker.CommandBar).GetAwaiter().GetResult();
                bool found = rOn.Search("inchide alertele", ActionInvoker.CommandBar).Any(x => x.Id == ActivityActions.DismissAllId) &&
                             rOn.Search("dismiss", ActionInvoker.CommandBar).Any(x => x.Id == ActivityActions.DismissAllId);
                Check("AF3", "Comutator pornit: acțiunea închide tot (gazda e apelată) și e găsită după „închide alertele” / „dismiss”",
                      res.Success && host.Calls == 1 && res.Message == "Am închis 2 activități" && found, res.Message);
                var resOff = rOff.InvokeAsync(ActivityActions.DismissAllId, null, ActionInvoker.CommandBar).GetAwaiter().GetResult();
                Check("AF4", "Comutator oprit: acțiunea e indisponibilă (Failed, gazda nu e apelată, nu apare la căutare)",
                      !resOff.Success && host.Calls == 1 && !rOff.Search("dismiss", ActionInvoker.CommandBar).Any());
            }

            // ---- the old alerts, as activities
            {
                var bad = new List<string>();
                foreach (var la in LegacyAlerts.All)
                {
                    var payload = new object();
                    var a = ActivityRouting.FromAlert(la.Id, la.Width, la.Height, la.DurationMs, la.Important, payload);
                    if (a.Priority != (la.Important ? ActivityPriority.High : ActivityPriority.Normal) || a.Persistent || a.Priority == ActivityPriority.Low ||
                        a.Key != la.Key || a.Interactive != la.Interactive || a.Duration.TotalMilliseconds != la.DurationMs || a.Width != la.Width || a.Height != la.Height ||
                        !ReferenceEquals(a.Payload, payload) || !a.IsValid) bad.Add(la.Id);
                }
                var unknown = ActivityRouting.FromAlert("alta-alerta", 300, 40, 2000, false, null);
                Check("AF5", "Fiecare alertă veche devine o activitate cu același UI, mărime și durată; „importantă” → High, altfel Normal; niciuna Low sau persistentă",
                      bad.Count == 0 && unknown.Key == "alta-alerta" && !unknown.Interactive, string.Join(",", bad));
                Check("AF6", "Răspunsul pentru apelant, ca ShowLive: alerta cu butoane doar dacă e pe ecran acum; celelalte dacă nu au fost aruncate",
                      ActivityRouting.Accepted(PostResult.Queued, false, false) && ActivityRouting.Accepted(PostResult.Grouped, false, false) && !ActivityRouting.Accepted(PostResult.Dropped, false, true) &&
                      ActivityRouting.Accepted(PostResult.Shown, true, true) && !ActivityRouting.Accepted(PostResult.Shown, true, false) && !ActivityRouting.Accepted(PostResult.Queued, true, true));
            }

            // ---- the activity path vs the old one: what changes on purpose (the rest is in AlertCharacterizationTests)
            {
                var s = new ActivitySink();
                s.Show(LegacyAlerts.Find(LegacyAlerts.BatteryLow)); s.Sec(1);
                bool accepted = s.Show(LegacyAlerts.Find(LegacyAlerts.Track));
                bool batteryStays = s.ShownId == LegacyAlerts.BatteryLow;
                s.Sec(4); bool trackAfter = s.ShownId == LegacyAlerts.Track;
                Check("AF7", "Cu Activity Manager: o piesă nouă nu mai acoperă „Baterie descărcată”; așteaptă și apare după ea",
                      accepted && batteryStays && trackAfter, $"{accepted} {batteryStays} {trackAfter}");
            }
        }
    }
}
