using System.Runtime.InteropServices;
using System.Text;
using Raf.Core;

namespace Raf.App;

/// <summary>
/// O an önde duran pencere hakkında sorular (masaüstü mü, tam ekran bir uygulama mı, Raf'ın kendisi mi)
/// ve öndeki pencere değiştiğinde haber veren olay.
/// </summary>
internal static class FrontWindow
{
    private const uint EventSystemForeground = 0x0003;
    private const uint MonitorDefaultToNearest = 2;
    private const int GwlStyle = -16;
    private const long WsCaption = 0x00C00000;

    // Temsilci alanda tutulur ki çöp toplayıcı onu almasın.
    private static WinEventProc? _callback;
    private static IntPtr _hook;

    /// <summary>Öndeki pencere değişince tetiklenir (<see cref="Start"/> çağrılan iş parçacığında).</summary>
    public static event Action? Changed;

    /// <summary>İzlemeyi başlatır. İleti döngüsü olan iş parçacığından (arayüz) bir kez çağrılmalıdır.</summary>
    public static void Start()
    {
        if (_hook != IntPtr.Zero) return;
        _callback = (_, _, _, _, _, _, _) => Changed?.Invoke();
        _hook = SetWinEventHook(EventSystemForeground, EventSystemForeground, IntPtr.Zero, _callback, 0, 0, 0);
    }

    public static void Stop()
    {
        if (_hook != IntPtr.Zero) UnhookWinEvent(_hook);
        _hook = IntPtr.Zero;
    }

    /// <summary>Önde masaüstünün kendisi mi duruyor (boş masaüstüne tıklanmış ya da "masaüstünü göster" yapılmış)?</summary>
    public static bool IsDesktop()
    {
        var window = GetForegroundWindow();
        if (window == IntPtr.Zero) return false;
        return window == GetShellWindow() || ClassOf(window) is "Progman" or "WorkerW";
    }

    public static bool IsOwnProcess()
    {
        var window = GetForegroundWindow();
        if (window == IntPtr.Zero) return false;
        GetWindowThreadProcessId(window, out uint processId);
        return processId == Environment.ProcessId;
    }

    /// <summary>Öndeki pencere, başka bir uygulamaya ait tam ekran bir pencere mi (oyun, tam ekran video)?</summary>
    public static bool IsFullscreenApp()
    {
        var window = GetForegroundWindow();
        if (window == IntPtr.Zero || IsOwnProcess() || IsDesktop()) return false;

        var monitor = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (!GetWindowRect(window, out var rect) || !GetMonitorInfo(MonitorFromWindow(window, MonitorDefaultToNearest), ref monitor))
            return false;

        long style = GetWindowLongPtr(window, GwlStyle).ToInt64();
        return ScreenRules.IsFullscreen(
            (rect.Left, rect.Top, rect.Right, rect.Bottom),
            (monitor.rcMonitor.Left, monitor.rcMonitor.Top, monitor.rcMonitor.Right, monitor.rcMonitor.Bottom),
            IsZoomed(window),
            (style & WsCaption) == WsCaption);
    }

    private static string ClassOf(IntPtr window)
    {
        var name = new StringBuilder(64);
        GetClassName(window, name, name.Capacity);
        return name.ToString();
    }

    private delegate void WinEventProc(IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint thread, uint time);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr module, WinEventProc callback, uint process, uint thread, uint flags);

    [DllImport("user32.dll")]
    private static extern bool UnhookWinEvent(IntPtr hook);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern IntPtr GetShellWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hwnd, StringBuilder name, int capacity);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    [DllImport("user32.dll")]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);

    [DllImport("user32.dll")]
    private static extern bool IsZoomed(IntPtr hwnd);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
}
