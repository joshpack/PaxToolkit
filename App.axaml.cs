using System.IO.Pipes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using pxdToolkit.Core;
using pxdToolkit.Views;

namespace pxdToolkit;

public partial class App : Application
{
    private static readonly TimeSpan CheckEvery = TimeSpan.FromHours(6);

    private Toolkit _kit = null!;
    private MainWindow? _window;
    private TrayIcon _tray = null!;
    private IClassicDesktopStyleApplicationLifetime _lifetime = null!;
    private WindowIcon _icon = null!, _badgeIcon = null!;
    private bool? _shownBadge;
    private readonly CancellationTokenSource _quit = new();
    private DispatcherTimer? _trayRefresh;
    private IDisposable? _linkWatch;

    public Toolkit Kit => _kit;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime lifetime)
        {
            _lifetime = lifetime;
            lifetime.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            pxdStandard.pxdChrome.Configure("pxdToolkit", Paths.Data);
            Styles.Add(pxdStandard.pxdChrome.Styles());

            if (SelfTest.Dir is not null)
            {
                Dispatcher.UIThread.Post(async () =>
                {
                    await SelfTest.RunAsync();
                    lifetime.Shutdown(SelfTest.ExitCode);
                });
                base.OnFrameworkInitializationCompleted();
                return;
            }

            SelfUpdate.CleanUp();
            Migration.Own();
            _kit = new Toolkit();
            _kit.Changed += _ => Dispatcher.UIThread.Post(QueueTrayUpdate);

            _icon = AppIcon.Window();
            _badgeIcon = AppIcon.Window(badge: true);
            _tray = new TrayIcon { Icon = _icon, ToolTipText = "pxd Toolkit", IsVisible = pxdStandard.pxdLink.TrayIconVisible("pxdToolkit") };
            _tray.Clicked += (_, _) => OpenWindow();
            TrayIcon.SetIcons(this, new TrayIcons { _tray });
            UpdateTray();
            // pxd App Standard: the tray icon follows the shared switch (Settings here, or toolkit.json).
            _linkWatch = pxdStandard.pxdLink.Watch(() => Dispatcher.UIThread.Post(() =>
                _tray.IsVisible = pxdStandard.pxdLink.TrayIconVisible("pxdToolkit")));

            _ = Task.Run(ListenForCommands);

            if (!Program.StartMinimized)
                OpenWindow();
            else
                DispatcherTimer.RunOnce(MemoryTrim.Trim, TimeSpan.FromSeconds(5));

            // The app list now; the release check soon after start (or right away if the last one is old), then every few hours.
            _ = _kit.RefreshCatalogAsync(_quit.Token);
            var due = _kit.Settings.LastCheckUtc is { } last ? last + CheckEvery - DateTime.UtcNow : TimeSpan.Zero;
            // Apps still installed under their former names: check now and move them, whatever the schedule says.
            if (_kit.Apps.Any(e => Migration.FormerInstall(_kit, e) is not null))
                due = TimeSpan.Zero;
            DispatcherTimer.RunOnce(() => _ = due == TimeSpan.Zero ? MigrateNowAsync() : CheckAsync(auto: true), due < TimeSpan.FromSeconds(3) ? TimeSpan.FromSeconds(3) : due > CheckEvery ? CheckEvery : due);
            var timer = new DispatcherTimer { Interval = CheckEvery };
            timer.Tick += (_, _) => _ = CheckAsync(auto: true);
            timer.Start();

            // Running dots in the tray menu and the window.
            var running = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
            running.Tick += (_, _) =>
            {
                if (_window is not null || DateTime.Now.Second < 4)
                    _kit.RefreshRunning();
            };
            running.Start();
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>A check that always runs (even with update checks off), then the usual check's follow-ups.</summary>
    private async Task MigrateNowAsync()
    {
        try
        {
            await _kit.CheckAsync(_quit.Token);
            await Migration.AppsAsync(_kit, _quit.Token);
            if (_kit.Settings.AutoUpdate)
                await UpdateAllAsync();
        }
        catch
        {
            /* tried again at the next check */
        }
    }

    /// <summary>Checks GitHub; when the user has asked for it, installs every update it finds.</summary>
    public async Task CheckAsync(bool auto)
    {
        if (auto && !_kit.Settings.CheckForUpdates)
            return;
        try
        {
            await _kit.CheckAsync(_quit.Token);
        }
        catch
        {
            return;
        }

        // Apps still installed under their former names move to the pxd ones (no-op once done).
        try
        {
            await Migration.AppsAsync(_kit, _quit.Token);
        }
        catch
        {
            /* tried again at the next check */
        }

        if (auto && _kit.Settings.AutoUpdate)
            await UpdateAllAsync();
    }

    public async Task UpdateAllAsync()
    {
        foreach (var e in _kit.Apps.Where(a => a.UpdateAvailable && !a.Busy).ToList())
        {
            try
            {
                await _kit.InstallAsync(e, _quit.Token);
            }
            catch
            {
                /* the card shows the error */
            }
        }
    }

    private void QueueTrayUpdate()
    {
        // Changes come in bursts (download progress): redraw the menu at most a few times a second.
        if (_trayRefresh is not null)
            return;
        _trayRefresh = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _trayRefresh.Tick += (_, _) =>
        {
            _trayRefresh!.Stop();
            _trayRefresh = null;
            UpdateTray();
        };
        _trayRefresh.Start();
    }

    /// <summary>Tray menu: open the toolkit, every app that's on (with start / tray / update), checks, quit.</summary>
    private void UpdateTray()
    {
        var menu = new NativeMenu();
        var open = new NativeMenuItem("Open pxd Toolkit");
        open.Click += (_, _) => OpenWindow();
        menu.Add(open);
        menu.Add(new NativeMenuItemSeparator());

        var installed = _kit.Apps.Where(a => a.IsInstalled).ToList();
        foreach (var e in installed)
        {
            var entry = e;
            var label = e.App.Name + (e.IsRunning ? "  ●" : "") + (e.UpdateAvailable ? "  (update)" : "");
            var sub = new NativeMenu();
            var start = new NativeMenuItem(e.IsRunning ? "Open" : "Start");
            start.Click += (_, _) => _kit.Launch(entry);
            sub.Add(start);
            if (e.App.TrayArgs is not null && !e.IsRunning)
            {
                var tray = new NativeMenuItem("Start in tray");
                tray.Click += (_, _) => _kit.Launch(entry, tray: true);
                sub.Add(tray);
            }

            if (e.App.GuiArgs is not null)
            {
                var gui = new NativeMenuItem("Open window");
                gui.Click += (_, _) => _kit.Launch(entry, gui: true);
                sub.Add(gui);
            }

            if (e.UpdateAvailable)
            {
                var upd = new NativeMenuItem($"Update to {Versions.Show(e.Latest?.Version)}");
                upd.Click += async (_, _) =>
                {
                    try
                    {
                        await _kit.InstallAsync(entry);
                    }
                    catch
                    {
                        /* shown on the card */
                    }
                };
                sub.Add(upd);
            }

            if (e.IsRunning)
            {
                var stop = new NativeMenuItem("Close");
                stop.Click += (_, _) => _kit.StopApp(entry);
                sub.Add(stop);
            }

            var folder = new NativeMenuItem("Open folder");
            folder.Click += (_, _) => _kit.OpenFolder(entry);
            sub.Add(folder);
            menu.Add(new NativeMenuItem(label) { Menu = sub });
        }

        if (installed.Count == 0)
            menu.Add(new NativeMenuItem("No apps turned on yet") { IsEnabled = false });

        menu.Add(new NativeMenuItemSeparator());
        var updates = _kit.UpdateCount;
        if (updates > 0)
        {
            var all = new NativeMenuItem($"Install {updates} update{(updates == 1 ? "" : "s")}");
            all.Click += (_, _) => _ = UpdateAllAsync();
            menu.Add(all);
        }

        var check = new NativeMenuItem(_kit.Checking ? "Checking for updates…" : "Check for updates") { IsEnabled = !_kit.Checking };
        check.Click += (_, _) => _ = CheckAsync(auto: false);
        menu.Add(check);
        var quit = new NativeMenuItem("Quit");
        quit.Click += (_, _) => Quit();
        menu.Add(quit);
        _tray.Menu = menu;

        var badge = updates > 0 || _kit.ToolkitUpdate is not null;
        _tray.ToolTipText = badge
            ? $"pxd Toolkit: {(updates > 0 ? $"{updates} update{(updates == 1 ? "" : "s")} ready" : "a new toolkit version is ready")}"
            : $"pxd Toolkit: {installed.Count} app{(installed.Count == 1 ? "" : "s")} on";
        if (badge != _shownBadge)
        {
            _shownBadge = badge;
            _tray.Icon = badge ? _badgeIcon : _icon;
        }
    }

    private async Task ListenForCommands()
    {
        while (!_quit.IsCancellationRequested)
        {
            try
            {
                await using var pipe = new NamedPipeServerStream(Program.PipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                await pipe.WaitForConnectionAsync(_quit.Token);
                using var reader = new StreamReader(pipe);
                var args = (await reader.ReadToEndAsync(_quit.Token)).Split('\n', StringSplitOptions.RemoveEmptyEntries);
                if (args.Length == 0 || Program.Has(args, "--show"))
                    Dispatcher.UIThread.Post(OpenWindow);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch
            {
                await Task.Delay(500);
            }
        }
    }

    public void OpenWindow()
    {
        if (_window is not null)
        {
            if (_window.WindowState == WindowState.Minimized)
                _window.WindowState = WindowState.Normal;
            _window.Activate();
            return;
        }

        _window = new MainWindow(_kit, this);
        _window.Closed += (_, _) =>
        {
            _window = null;
            DispatcherTimer.RunOnce(MemoryTrim.Trim, TimeSpan.FromSeconds(1));
        };
        _window.Show();
        _window.Activate();
        _kit.DetectAll();
    }

    public void Quit()
    {
        _quit.Cancel();
        _window?.Close();
        if (_tray is not null)
        {
            _tray.IsVisible = false;
            _tray.Dispose();
        }

        _kit?.Dispose();
        _lifetime.Shutdown();
    }
}
