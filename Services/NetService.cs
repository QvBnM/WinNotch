using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace WinNotch.Services
{
    /// <summary>One finished speed test, kept in the history.</summary>
    public sealed class SpeedRecord
    {
        public DateTime At { get; set; }
        public double Down { get; set; }          // Mb/s
        public double Up { get; set; }            // Mb/s, 0 = not measured
        public int Ping { get; set; } = -1;       // ms to the internet (1.1.1.1)
        public int Jitter { get; set; } = -1;     // ms
        public int Router { get; set; } = -1;     // ms to the router (default gateway)
        public int RouterLoss { get; set; }       // % lost pings to the router
        public int NetLoss { get; set; }          // % lost pings to the internet
        public string Conn { get; set; } = "";    // Wi-Fi / Ethernet
        public string Error { get; set; }
        public bool Ok => Error == null && Down > 0;
    }

    /// <summary>Live state while a test runs.</summary>
    public sealed class SpeedProgress
    {
        public string Phase;        // "ping", "down", "up"
        public double Mbps;         // current speed in this phase
        public double Fraction;     // 0..1 of the whole test
    }

    /// <summary>Connection type, router/internet ping and a speed test against Cloudflare's public speed servers.</summary>
    public static class NetService
    {
        private static readonly HttpClient Http = CreateClient();

        private static HttpClient CreateClient()
        {
            var h = new SocketsHttpHandler { MaxConnectionsPerServer = 16, PooledConnectionLifetime = TimeSpan.FromMinutes(2) };
            var c = new HttpClient(h) { Timeout = Timeout.InfiniteTimeSpan };
            // Cloudflare rejects requests that don't look like they come from its own speed test page.
            c.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/128.0 Safari/537.36 WinNotch");
            c.DefaultRequestHeaders.Referrer = new Uri("https://speed.cloudflare.com/");
            c.DefaultRequestHeaders.TryAddWithoutValidation("Origin", "https://speed.cloudflare.com");
            return c;
        }

        private static NetworkInterface ActiveNic()
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up &&
                            n.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                            n.NetworkInterfaceType != NetworkInterfaceType.Tunnel &&
                            n.GetIPProperties().GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork && !g.Address.Equals(IPAddress.Any)))
                .OrderByDescending(n => n.GetIPStatistics().BytesReceived)
                .FirstOrDefault();
        }

        /// <summary>"Wi-Fi", "Ethernet" or "Offline" for the adapter that currently carries traffic.</summary>
        public static string ConnectionType()
        {
            try
            {
                var nic = ActiveNic();
                if (nic == null) return "Offline";
                return nic.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 ? "Wi-Fi" : "Ethernet";
            }
            catch { return ""; }
        }

        /// <summary>The router's address (default gateway), or null.</summary>
        public static IPAddress Gateway()
        {
            try
            {
                return ActiveNic()?.GetIPProperties().GatewayAddresses
                    .Select(g => g.Address)
                    .FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork && !a.Equals(IPAddress.Any));
            }
            catch { return null; }
        }

        /// <summary>Sends <paramref name="count"/> pings; returns average ms (-1 if none answered), jitter and loss in %.</summary>
        public static async Task<(int Avg, int Jitter, int Loss)> PingSeriesAsync(IPAddress target, int count = 10)
        {
            if (target == null) return (-1, -1, 100);
            var times = new List<long>();
            try
            {
                using var p = new Ping();
                for (int i = 0; i < count; i++)
                {
                    try
                    {
                        var r = await p.SendPingAsync(target, 1000);
                        if (r.Status == IPStatus.Success) times.Add(r.RoundtripTime);
                    }
                    catch { }
                    await Task.Delay(120);
                }
            }
            catch { }
            int loss = (int)Math.Round((count - times.Count) * 100.0 / count);
            if (times.Count == 0) return (-1, -1, loss);
            int avg = (int)Math.Round(times.Average());
            int jitter = times.Count > 1 ? (int)Math.Round(times.Zip(times.Skip(1), (a, b) => Math.Abs(a - b)).Average()) : 0;
            return (avg, jitter, loss);
        }

        public static async Task<int> PingAsync() => (await PingSeriesAsync(IPAddress.Parse("1.1.1.1"), 3)).Avg;

        /// <summary>
        /// Full test, about 20 seconds: ping to the router and to the internet, then 8 s download and 6 s upload
        /// over several parallel connections (needed to fill fast lines).
        /// </summary>
        public static async Task<SpeedRecord> SpeedTestAsync(IProgress<SpeedProgress> progress = null)
        {
            var rec = new SpeedRecord { At = DateTime.Now, Conn = ConnectionType() };
            try
            {
                progress?.Report(new SpeedProgress { Phase = "ping", Fraction = 0.02 });
                var gw = Gateway();
                var routerTask = PingSeriesAsync(gw, 12);
                var netTask = PingSeriesAsync(IPAddress.Parse("1.1.1.1"), 12);
                await Task.WhenAll(routerTask, netTask);
                (rec.Router, _, rec.RouterLoss) = routerTask.Result;
                (rec.Ping, rec.Jitter, rec.NetLoss) = netTask.Result;
                if (gw == null) rec.RouterLoss = 0;

                rec.Down = await MeasureAsync(true, TimeSpan.FromSeconds(8), progress, 0.1, 0.6);
                if (rec.Down <= 0) throw new Exception("descărcarea nu a pornit");
                try { rec.Up = await MeasureAsync(false, TimeSpan.FromSeconds(6), progress, 0.6, 1.0); }
                catch (Exception ex) { App.Log("Test viteză, încărcare: " + ex.Message); rec.Up = 0; }
            }
            catch (Exception ex)
            {
                rec.Error = ex.Message;
                App.Log("Test viteză: " + ex);
            }
            return rec;
        }

        private static async Task<double> MeasureAsync(bool download, TimeSpan duration, IProgress<SpeedProgress> progress, double f0, double f1)
        {
            long bytes = 0;
            string lastError = null;
            using var cts = new CancellationTokenSource(duration);
            var sw = Stopwatch.StartNew();
            const int streams = 6;

            async Task Worker()
            {
                var buf = new byte[64 * 1024];
                while (!cts.IsCancellationRequested)
                {
                    try
                    {
                        if (download)
                        {
                            using var resp = await Http.GetAsync("https://speed.cloudflare.com/__down?bytes=100000000", HttpCompletionOption.ResponseHeadersRead, cts.Token);
                            resp.EnsureSuccessStatusCode();
                            using var s = await resp.Content.ReadAsStreamAsync(cts.Token);
                            int n;
                            while ((n = await s.ReadAsync(buf, 0, buf.Length, cts.Token)) > 0) Interlocked.Add(ref bytes, n);
                        }
                        else
                        {
                            using var content = new CountingContent(25_000_000, n => Interlocked.Add(ref bytes, n));
                            using var resp = await Http.PostAsync("https://speed.cloudflare.com/__up", content, cts.Token);
                            resp.EnsureSuccessStatusCode();
                        }
                    }
                    catch (OperationCanceledException) { return; }
                    catch (Exception ex)
                    {
                        lastError = ex.Message;
                        try { await Task.Delay(300, cts.Token); } catch { return; }
                    }
                }
            }

            var workers = Enumerable.Range(0, streams).Select(_ => Task.Run(Worker)).ToArray();

            // Ignore the first second (connections opening, TCP ramp-up), then measure the rest.
            long warmBytes = 0;
            TimeSpan warmAt = TimeSpan.Zero;
            long prevBytes = 0;
            var prevAt = TimeSpan.Zero;
            double shown = 0;
            while (!cts.IsCancellationRequested)
            {
                try { await Task.Delay(250, cts.Token); } catch { break; }
                var now = sw.Elapsed;
                long b = Interlocked.Read(ref bytes);
                if (warmAt == TimeSpan.Zero && now.TotalSeconds >= 1) { warmAt = now; warmBytes = b; }
                double inst = (b - prevBytes) * 8 / Math.Max(0.05, (now - prevAt).TotalSeconds) / 1e6;
                shown = shown == 0 ? inst : shown * 0.7 + inst * 0.3;
                prevBytes = b; prevAt = now;
                progress?.Report(new SpeedProgress
                {
                    Phase = download ? "down" : "up",
                    Mbps = shown,
                    Fraction = f0 + (f1 - f0) * Math.Min(1, now.TotalSeconds / duration.TotalSeconds)
                });
            }
            try { await Task.WhenAll(workers); } catch { }
            var end = sw.Elapsed;
            long total = Interlocked.Read(ref bytes);
            if (total == 0 && lastError != null) throw new Exception(lastError);
            if (warmAt == TimeSpan.Zero || end - warmAt < TimeSpan.FromSeconds(0.5))
                return total * 8 / Math.Max(0.05, end.TotalSeconds) / 1e6;
            return (total - warmBytes) * 8 / (end - warmAt).TotalSeconds / 1e6;
        }

        /// <summary>Upload body that reports every chunk as it's sent, so upload speed can be shown live.</summary>
        private sealed class CountingContent : HttpContent
        {
            private readonly int _size;
            private readonly Action<int> _sent;
            private static readonly byte[] Chunk = MakeChunk();

            public CountingContent(int size, Action<int> sent)
            {
                _size = size;
                _sent = sent;
                Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
            }

            private static byte[] MakeChunk()
            {
                var b = new byte[64 * 1024];
                new Random(7).NextBytes(b);
                return b;
            }

            protected override async Task SerializeToStreamAsync(Stream stream, TransportContext context) =>
                await SerializeToStreamAsync(stream, context, CancellationToken.None);

            protected override async Task SerializeToStreamAsync(Stream stream, TransportContext context, CancellationToken ct)
            {
                int left = _size;
                while (left > 0)
                {
                    int n = Math.Min(left, Chunk.Length);
                    await stream.WriteAsync(Chunk, 0, n, ct);
                    left -= n;
                    _sent(n);
                }
            }

            protected override bool TryComputeLength(out long length) { length = _size; return true; }
        }

        /// <summary>Plain-language verdict: is it the router, the provider, or all fine?</summary>
        public static (string Text, string Level) Verdict(SpeedRecord r, SpeedRecord previousBest)
        {
            if (!r.Ok) return ("Testul nu a reușit: " + (r.Error ?? "fără răspuns") + ". Dacă nici paginile nu se încarcă, repornește routerul.", "bad");

            bool wifi = r.Conn == "Wi-Fi";
            int routerLimit = wifi ? 40 : 10;
            bool routerBad = r.Router < 0 ? false : (r.RouterLoss >= 15 || r.Router > routerLimit);
            bool routerDead = r.Router < 0 && r.RouterLoss >= 100;
            bool netBad = r.NetLoss >= 10 || r.Ping > 120 || r.Jitter > 40;
            bool slow = previousBest != null && previousBest.Down > 0 && r.Down < previousBest.Down * 0.5;

            if (routerBad)
                return (wifi
                    ? $"Routerul răspunde greu ({r.Router} ms, {r.RouterLoss}% pierderi). Restartează routerul; dacă nu ajută, apropie-te de el sau folosește cablu."
                    : $"Routerul răspunde greu ({r.Router} ms, {r.RouterLoss}% pierderi). Un restart al routerului ar trebui să ajute.", "bad");
            if (netBad && !routerDead)
                return ($"Routerul e în regulă ({Math.Max(r.Router, 0)} ms), dar legătura spre internet are probleme (ping {r.Ping} ms, {r.NetLoss}% pierderi). Poate fi furnizorul; un restart al routerului poate ajuta.", "warn");
            if (slow)
                return ($"Viteza e la jumătate față de cel mai bun test ({Math.Round(previousBest.Down)} Mb/s). Închide descărcările din fundal; dacă rămâne așa, restartează routerul.", "warn");
            return ("Totul arată bine. Nu e nevoie de restart la router.", "ok");
        }
    }
}
