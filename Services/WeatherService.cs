using System;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace WinNotch.Services
{
    public sealed class Weather
    {
        public bool Ok;
        public double Temp;
        public double Max;
        public double Min;
        public int Code;
        public bool IsDay = true;
        public System.Collections.Generic.List<(DateTime Time, double Temp)> Hours = new System.Collections.Generic.List<(DateTime, double)>();
        public string Text => Describe(Code);
        public bool IsSunny => Code <= 1;

        public static string Describe(int c)
        {
            if (c == 0) return "Senin";
            if (c <= 2) return "Parțial noros";
            if (c == 3) return "Înnorat";
            if (c == 45 || c == 48) return "Ceață";
            if (c >= 51 && c <= 57) return "Burniță";
            if (c >= 61 && c <= 67) return "Ploaie";
            if (c >= 71 && c <= 77) return "Ninsoare";
            if (c >= 80 && c <= 82) return "Averse";
            if (c >= 85 && c <= 86) return "Averse de ninsoare";
            if (c >= 95) return "Furtună";
            return "—";
        }
    }

    /// <summary>Current weather from Open-Meteo (free, no API key).</summary>
    public static class WeatherService
    {
        private static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(10), MaxResponseContentBufferSize = 1024 * 1024 };

        public static async Task<Weather> GetAsync(double lat, double lon)
        {
            var w = new Weather();
            try
            {
                string url = string.Format(CultureInfo.InvariantCulture,
                    "https://api.open-meteo.com/v1/forecast?latitude={0}&longitude={1}&current=temperature_2m,weather_code,is_day&hourly=temperature_2m&daily=temperature_2m_max,temperature_2m_min&timezone=auto&forecast_days=2",
                    lat, lon);
                string json = await Http.GetStringAsync(url);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                var cur = root.GetProperty("current");
                w.Temp = cur.GetProperty("temperature_2m").GetDouble();
                w.Code = cur.GetProperty("weather_code").GetInt32();
                if (cur.TryGetProperty("is_day", out var isDay)) w.IsDay = isDay.GetInt32() == 1;
                if (root.TryGetProperty("hourly", out var hourly))
                {
                    var times = hourly.GetProperty("time");
                    var temps = hourly.GetProperty("temperature_2m");
                    var now = DateTime.Now;
                    for (int i = 0; i < times.GetArrayLength() && w.Hours.Count < 4; i++)
                    {
                        if (!DateTime.TryParse(times[i].GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var t)) continue;
                        if (t <= now) continue;
                        w.Hours.Add((t, temps[i].GetDouble()));
                    }
                }
                var daily = root.GetProperty("daily");
                w.Max = daily.GetProperty("temperature_2m_max")[0].GetDouble();
                w.Min = daily.GetProperty("temperature_2m_min")[0].GetDouble();
                w.Ok = true;
            }
            catch (Exception ex) { App.Log("Vreme: " + ex.Message); }
            return w;
        }
    }
}
