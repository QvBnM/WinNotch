using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WinNotch.Services;

namespace WinNotch.Widgets
{
    /// <summary>Scurtături: apps, folders, files and web links you pick (one per line), with their real icons.</summary>
    internal sealed class ShortcutsWidget : Widget
    {
        public override Thickness CardPadding => new Thickness(6, 4, 6, 4);

        public ShortcutsWidget(NotchWindow w, WidgetSlot s) : base(w, s)
        {
            var items = Parse(s.Opt("items", ""));
            if (items.Count == 0)
            {
                Children.Add(Stack(Cap("SCURTĂTURI"), Ui.T("Adaugă-le din Editor (rotița de pe widget)", 11.5, "DimBrush")));
                return;
            }
            int perRow = Math.Max(1, (int)Math.Floor((Cw * 115 - 12) / 58.0));
            int rows = Math.Max(1, Ch);
            var g = new System.Windows.Controls.Primitives.UniformGrid { Columns = perRow, Rows = rows, VerticalAlignment = VerticalAlignment.Center };
            foreach (var (name, target) in items.Take(perRow * rows))
            {
                var img = FileIcon(target);
                UIElement icon = img != null
                    ? new Image { Source = img, Width = 28, Height = 28 }
                    : (UIElement)Ui.AppBadge(name, 28);
                var label = Ui.T(name, 11);
                label.HorizontalAlignment = HorizontalAlignment.Center;
                label.MaxWidth = 54;
                var b = new Button { Style = Ui.S("IconButton"), Width = 56, Height = Tall ? 56 : 52, ToolTip = target, Content = Ui.V(3, icon, label) };
                ((FrameworkElement)icon).HorizontalAlignment = HorizontalAlignment.Center;
                var t = target;
                b.Click += (o, e) => Open(t);
                g.Children.Add(b);
            }
            Children.Add(g);
        }

        /// <summary>"Nume = cale" or just a path / link per line.</summary>
        internal static List<(string Name, string Target)> Parse(string text)
        {
            var list = new List<(string, string)>();
            foreach (var raw in (text ?? "").Split('\n'))
            {
                var line = raw.Trim();
                if (line.Length == 0) continue;
                string name = null, target = line;
                int eq = line.IndexOf(" = ", StringComparison.Ordinal);
                if (eq > 0) { name = line.Substring(0, eq).Trim(); target = line.Substring(eq + 3).Trim().Trim('"'); }
                else target = target.Trim('"');
                if (string.IsNullOrEmpty(name))
                {
                    if (Uri.TryCreate(target, UriKind.Absolute, out var u) && (u.Scheme == "http" || u.Scheme == "https"))
                        name = u.Host.StartsWith("www.") ? u.Host.Substring(4) : u.Host;
                    else name = Path.GetFileNameWithoutExtension(target.TrimEnd('\\'));
                    if (string.IsNullOrEmpty(name)) name = target;
                }
                list.Add((name, target));
            }
            return list;
        }

        internal static void Open(string target)
        {
            try
            {
                if (Uri.TryCreate(target, UriKind.Absolute, out var u) && u.Scheme != "http" && u.Scheme != "https" && u.Scheme != "file" && u.Scheme != "ms-settings")
                    return;     // only web, files and Windows settings: no other protocol handlers
                if (Widget.IsNetworkPath(target)) return;      // \\server\share: Windows would log in there with your account
                Shell.Open(target);
            }
            catch (Exception ex) { App.Log("Scurtătură: " + ex.Message); }
        }
    }

    /// <summary>Text: a note, quote, code or reminder, at the size you choose.</summary>
    internal sealed class TextWidget : Widget
    {
        public TextWidget(NotchWindow w, WidgetSlot s) : base(w, s)
        {
            double size = double.TryParse(s.Opt("size", "14").Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) ? Math.Clamp(v, 10, 40) : 14;
            var t = new TextBlock
            {
                Text = s.Opt("text", "Scrie textul din Editor"), FontSize = size, TextWrapping = TextWrapping.Wrap,
                TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = s.Opt("align", "left") == "center" ? TextAlignment.Center : TextAlignment.Left
            };
            t.SetResourceReference(TextBlock.ForegroundProperty, s.Opt("muted", "0") == "1" ? "MutedBrush" : "InkBrush");
            if (s.Opt("bold", "0") == "1") t.FontWeight = FontWeights.SemiBold;
            if (s.Opt("mono", "0") == "1") t.FontFamily = Ui.Mono;
            Children.Add(t);
        }
    }

    /// <summary>Link web: opens a site (http/https only).</summary>
    internal sealed class LinkWidget : Widget
    {
        public LinkWidget(NotchWindow w, WidgetSlot s) : base(w, s)
        {
            string url = s.Opt("url", "");
            bool ok = Uri.TryCreate(url, UriKind.Absolute, out var u) && (u.Scheme == "http" || u.Scheme == "https");
            string title = s.Opt("title", ok ? (u.Host.StartsWith("www.") ? u.Host.Substring(4) : u.Host) : "Link");
            var g = Ui.Cols(Ui.Auto, Ui.Px(8), Ui.Star());
            g.Put(Ui.AppBadge(title, Tiny ? 26 : 30));
            if (!Tiny) g.Put(Ui.V(0, Ui.T(title, 12.5, "InkBrush", true), Ui.T(ok ? u.Host : "adaugă adresa în Editor", 11, "DimBrush")), 2);
            g.VerticalAlignment = VerticalAlignment.Center;
            if (Tiny) g.HorizontalAlignment = HorizontalAlignment.Center;
            Children.Add(g);
            if (ok) this.OnClick(() => Shell.Open(u.AbsoluteUri));
            ToolTip = ok ? u.AbsoluteUri : null;
        }
    }

    /// <summary>Comandă: runs a program or script you choose, on click.</summary>
    internal sealed class CommandWidget : Widget
    {
        public CommandWidget(NotchWindow w, WidgetSlot s) : base(w, s)
        {
            string file = s.Opt("file", "").Trim().Trim('"');
            string args = s.Opt("args", "");
            string title = s.Opt("title", string.IsNullOrEmpty(file) ? "Comandă" : Path.GetFileNameWithoutExtension(file));
            var g = Ui.Cols(Ui.Auto, Ui.Px(8), Ui.Star());
            var img = FileIcon(file);
            g.Put(img != null ? new Image { Source = img, Width = 26, Height = 26 } : (UIElement)Ui.Icon("", 18));
            if (!Tiny) g.Put(Ui.V(0, Ui.T(title, 12.5, "InkBrush", true), Ui.T(string.IsNullOrEmpty(file) ? "alege programul în Editor" : "click ca să rulezi", 11, "DimBrush")), 2);
            g.VerticalAlignment = VerticalAlignment.Center;
            Children.Add(g);
            ToolTip = file + (args.Length > 0 ? " " + args : "");
            if (string.IsNullOrEmpty(file)) return;
            this.OnClick(() =>
            {
                try
                {
                    // As administrator, run it with normal rights (through Explorer); arguments aren't possible that way.
                    if (App.IsAdmin) { Shell.Open(file); return; }
                    var psi = new ProcessStartInfo(file) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(file) ?? "" };
                    if (args.Length > 0) psi.Arguments = args;
                    Process.Start(psi);
                }
                catch (Exception ex) { App.Log("Comandă: " + ex.Message); }
            });
        }
    }
}
