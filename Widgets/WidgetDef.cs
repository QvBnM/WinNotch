using System;
using System.Collections.Generic;
using System.Linq;

namespace WinNotch.Widgets
{
    public enum OptionKind { Text, MultiLine, Choice, Bool, Number, Color, Files }

    /// <summary>A setting of a widget, edited in the editor window's inspector.</summary>
    internal sealed class OptionDef
    {
        public string Key, Label, Default = "";
        public OptionKind Kind;
        public (string Value, string Label)[] Choices = Array.Empty<(string, string)>();
        public string Hint;
    }

    /// <summary>One kind of widget in the gallery.</summary>
    internal sealed class WidgetDef
    {
        public string Type, Name, Category, Glyph, Description;
        public (int W, int H)[] Sizes;
        public OptionDef[] Options = Array.Empty<OptionDef>();
        public bool Custom;                                   // shown under "Personalizate"
        public Func<NotchWindow, WidgetSlot, Widget> Create;

        public (int W, int H) DefaultSize => Sizes[Math.Min(1, Sizes.Length - 1)];

        /// <summary>The allowed size closest to what the user dragged to.</summary>
        public (int W, int H) Nearest(int w, int h) =>
            Sizes.OrderBy(s => Math.Abs(s.W - w) * 2 + Math.Abs(s.H - h) * 3).First();
    }
}
