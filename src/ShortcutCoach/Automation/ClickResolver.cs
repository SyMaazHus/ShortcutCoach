using System.Diagnostics;
using System.Threading.Channels;
using FlaUI.UIA3;
using ShortcutCoach.Core;
using Point = System.Drawing.Point;

namespace ShortcutCoach.Automation;

/// <summary>
/// Background worker that turns queued click points into <see cref="ClickInfo"/>
/// using UI Automation. Runs on its own thread so slow or hung target apps never
/// touch the hook or the UI thread.
/// </summary>
internal sealed class ClickResolver : IDisposable
{
    private readonly ChannelReader<Point> _clicks;
    private readonly CancellationTokenSource _stop = new();
    private readonly Thread _thread;
    private readonly int _ownProcessId = Environment.ProcessId;

    private long _resolvedCount;
    private long _skippedCount;

    /// <summary>
    /// Raised on the worker thread for each successfully resolved click.
    /// Handlers that touch WPF controls must marshal to the UI thread.
    /// An <c>event</c> is a list of callbacks, like an array of function pointers
    /// that the class calls in turn.
    /// </summary>
    public event Action<ClickInfo>? Resolved;

    public ClickResolver(ChannelReader<Point> clicks)
    {
        _clicks = clicks;
        _thread = new Thread(Run) { IsBackground = true, Name = "ClickResolver" };
        // UIA's COM objects are free-threaded; MTA avoids needing a message pump here.
        _thread.SetApartmentState(ApartmentState.MTA);
        _thread.Start();
    }

    public long ResolvedCount => Interlocked.Read(ref _resolvedCount);

    /// <summary>Clicks dropped: own window, no element, elevated target, or UIA error.</summary>
    public long SkippedCount => Interlocked.Read(ref _skippedCount);

    private void Run()
    {
        // Created on this thread and only ever used from it.
        using var automation = new UIA3Automation
        {
            // Default is 20 s; don't let one hung app stall every later click.
            TransactionTimeout = TimeSpan.FromSeconds(2),
        };

        try
        {
            // Blocks this thread until a click arrives (or we're stopped),
            // then drains everything queued.
            while (_clicks.WaitToReadAsync(_stop.Token).AsTask().GetAwaiter().GetResult())
            {
                while (_clicks.TryRead(out Point point))
                    Resolve(automation, point);
            }
        }
        catch (OperationCanceledException)
        {
            // Dispose() was called.
        }
    }

    private void Resolve(UIA3Automation automation, Point point)
    {
        try
        {
            var element = automation.FromPoint(point);
            int processId = element.Properties.ProcessId.ValueOrDefault;

            if (processId == _ownProcessId)
            {
                Interlocked.Increment(ref _skippedCount);
                return;
            }

            string name = element.Properties.Name.ValueOrDefault ?? "";
            string controlType = element.Properties.ControlType.ValueOrDefault.ToString();

            using var process = Process.GetProcessById(processId);

            Interlocked.Increment(ref _resolvedCount);
            Resolved?.Invoke(new ClickInfo(process.ProcessName, name, controlType, DateTime.Now));
        }
        catch (Exception)
        {
            // Elevated windows (access denied), apps that exited, COM timeouts, etc.
            // Skip quietly: one bad click must never kill the worker thread.
            Interlocked.Increment(ref _skippedCount);
        }
    }

    public void Dispose()
    {
        _stop.Cancel();
        // A UIA call may be stuck on a hung app; don't block shutdown on it.
        _thread.Join(TimeSpan.FromSeconds(1));
        _stop.Dispose();
    }
}
