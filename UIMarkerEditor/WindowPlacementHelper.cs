using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace UIMarkerEditor;

internal static class WindowPlacementHelper
{
    private const uint MonitorDefaultToNearest = 2;

    public static void ApplySavedBounds(Window window, Rect savedBounds)
    {
        ArgumentNullException.ThrowIfNull(window);
        if (!IsFinitePositive(savedBounds.Width) || !IsFinitePositive(savedBounds.Height))
        {
            return;
        }

        window.WindowStartupLocation = WindowStartupLocation.Manual;
        ApplyBounds(window, savedBounds);

        if (new WindowInteropHelper(window).Handle != IntPtr.Zero)
        {
            ConstrainWindowToCurrentWorkArea(window, savedBounds);
            return;
        }

        EventHandler? sourceInitializedHandler = null;
        sourceInitializedHandler = (_, _) =>
        {
            window.SourceInitialized -= sourceInitializedHandler;
            ConstrainWindowToCurrentWorkArea(window, savedBounds);
        };
        window.SourceInitialized += sourceInitializedHandler;
    }

    public static void ConstrainToCurrentWorkArea(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        Rect currentBounds = new(window.Left, window.Top, window.Width, window.Height);
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        ConstrainWindowToCurrentWorkArea(window, currentBounds);
    }

    internal static Rect ConstrainToWorkArea(
        Rect bounds,
        Rect workArea,
        double minWidth,
        double minHeight)
    {
        if (!IsFinitePositive(workArea.Width) || !IsFinitePositive(workArea.Height))
        {
            return bounds;
        }

        double width = Math.Min(
            Math.Max(IsFinitePositive(bounds.Width) ? bounds.Width : minWidth, Math.Max(0, minWidth)),
            workArea.Width);
        double height = Math.Min(
            Math.Max(IsFinitePositive(bounds.Height) ? bounds.Height : minHeight, Math.Max(0, minHeight)),
            workArea.Height);
        double left = double.IsFinite(bounds.Left)
            ? Math.Clamp(bounds.Left, workArea.Left, workArea.Right - width)
            : workArea.Left + (workArea.Width - width) / 2;
        double top = double.IsFinite(bounds.Top)
            ? Math.Clamp(bounds.Top, workArea.Top, workArea.Bottom - height)
            : workArea.Top + (workArea.Height - height) / 2;
        return new Rect(left, top, width, height);
    }

    private static void ConstrainWindowToCurrentWorkArea(Window window, Rect savedBounds)
    {
        Rect workArea = GetCurrentMonitorWorkArea(window);
        Rect constrainedBounds = ConstrainToWorkArea(
            savedBounds,
            workArea,
            window.MinWidth,
            window.MinHeight);
        WindowState originalState = window.WindowState;
        if (originalState != WindowState.Normal)
        {
            window.WindowState = WindowState.Normal;
        }

        ApplyBounds(window, constrainedBounds);
        if (originalState != WindowState.Normal)
        {
            window.WindowState = originalState;
        }
    }

    private static Rect GetCurrentMonitorWorkArea(Window window)
    {
        IntPtr windowHandle = new WindowInteropHelper(window).Handle;
        IntPtr monitor = MonitorFromWindow(windowHandle, MonitorDefaultToNearest);
        MonitorInfo monitorInfo = new()
        {
            Size = Marshal.SizeOf<MonitorInfo>()
        };
        if (monitor == IntPtr.Zero || !GetMonitorInfo(monitor, ref monitorInfo))
        {
            return SystemParameters.WorkArea;
        }

        HwndSource? source = PresentationSource.FromVisual(window) as HwndSource;
        Matrix fromDevice = source?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
        Point topLeft = fromDevice.Transform(new Point(monitorInfo.WorkArea.Left, monitorInfo.WorkArea.Top));
        Point bottomRight = fromDevice.Transform(new Point(monitorInfo.WorkArea.Right, monitorInfo.WorkArea.Bottom));
        return new Rect(topLeft, bottomRight);
    }

    private static void ApplyBounds(Window window, Rect bounds)
    {
        window.Left = bounds.Left;
        window.Top = bounds.Top;
        window.Width = bounds.Width;
        window.Height = bounds.Height;
    }

    private static bool IsFinitePositive(double value)
    {
        return double.IsFinite(value) && value > 0;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr windowHandle, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo monitorInfo);

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect MonitorArea;
        public NativeRect WorkArea;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}
