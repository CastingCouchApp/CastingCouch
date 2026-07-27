using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace CreatorControlSuite.App.Helpers;

internal static class NativeWindowHelper
{
    private const int WmGetMinMaxInfo = 0x0024;
    private const int MonitorDefaultToNearest = 2;

    public readonly record struct MonitorInfo(int Index, string Name, Rect BoundsDip, bool IsPrimary);

    /// <summary>
    /// Verhindert, dass ein Window mit WindowStyle="None" beim Maximieren die Taskleiste überdeckt,
    /// indem WM_GETMINMAXINFO auf den Arbeitsbereich des aktuellen Monitors begrenzt wird.
    /// </summary>
    public static void RestrictMaximizeToWorkArea(Window window)
    {
        window.SourceInitialized += (_, _) =>
        {
            nint handle = new WindowInteropHelper(window).Handle;
            HwndSource.FromHwnd(handle)?.AddHook(WindowProc);
        };
    }

    private static IntPtr WindowProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmGetMinMaxInfo)
        {
            ApplyWorkAreaToMinMaxInfo(hwnd, lParam);
            handled = true;
        }

        return IntPtr.Zero;
    }

    private static void ApplyWorkAreaToMinMaxInfo(IntPtr hwnd, IntPtr lParam)
    {
        var minMaxInfo = Marshal.PtrToStructure<MinMaxInfo>(lParam);

        IntPtr monitor = MonitorFromWindow(hwnd, MonitorDefaultToNearest);
        if (monitor != IntPtr.Zero)
        {
            var monitorInfo = new MonitorInfoNative { Size = Marshal.SizeOf<MonitorInfoNative>() };
            if (GetMonitorInfo(monitor, ref monitorInfo))
            {
                RectNative workArea = monitorInfo.Work;
                RectNative monitorArea = monitorInfo.Monitor;

                minMaxInfo.MaxPosition.X = workArea.Left - monitorArea.Left;
                minMaxInfo.MaxPosition.Y = workArea.Top - monitorArea.Top;
                minMaxInfo.MaxSize.X = workArea.Right - workArea.Left;
                minMaxInfo.MaxSize.Y = workArea.Bottom - workArea.Top;
            }
        }

        Marshal.StructureToPtr(minMaxInfo, lParam, true);
    }

    public static IReadOnlyList<MonitorInfo> GetMonitors()
    {
        var monitors = new List<MonitorInfo>();
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (hMonitor, _, _, _) =>
        {
            var info = new MonitorInfoNative { Size = Marshal.SizeOf<MonitorInfoNative>() };
            if (!GetMonitorInfo(hMonitor, ref info))
            {
                return true;
            }

            GetDpiForMonitor(hMonitor, 0, out uint dpiX, out uint dpiY);
            if (dpiX == 0)
            {
                dpiX = 96;
            }

            if (dpiY == 0)
            {
                dpiY = 96;
            }

            double left = info.Monitor.Left * 96.0 / dpiX;
            double top = info.Monitor.Top * 96.0 / dpiY;
            double width = (info.Monitor.Right - info.Monitor.Left) * 96.0 / dpiX;
            double height = (info.Monitor.Bottom - info.Monitor.Top) * 96.0 / dpiY;
            bool isPrimary = (info.Flags & 1) != 0;
            string name = string.IsNullOrWhiteSpace(info.Device)
                ? $"Monitor {monitors.Count + 1}"
                : info.Device.TrimEnd('\0');

            monitors.Add(new MonitorInfo(
                monitors.Count,
                name,
                new Rect(left, top, width, height),
                isPrimary));
            return true;
        }, IntPtr.Zero);

        if (monitors.Count == 0)
        {
            monitors.Add(new MonitorInfo(
                0,
                "Primär",
                new Rect(0, 0, SystemParameters.PrimaryScreenWidth, SystemParameters.PrimaryScreenHeight),
                true));
        }

        return monitors;
    }

    public static MonitorInfo ResolveMonitor(int monitorIndex)
    {
        IReadOnlyList<MonitorInfo> monitors = GetMonitors();
        if (monitorIndex >= 0 && monitorIndex < monitors.Count)
        {
            return monitors[monitorIndex];
        }

        foreach (MonitorInfo monitor in monitors)
        {
            if (monitor.IsPrimary)
            {
                return monitor;
            }
        }

        return monitors[0];
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, int flags);

    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc callback, IntPtr data);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MonitorInfoNative info);

    [DllImport("Shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr hMonitor, int dpiType, out uint dpiX, out uint dpiY);

    private delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdcMonitor, IntPtr lprcMonitor, IntPtr data);

    [StructLayout(LayoutKind.Sequential)]
    private struct PointNative
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MinMaxInfo
    {
        public PointNative Reserved;
        public PointNative MaxSize;
        public PointNative MaxPosition;
        public PointNative MinTrackSize;
        public PointNative MaxTrackSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RectNative
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MonitorInfoNative
    {
        public int Size;
        public RectNative Monitor;
        public RectNative Work;
        public int Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string Device;
    }
}
