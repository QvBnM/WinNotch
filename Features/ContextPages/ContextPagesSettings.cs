using System;
using System.Collections.Generic;
using System.Linq;
using WinNotch.Features.ContextPages;

namespace WinNotch
{
    public sealed partial class AppSettings
    {
        /// <summary>
        /// P27: context category ("Dev", "Browser", "Meeting", "Game", "Media", "Office", "Creator") → page id ("home",
        /// "system", "devices", "tools" or a page of yours). A missing key (or a settings.json from before 0.6.15) means
        /// „—”: the notch opens where it was. Never changed in place: Settings and the smoke test put a new dictionary
        /// (a save from another thread may be reading the old one).
        /// </summary>
        public Dictionary<string, string> ContextPages { get; set; } = NewContextPages();

        internal static Dictionary<string, string> NewContextPages() => new Dictionary<string, string>(StringComparer.Ordinal);

        /// <summary>
        /// Called after loading: null (hand-edited file) becomes empty; unknown categories and empty pages are dropped;
        /// keys get their canonical spelling ("dev" → "Dev").
        /// </summary>
        internal void NormalizeContextPages() => ContextPages = CleanContextPages(ContextPages);

        internal static Dictionary<string, string> CleanContextPages(IEnumerable<KeyValuePair<string, string>> map)
        {
            var clean = NewContextPages();
            foreach (var kv in map ?? Enumerable.Empty<KeyValuePair<string, string>>())
            {
                var cat = ContextPageRules.ParseKey(kv.Key);
                string page = kv.Value?.Trim();
                if (cat == null || string.IsNullOrEmpty(page) || page.Length > 64) continue;
                clean[ContextPageRules.Key(cat.Value)] = page;
            }
            return clean;
        }

        /// <summary>A copy with <paramref name="category"/> mapped to <paramref name="pageId"/> (null or empty = „—”).</summary>
        internal Dictionary<string, string> WithContextPage(Core.Context.AppCategory category, string pageId)
        {
            var copy = CleanContextPages(ContextPages);
            if (string.IsNullOrWhiteSpace(pageId)) copy.Remove(ContextPageRules.Key(category));
            else copy[ContextPageRules.Key(category)] = pageId.Trim();
            return CleanContextPages(copy);
        }
    }
}
