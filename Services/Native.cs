using System;
using System.Runtime.InteropServices;
using System.Text;

namespace WinNotch.Services
{
    /// <summary>Win32 calls used by the notch (window styles, monitors, z-order, stats).</summary>
    public static class Native
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct POINT { public int X; public int Y; }

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left, Top, Right, Bottom;
            public int Width => Right - Left;
            public int Height => Bottom - Top;
            public bool Contains(int x, int y) => x >= Left && x < Right && y >= Top && y < Bottom;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        public struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        public struct MEMORYSTATUSEX
        {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct SYSTEM_POWER_STATUS
        {
            public byte ACLineStatus;
            public byte BatteryFlag;
            public byte BatteryLifePercent;
            public byte SystemStatusFlag;
            public int BatteryLifeTime;
            public int BatteryFullLifeTime;
        }

        public const int GWL_EXSTYLE = -20;
        public const long WS_EX_TRANSPARENT = 0x00000020;
        public const long WS_EX_TOOLWINDOW = 0x00000080;
        public const long WS_EX_LAYERED = 0x00080000;
        public const long WS_EX_NOACTIVATE = 0x08000000;

        public const uint MONITOR_DEFAULTTONEAREST = 2;
        public const uint SWP_NOSIZE = 0x0001;
        public const uint SWP_NOMOVE = 0x0002;
        public const uint SWP_NOACTIVATE = 0x0010;
        public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);

        public const int WM_HOTKEY = 0x0312;
        public const int WM_CLIPBOARDUPDATE = 0x031D;
        public const uint MOD_ALT = 0x0001;
        public const uint MOD_WIN = 0x0008;
        public const uint MOD_NOREPEAT = 0x4000;

        public const int DWMWA_CLOAKED = 14;

        public delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdc, ref RECT rect, IntPtr data);
        public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT pt);
        [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int vKey);
        [DllImport("user32.dll")] public static extern IntPtr MonitorFromPoint(POINT pt, uint flags);
        [DllImport("user32.dll")] public static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
        [DllImport("user32.dll", CharSet = CharSet.Auto)] public static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO info);
        [DllImport("user32.dll")] public static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc proc, IntPtr data);
        [DllImport("shcore.dll")] public static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);

        [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc proc, IntPtr lParam);
        [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern bool IsZoomed(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(IntPtr hWnd, StringBuilder name, int max);
        [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, out int value, int size);

        [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int index);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int index, IntPtr value);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetWindowLong32(IntPtr hWnd, int index);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongW")] private static extern int SetWindowLong32(IntPtr hWnd, int index, int value);

        // ---- window management (workspaces, active-window actions) ----
        public const long WS_EX_TOPMOST = 0x00000008;
        public static readonly IntPtr HWND_NOTOPMOST = new IntPtr(-2);
        public const uint SWP_NOZORDER = 0x0004;
        public const int SW_RESTORE = 9;
        public const int SW_MAXIMIZE = 3;
        public const int SW_SHOWNOACTIVATE = 4;

        [StructLayout(LayoutKind.Sequential)]
        public struct WINDOWPLACEMENT
        {
            public int length;
            public int flags;
            public int showCmd;
            public POINT ptMinPosition;
            public POINT ptMaxPosition;
            public RECT rcNormalPosition;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct LASTINPUTINFO { public uint cbSize; public uint dwTime; }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct DEVMODE
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
            public short dmSpecVersion, dmDriverVersion, dmSize, dmDriverExtra;
            public int dmFields;
            public int dmPositionX, dmPositionY;
            public int dmDisplayOrientation, dmDisplayFixedOutput;
            public short dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
            public short dmLogPixels;
            public int dmBitsPerPel, dmPelsWidth, dmPelsHeight, dmDisplayFlags, dmDisplayFrequency;
            public int dmICMMethod, dmICMIntent, dmMediaType, dmDitherType, dmReserved1, dmReserved2, dmPanningWidth, dmPanningHeight;
        }

        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern bool BringWindowToTop(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool attach);
        [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();

        /// <summary>
        /// Brings our window to the front even when another app has the focus (Windows normally only lets the app in
        /// front do that): joins that app's input queue for a moment.
        /// </summary>
        public static void ForceForeground(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return;
            try
            {
                var fg = GetForegroundWindow();
                uint fgThread = GetWindowThreadProcessId(fg, out _), me = GetCurrentThreadId();
                bool attach = fg != IntPtr.Zero && fgThread != me && fgThread != 0;
                if (attach) AttachThreadInput(me, fgThread, true);
                BringWindowToTop(hwnd);
                SetForegroundWindow(hwnd);
                if (attach) AttachThreadInput(me, fgThread, false);
            }
            catch { }
        }
        [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int cmd);
        [DllImport("user32.dll")] public static extern bool GetWindowPlacement(IntPtr hWnd, ref WINDOWPLACEMENT wp);
        [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
        [DllImport("user32.dll")] public static extern bool GetLastInputInfo(ref LASTINPUTINFO info);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern bool EnumDisplaySettings(string device, int mode, ref DEVMODE dm);
        [DllImport("kernel32.dll")] public static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
        [DllImport("kernel32.dll")] public static extern bool CloseHandle(IntPtr h);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] public static extern bool QueryFullProcessImageName(IntPtr h, int flags, StringBuilder name, ref int size);

        public static string ProcessPath(IntPtr hwnd)
        {
            GetWindowThreadProcessId(hwnd, out uint pid);
            return ProcessPathFromPid(pid);
        }

        public static string ProcessPathFromPid(uint pid)
        {
            IntPtr h = OpenProcess(0x1000 /* PROCESS_QUERY_LIMITED_INFORMATION */, false, pid);
            if (h == IntPtr.Zero) return null;
            try
            {
                var sb = new StringBuilder(1024);
                int size = sb.Capacity;
                return QueryFullProcessImageName(h, 0, sb, ref size) ? sb.ToString() : null;
            }
            finally { CloseHandle(h); }
        }

        /// <summary>Seconds since the last keyboard or mouse input.</summary>
        public static double IdleSeconds()
        {
            var li = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf(typeof(LASTINPUTINFO)) };
            if (!GetLastInputInfo(ref li)) return 0;
            return unchecked((uint)Environment.TickCount - li.dwTime) / 1000.0;
        }

        [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint mods, uint vk);
        [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr hWnd, int id);
        [DllImport("user32.dll")] public static extern bool AddClipboardFormatListener(IntPtr hwnd);
        [DllImport("user32.dll")] public static extern bool RemoveClipboardFormatListener(IntPtr hwnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowTextLength(IntPtr hWnd);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int max);

        public static string Title(IntPtr h)
        {
            var sb = new StringBuilder(256);
            GetWindowText(h, sb, sb.Capacity);
            return sb.ToString();
        }

        [DllImport("kernel32.dll")] public static extern bool GetSystemTimes(out long idle, out long kernel, out long user);
        [DllImport("kernel32.dll", CharSet = CharSet.Auto)] public static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX status);
        [DllImport("kernel32.dll")] public static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS status);

        public static long GetExStyle(IntPtr h) =>
            IntPtr.Size == 8 ? GetWindowLongPtr64(h, GWL_EXSTYLE).ToInt64() : GetWindowLong32(h, GWL_EXSTYLE);

        public static void SetExStyle(IntPtr h, long style)
        {
            if (IntPtr.Size == 8) SetWindowLongPtr64(h, GWL_EXSTYLE, new IntPtr(style));
            else SetWindowLong32(h, GWL_EXSTYLE, (int)style);
        }

        public static string ClassName(IntPtr h)
        {
            var sb = new StringBuilder(256);
            GetClassName(h, sb, sb.Capacity);
            return sb.ToString();
        }

        public static bool IsCloaked(IntPtr h)
        {
            try { return DwmGetWindowAttribute(h, DWMWA_CLOAKED, out int c, sizeof(int)) == 0 && c != 0; }
            catch { return false; }
        }

        public static double ScaleForMonitor(IntPtr hMon)
        {
            try
            {
                if (GetDpiForMonitor(hMon, 0, out uint dx, out _) == 0 && dx > 0) return dx / 96.0;
            }
            catch { }
            return 1.0;
        }
    }
}
