using System.Runtime.InteropServices;

namespace ShortcutCoach.Native;

/// <summary>
/// Win32 declarations used by the app. Each <c>[DllImport]</c> is the C# way of
/// declaring a function prototype that lives in a DLL, like an <c>extern</c> in a C header.
/// </summary>
internal static class NativeMethods
{
    public const int WH_MOUSE_LL = 14;
    public const int WM_LBUTTONDOWN = 0x0201;
    public const uint WM_QUIT = 0x0012;
    public const uint PM_NOREMOVE = 0x0000;

    /// <summary>
    /// Signature of a low-level mouse hook callback.
    /// A <c>delegate</c> is a type-safe function pointer:
    /// C equivalent is <c>typedef LRESULT (*LowLevelMouseProc)(int, WPARAM, LPARAM);</c>
    /// </summary>
    public delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MSLLHOOKSTRUCT
    {
        public POINT pt;
        public uint mouseData;
        public uint flags;
        public uint time;
        public UIntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public UIntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public POINT pt;
        public uint lPrivate;
    }

    /// <summary>Returns &gt;0 for a message, 0 for WM_QUIT, -1 on error.</summary>
    [DllImport("user32.dll", SetLastError = true)]
    public static extern int GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool PeekMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax, uint wRemoveMsg);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool PostThreadMessage(uint idThread, uint Msg, UIntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    public static extern uint GetCurrentThreadId();

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr GetModuleHandle(string? lpModuleName);

    /// <summary>Deepest visible window at a screen point. Sends no messages to other threads' windows.</summary>
    [DllImport("user32.dll")]
    public static extern IntPtr WindowFromPoint(POINT point);

    /// <summary>
    /// Copies the window's class name into <paramref name="className"/> and returns its length.
    /// Sends no messages. The char[] is pinned and written in place, so reusing one
    /// buffer avoids any allocation (unlike a StringBuilder).
    /// </summary>
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetClassName(IntPtr hWnd, [Out] char[] className, int maxCount);
}
