using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using WinNotch.Core.Actions;

namespace WinNotch.Features.SmartClipboard
{
    /// <summary>One chip under the Clipboard widget's title: the action it starts and its short Romanian label.</summary>
    public sealed class SmartChip
    {
        public string ActionId { get; init; }
        public string Label { get; init; }
        /// <summary>The colour swatch in front of the label (only for a colour; it is the copied colour, not the theme's).</summary>
        public bool Swatch { get; init; }
    }

    /// <summary>What the actions need from the app (the notch in the app, a fake in the tests).</summary>
    public interface ISmartClipboardHost
    {
        /// <summary>
        /// The text copied last and recorded (never one marked private by a password manager, never one of another
        /// format): null when there is none, it was private, or the switch is off. Any thread.
        /// </summary>
        string CurrentText { get; }
        /// <summary>UI thread: puts the text in the clipboard as WinNotch's own (not recorded again, no peek); false if the clipboard is busy.</summary>
        bool SetText(string text);
        /// <summary>UI thread: an http(s) link, already checked, through Shell.Open; null when done, else a short Romanian reason.</summary>
        string OpenUrl(string url);
        /// <summary>UI thread: opens the folder of a local path (checked again there); null when done, else a short Romanian reason.</summary>
        string OpenFolder(string path);
    }

    /// <summary>
    /// P21 as actions ("clipboard.*", ADR 0010): each works on the text copied last, puts its result in the clipboard
    /// (or opens a link / a folder) and is available only when that text is of its kind. Safe, behind the
    /// "smart-clipboard" switch, on the UI thread (the clipboard). No parameters: the content never goes through the
    /// registry, so it can't reach its log either. Also the chips and the peek's title for a recognized text.
    /// </summary>
    public static class SmartClipboardActions
    {
        public const string FeatureId = "smart-clipboard";
        public const string Category = "Clipboard";

        public const string FormatJsonId = "clipboard.format-json", MinifyJsonId = "clipboard.minify-json", CleanUrlId = "clipboard.clean-url",
            OpenUrlId = "clipboard.open-url", DecodeJwtId = "clipboard.decode-jwt", CopyColorId = "clipboard.copy-color-rgb",
            CopyEmailId = "clipboard.copy-email", CopyIpId = "clipboard.copy-ip", CopyPhoneId = "clipboard.copy-phone", OpenFolderId = "clipboard.open-folder";

        public static readonly IReadOnlyList<string> AllIds = new[]
        {
            FormatJsonId, MinifyJsonId, CleanUrlId, OpenUrlId, DecodeJwtId, CopyColorId, CopyEmailId, CopyIpId, CopyPhoneId, OpenFolderId,
        };

        /// <summary>Segoe Fluent / MDL2 "Paste" (the Clipboard widget's glyph).</summary>
        internal const string GClipboard = "";
        private const string GCode = "", GLink = "", GKey = "", GColor = "", GMail = "", GNet = "", GFolder = "", GPhone = "";

        private const string Busy = "Clipboard-ul e folosit de altă aplicație; încearcă din nou.";

        /// <summary>The kind as a short label in front of the chips ("JSON", "Link").</summary>
        public static string KindLabel(SmartClipKind k) => k switch
        {
            SmartClipKind.Json => "JSON", SmartClipKind.Jwt => "JWT", SmartClipKind.Url => "Link", SmartClipKind.Email => "E-mail",
            SmartClipKind.Color => "Culoare", SmartClipKind.Ip => "IP", SmartClipKind.Path => "Cale", SmartClipKind.Phone => "Telefon", _ => "",
        };

        /// <summary>
        /// The chips for a text (at most 3, in this order); none for plain text. A chip that would change nothing isn't
        /// offered: "Formatează" for JSON already formatted, "Curăță" for a link without tracking parameters.
        /// </summary>
        public static IReadOnlyList<SmartChip> ChipsFor(SmartClip c)
        {
            var list = new List<SmartChip>();
            if (c == null) return list;
            switch (c.Kind)
            {
                case SmartClipKind.Json:
                    if (Lf(c.Formatted) != Lf(c.Text)) list.Add(new SmartChip { ActionId = FormatJsonId, Label = "Formatează" });
                    if (c.Minified != c.Text) list.Add(new SmartChip { ActionId = MinifyJsonId, Label = "Compactează" });
                    break;
                case SmartClipKind.Jwt: list.Add(new SmartChip { ActionId = DecodeJwtId, Label = "Decodează" }); break;
                case SmartClipKind.Url:
                    if (c.TrackingRemoved > 0) list.Add(new SmartChip { ActionId = CleanUrlId, Label = "Curăță link-ul" });
                    list.Add(new SmartChip { ActionId = OpenUrlId, Label = "Deschide" });
                    break;
                case SmartClipKind.Email: list.Add(new SmartChip { ActionId = CopyEmailId, Label = "Copiază adresa" }); break;
                case SmartClipKind.Color: list.Add(new SmartChip { ActionId = CopyColorId, Label = "Copiază rgb()", Swatch = true }); break;
                case SmartClipKind.Ip: list.Add(new SmartChip { ActionId = CopyIpId, Label = "Copiază IP-ul" }); break;
                case SmartClipKind.Path: list.Add(new SmartChip { ActionId = OpenFolderId, Label = "Deschide folderul" }); break;
                case SmartClipKind.Phone: list.Add(new SmartChip { ActionId = CopyPhoneId, Label = "Copiază numărul" }); break;
            }
            return list;
        }

        /// <summary>
        /// The peek's title when you copy (only with the option on and the Activity Manager on): a fixed text per kind,
        /// never the content. Only where the chip saves work: JSON to format, a link with tracking, a JWT. Else null.
        /// </summary>
        public static string PeekTitle(SmartClip c)
        {
            if (c == null) return null;
            var chips = ChipsFor(c);
            return c.Kind switch
            {
                SmartClipKind.Json when chips.Any(x => x.ActionId == FormatJsonId) => "JSON copiat · Formatează",
                SmartClipKind.Url when c.TrackingRemoved > 0 => "Link cu urmărire copiat · Curăță",
                SmartClipKind.Jwt => "Token JWT copiat · Decodează",
                _ => null,
            };
        }

        private static string Lf(string s) => (s ?? "").Replace("\r\n", "\n");

        // ------------------------------------------------------------------ the actions

        /// <param name="cache">The notch's own cache (R1: a text is analyzed once for the widget and the actions); null = a new one.</param>
        public static IReadOnlyList<ActionDescriptor> Create(ISmartClipboardHost host, SmartClipCache cache = null)
        {
            if (host == null) throw new ArgumentNullException(nameof(host));
            cache ??= new SmartClipCache();
            SmartClip Current() => cache.Get(host.CurrentText);

            ActionDescriptor Make(string id, string title, SmartClipKind kind, string icon, string unavailable, string[] aliases,
                                  Func<SmartClip, bool> when, Func<SmartClip, ActionResult> run) =>
                new ActionDescriptor(id, title, (args, ct) =>
                {
                    var c = Current();                        // read again on the UI thread: a newer copy may have come
                    if (c.Kind != kind || !when(c)) return Task.FromResult(ActionResult.Failed(unavailable));
                    return Task.FromResult(run(c));
                }, () => { var c = Current(); return c.Kind == kind && when(c); })
                {
                    Aliases = aliases, Category = Category, Icon = icon, FeatureId = FeatureId, RequiresUiThread = true, UnavailableMessage = unavailable,
                };

            ActionResult Put(string text, string ok) => host.SetText(text) ? ActionResult.Ok(ok) : ActionResult.Failed(Busy);

            return new[]
            {
                Make(FormatJsonId, "Smart Clipboard: formatează JSON-ul copiat", SmartClipKind.Json, GCode, "Nu ai copiat un JSON de formatat.",
                     new[] { "formatează json", "json frumos", "indentează", "format json", "pretty print", "beautify" },
                     c => Lf(c.Formatted) != Lf(c.Text), c => Put(c.Formatted, "JSON formatat, în clipboard")),
                Make(MinifyJsonId, "Smart Clipboard: compactează JSON-ul copiat", SmartClipKind.Json, GCode, "Nu ai copiat un JSON de compactat.",
                     new[] { "compactează json", "json pe un rând", "minify json", "compact json" },
                     c => c.Minified != c.Text, c => Put(c.Minified, "JSON compactat, în clipboard")),
                Make(CleanUrlId, "Smart Clipboard: curăță link-ul de parametrii de urmărire", SmartClipKind.Url, GLink, "Link-ul copiat nu are parametri de urmărire.",
                     new[] { "curăță link", "scoate utm", "fără urmărire", "clean url", "remove tracking", "strip utm" },
                     c => c.TrackingRemoved > 0,
                     c => Put(c.Normalized, c.TrackingRemoved == 1 ? "Link curățat (un parametru de urmărire scos)" : "Link curățat (" + c.TrackingRemoved + " parametri de urmărire scoși)")),
                Make(OpenUrlId, "Smart Clipboard: deschide link-ul copiat", SmartClipKind.Url, GLink, "Nu ai copiat un link http sau https.",
                     new[] { "deschide link", "deschide în browser", "open link", "open url" },
                     c => true, c =>
                     {
                         if (!SmartClipRecognizer.IsUrl(c.Text)) return ActionResult.Failed("Nu ai copiat un link http sau https.");     // checked again, right before
                         string why = host.OpenUrl(c.Text);
                         return why == null ? ActionResult.Ok("Link deschis în browser") : ActionResult.Failed(why);
                     }),
                Make(DecodeJwtId, "Smart Clipboard: decodează token-ul JWT copiat (local)", SmartClipKind.Jwt, GKey, "Nu ai copiat un token JWT.",
                     new[] { "decodează jwt", "token", "jwt decode", "decode token" },
                     c => true, c => Put(c.Normalized, "Antetul și conținutul token-ului, în clipboard (semnătura nu e verificată)")),
                Make(CopyColorId, "Smart Clipboard: copiază culoarea ca rgb()", SmartClipKind.Color, GColor, "Nu ai copiat o culoare hex (#RRGGBB).",
                     new[] { "culoare rgb", "hex în rgb", "color rgb", "hex to rgb" },
                     c => true, c => Put(SmartClipRecognizer.Rgb(c), "Culoarea ca rgb(), în clipboard")),
                Make(CopyEmailId, "Smart Clipboard: copiază adresa de e-mail curățată", SmartClipKind.Email, GMail, "Nu ai copiat o adresă de e-mail.",
                     new[] { "adresă email", "copiază emailul", "copy email" },
                     c => true, c => Put(c.Normalized, "Adresa, în clipboard")),
                Make(CopyIpId, "Smart Clipboard: copiază adresa IP", SmartClipKind.Ip, GNet, "Nu ai copiat o adresă IP.",
                     new[] { "adresă ip", "copiază ip", "copy ip" },
                     c => true, c => Put(c.Normalized, "Adresa IP, în clipboard")),
                Make(CopyPhoneId, "Smart Clipboard: copiază numărul de telefon (doar cifre)", SmartClipKind.Phone, GPhone, "Nu ai copiat un număr de telefon.",
                     new[] { "număr de telefon", "copiază telefonul", "copy phone" },
                     c => true, c => Put(c.Normalized, "Numărul, în clipboard")),
                Make(OpenFolderId, "Smart Clipboard: deschide folderul căii copiate", SmartClipKind.Path, GFolder, "Nu ai copiat o cale de pe acest PC.",
                     new[] { "deschide folderul", "calea copiată", "open folder", "show in explorer" },
                     c => true, c =>
                     {
                         if (SmartClipRecognizer.IsNetworkPath(c.Normalized) || !SmartClipRecognizer.TryPath(c.Normalized, out var p)) return ActionResult.Failed("Nu ai copiat o cale de pe acest PC.");
                         string why = host.OpenFolder(p);
                         return why == null ? ActionResult.Ok("Folder deschis") : ActionResult.Failed(why);
                     }),
            };
        }

        /// <summary>Registered once at startup, from App.RegisterActions.</summary>
        public static void Register(ActionRegistry registry, ISmartClipboardHost host, SmartClipCache cache = null)
        {
            foreach (var a in Create(host, cache)) registry.Register(a);
        }
    }
}
