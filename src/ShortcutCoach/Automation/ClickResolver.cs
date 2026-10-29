using System.Diagnostics;
using System.Threading.Channels;
using FlaUI.UIA3;
using ShortcutCoach.Core;
using Point = System.Drawing.Point;

namespace ShortcutCoach.Automation;

/// <summary>Lifecycle of the resolver's worker loop, for display.</summary>
/// <remarks>Like a C <c>enum</c>, but the names live inside the type: <c>ResolverStatus.Running</c>.</remarks>
internal enum ResolverStatus
{
    Starting,
    Running,
    Restarting,
    Stopped,
}

/// <summary>
/// Background worker that turns queued click points into <see cref="ClickInfo"/>
/// using UI Automation. Runs on its own thread so slow or hung target apps never
/// touch the hook or the UI thread.
/// </summary>
internal sealed class ClickResolver : IDisposable
{
    private static readonly TimeSpan RestartDelay = TimeSpan.FromSeconds(1);

    private readonly ChannelReader<Point> _clicks;
    private readonly CancellationTokenSource _stop = new();
    private readonly Thread _thread;
    private readonly int _ownProcessId = Environment.ProcessId;

    private long _resolvedCount;
    private long _ignoredCount;
    private long _skippedCount;
    private long _restartCount;
    private volatile ResolverStatus _status = ResolverStatus.Starting;

#if DEBUG
    private volatile bool _faultRequested;
#endif

    /// <summary>
    /// Raised on the worker thread for each successfully resolved click.
    /// Handlers that touch WPF controls must marshal to the UI thread.
    /// An <c>event</c> is a list of callbacks, like an array of function pointers
    /// that the class calls in turn.
    /// </summary>
    public event Action<ClickInfo>? Resolved;

    /// <summary>
    /// Raised on the worker thread when the loop crashes, just before it restarts.
    /// Handlers that touch WPF controls must marshal to the UI thread.
    /// </summary>
    public event Action<Exception>? Faulted;

    public ClickResolver(ChannelReader<Point> clicks)
    {
        _clicks = clicks;
        _thread = new Thread(Run) { IsBackground = true, Name = "ClickResolver" };
        // UIA's COM objects are free-threaded; MTA avoids needing a message pump here.
        _thread.SetApartmentState(ApartmentState.MTA);
        _thread.Start();
    }

    public long ResolvedCount => Interlocked.Read(ref _resolvedCount);

    /// <summary>Clicks on our own windows; deliberately not resolved.</summary>
    public long IgnoredCount => Interlocked.Read(ref _ignoredCount);

    /// <summary>Clicks that failed to resolve: no element, elevated or exited target, or UIA error/timeout.</summary>
    public long SkippedCount => Interlocked.Read(ref _skippedCount);

    /// <summary>How many times the loop crashed and was restarted.</summary>
    public long RestartCount => Interlocked.Read(ref _restartCount);

    public ResolverStatus Status => _status;

#if DEBUG
    /// <summary>
    /// Test hook: makes the loop throw on the next click, outside the per-click
    /// catch, to exercise the restart path.
    /// </summary>
    public void InjectFault() => _faultRequested = true;
#endif

    // Supervisor: anything that escapes RunOnce is reported and the loop starts
    // again. Without this, an exception here would end the process unlogged,
    // since .NET terminates on an unhandled exception on any thread.
    private void Run()
    {
        while (!_stop.IsCancellationRequested)
        {
            try
            {
                RunOnce();
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested)
            {
                // Dispose() was called.
            }
            catch (Exception ex)
            {
                _status = ResolverStatus.Restarting;
                Interlocked.Increment(ref _restartCount);

                try
                {
                    Faulted?.Invoke(ex);
                }
                catch
                {
                    // A broken Faulted handler must not stop the restart.
                }

                // Pause so a fault that repeats on every start can't spin the CPU.
                // Returns early if Dispose() is called meanwhile.
                _stop.Token.WaitHandle.WaitOne(RestartDelay);
            }
        }

        _status = ResolverStatus.Stopped;
    }

    private void RunOnce()
    {
        // Created on this thread and only ever used from it.
        using var automation = new UIA3Automation
        {
            // Default is 20 s; don't let one hung app stall every later click.
            TransactionTimeout = TimeSpan.FromSeconds(2),
        };

        _status = ResolverStatus.Running;

        // Blocks this thread until a click arrives (or we're stopped),
        // then drains everything queued.
        while (_clicks.WaitToReadAsync(_stop.Token).AsTask().GetAwaiter().GetResult())
        {
            while (_clicks.TryRead(out Point point))
            {
#if DEBUG
                if (_faultRequested)
                {
                    _faultRequested = false;
                    throw new InvalidOperationException("DEBUG test fault (resolver loop)");
                }
#endif
                // Raised outside Resolve's catch so a bug in a subscriber reaches
                // the supervisor (logged) instead of being counted as a skip.
                ClickInfo? info = Resolve(automation, point);
                if (info != null)
                    Resolved?.Invoke(info);
            }
        }
    }

    /// <summary>Returns the clicked element's info, or null if the click should be skipped.</summary>
    private ClickInfo? Resolve(UIA3Automation automation, Point point)
    {
        try
        {
            var element = automation.FromPoint(point);
            int processId = element.Properties.ProcessId.ValueOrDefault;

            if (processId == _ownProcessId)
            {
                Interlocked.Increment(ref _ignoredCount);
                return null;
            }

            string name = element.Properties.Name.ValueOrDefault ?? "";
            string controlType = element.Properties.ControlType.ValueOrDefault.ToString();

            using var process = Process.GetProcessById(processId);

            Interlocked.Increment(ref _resolvedCount);
            return new ClickInfo(process.ProcessName, name, controlType, DateTime.Now);
        }
        catch (Exception)
        {
            // Elevated windows (access denied), apps that exited, COM timeouts, etc.
            // Skip quietly: one bad click must never kill the worker thread.
            Interlocked.Increment(ref _skippedCount);
            return null;
        }
    }

    public void Dispose()
    {
        _stop.Cancel();
        // A UIA call may be stuck on a hung app; don't block shutdown on it.
        // If it's still running it may yet touch _stop, so only dispose after a clean exit.
        if (_thread.Join(TimeSpan.FromSeconds(1)))
            _stop.Dispose();
    }
}
