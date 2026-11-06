using System.Runtime.InteropServices;
using System.Text;
using System.Drawing;

namespace MianMianPet.Infrastructure;

internal static class UserActivityHelper
{
    internal static bool IsOtherApplicationFullScreen()
    {
        var window = GetForegroundWindow();
        if (window == IntPtr.Zero || IsIconic(window)) return false;
        GetWindowThreadProcessId(window, out var processId);
        if (processId == Environment.ProcessId) return false;
        var className = new StringBuilder(256);
        GetClassName(window, className, className.Capacity);
        if (className.ToString() is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd")
            return false;

        // Exclusive Direct3D / presentation mode can hide the actual window bounds.
        if (SHQueryUserNotificationState(out var state) == 0 && state is 3 or 4) return true;
        if (!GetClientRect(window, out var client)) return false;
        var origin = new NativePoint();
        if (!ClientToScreen(window, ref origin)) return false;
        var bounds = new Rectangle(origin.X, origin.Y, client.Right - client.Left, client.Bottom - client.Top);
        var monitor = System.Windows.Forms.Screen.FromHandle(window).Bounds;
        return CoversMonitor(bounds, monitor);
    }

    // Compare physical client pixels, not the working area or an enlarged window frame.
    internal static bool CoversMonitor(Rectangle client, Rectangle monitor) =>
        client.Width > 0 && client.Height > 0 &&
        client.Left <= monitor.Left && client.Top <= monitor.Top &&
        client.Right >= monitor.Right && client.Bottom >= monitor.Bottom;

    internal static TimeSpan GetSystemIdleTime()
    {
        var info = new LastInputInfo
        {
            Size = (uint)Marshal.SizeOf<LastInputInfo>()
        };

        if (!GetLastInputInfo(ref info))
        {
            return TimeSpan.Zero;
        }

        var elapsedMilliseconds = unchecked((uint)Environment.TickCount - info.Time);
        return TimeSpan.FromMilliseconds(elapsedMilliseconds);
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetLastInputInfo(ref LastInputInfo info);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(IntPtr window);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr window, StringBuilder className, int maxCount);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetClientRect(IntPtr window, out NativeRect rect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ClientToScreen(IntPtr window, ref NativePoint point);

    [DllImport("shell32.dll")]
    private static extern int SHQueryUserNotificationState(out int state);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { internal int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint { internal int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct LastInputInfo
    {
        internal uint Size;
        internal uint Time;
    }
}
