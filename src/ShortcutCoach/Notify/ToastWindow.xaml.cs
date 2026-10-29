using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using static ShortcutCoach.Native.NativeMethods;

namespace ShortcutCoach.Notify;

/// <summary>
/// Small click-through toast shown near the cursor. Never takes focus.
/// One instance is reused; a new message replaces the old one. UI thread only.
/// </summary>
public partial class ToastWindow : Window
{
    private const int CursorOffset = 16;
    private static readonly TimeSpan DisplayTime = TimeSpan.FromSeconds(2.5);

    // A DispatcherTimer is like SetTimer: its Tick arrives on the UI thread.
    private readonly DispatcherTimer _hideTimer;
    private readonly IntPtr _hwnd;

    public ToastWindow()
    {
        InitializeComponent();

        _hideTimer = new DispatcherTimer { Interval = DisplayTime };
        _hideTimer.Tick += (_, _) =>
        {
            _hideTimer.Stop();
            Hide();
        };

        SourceInitialized += (_, _) => AddNoFocusStyles();

        // WindowInteropHelper exposes the raw HWND behind a WPF window.
        // EnsureHandle creates it now (firing SourceInitialized), so the styles
        // are in place before the first Show.
        _hwnd = new WindowInteropHelper(this).EnsureHandle();
    }

    /// <summary>Shows <paramref name="text"/> near the cursor and restarts the hide timer.</summary>
    public void ShowSuggestion(string text)
    {
        MessageText.Text = text;
        UpdateLayout();

        GetCursorPos(out POINT cursor);

        // Move to the cursor while still hidden, so the normal case appears in place.
        SetWindowPos(_hwnd, HWND_TOPMOST, cursor.X + CursorOffset, cursor.Y + CursorOffset, 0, 0,
            SWP_NOSIZE | SWP_NOACTIVATE);

        if (!IsVisible)
            Show(); // ShowActivated=false → SW_SHOWNA

        // After Show, so any DPI-driven resize from crossing monitors is applied,
        // and so whatever WPF does on first Show is overridden.
        PlaceNear(cursor);

        _hideTimer.Stop();
        _hideTimer.Start();
    }

    private void AddNoFocusStyles()
    {
        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        long exStyle = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
        exStyle |= WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW | WS_EX_TRANSPARENT;
        SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(exStyle));
    }

    // Below-right of the cursor; flipped to the other side if it would leave the
    // monitor's work area, then clamped. All in physical pixels.
    private void PlaceNear(POINT cursor)
    {
        GetWindowRect(_hwnd, out RECT rect);
        int width = rect.Right - rect.Left;
        int height = rect.Bottom - rect.Top;

        var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        GetMonitorInfo(MonitorFromPoint(cursor, MONITOR_DEFAULTTONEAREST), ref info);
        RECT work = info.rcWork;

        int x = cursor.X + CursorOffset;
        int y = cursor.Y + CursorOffset;
        if (x + width > work.Right)
            x = cursor.X - CursorOffset - width;
        if (y + height > work.Bottom)
            y = cursor.Y - CursorOffset - height;

        x = Math.Clamp(x, work.Left, Math.Max(work.Left, work.Right - width));
        y = Math.Clamp(y, work.Top, Math.Max(work.Top, work.Bottom - height));

        SetWindowPos(_hwnd, HWND_TOPMOST, x, y, 0, 0, SWP_NOSIZE | SWP_NOACTIVATE);
    }
}
