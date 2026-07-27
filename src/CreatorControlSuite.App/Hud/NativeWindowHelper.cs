using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace CreatorControlSuite.App.Hud;

internal static class NativeWindowHelper
{
    private const uint WdaExcludeFromCapture = 0x11;
    private const int GwlExstyle = -20;
    private const int WsExTransparent = 0x00000020;
    private const int WsExLayered = 0x00080000;
    private const int WsExToolwindow = 0x00000080;
    private const int WsExNoactivate = 0x08000000;

    public readonly record struct MonitorInfo(int Index, string Name, Rect BoundsDip, bool IsPrimary);

    public static void ExcludeFromCapture(Window window)
    {
        nint hwnd = new WindowInteropHelper(window).EnsureHandle();
        SetWindowDisplayAffinity(hwnd, WdaExcludeFromCapture);
    }

    public static void ApplyToolWindowStyles(Window window)
    {
        nint hwnd = new WindowInteropHelper(window).EnsureHandle();
        long style = GetWindowLongPtr(hwnd, GwlExstyle).ToInt64();
        style |= WsExToolwindow | WsExNoactivate | WsExLayered;
        SetWindowLongPtr(hwnd, GwlExstyle, new IntPtr(style));
    }

    public static void SetClickThrough(Window window, bool enabled)
    {
        nint hwnd = new WindowInteropHelper(window).EnsureHandle();
        long style = GetWindowLongPtr(hwnd, GwlExstyle).ToInt64();
        if (enabled)
        {
            style |= WsExTransparent | WsExLayered;
        }
        else
        {
            style &= ~WsExTransparent;
        }

        SetWindowLongPtr(hwnd, GwlExstyle, new IntPtr(style));
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
    private static extern bool SetWindowDisplayAffinity(IntPtr hwnd, uint affinity);

    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc callback, IntPtr data);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MonitorInfoNative info);

    [DllImport("Shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr hMonitor, int dpiType, out uint dpiX, out uint dpiY);

    private static IntPtr GetWindowLongPtr(IntPtr hwnd, int index)
        => IntPtr.Size == 8 ? GetWindowLongPtr64(hwnd, index) : new IntPtr(GetWindowLong32(hwnd, index));

    private static IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value)
        => IntPtr.Size == 8 ? SetWindowLongPtr64(hwnd, index, value) : new IntPtr(SetWindowLong32(hwnd, index, value.ToInt32()));

    [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
    private static extern int GetWindowLong32(IntPtr hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
    private static extern IntPtr GetWindowLongPtr64(IntPtr hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLong")]
    private static extern int SetWindowLong32(IntPtr hwnd, int index, int value);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
    private static extern IntPtr SetWindowLongPtr64(IntPtr hwnd, int index, IntPtr value);

    private delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdcMonitor, IntPtr lprcMonitor, IntPtr data);

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
