using System.Windows.Controls;

namespace WinNotch.Panes
{
    /// <summary>One screen of the open notch (Acasă, Sistem, Dispozitive, Unelte, surse audio).</summary>
    internal abstract class Pane : Grid
    {
        protected readonly NotchWindow W;
        protected Pane(NotchWindow w) { W = w; }

        /// <summary>Height of the whole open notch while this screen is shown.</summary>
        public abstract double PanelHeight { get; }

        /// <summary>About once a second while visible.</summary>
        public virtual void Refresh() { }

        /// <summary>Every frame (~30/s) while visible: visualizer, lyrics, progress.</summary>
        public virtual void Fast() { }

        public virtual void Shown() { }
        public virtual void Hidden() { }
    }
}
