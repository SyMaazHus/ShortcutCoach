using System.Threading.Channels;
using System.Windows;
using ShortcutCoach.Automation;
using ShortcutCoach.Core;
using ShortcutCoach.Input;
using ShortcutCoach.Notify;

namespace ShortcutCoach;

public partial class App : Application
{
    // Bounded so a slow consumer can never block the hook or grow memory:
    // when full, the oldest queued click is dropped.
    private readonly Channel<ClickPoint> _clicks = Channel.CreateBounded<ClickPoint>(
        new BoundedChannelOptions(64) { FullMode = BoundedChannelFullMode.DropOldest });

    private MouseHook? _mouseHook;
    private ClickResolver? _clickResolver;
    private MainWindow? _mainWindow;
    private ToastWindow? _toast;
    private ShortcutResolver? _shortcutResolver;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // A bug in UI-thread code shouldn't take down the hook; log it and keep running.
        DispatcherUnhandledException += (_, args) =>
        {
            _mainWindow?.AddError($"{args.Exception.GetType().Name}: {args.Exception.Message}");
            args.Handled = true;
        };

        // Throws if the embedded DB is malformed; the Core tests guard against that.
        _shortcutResolver = new ShortcutResolver(ShortcutDb.LoadBuiltIn());

        _clickResolver = new ClickResolver(_clicks.Reader);
        _mouseHook = new MouseHook(_clicks.Writer);
        _toast = new ToastWindow();
        _mainWindow = new MainWindow(_mouseHook, _clickResolver, _toast);
        _clickResolver.Resolved += OnClickResolved;
        // WPF makes the first window constructed the MainWindow (the toast here);
        // with ShutdownMode=OnMainWindowClose, closing the debug window must exit.
        MainWindow = _mainWindow;
        _mainWindow.Show();
    }

    // Runs on the resolver's worker thread. ShortcutResolver is read-only after
    // construction, so resolving here is safe; only the UI updates go to the UI thread.
    // The click is resolved once, and the log records exactly what the toast showed
    // (M3 will suppress some suggestions, and the log must reflect that).
    // Every click with a shortcut gets a toast for now.
    private void OnClickResolved(ClickInfo info)
    {
        Suggestion? shown = _shortcutResolver?.Resolve(info);

        Dispatcher.BeginInvoke(() =>
        {
            _mainWindow?.AddClick(info, shown);
            if (shown != null)
                _toast?.ShowSuggestion($"{shown.ElementName} → {shown.Shortcut}");
        });
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_clickResolver != null)
            _clickResolver.Resolved -= OnClickResolved;
        _toast?.Close();
        _mouseHook?.Dispose();
        _clickResolver?.Dispose();
        base.OnExit(e);
    }
}
