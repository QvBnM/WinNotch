using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace WinNotch.Services
{
    public sealed class CalendarEvent
    {
        public string Title;
        public DateTime Start;      // local time
        public bool AllDay;
    }

    /// <summary>
    /// Next events from a private calendar link (.ics): Google Calendar → Settings → "Secret address in iCal format",
    /// Outlook → Settings → Shared calendars → "Publish a calendar" → ICS. Weekly and daily repeats are handled.
    /// </summary>
    public static partial class CalendarService
    {
        // The host you typed may be on your own network (a home server); anything a redirect points to must be public,
        // so a calendar server can't bounce WinNotch onto your router or other devices.
        private static readonly System.Threading.AsyncLocal<string> TrustedHost = new System.Threading.AsyncLocal<string>();
        private static readonly HttpClient Http = new HttpClient(new SocketsHttpHandler
        {
            AllowAutoRedirect = true, MaxAutomaticRedirections = 3, ConnectTimeout = TimeSpan.FromSeconds(8),
            UseProxy = false,              // the address check below must see the calendar server, not a proxy
            ConnectCallback = async (ctx, ct) =>
            {
                var ips = await System.Net.Dns.GetHostAddressesAsync(ctx.DnsEndPoint.Host, ct);
                bool trusted = string.Equals(ctx.DnsEndPoint.Host, TrustedHost.Value, StringComparison.OrdinalIgnoreCase);
                var ip = ips.FirstOrDefault(a => trusted || !NetSafety.IsPrivate(a)) ?? throw new HttpRequestException("redirecționare spre o adresă locală, refuzată");
                var socket = new System.Net.Sockets.Socket(ip.AddressFamily, System.Net.Sockets.SocketType.Stream, System.Net.Sockets.ProtocolType.Tcp) { NoDelay = true };
                try { await socket.ConnectAsync(ip, ctx.DnsEndPoint.Port, ct); }
                catch { socket.Dispose(); throw; }
                return new System.Net.Sockets.NetworkStream(socket, true);
            }
        }) { Timeout = TimeSpan.FromSeconds(15), MaxResponseContentBufferSize = 4 * 1024 * 1024 };
        private static int _running;

        /// <summary>At most this many events are read, and this many repeat steps computed in total (a huge or hostile file can't stall the PC).</summary>
        private const int MaxEvents = 5000, StepBudget = 500_000;
        [ThreadStatic] private static int _steps;

        public static async Task<List<CalendarEvent>> GetUpcomingAsync(string url, int days = 7)
        {
            var result = new List<CalendarEvent>();
            if (string.IsNullOrWhiteSpace(url)) return result;
            if (System.Threading.Interlocked.Exchange(ref _running, 1) == 1) return null;      // the previous refresh is still running
            try
            {
                if (url.StartsWith("webcal://", StringComparison.OrdinalIgnoreCase)) url = "https://" + url.Substring(9);
                if (!Uri.TryCreate(url, UriKind.Absolute, out var u) || (u.Scheme != "https" && u.Scheme != "http")) return result;
                TrustedHost.Value = u.Host;
                string text = await Http.GetStringAsync(u).ConfigureAwait(false);      // parse off the UI thread
                return Upcoming(text, DateTime.Now, days);
            }
            catch (Exception ex) { App.Log("Calendar: " + ex.Message); }
            finally { System.Threading.Interlocked.Exchange(ref _running, 0); }
            return result;
        }

        /// <summary>The next events (at most 10) from an .ics text, as seen at <paramref name="now"/>.</summary>
        public static List<CalendarEvent> Upcoming(string ics, DateTime now, int days = 7)
        {
            var result = new List<CalendarEvent>();
            var until = now.AddDays(days);
            _steps = StepBudget;
            foreach (var ev in ParseEvents(ics).Take(MaxEvents))
            {
                if (_steps <= 0) break;
                try { AddOccurrences(ev, now, until, result); }
                catch { /* one malformed event (absurd INTERVAL, dates out of range) doesn't empty the whole calendar */ }
            }
            return result.OrderBy(e => e.Start).Take(10).ToList();
        }

        private static void AddOccurrences(RawEvent ev, DateTime now, DateTime until, List<CalendarEvent> result)
        {
            {
                // All-day events start at 00:00: look from the start of today, or they'd vanish after 01:00.
                foreach (var start in Occurrences(ev, ev.AllDay ? now.Date : now.AddHours(-1), until))
                {
                    if (start < now && !(ev.AllDay && start.Date == now.Date)) continue;
                    result.Add(new CalendarEvent { Title = ev.Title, Start = start, AllDay = ev.AllDay });
                }
            }
        }

        private sealed class RawEvent
        {
            public string Title = "";
            public DateTime Start;               // wall time in the event's own time zone (Tz), or local when Tz is null
            public TimeZoneInfo Tz;
            public bool AllDay;
            public string RRule;
            public string Uid = "";
            public DateTime? RecurrenceId;       // set on an edited single occurrence of a repeating event (local time)
            public readonly HashSet<DateTime> ExDates = new HashSet<DateTime>();   // cancelled occurrences (local time)
        }

        private static List<RawEvent> ParseEvents(string ics)
        {
            // Unfold lines: a line starting with a space continues the previous one.
            var lines = new List<string>();
            foreach (var raw in ics.Replace("\r\n", "\n").Split('\n'))
            {
                if ((raw.StartsWith(" ") || raw.StartsWith("\t")) && lines.Count > 0) lines[lines.Count - 1] += raw.Substring(1);
                else lines.Add(raw);
            }

            var events = new List<RawEvent>();
            RawEvent cur = null;
            foreach (var l in lines)
            {
                if (l == "BEGIN:VEVENT") { cur = new RawEvent(); continue; }
                if (l == "END:VEVENT") { if (cur != null && cur.Start != default) events.Add(cur); cur = null; continue; }
                if (cur == null) continue;
                int colon = l.IndexOf(':');
                if (colon < 0) continue;
                string name = l.Substring(0, colon), value = l.Substring(colon + 1);
                string key = name.Split(';')[0].ToUpperInvariant();
                if (key == "SUMMARY") cur.Title = value.Replace("\\,", ",").Replace("\\;", ";").Replace("\\n", " ");
                else if (key == "UID") cur.Uid = value.Trim();
                else if (key == "DTSTART")
                {
                    cur.AllDay = name.ToUpperInvariant().Contains("VALUE=DATE") && !value.Contains("T");
                    var tz = cur.AllDay ? null : Zone(name);
                    var (d, isUtc) = ParseRaw(value);
                    if (isUtc) { cur.Start = d == default ? default : DateTime.SpecifyKind(d, DateTimeKind.Utc).ToLocalTime(); cur.Tz = null; }
                    else { cur.Start = d; cur.Tz = tz; }
                }
                else if (key == "RRULE") cur.RRule = value.ToUpperInvariant();
                else if (key == "EXDATE")
                {
                    var tz = Zone(name);
                    foreach (var v in value.Split(','))
                    {
                        var d = ToLocal(v, tz);
                        if (d != default) cur.ExDates.Add(d);
                    }
                }
                else if (key == "RECURRENCE-ID")
                {
                    var d = ToLocal(value, Zone(name));
                    if (d != default) cur.RecurrenceId = d;
                }
            }

            // An edited occurrence replaces the original one of its series: hide that one, keep the edited one.
            foreach (var ov in events.Where(e => e.RecurrenceId != null && e.Uid.Length > 0))
                foreach (var master in events.Where(e => e != ov && e.Uid == ov.Uid && e.RRule != null))
                    master.ExDates.Add(ov.RecurrenceId.Value);
            return events;
        }

        /// <summary>The time zone named in a property (TZID=Europe/Bucharest or a Windows name), or null.</summary>
        private static TimeZoneInfo Zone(string name)
        {
            var m = System.Text.RegularExpressions.Regex.Match(name, "TZID=\"?([^;:\"]+)");
            if (!m.Success) return null;
            string id = m.Groups[1].Value.Trim();
            if (TimeZoneInfo.TryFindSystemTimeZoneById(id, out var tz)) return tz;
            if (TimeZoneInfo.TryConvertIanaIdToWindowsId(id, out var win) && TimeZoneInfo.TryFindSystemTimeZoneById(win, out tz)) return tz;
            return null;
        }

        private static (DateTime, bool) ParseRaw(string v)
        {
            v = v.Trim();
            bool utc = v.EndsWith("Z");
            v = v.TrimEnd('Z');
            string[] fmts = { "yyyyMMdd'T'HHmmss", "yyyyMMdd'T'HHmm", "yyyyMMdd" };
            return DateTime.TryParseExact(v, fmts, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? (d, utc) : (default, false);
        }

        /// <summary>Any ICS date/time as local time.</summary>
        private static DateTime ToLocal(string v, TimeZoneInfo tz)
        {
            var (d, utc) = ParseRaw(v);
            if (d == default) return default;
            if (utc) return DateTime.SpecifyKind(d, DateTimeKind.Utc).ToLocalTime();
            return tz != null ? Convert(d, tz) : d;
        }

        private static DateTime Convert(DateTime wall, TimeZoneInfo tz)
        {
            try { return TimeZoneInfo.ConvertTime(DateTime.SpecifyKind(wall, DateTimeKind.Unspecified), tz, TimeZoneInfo.Local); }
            catch { return wall; }       // a time skipped by a DST change
        }

        private static readonly string[] Days = { "MO", "TU", "WE", "TH", "FR", "SA", "SU" };

        /// <summary>
        /// Occurrences between from and until, in local time. Supports DAILY, WEEKLY (with BYDAY), MONTHLY (same day, or
        /// BYDAY like 2TU / -1FR), YEARLY, INTERVAL, COUNT, UNTIL, EXDATE and edited occurrences.
        /// </summary>
        private static IEnumerable<DateTime> Occurrences(RawEvent ev, DateTime from, DateTime until)
        {
            DateTime Local(DateTime wall) => ev.Tz != null ? Convert(wall, ev.Tz) : wall;

            if (string.IsNullOrEmpty(ev.RRule))
            {
                var one = Local(ev.Start);
                if (one >= from && one <= until && !ev.ExDates.Contains(one)) yield return one;
                yield break;
            }
            string freq = Part(ev.RRule, "FREQ");
            int interval = int.TryParse(Part(ev.RRule, "INTERVAL"), out var iv) ? Math.Max(1, iv) : 1;
            int count = int.TryParse(Part(ev.RRule, "COUNT"), out var c) ? c : int.MaxValue;
            DateTime end = ToLocal(Part(ev.RRule, "UNTIL") ?? "", ev.Tz);
            if (end == default) end = DateTime.MaxValue;
            if (ev.AllDay && end != DateTime.MaxValue) end = end.Date.AddDays(1).AddTicks(-1);
            string byday = Part(ev.RRule, "BYDAY");

            int produced = 0;
            foreach (var wall in Candidates(ev.Start, freq, interval, byday))
            {
                if (--_steps <= 0) yield break;
                var t = Local(wall);
                if (t > until || t > end || produced >= count) yield break;
                produced++;                               // COUNT includes past occurrences
                if (t >= from && !ev.ExDates.Contains(t)) yield return t;
            }
        }

        /// <summary>Every occurrence of the series in order, from the first one (at most ~20 000, as a safety limit).</summary>
        private static IEnumerable<DateTime> Candidates(DateTime start, string freq, int interval, string byday)
        {
            const int Limit = 20000;
            if (freq == "DAILY")
            {
                for (int k = 0; k < Limit; k++) yield return start.AddDays((double)k * interval);
            }
            else if (freq == "WEEKLY")
            {
                var days = (byday ?? "").Split(',').Select(d => Array.IndexOf(Days, d.Trim())).Where(i => i >= 0).Distinct().OrderBy(i => i).ToList();
                if (days.Count == 0) { for (int k = 0; k < Limit; k++) yield return start.AddDays(7.0 * k * interval); yield break; }
                int startDow = ((int)start.DayOfWeek + 6) % 7;                 // Monday = 0
                var weekStart = start.Date.AddDays(-startDow);
                int n = 0;
                for (int w = 0; n < Limit; w++)
                    foreach (var d in days)
                    {
                        var t = weekStart.AddDays(7.0 * w * interval + d) + start.TimeOfDay;
                        if (t < start) continue;
                        n++;
                        yield return t;
                    }
            }
            else if (freq == "MONTHLY")
            {
                var m = System.Text.RegularExpressions.Regex.Match(byday ?? "", @"^([+-]?\d)(MO|TU|WE|TH|FR|SA|SU)$");
                for (int k = 0; k < Limit / 10; k++)
                {
                    var month = new DateTime(start.Year, start.Month, 1).AddMonths(k * interval);
                    if (m.Success)
                    {
                        int nth = int.Parse(m.Groups[1].Value);
                        var dow = (DayOfWeek)((Array.IndexOf(Days, m.Groups[2].Value) + 1) % 7);
                        DateTime t;
                        if (nth > 0)
                        {
                            var first = month;
                            while (first.DayOfWeek != dow) first = first.AddDays(1);
                            t = first.AddDays(7 * (nth - 1));
                        }
                        else
                        {
                            var last = month.AddMonths(1).AddDays(-1);
                            while (last.DayOfWeek != dow) last = last.AddDays(-1);
                            t = last.AddDays(7 * (nth + 1));
                        }
                        if (t.Month != month.Month) continue;
                        t += start.TimeOfDay;
                        if (t >= start) yield return t;
                    }
                    else if (start.Day <= DateTime.DaysInMonth(month.Year, month.Month))
                        yield return month.AddDays(start.Day - 1) + start.TimeOfDay;    // months without that day are skipped
                }
            }
            else if (freq == "YEARLY")
            {
                for (int k = 0; k < 200; k++)
                {
                    int y = start.Year + k * interval;
                    if (start.Month == 2 && start.Day == 29 && !DateTime.IsLeapYear(y)) continue;
                    yield return new DateTime(y, start.Month, start.Day) + start.TimeOfDay;
                }
            }
            else yield return start;
        }

        private static string Part(string rrule, string name)
        {
            foreach (var p in rrule.Split(';'))
            {
                var kv = p.Split('=');
                if (kv.Length == 2 && kv[0] == name) return kv[1];
            }
            return null;
        }
    }
}
