using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Aion2Meter.App;

/// <summary>Win32 calls WPF does not wrap (like JNI, but declared inline).</summary>
internal static partial class Native
{
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x20, WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x08000000;
    public const int WM_HOTKEY = 0x0312;
    public const uint MOD_ALT = 0x1, MOD_CONTROL = 0x2, MOD_NOREPEAT = 0x4000;

    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static partial nint GetWindowLongPtr(nint hwnd, int index);

    [LibraryImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static partial nint SetWindowLongPtr(nint hwnd, int index, nint value);

    [LibraryImport("user32.dll")]
    private static partial nint GetForegroundWindow();

    [LibraryImport("user32.dll")]
    private static partial uint GetWindowThreadProcessId(nint hwnd, out uint processId);

    /// <summary>The process id of the window the user is working in now (0: none).</summary>
    public static uint ForegroundProcess()
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == 0) return 0;
        GetWindowThreadProcessId(hwnd, out uint pid);
        return pid;
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool RegisterHotKey(nint hwnd, int id, uint modifiers, uint key);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool UnregisterHotKey(nint hwnd, int id);

    /// <summary>Overlay windows: never take focus from the game and stay out of Alt+Tab.</summary>
    public static void MakeOverlay(Window w)
    {
        nint h = new WindowInteropHelper(w).EnsureHandle();
        SetWindowLongPtr(h, GWL_EXSTYLE, GetWindowLongPtr(h, GWL_EXSTYLE) | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
    }

    /// <summary>Mouse clicks go through the window to the game below.</summary>
    public static void SetClickThrough(Window w, bool on)
    {
        nint h = new WindowInteropHelper(w).EnsureHandle();
        nint style = GetWindowLongPtr(h, GWL_EXSTYLE);
        SetWindowLongPtr(h, GWL_EXSTYLE, on ? style | WS_EX_TRANSPARENT : style & ~(nint)WS_EX_TRANSPARENT);
    }
}
