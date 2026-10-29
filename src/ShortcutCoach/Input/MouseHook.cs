using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Threading.Channels;
using ShortcutCoach.Core;
using ShortcutCoach.Native;

namespace ShortcutCoach.Input;

/// <summary>
/// System-wide low-level mouse hook. On every left-button press it queues the
/// screen point into a channel and returns immediately; all slow work happens
/// on whoever reads the channel.
/// </summary>
/// <remarks>
/// Windows delivers the callback through the message loop of the thread that
/// installed the hook. That thread is ours alone, so a busy UI thread can never
/// delay a click past Windows' hook timeout.
/// </remarks>
internal sealed class MouseHook : IDisposable
{
    private const int MetricWindow = 100;

    // Chromium (Chrome, Edge, Electron) renders page content into a child window
    // of this class; its own toolbars and menus use other windows.
    private const string WebContentClass = "Chrome_RenderWidgetHostHWND";

    private readonly ChannelWriter<ClickPoint> _clicks;

    // Reused by every callback so reading a class name never allocates.
    // Only touched on the hook thread.
    private readonly char[] _classBuffer = new char[64];

    // Kept in a field so the GC can't collect it while Windows still holds the
    // pointer. A local variable here would crash the app once the GC runs.
    private readonly NativeMethods.LowLevelMouseProc _callback;

    private readonly Thread _thread;
    private uint _threadId;

    // Only touched on the hook thread; the latest Max is published to the
    // fields below for other threads to read.
    private readonly RollingMax _callbackTicks = new(MetricWindow);
    private readonly RollingMax _lagMs = new(MetricWindow);

    private long _clickCount;
    private long _maxCallbackTicks;
    private long _maxLagMs;

    public MouseHook(ChannelWriter<ClickPoint> clicks)
    {
        _clicks = clicks;
        _callback = HookCallback;

        // Start the hook thread and wait until the hook is installed, so a
        // failure still surfaces here as an exception from the constructor.
        using var ready = new ManualResetEventSlim();
        Exception? installError = null;

        // A lambda that uses local variables, like a C callback plus a context
        // struct holding pointers to `ready` and `installError`.
        _thread = new Thread(() => Run(ready, e => installError = e))
        {
            IsBackground = true,
            Name = "MouseHook",
        };
        _thread.Start();
        ready.Wait();

        if (installError != null)
            ExceptionDispatchInfo.Throw(installError); // rethrow, keeping the original stack trace
    }

    /// <summary>Total left clicks seen since the hook was installed.</summary>
    public long ClickCount => Interlocked.Read(ref _clickCount);

    /// <summary>Slowest callback over the last 100 clicks, in microseconds.</summary>
    public double MaxCallbackMicroseconds =>
        Interlocked.Read(ref _maxCallbackTicks) * 1_000_000.0 / Stopwatch.Frequency;

    /// <summary>
    /// Worst delay over the last 100 clicks between the click happening and our
    /// callback seeing it, in ms (~16 ms resolution). Includes any time the event
    /// waited for this thread, which is what Windows' hook timeout counts.
    /// </summary>
    public long MaxLagMilliseconds => Interlocked.Read(ref _maxLagMs);

    private void Run(ManualResetEventSlim ready, Action<Exception> reportInstallError)
    {
        _threadId = NativeMethods.GetCurrentThreadId();

        // Force this thread's message queue to exist now, so Dispose's
        // PostThreadMessage(WM_QUIT) can never be lost.
        NativeMethods.PeekMessage(out _, IntPtr.Zero, 0, 0, NativeMethods.PM_NOREMOVE);

        // The first P/Invoke call pays a one-off setup cost (~10 ms); pay it now
        // rather than inside the first real callback.
        IsWebContentAt(default);

        IntPtr hook = NativeMethods.SetWindowsHookEx(
            NativeMethods.WH_MOUSE_LL, _callback, NativeMethods.GetModuleHandle(null), 0);

        if (hook == IntPtr.Zero)
        {
            reportInstallError(new Win32Exception(Marshal.GetLastWin32Error(), "SetWindowsHookEx failed"));
            ready.Set();
            return;
        }

        ready.Set();

        // Plain Win32 message loop. There are no windows on this thread, so there
        // is nothing to dispatch: GetMessage just lets Windows run the hook
        // callback. Returns 0 on WM_QUIT, -1 on error.
        while (NativeMethods.GetMessage(out _, IntPtr.Zero, 0, 0) > 0)
        {
        }

        NativeMethods.UnhookWindowsHookEx(hook);
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        // Keep this path tiny: Windows silently removes hooks that are too slow.
        if (nCode >= 0 && wParam == NativeMethods.WM_LBUTTONDOWN)
        {
            long start = Stopwatch.GetTimestamp();

            var info = Marshal.PtrToStructure<NativeMethods.MSLLHOOKSTRUCT>(lParam);

            // Read here, at mouse-down, before a page navigates or a menu closes.
            // Safe in the callback: neither call sends a window message, and the
            // cost shows up in MaxCallbackMicroseconds below.
            bool isWebContent = IsWebContentAt(info.pt);
            _clicks.TryWrite(new ClickPoint(new Point(info.pt.X, info.pt.Y), isWebContent));
            Interlocked.Increment(ref _clickCount);

            // info.time and TickCount share the same millisecond clock; the
            // unchecked subtraction handles its 49-day wrap-around.
            int lag = unchecked((int)((uint)Environment.TickCount - info.time));
            _lagMs.Add(Math.Max(lag, 0));
            Interlocked.Exchange(ref _maxLagMs, _lagMs.Max);

            _callbackTicks.Add(Stopwatch.GetTimestamp() - start);
            Interlocked.Exchange(ref _maxCallbackTicks, _callbackTicks.Max);
        }

        return NativeMethods.CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
    }

    private bool IsWebContentAt(NativeMethods.POINT pt)
    {
        IntPtr hwnd = NativeMethods.WindowFromPoint(pt);
        if (hwnd == IntPtr.Zero)
            return false;

        int length = NativeMethods.GetClassName(hwnd, _classBuffer, _classBuffer.Length);

        // A span is a pointer + length view into the buffer, like comparing
        // with memcmp instead of building a new string.
        return _classBuffer.AsSpan(0, length).SequenceEqual(WebContentClass);
    }

    public void Dispose()
    {
        if (_thread.IsAlive)
        {
            NativeMethods.PostThreadMessage(_threadId, NativeMethods.WM_QUIT, UIntPtr.Zero, IntPtr.Zero);
            _thread.Join(TimeSpan.FromSeconds(1));
        }
    }
}
