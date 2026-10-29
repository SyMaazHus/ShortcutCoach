using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using ShortcutCoach.Automation;
using ShortcutCoach.Core;
using ShortcutCoach.Input;
using ShortcutCoach.Notify;

namespace ShortcutCoach;

/// <summary>
/// Debug window: live log of resolved clicks (newest first) plus pipeline counters.
/// </summary>
public partial class MainWindow : Window
{
    private const int MaxEntries = 500;

    private readonly MouseHook _mouseHook;
    private readonly ClickResolver _clickResolver;
    private readonly ShortcutResolver _shortcutResolver;
    private readonly ToastWindow _toast;
    private readonly DispatcherTimer _statusTimer;

    // An ObservableCollection notifies the ListBox whenever items are added or
    // removed, so the list redraws itself (no manual "refresh" call needed).
    private readonly ObservableCollection<string> _entries = [];

    internal MainWindow(MouseHook mouseHook, ClickResolver clickResolver, ShortcutResolver shortcutResolver,
        ToastWindow toast)
    {
        InitializeComponent();

        _mouseHook = mouseHook;
        _clickResolver = clickResolver;
        _shortcutResolver = shortcutResolver;
        _toast = toast;
        ClickLog.ItemsSource = _entries;

        _clickResolver.Resolved += OnResolved;
        _clickResolver.Faulted += OnResolverFaulted;

        _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _statusTimer.Tick += (_, _) => UpdateStatus();
        _statusTimer.Start();
        UpdateStatus();

#if DEBUG
        KeyDown += OnDebugKeyDown;
#endif
    }

#if DEBUG
    // Fault injection for testing the crash handlers. Window-local keys (only
    // while this window has focus), not global hotkeys: the app registers no
    // keyboard input beyond the hint-mode hotkey. Compiled out of Release builds,
    // like code inside #ifdef DEBUG in C.
    private void OnDebugKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers != (ModifierKeys.Control | ModifierKeys.Shift))
            return;

        switch (e.Key)
        {
            case Key.U:
                // Should appear as "!!" via App's DispatcherUnhandledException handler.
                throw new InvalidOperationException("DEBUG test fault (UI thread)");

            case Key.R:
                _clickResolver.InjectFault();
                AddLine($"{DateTime.Now:HH:mm:ss}  -- resolver fault armed; click anywhere to trigger");
                e.Handled = true;
                break;

            case Key.T:
                // Delayed so there's time to switch to another app and check focus.
                var delay = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
                delay.Tick += (_, _) =>
                {
                    delay.Stop();
                    _toast.ShowSuggestion("Copy → Ctrl+C");
                };
                delay.Start();
                AddLine($"{DateTime.Now:HH:mm:ss}  -- test toast in 3 s; switch to another app");
                e.Handled = true;
                break;
        }
    }
#endif

    // Called on the resolver's worker thread; WPF controls may only be touched
    // from the UI thread, so queue the update onto it.
    private void OnResolved(ClickInfo info) =>
        Dispatcher.BeginInvoke(() => AddEntry(info));

    // Also called on the worker thread, just before the resolver restarts.
    private void OnResolverFaulted(Exception ex) =>
        Dispatcher.BeginInvoke(() =>
            AddError($"Resolver crashed, restarting: {ex.GetType().Name}: {ex.Message}"));

    // e.g. "10:42:07  Copy (MenuItem) — Notepad  → Ctrl+C [accelerator]"
    private void AddEntry(ClickInfo info)
    {
        string web = info.IsWebContent ? "  [web]" : "";
        string suggestion = _shortcutResolver.Resolve(info) is { } s
            ? $"  → {s.Shortcut} [{SourceLabel(s.Source)}]"
            : "";
        AddLine($"{info.Timestamp:HH:mm:ss}  {info}{web}{suggestion}");
    }

    // A switch expression maps each value to a result, like a C switch where
    // every case returns.
    private static string SourceLabel(ShortcutSource source) => source switch
    {
        ShortcutSource.Accelerator => "accelerator",
        ShortcutSource.Name => "name",
        ShortcutSource.Database => "db",
        _ => source.ToString(),
    };

    /// <summary>Adds an error line to the log. Must be called on the UI thread.</summary>
    internal void AddError(string message) =>
        AddLine($"{DateTime.Now:HH:mm:ss}  !! {message}");

    private void AddLine(string line)
    {
        _entries.Insert(0, line);
        if (_entries.Count > MaxEntries)
            _entries.RemoveAt(_entries.Count - 1);
    }

    private void UpdateStatus() =>
        StatusText.Text =
            $"Clicks: {_mouseHook.ClickCount}    " +
            $"Resolved: {_clickResolver.ResolvedCount}    " +
            $"Ignored: {_clickResolver.IgnoredCount}    " +
            $"Skipped: {_clickResolver.SkippedCount}    " +
            $"Hook max(100): {_mouseHook.MaxCallbackMicroseconds:F0} µs / lag {_mouseHook.MaxLagMilliseconds} ms    " +
            $"Resolver: {_clickResolver.Status} (restarts: {_clickResolver.RestartCount})";

    protected override void OnClosed(EventArgs e)
    {
        _clickResolver.Resolved -= OnResolved;
        _clickResolver.Faulted -= OnResolverFaulted;
        _statusTimer.Stop();
        base.OnClosed(e);
    }
}
