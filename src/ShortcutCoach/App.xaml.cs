using System.Threading.Channels;
using System.Windows;
using ShortcutCoach.Automation;
using ShortcutCoach.Core;
using ShortcutCoach.Input;

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
        var shortcutResolver = new ShortcutResolver(ShortcutDb.LoadBuiltIn());

        _clickResolver = new ClickResolver(_clicks.Reader);
        _mouseHook = new MouseHook(_clicks.Writer);
        _mainWindow = new MainWindow(_mouseHook, _clickResolver, shortcutResolver);
        _mainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _mouseHook?.Dispose();
        _clickResolver?.Dispose();
        base.OnExit(e);
    }
}
