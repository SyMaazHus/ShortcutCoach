using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Threading;
using ShortcutCoach.Automation;
using ShortcutCoach.Core;
using ShortcutCoach.Input;

namespace ShortcutCoach;

/// <summary>
/// Debug window: live log of resolved clicks (newest first) plus pipeline counters.
/// </summary>
public partial class MainWindow : Window
{
    private const int MaxEntries = 500;

    private readonly MouseHook _mouseHook;
    private readonly ClickResolver _clickResolver;
    private readonly DispatcherTimer _statusTimer;

    // An ObservableCollection notifies the ListBox whenever items are added or
    // removed, so the list redraws itself (no manual "refresh" call needed).
    private readonly ObservableCollection<string> _entries = [];

    internal MainWindow(MouseHook mouseHook, ClickResolver clickResolver)
    {
        InitializeComponent();

        _mouseHook = mouseHook;
        _clickResolver = clickResolver;
        ClickLog.ItemsSource = _entries;

        _clickResolver.Resolved += OnResolved;

        _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _statusTimer.Tick += (_, _) => UpdateStatus();
        _statusTimer.Start();
        UpdateStatus();
    }

    // Called on the resolver's worker thread; WPF controls may only be touched
    // from the UI thread, so queue the update onto it.
    private void OnResolved(ClickInfo info) =>
        Dispatcher.BeginInvoke(() => AddEntry(info));

    private void AddEntry(ClickInfo info) =>
        AddLine($"{info.Timestamp:HH:mm:ss}  {info}");

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
            $"Skipped: {_clickResolver.SkippedCount}    " +
            $"Hook max: {_mouseHook.MaxCallbackMicroseconds:F0} µs";

    protected override void OnClosed(EventArgs e)
    {
        _clickResolver.Resolved -= OnResolved;
        _statusTimer.Stop();
        base.OnClosed(e);
    }
}
