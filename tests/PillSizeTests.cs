using System;
using WinNotch.Core.Ui;

namespace WinNotch
{
    public static partial class T
    {
        /// <summary>
        /// Reported by the author on a 1440p monitor: the time and the date in standby were barely readable. Only the
        /// open panel and the alerts followed the window's scale; the pill kept its 34 px and its 11 px text. These
        /// check the rule that fixes it, and the hooks that use it.
        /// </summary>
        static void PillSizeTests()
        {
            Check("PS1", "Pastila de standby crește cu scara ferestrei (34 px la 100%, mai mare pe 1440p și 4K)",
                  PillSize.Height(1, false) == 34 && Math.Abs(PillSize.Height(1.2, false) - 40.8) < 0.001 &&
                  Math.Abs(PillSize.Height(1.35, false) - 45.9) < 0.001);

            Check("PS2", "Forma mică crește la fel (22 px la 100%)",
                  PillSize.Height(1, true) == 22 && Math.Abs(PillSize.Height(1.2, true) - 26.4) < 0.001 &&
                  PillSize.Height(1, true) < PillSize.Height(1, false));

            Check("PS3", "Raza rămâne jumătate din înălțime la orice scară (pastila e o capsulă)",
                  Math.Abs(PillSize.Radius(1, false) * 2 - PillSize.Height(1, false)) < 0.001 &&
                  Math.Abs(PillSize.Radius(1.35, false) * 2 - PillSize.Height(1.35, false)) < 0.001 &&
                  Math.Abs(PillSize.Radius(1.2, true) * 2 - PillSize.Height(1.2, true)) < 0.001);

            Check("PS4", "Scara nu coboară sub 1 și nu trece de limita ferestrei (1,75)",
                  PillSize.Scale(0) == 1 && PillSize.Scale(-3) == 1 && PillSize.Scale(0.5) == 1 &&
                  PillSize.Scale(1.2) == 1.2 && PillSize.Scale(9) == 1.75);

            Check("PS5", "Lățimea e cea măsurată (nescalată, fiindcă scara e o transformare) înmulțită cu scara",
                  Math.Abs(PillSize.Width(300, 1.2) - 360) < 0.001 && PillSize.Width(300, 1) == 300 &&
                  PillSize.Width(-10, 1.2) == 0);

            string notch = Src("NotchWindow.xaml.cs"), xaml = Src("NotchWindow.xaml");
            Check("PS6", "Notch-ul folosește regula, iar straturile de standby au scara lor (ca panoul deschis și alertele)",
                  notch.Contains("Core.Ui.PillSize.Height(UiScale, mini)") &&
                  notch.Contains("Core.Ui.PillSize.Radius(UiScale, mini)") &&
                  notch.Contains("Core.Ui.PillSize.Width(MiniWidth(), UiScale)") &&
                  notch.Contains("IdleScale.ScaleX = IdleScale.ScaleY = k;") &&
                  notch.Contains("MiniScale.ScaleX = MiniScale.ScaleY = k;") &&
                  xaml.Contains("x:Name=\"IdleScale\"") && xaml.Contains("x:Name=\"MiniScale\""));

            Check("PS7", "Nicio înălțime de pastilă scrisă de mână în ApplyMode (de acolo venea problema)",
                  !System.Text.RegularExpressions.Regex.IsMatch(Norm(notch), @"h = 34;|h = 22;|r = 17;|r = 11;"));
        }
    }
}
