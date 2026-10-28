using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading.Channels;
using ShortcutCoach.Native;

namespace ShortcutCoach.Input;

/// <summary>
/// System-wide low-level mouse hook. On every left-button press it queues the
/// screen point into a channel and returns immediately; all slow work happens
/// on whoever reads the channel.
/// </summary>
/// <remarks>
/// Must be created on a thread that pumps messages (the WPF UI thread does),
/// because Windows delivers the callback through that thread's message loop.
/// </remarks>
internal sealed class MouseHook : IDisposable
{
    private readonly ChannelWriter<Point> _clicks;

    // Kept in a field so the GC can't collect it while Windows still holds the
    // pointer. A local variable here would crash the app once the GC runs.
    private readonly NativeMethods.LowLevelMouseProc _callback;

    private IntPtr _hookHandle;
    private long _clickCount;
    private long _maxCallbackTicks;

    public MouseHook(ChannelWriter<Point> clicks)
    {
        _clicks = clicks;
        _callback = HookCallback;

        _hookHandle = NativeMethods.SetWindowsHookEx(
            NativeMethods.WH_MOUSE_LL, _callback, NativeMethods.GetModuleHandle(null), 0);

        if (_hookHandle == IntPtr.Zero)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "SetWindowsHookEx failed");
    }

    /// <summary>Total left clicks seen since the hook was installed.</summary>
    public long ClickCount => Interlocked.Read(ref _clickCount);

    /// <summary>Slowest click-handling time so far, in microseconds.</summary>
    public double MaxCallbackMicroseconds =>
        Interlocked.Read(ref _maxCallbackTicks) * 1_000_000.0 / Stopwatch.Frequency;

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        // Keep this path tiny: Windows silently removes hooks that are too slow.
        if (nCode >= 0 && wParam == NativeMethods.WM_LBUTTONDOWN)
        {
            long start = Stopwatch.GetTimestamp();

            var info = Marshal.PtrToStructure<NativeMethods.MSLLHOOKSTRUCT>(lParam);
            _clicks.TryWrite(new Point(info.pt.X, info.pt.Y));
            Interlocked.Increment(ref _clickCount);

            // Only this thread writes the max, so a plain compare is safe.
            long elapsed = Stopwatch.GetTimestamp() - start;
            if (elapsed > _maxCallbackTicks)
                Interlocked.Exchange(ref _maxCallbackTicks, elapsed);
        }

        return NativeMethods.CallNextHookEx(_hookHandle, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        if (_hookHandle != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_hookHandle);
            _hookHandle = IntPtr.Zero;
        }
    }
}
