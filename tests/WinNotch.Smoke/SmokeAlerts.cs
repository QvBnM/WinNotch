using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Capturing;
using FlaUI.Core.Definitions;
using FlaUI.Core.Exceptions;
using FlaUI.Core.Input;
using FlaUI.Core.WindowsAPI;
using FlaUI.UIA3;
using WinNotch.Features.Smoke;

namespace WinNotch.Smoke
{
    public static partial class SmokeProgram
    {
        private static void AlertChangesPill(string command)
        {
            var idle = WaitFor(ReadStatus, s => s.Mode == "Idle", TimeSpan.FromSeconds(15), "notch-ul în standby");
            Thread.Sleep(800);                                                    // let the standby animation settle
            idle = WaitFor(ReadStatus, s => s.Mode == "Idle", TimeSpan.FromSeconds(5), "notch-ul în standby");
            Command(command);
            WaitFor(ReadStatus, s => s.Mode == "Live" && !s.SameSize(idle), TimeSpan.FromSeconds(8), "alerta afișată (mode=Live, altă mărime decât " + idle + ")");
            WaitFor(ReadStatus, s => s.Mode == "Idle" && s.SameSize(idle), TimeSpan.FromSeconds(15), "pastila înapoi la " + idle);
        }

        private static void ToggleFeature()
        {
            int stops = LogCount("Context: oprit."), starts = LogCount("Context: pornit");
            Command("toggle feature context-engine");
            WaitFor(() => LogCount("Context: oprit."), n => n > stops, TimeSpan.FromSeconds(10), "„Context: oprit.” în log");
            Command("toggle feature context-engine");
            WaitFor(() => LogCount("Context: pornit"), n => n > starts, TimeSpan.FromSeconds(10), "„Context: pornit” în log");
            Command("toggle feature nu-exista");                                  // unknown: ignored, nothing breaks
            WaitFor(() => LogCount("funcție necunoscută: nu-exista"), n => n > 0, TimeSpan.FromSeconds(10), "funcția necunoscută ignorată");
        }

        // ------------------------------------------------------------------ P13: the Activity Manager

        private static void ActivityManagerOn()
        {
            Command("toggle feature " + ActivityFeature);
            WaitFor(() => LogCount("Activity Manager: pornit."), n => n > 0, TimeSpan.FromSeconds(10), "„Activity Manager: pornit.” în log");
        }

        /// <summary>Off: the command is refused (a log line, no fatal one) and the pill stays in standby, without activity fields.</summary>
        private static void Refused(string command)
        {
            int before = LogCount("managerul de activități e oprit; comanda e ignorată");
            Command(command);
            WaitFor(() => LogCount("managerul de activități e oprit; comanda e ignorată"), n => n > before, TimeSpan.FromSeconds(10), "comanda „" + command + "” refuzată în log");
            Thread.Sleep(1000);
            var s = ReadStatus();
            if (s.Mode != "Idle" || s.Split + s.Group + s.Peek != 0) Fail("Cu „" + ActivityFeature + "” oprit, „" + command + "” a schimbat pastila: " + s);
        }

        private static void SplitPill()
        {
            SettledIdle();
            if (!_activityOn)
            {
                Refused("post-activity persistent 1");
                Refused("post-activity persistent 2");
                int before = LogCount("activity.dismiss-all → indisponibil");
                Command("dismiss-activities");
                WaitFor(() => LogCount("activity.dismiss-all → indisponibil"), n => n > before, TimeSpan.FromSeconds(10), "„activity.dismiss-all” indisponibilă în log");
                return;
            }
            Command("post-activity persistent 1");
            WaitFor(ReadStatus, s => s.Mode == "Live" && s.Split == 0, TimeSpan.FromSeconds(8), "o activitate persistentă (mode=Live, fără split)");
            Command("post-activity persistent 2");
            var split = WaitFor(ReadStatus, s => s.Mode == "Live" && s.Split == 1, TimeSpan.FromSeconds(8), "pastila împărțită (split=1)");
            Thread.Sleep(5000);                                                   // persistent: still there after an alert would have ended
            var still = ReadStatus();
            if (still.Split != 1) Fail("Pastila împărțită a dispărut singură: " + still);
            Command("dismiss-activities");
            WaitFor(() => LogCount("activity.dismiss-all → făcut"), n => n > 0, TimeSpan.FromSeconds(10), "„activity.dismiss-all → făcut” în log");
            WaitFor(ReadStatus, s => s.Mode == "Idle" && s.Split == 0, TimeSpan.FromSeconds(10), "standby după „activity.dismiss-all” (de la " + split + ")");
        }

        private static void Burst()
        {
            SettledIdle();
            if (!_activityOn) { Refused("post-activity burst 5"); return; }
            Command("post-activity burst 5");
            var g = WaitFor(ReadStatus, s => s.Mode == "Live" && s.Group == 5, TimeSpan.FromSeconds(8), "„5 noutăți” (group=5)");
            WaitFor(ReadStatus, s => s.Mode == "Idle" && s.Group == 0, TimeSpan.FromSeconds(15), "standby după „5 noutăți” (de la " + g + ")");
        }

        private static void Peek()
        {
            SettledIdle();
            // runs after the burst: wait out its 5 s window, so the Low activity can't be counted with it (timing-independent)
            if (_activityOn) Thread.Sleep(5500);
            if (!_activityOn) { Refused("post-activity low"); return; }
            Command("post-activity low");
            var p = WaitFor(ReadStatus, s => s.Mode == "Live" && s.Peek == 1, TimeSpan.FromSeconds(8), "„peek” (peek=1)");
            if (p.H > 40) Fail("„Peek” ar trebui să rămână cât pastila mică (înălțime ≤ 40): " + p);
            WaitFor(ReadStatus, s => s.Mode == "Idle" && s.Peek == 0, TimeSpan.FromSeconds(10), "standby după „peek”");
        }
    }
}
