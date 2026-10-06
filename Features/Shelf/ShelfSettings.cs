using System.Collections.Generic;
using WinNotch.Features.Shelf;

namespace WinNotch
{
    public sealed partial class AppSettings
    {
        /// <summary>
        /// P23: the shelf's references (paths, never copies; a folder ends with "\"), in order, at most 20, kept until you
        /// empty it. The key "Shelf" was already in settings.json, unused, since 0.6.6 (always empty): the shelf took it over.
        /// Saved atomically with the other settings (settings.json.tmp, then a move). Never changed in place: a new list is
        /// put (a save from another thread may be reading the old one).
        /// </summary>
        public List<string> Shelf { get; set; } = new List<string>();

        /// <summary>Called after loading: null becomes empty; entries with a bad form, network paths and duplicates go; at most 20.</summary>
        internal void NormalizeShelf()
        {
            var m = new ShelfModel();
            m.Load(Shelf);
            Shelf = m.ToSettings();
        }
    }
}
