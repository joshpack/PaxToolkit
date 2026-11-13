using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using pxdStandard;
using pxdToolkit.Core;

namespace pxdToolkit.Views;

/// <summary>
/// The toolkit window: a slim sidebar listing the apps (installed first, then available) and a detail pane for the
/// selected app with one main action, its tray-icon switch and everything else tucked below. Settings sit at the foot
/// of the sidebar.
/// </summary>
public sealed class MainWindow : Window
{
    private static readonly IBrush Muted = Brush("#8A92A6");
    private static readonly IBrush Good = Brush("#19F58C");
    private static readonly IBrush Info = Brush("#00C8FF");
    private static readonly IBrush Warn = Brush("#F5B14C");
    private static readonly IBrush Bad = Brush("#F2666A");
    private static readonly IBrush Text = Brush("#E8EAF0");

    private readonly Toolkit _kit;
    private readonly App _app;
    private readonly StackPanel _list = new();
    private readonly Border _detailHost = new();
    private readonly StackPanel _sidebarFoot = new() { Spacing = 8, Margin = new Thickness(12, 8, 12, 12) };
    private readonly Dictionary<string, (ProgressBar Bar, TextBlock Sub)> _rowParts = new(StringComparer.OrdinalIgnoreCase);
    private ProgressBar? _detailBar;
    private TextBlock? _detailBusy;
    private string? _selected;
    private bool _settings;
    private bool _commands;
    private string _listKey = "", _detailKey = "";
    private bool _pending;
    private bool _confirmRemove;
    private IDisposable? _linkWatch;

    /// <summary>README screenshots: no banner showing where this copy happens to run from.</summary>
    public static bool HideSetupBanner { get; set; }

    public MainWindow(Toolkit kit, App app)
    {
        _kit = kit;
        _app = app;
        Title = "pxd Toolkit";
        Icon = AppIcon.Window();
        Width = 980;
        Height = 660;
        MinWidth = 760;
        MinHeight = 500;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        var sidebar = new DockPanel { Width = 300, Background = Brush("#0B0E13") };
        DockPanel.SetDock(_sidebarFoot, Dock.Bottom);
        sidebar.Children.Add(_sidebarFoot);
        sidebar.Children.Add(new ScrollViewer { Content = _list, Padding = new Thickness(0, 4, 0, 8) });
        var sideEdge = new Border { BorderBrush = Brush("#1B2030"), BorderThickness = new Thickness(0, 0, 1, 0), Child = sidebar };

        var body = new DockPanel();
        DockPanel.SetDock(sideEdge, Dock.Left);
        body.Children.Add(sideEdge);
        body.Children.Add(new ScrollViewer { Content = _detailHost });

        Content = pxdChrome.Wrap(this, body, "pxd Toolkit", AppIcon.Render(36));
        pxdChrome.RememberPlacement(this, "main");

        _kit.Changed += OnChanged;
        _linkWatch = pxdLink.Watch(() => OnChanged(null));
        Closed += (_, _) =>
        {
            _kit.Changed -= OnChanged;
            _linkWatch?.Dispose();
        };
        _selected = _kit.Apps.FirstOrDefault(a => a.IsInstalled)?.App.Id ?? _kit.Apps.FirstOrDefault()?.App.Id;
        Refresh();
    }

    public void Select(string id)
    {
        _selected = id;
        _settings = false;
        _commands = false;
        _confirmRemove = false;
        Refresh(force: true);
    }

    public void ShowSettings()
    {
        _settings = true;
        _commands = false;
        Refresh(force: true);
    }

    /// <summary>The PowerShell one-liners page: every app's command (from the catalog) with a Copy button.</summary>
    public void ShowCommands()
    {
        _commands = true;
        _settings = false;
        Refresh(force: true);
    }

    /// <summary>pxd Toolkit's own one-liner first, then every catalog app that has one, in catalog order.</summary>
    private List<(CatalogApp? App, string Name, string Command, string Note)> Commands()
    {
        var list = new List<(CatalogApp?, string, string, string)>
        {
            (null, "pxd Toolkit in PowerShell", ToolkitConsoleCommand, "Lists the pxd apps that have a PowerShell edition and whether they're installed, and runs them in memory. Installs nothing and saves nothing on the PC."),
            (null, "Install pxd Toolkit", ToolkitInstallCommand, "Installs or updates this app on any PC, no admin needed. It starts in the tray; its window opens only if you say so."),
        };
        foreach (var e in _kit.Apps)
            if (e.App.InstallCommand is { Length: > 0 } cmd)
                list.Add((e.App, e.App.Name, cmd, e.App.CommandText));
        return list;
    }

    private string CommandsKey() => string.Join("|", _kit.Apps.Select(a => a.App.Id + "=" + a.App.InstallCommand + a.App.CommandNote));

    private void OnChanged(AppEntry? e)
    {
        if (_pending)
            return;
        _pending = true;
        Dispatcher.UIThread.Post(() =>
        {
            _pending = false;
            Refresh();
        }, DispatcherPriority.Background);
    }

    // ───────────────────────────── sidebar ─────────────────────────────

    public void Refresh(bool force = false)
    {
        var listKey = string.Join("|", _kit.Apps.Select(RowKey)) + $"|{_selected}|{_settings}|{_commands}|{CommandsKey()}|{_kit.Checking}|{_kit.UpdateCount}|{_kit.LastCheckError}|{_kit.ToolkitUpdate?.Tag}";
        if (force || listKey != _listKey)
        {
            _listKey = listKey;
            BuildList();
            BuildFoot();
        }
        else
        {
            foreach (var e in _kit.Apps)
                if (e.Busy && _rowParts.TryGetValue(e.App.Id, out var parts))
                {
                    parts.Bar.IsIndeterminate = e.Progress < 0;
                    if (e.Progress >= 0)
                        parts.Bar.Value = e.Progress;
                }
        }

        var entry = _settings || _commands ? null : _kit.Find(_selected ?? "");
        var detailKey = _settings ? "settings" + _kit.ToolkitUpdate?.Tag
            : _commands ? "commands" + CommandsKey()
            : entry is null ? "none" : DetailKey(entry);
        if (force || detailKey != _detailKey)
        {
            _detailKey = detailKey;
            _detailHost.Child = _settings ? BuildSettings() : _commands ? BuildCommands() : entry is null ? new Panel() : BuildDetail(entry);
        }
        else if (entry is { Busy: true } && _detailBar is not null)
        {
            _detailBar.IsIndeterminate = entry.Progress < 0;
            if (entry.Progress >= 0)
                _detailBar.Value = entry.Progress;
            if (_detailBusy is not null)
                _detailBusy.Text = entry.BusyText;
        }
    }

    private static string RowKey(AppEntry e) =>
        $"{e.App.Id}:{e.IsInstalled}:{e.Busy}:{e.BusyText}:{e.IsRunning}:{e.UpdateAvailable}:{e.Latest?.Tag}:{e.NoRelease}:{e.Error}";

    private string DetailKey(AppEntry e) =>
        RowKey(e) + $":{e.InstalledVersion}:{e.InstallDir}:{e.MissingRuntimes.Count}:{(e.App.Tray ? pxdLink.TrayIconVisible(e.App.Id) : false)}:{_confirmRemove}:{e.CheckError}";

    private void BuildList()
    {
        _list.Children.Clear();
        _rowParts.Clear();
        _list.Children.Add(CommandsRow());
        var installed = _kit.Apps.Where(a => a.IsInstalled).ToList();
        var available = _kit.Apps.Where(a => !a.IsInstalled).ToList();
        if (installed.Count > 0)
        {
            _list.Children.Add(new TextBlock { Text = "INSTALLED", Classes = { "section" } });
            foreach (var e in installed)
                _list.Children.Add(Row(e));
        }

        if (available.Count > 0)
        {
            _list.Children.Add(new TextBlock { Text = "AVAILABLE", Classes = { "section" } });
            foreach (var e in available)
                _list.Children.Add(Row(e));
        }
    }

    private Control Row(AppEntry e)
    {
        var (sub, subBrush) = e switch
        {
            { Busy: true } => (e.BusyText ?? "Working…", Info),
            { Error: not null } => ("Something went wrong", Bad),
            { UpdateAvailable: true } => ($"Update to v{Versions.Show(e.Latest?.Version)}", Warn),
            { IsRunning: true } => ($"v{Versions.Show(e.InstalledVersion)} · Running", Good),
            { IsInstalled: true } => ($"v{Versions.Show(e.InstalledVersion)}", Muted),
            { NoRelease: true } => ("Not released yet", Muted),
            { Download: { } d } => (e.App.Tagline, Muted),
            _ => (e.App.Tagline, Muted),
        };

        var name = new TextBlock { Text = e.App.Name, FontSize = 13.5, FontWeight = FontWeight.SemiBold, Foreground = Text, TextTrimming = TextTrimming.CharacterEllipsis };
        var subText = new TextBlock { Text = sub, FontSize = 11.5, Foreground = subBrush, TextTrimming = TextTrimming.CharacterEllipsis };
        var texts = new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 6, 0) };
        texts.Children.Add(name);
        texts.Children.Add(subText);

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        grid.Children.Add(Icons.Image(e.App, 36));
        Grid.SetColumn(texts, 1);
        grid.Children.Add(texts);
        if (e.UpdateAvailable || e.IsRunning)
        {
            var dot = new Avalonia.Controls.Shapes.Ellipse
            {
                Width = 8, Height = 8, VerticalAlignment = VerticalAlignment.Center,
                Fill = e.UpdateAvailable ? Warn : Good,
            };
            Grid.SetColumn(dot, 2);
            grid.Children.Add(dot);
        }

        var bar = new ProgressBar { Minimum = 0, Maximum = 1, Height = 2, MinHeight = 2, Margin = new Thickness(48, 6, 4, 0), IsVisible = e.Busy, IsIndeterminate = e.Progress < 0, Value = Math.Max(0, e.Progress) };
        var stack = new StackPanel();
        stack.Children.Add(grid);
        stack.Children.Add(bar);
        _rowParts[e.App.Id] = (bar, subText);

        var row = new Border { Classes = { "row" }, Child = stack };
        if (!_settings && !_commands && string.Equals(_selected, e.App.Id, StringComparison.OrdinalIgnoreCase))
            row.Classes.Add("selected");
        row.PointerPressed += (_, ev) =>
        {
            if (ev.GetCurrentPoint(row).Properties.IsLeftButtonPressed)
                Select(e.App.Id);
        };
        return row;
    }

    private void BuildFoot()
    {
        _sidebarFoot.Children.Clear();
        var updates = _kit.UpdateCount;
        if (updates > 0)
        {
            var all = new Button { Content = $"Update all ({updates})", Classes = { "primary" }, HorizontalAlignment = HorizontalAlignment.Stretch };
            all.Click += async (_, _) => await _app.UpdateAllAsync();
            _sidebarFoot.Children.Add(all);
        }

        var status = new TextBlock
        {
            FontSize = 11.5, Foreground = Muted, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center,
            Text = _kit.Checking ? "Checking GitHub…" : _kit.LastCheckError ?? (_kit.Settings.LastCheckUtc is { } t ? "Checked " + Ago(t) : "Not checked yet"),
        };
        var check = new Button { Content = "↻", Classes = { "ghost", "small" }, IsEnabled = !_kit.Checking, Padding = new Thickness(10, 4), FontSize = 15 };
        ToolTip.SetTip(check, "Check for updates");
        check.Click += async (_, _) => await _app.CheckAsync(auto: false);
        var settings = new Button { Content = "⚙  Settings", Classes = { "ghost", "small" } };
        if (_settings)
            settings.Foreground = Good;
        settings.Click += (_, _) => ShowSettings();

        var line = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto") };
        line.Children.Add(status);
        Grid.SetColumn(check, 1);
        line.Children.Add(check);
        Grid.SetColumn(settings, 2);
        line.Children.Add(settings);
        if (_kit.ToolkitUpdate is { } tu)
        {
            var upd = new Button { Content = $"pxd Toolkit v{Versions.Show(tu.Version)} is out", Classes = { "small" }, HorizontalAlignment = HorizontalAlignment.Stretch };
            upd.Click += (_, _) => ShowSettings();
            _sidebarFoot.Children.Add(upd);
        }

        _sidebarFoot.Children.Add(line);
    }

    // ───────────────────────────── app detail ─────────────────────────────

    private Control BuildDetail(AppEntry e)
    {
        var app = e.App;
        var root = new StackPanel { Margin = new Thickness(36, 30, 36, 30), Spacing = 0, MaxWidth = 760, HorizontalAlignment = HorizontalAlignment.Left };

        // Header: icon, name, tagline, status pill.
        var head = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        head.Children.Add(Icons.Image(app, 76));
        var titles = new StackPanel { Margin = new Thickness(20, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Spacing = 4 };
        titles.Children.Add(new TextBlock { Text = app.Name, FontSize = 26, FontWeight = FontWeight.Bold, Foreground = Text });
        var tagRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        tagRow.Children.Add(new TextBlock { Text = app.Tagline, Foreground = Muted, VerticalAlignment = VerticalAlignment.Center });
        tagRow.Children.Add(StatusPill(e));
        titles.Children.Add(tagRow);
        Grid.SetColumn(titles, 1);
        head.Children.Add(titles);
        root.Children.Add(head);

        // Actions: one main button, a few contextual ones.
        var actions = new WrapPanel { Orientation = Orientation.Horizontal, ItemSpacing = 10, LineSpacing = 10, Margin = new Thickness(0, 24, 0, 0) };
        if (!e.Busy)
        {
            if (!e.IsInstalled)
            {
                if (e.Download is not null || e.Latest is null && !e.NoRelease)
                    actions.Children.Add(Btn(e.Download is { } d ? $"Install  ·  {Sizes.Show(d.Size)}" : "Install", () => _ = Run(() => _kit.InstallAsync(e)), "primary", "big"));
                else
                    actions.Children.Add(new TextBlock { Text = "There's no download for this app yet.", Foreground = Muted, VerticalAlignment = VerticalAlignment.Center });
            }
            else
            {
                if (e.UpdateAvailable)
                    actions.Children.Add(Btn($"Update to v{Versions.Show(e.Latest!.Version)}", () => _ = Run(() => _kit.InstallAsync(e)), "primary", "big"));
                actions.Children.Add(Btn(app.IsScript ? "Run" : e.IsRunning ? "Open" : "Start", () => _kit.Launch(e), e.UpdateAvailable ? "big" : "primary", "big"));
                if (app.GuiArgs is not null)
                    actions.Children.Add(Btn("Open window", () => _kit.Launch(e, gui: true), "big"));
                if (app.TrayArgs is not null && !e.IsRunning)
                    actions.Children.Add(Btn("Start in tray", () => _kit.Launch(e, tray: true), "big"));
                if (e.IsRunning)
                    actions.Children.Add(Btn("Close", () => _kit.StopApp(e), "big"));
            }
        }

        root.Children.Add(actions);

        if (e.Busy)
        {
            _detailBar = new ProgressBar { Minimum = 0, Maximum = 1, Margin = new Thickness(0, 4, 0, 0), IsIndeterminate = e.Progress < 0, Value = Math.Max(0, e.Progress), MaxWidth = 420, HorizontalAlignment = HorizontalAlignment.Left, Width = 420 };
            _detailBusy = new TextBlock { Text = e.BusyText, Foreground = Info, FontSize = 12, Margin = new Thickness(0, 8, 0, 0) };
            root.Children.Add(_detailBar);
            root.Children.Add(_detailBusy);
        }
        else
        {
            _detailBar = null;
            _detailBusy = null;
        }

        if (e.Error is not null)
            root.Children.Add(new TextBlock { Text = e.Error, Foreground = Bad, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0) });

        if (e.IsInstalled && e.MissingRuntimes.Count > 0 && !e.Busy)
        {
            var fix = Btn("Install it", () => _ = InstallRuntimeAsync(e), "small");
            var text = "Needs " + string.Join(" and ", e.MissingRuntimes.Select(r => $"{Pretty(r.Name)} {r.Major}")) + " to run.";
            root.Children.Add(Banner(text, fix, Warn));
        }

        root.Children.Add(new TextBlock { Text = app.Description, TextWrapping = TextWrapping.Wrap, Foreground = Brush("#C3C8D4"), FontSize = 14, LineHeight = 21, Margin = new Thickness(0, 24, 0, 0) });

        // Its PowerShell one-liner, for a PC without the toolkit.
        if (app.InstallCommand is { Length: > 0 } cmd)
        {
            var card = CommandCard(app.IsScript || app.CommandNote is not null ? "RUN WITH POWERSHELL" : "INSTALL WITH POWERSHELL", cmd, app.CommandText);
            card.Margin = new Thickness(0, 24, 0, 0);
            root.Children.Add(card);
        }

        // Tray icon switch (apps that have one), straight from the pxd Toolkit link.
        if (app.Tray && e.IsInstalled)
        {
            var on = pxdLink.TrayIconVisible(app.Id);
            var toggle = new ToggleSwitch { IsChecked = on, OnContent = "", OffContent = "", VerticalAlignment = VerticalAlignment.Center };
            toggle.IsCheckedChanged += (_, _) => pxdLink.SetTrayIconVisible(app.Id, toggle.IsChecked == true);
            var texts = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 24, 0) };
            texts.Children.Add(new TextBlock { Text = "Tray icon", FontWeight = FontWeight.SemiBold, Foreground = Text });
            texts.Children.Add(new TextBlock
            {
                Text = $"Show {app.Name} in the notification area. When it's off the app keeps running; open it from here.",
                Foreground = Muted, FontSize = 12, TextWrapping = TextWrapping.Wrap,
            });
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
            g.Children.Add(texts);
            Grid.SetColumn(toggle, 1);
            g.Children.Add(toggle);
            root.Children.Add(new Border { Classes = { "card" }, Padding = new Thickness(18, 14), Margin = new Thickness(0, 24, 0, 0), Child = g });
        }

        // Facts.
        var facts = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), Margin = new Thickness(0, 24, 0, 0) };
        var r = 0;
        void Fact(string label, Control value)
        {
            facts.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var l = new TextBlock { Text = label, Foreground = Muted, FontSize = 12.5, Margin = new Thickness(0, 4, 24, 4) };
            Grid.SetRow(l, r);
            facts.Children.Add(l);
            Grid.SetRow(value, r);
            Grid.SetColumn(value, 1);
            value.Margin = new Thickness(0, 4);
            facts.Children.Add(value);
            r++;
        }

        TextBlock V(string s, IBrush? b = null) => new() { Text = s, Foreground = b ?? Text, FontSize = 12.5, TextWrapping = TextWrapping.Wrap };
        if (e.IsInstalled)
            Fact("Installed", V($"v{Versions.Show(e.InstalledVersion)}" + (e.External ? " (its own setup)" : "")));
        if (e.Latest is { } latest)
            Fact("Latest", V($"v{Versions.Show(latest.Version)}" + (latest.Published > DateTime.MinValue ? $" · {latest.Published.ToLocalTime():MMM d, yyyy}" : "")));
        else if (e.CheckError is { } ce)
            Fact("Latest", V(ce, Warn));
        if (e.InstallDir is { } dir)
        {
            var open = new Button
            {
                Content = new TextBlock { Text = Shorten(dir), TextTrimming = TextTrimming.PathSegmentEllipsis, FontSize = 12.5 },
                Classes = { "link" }, HorizontalAlignment = HorizontalAlignment.Left, MaxWidth = 460,
            };
            ToolTip.SetTip(open, "Open folder");
            open.Click += (_, _) => _kit.OpenFolder(e);
            Fact("Location", open);
        }

        Fact("Source", Link("github.com/" + app.Repo, () => Toolkit.OpenUrl(app.RepoUrl)));
        root.Children.Add(facts);

        // Quieter actions.
        var more = new WrapPanel { ItemSpacing = 22, LineSpacing = 8, Margin = new Thickness(0, 18, 0, 0) };
        if (e.Latest is { Url.Length: > 0 } rel)
            more.Children.Add(Link("What's new", () => Toolkit.OpenUrl(rel.Url)));
        if (e.IsInstalled && !e.Busy)
        {
            if (e.Download is not null && !e.UpdateAvailable)
                more.Children.Add(Link("Reinstall", () => _ = Run(() => _kit.InstallAsync(e))));
            var remove = Link("Uninstall…", () =>
            {
                _confirmRemove = true;
                Refresh(force: true);
            });
            remove.Classes.Add("danger");
            more.Children.Add(remove);
        }

        root.Children.Add(more);

        if (_confirmRemove && e.IsInstalled)
        {
            var yes = Btn("Uninstall", () =>
            {
                _confirmRemove = false;
                _ = Run(() => _kit.UninstallAsync(e));
            }, "small", "warn");
            var no = Btn("Keep it", () =>
            {
                _confirmRemove = false;
                Refresh(force: true);
            }, "small");
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            buttons.Children.Add(yes);
            buttons.Children.Add(no);
            var where = e.External ? "Its own uninstaller runs" : "Its files are removed";
            root.Children.Add(Banner($"Uninstall {app.Name}? It's closed if it's running. {where}; your settings stay, so installing it again picks up where you left off.", buttons, Bad));
        }

        return root;
    }

    private static Control StatusPill(AppEntry e)
    {
        var (text, color) = e switch
        {
            { Busy: true } => ("WORKING", Info),
            { IsInstalled: true, UpdateAvailable: true } => ("UPDATE", Warn),
            { IsRunning: true } => ("RUNNING", Good),
            { IsInstalled: true } => ("INSTALLED", Info),
            { NoRelease: true } => ("NOT RELEASED", Muted),
            _ => ("NOT INSTALLED", Muted),
        };
        return new Border
        {
            Classes = { "pill" },
            BorderBrush = color,
            Background = new SolidColorBrush(((ISolidColorBrush)color).Color, 0.12),
            Child = new TextBlock { Text = text, Foreground = color },
        };
    }

    private async Task InstallRuntimeAsync(AppEntry e)
    {
        var ok = true;
        foreach (var (name, major) in e.MissingRuntimes.ToList())
            ok &= await Runtimes.InstallAsync(Runtimes.WingetId(name, major));
        if (!ok)
            Toolkit.OpenUrl($"{Runtimes.DownloadPage}/{e.MissingRuntimes.FirstOrDefault().Major}.0");
        _kit.Detect(e);
        Refresh(force: true);
    }

    private static async Task Run(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch
        {
            /* the error is on the entry and shows in the pane */
        }
    }

    // ───────────────────────────── settings ─────────────────────────────

    private Control BuildSettings()
    {
        var s = _kit.Settings;
        var panel = new StackPanel { Spacing = 14, Margin = new Thickness(36, 30, 36, 30), MaxWidth = 720, HorizontalAlignment = HorizontalAlignment.Left };
        panel.Children.Add(new TextBlock { Text = "Settings", FontSize = 26, FontWeight = FontWeight.Bold, Foreground = Text, Margin = new Thickness(0, 0, 0, 6) });

        if (_kit.ToolkitUpdate is { } upd)
        {
            var btn = Btn($"Update to v{Versions.Show(upd.Version)}", () => { }, "primary", "small");
            btn.Click += async (_, _) =>
            {
                btn.IsEnabled = false;
                try
                {
                    await SelfUpdate.ApplyAsync(_kit.Http, upd, p => Dispatcher.UIThread.Post(() => btn.Content = $"Downloading {p:P0}"));
                }
                catch (Exception ex)
                {
                    btn.Content = Toolkit.Friendly(ex);
                }
            };
            panel.Children.Add(Banner($"A new pxd Toolkit is out (v{Versions.Show(upd.Version)}).", btn, Info));
        }

        if (!SelfUpdate.RunningFromHome && !SelfUpdate.IsDevBuild && !HideSetupBanner)
        {
            var btn = Btn("Move it there", () => { }, "small");
            btn.Click += (_, _) =>
            {
                try
                {
                    SelfUpdate.InstallHere();
                }
                catch (Exception ex)
                {
                    btn.Content = Toolkit.Friendly(ex);
                }
            };
            panel.Children.Add(Banner($"pxd Toolkit is running from {Path.GetDirectoryName(SelfUpdate.CurrentExe)}. Keep it in {Paths.ToolkitHome} with a Start menu shortcut?", btn, Info));
        }

        var startup = new CheckBox { Content = "Start pxd Toolkit with Windows (in the tray)", IsChecked = Autostart.IsEnabled() };
        startup.IsCheckedChanged += (_, _) =>
        {
            try
            {
                Autostart.Set(startup.IsChecked == true);
            }
            catch
            {
                startup.IsChecked = Autostart.IsEnabled();
            }
        };
        var tray = new CheckBox { Content = "Show pxd Toolkit in the tray", IsChecked = pxdLink.TrayIconVisible("pxdToolkit") };
        tray.IsCheckedChanged += (_, _) => pxdLink.SetTrayIconVisible("pxdToolkit", tray.IsChecked == true);

        var check = new CheckBox { Content = "Check GitHub for updates every 6 hours", IsChecked = s.CheckForUpdates };
        var auto = new CheckBox { Content = "Install app updates automatically (running apps are restarted)", IsChecked = s.AutoUpdate };
        var shortcuts = new CheckBox { Content = "Add Start menu shortcuts (Start → pxd Toolkit) for apps I install", IsChecked = s.StartMenuShortcuts };
        check.IsCheckedChanged += (_, _) => { s.CheckForUpdates = check.IsChecked == true; s.Save(); };
        auto.IsCheckedChanged += (_, _) => { s.AutoUpdate = auto.IsChecked == true; s.Save(); };
        shortcuts.IsCheckedChanged += (_, _) => { s.StartMenuShortcuts = shortcuts.IsChecked == true; s.Save(); };

        var folder = new TextBox { Text = s.EffectiveAppsRoot, Width = 440 };
        var browse = Btn("Browse…", () => { });
        browse.Click += async (_, _) =>
        {
            var picked = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Where should pxd apps go?", AllowMultiple = false });
            if (picked.FirstOrDefault()?.TryGetLocalPath() is { } path)
                folder.Text = path;
        };
        folder.LostFocus += (_, _) => SaveRoot(folder);
        folder.TextChanged += (_, _) => DispatcherTimer.RunOnce(() => SaveRoot(folder), TimeSpan.FromMilliseconds(800));
        var folderRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        folderRow.Children.Add(folder);
        folderRow.Children.Add(browse);

        panel.Children.Add(Section("pxd TOOLKIT", startup, tray,
            new TextBlock { Text = "With the tray icon off, pxd Toolkit keeps running and opens when you start it again.", Foreground = Muted, FontSize = 12, TextWrapping = TextWrapping.Wrap }));
        panel.Children.Add(CommandCard("INSTALL WITH POWERSHELL", ToolkitInstallCommand,
            "Installs or updates pxd Toolkit on any PC, no admin needed. Paste it into PowerShell. (irm pxdtek.com/tk | iex on its own only lists and runs the PowerShell tools, without installing anything.)"));
        panel.Children.Add(Section("UPDATES", check, auto));
        panel.Children.Add(Section("INSTALLING", shortcuts,
            new TextBlock { Text = "Install folder", Margin = new Thickness(0, 6, 0, 0) },
            folderRow,
            new TextBlock { Text = "New apps go into their own folder in here. Apps that are already installed stay where they are.", Foreground = Muted, FontSize = 12, TextWrapping = TextWrapping.Wrap }));
        panel.Children.Add(Section("ABOUT",
            new TextBlock { Text = $"pxd Toolkit v{Versions.Show(Versions.Current)} · settings in {Paths.Data}", Foreground = Muted, TextWrapping = TextWrapping.Wrap },
            Link("pxd Toolkit on GitHub", () => Toolkit.OpenUrl("https://github.com/pxdtek/pxdToolkit"))));
        return panel;
    }

    private void SaveRoot(TextBox box)
    {
        var text = box.Text?.Trim() ?? "";
        var value = string.Equals(text, Paths.DefaultAppsRoot, StringComparison.OrdinalIgnoreCase) ? "" : text;
        if (value == _kit.Settings.AppsRoot || text.Length > 0 && !Path.IsPathRooted(Paths.Expand(text)))
            return;
        _kit.Settings.AppsRoot = value;
        _kit.Settings.Save();
        _kit.DetectAll();
    }

    // ───────────────────────────── helpers ─────────────────────────────

    /// <summary>install.ps1 through the pxdtek.com short link: on its own, the temporary in-memory console.</summary>
    public const string ToolkitConsoleCommand = "irm pxdtek.com/tk | iex";

    /// <summary>The same script with -Install: installs (or updates) the pxd Toolkit app.</summary>
    public const string ToolkitInstallCommand = "irm pxdtek.com/tk/install | iex";

    // ───────────────────────────── PowerShell one-liners ─────────────────────────────

    /// <summary>The pinned sidebar row that opens the one-liners page.</summary>
    private Control CommandsRow()
    {
        var count = Commands().Count;
        var tile = new Border
        {
            Width = 36, Height = 36, CornerRadius = new CornerRadius(9), Background = Brush("#0E2A1D"),
            BorderBrush = new SolidColorBrush(((ISolidColorBrush)Good).Color, 0.5), BorderThickness = new Thickness(1),
            Child = new TextBlock
            {
                Text = ">_", FontFamily = new FontFamily("Cascadia Mono, Consolas, monospace"), FontWeight = FontWeight.Bold,
                FontSize = 14, Foreground = Good, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            },
        };
        var texts = new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 6, 0) };
        texts.Children.Add(new TextBlock { Text = "PowerShell one-liners", FontSize = 13.5, FontWeight = FontWeight.SemiBold, Foreground = Text });
        texts.Children.Add(new TextBlock { Text = $"{count} command{(count == 1 ? "" : "s")} to copy", FontSize = 11.5, Foreground = Muted });
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        grid.Children.Add(tile);
        Grid.SetColumn(texts, 1);
        grid.Children.Add(texts);
        var row = new Border { Classes = { "row" }, Child = grid, Margin = new Thickness(0, 4, 0, 0) };
        if (_commands)
            row.Classes.Add("selected");
        row.PointerPressed += (_, ev) =>
        {
            if (ev.GetCurrentPoint(row).Properties.IsLeftButtonPressed)
                ShowCommands();
        };
        return row;
    }

    private Control BuildCommands()
    {
        var panel = new StackPanel { Spacing = 14, Margin = new Thickness(36, 30, 36, 30), MaxWidth = 760, HorizontalAlignment = HorizontalAlignment.Left };
        panel.Children.Add(new TextBlock { Text = "PowerShell one-liners", FontSize = 26, FontWeight = FontWeight.Bold, Foreground = Text });
        panel.Children.Add(new TextBlock
        {
            Text = "Paste one into PowerShell on any PC, no toolkit needed. New apps show up here on their own when they get a one-liner.",
            Foreground = Muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6),
        });
        foreach (var (app, name, command, note) in Commands())
        {
            var icon = app is null ? new Image { Source = AppIcon.Render(64), Width = 30, Height = 30 } : Icons.Image(app, 30);
            var head = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
            head.Children.Add(icon);
            head.Children.Add(new TextBlock { Text = name, FontWeight = FontWeight.SemiBold, FontSize = 15, Foreground = Text, VerticalAlignment = VerticalAlignment.Center });
            var card = CommandCard(head, command, note);
            if (app is not null)
            {
                ToolTip.SetTip(head, $"Open {name}");
                head.Cursor = new Cursor(StandardCursorType.Hand);
                var id = app.Id;
                head.PointerPressed += (_, ev) =>
                {
                    if (ev.GetCurrentPoint(head).Properties.IsLeftButtonPressed)
                        Select(id);
                };
            }

            panel.Children.Add(card);
        }

        return panel;
    }

    /// <summary>A card with a PowerShell command in a code box and a Copy button.</summary>
    private Border CommandCard(string label, string command, string note) =>
        CommandCard(new TextBlock { Text = label, Classes = { "label" } }, command, note);

    private Border CommandCard(Control header, string command, string note)
    {
        var code = new SelectableTextBlock
        {
            Text = command, FontFamily = new FontFamily("Cascadia Mono, Consolas, monospace"), FontSize = 13.5,
            Foreground = Good, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap,
        };
        var prompt = new TextBlock { Text = "PS>", FontFamily = code.FontFamily, FontSize = 13.5, Foreground = Muted, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
        var copy = new Button { Content = "Copy", Classes = { "small" }, VerticalAlignment = VerticalAlignment.Center, MinWidth = 78, HorizontalContentAlignment = HorizontalAlignment.Center };
        ToolTip.SetTip(copy, "Copy the command");
        copy.Click += async (_, _) =>
        {
            try
            {
                if (Clipboard is { } cb)
                {
                    await cb.SetTextAsync(command);
                    copy.Content = "✓ Copied";
                    copy.Foreground = Good;
                }
            }
            catch
            {
                copy.Content = "Couldn't copy";
            }

            DispatcherTimer.RunOnce(() =>
            {
                copy.Content = "Copy";
                copy.ClearValue(ForegroundProperty);
            }, TimeSpan.FromSeconds(2));
        };

        var line = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        line.Children.Add(prompt);
        Grid.SetColumn(code, 1);
        line.Children.Add(code);
        Grid.SetColumn(copy, 2);
        line.Children.Add(copy);
        var box = new Border
        {
            Background = Brush("#07090D"), BorderBrush = Brush("#1F2636"), BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8), Padding = new Thickness(14, 8, 8, 8), Child = line,
        };

        var stack = new StackPanel { Spacing = 10 };
        stack.Children.Add(header);
        stack.Children.Add(box);
        stack.Children.Add(new TextBlock { Text = note, Foreground = Muted, FontSize = 12, TextWrapping = TextWrapping.Wrap });
        return new Border { Classes = { "card" }, Child = stack };
    }

    private static Control Section(string label, params Control[] items)
    {
        var stack = new StackPanel { Spacing = 10 };
        stack.Children.Add(new TextBlock { Text = label, Classes = { "label" } });
        foreach (var i in items)
            stack.Children.Add(i);
        return new Border { Classes = { "card" }, Child = stack };
    }

    private static Border Banner(string text, Control action, IBrush accent)
    {
        var c = ((ISolidColorBrush)accent).Color;
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        grid.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0), Foreground = Text });
        Grid.SetColumn(action, 1);
        grid.Children.Add(action);
        return new Border
        {
            Classes = { "banner" }, Child = grid, Margin = new Thickness(0, 16, 0, 0),
            Background = new SolidColorBrush(c, 0.10), BorderBrush = new SolidColorBrush(c, 0.45),
        };
    }

    private static Button Btn(string text, Action click, params string[] classes)
    {
        var b = new Button { Content = text };
        foreach (var c in classes)
            b.Classes.Add(c);
        b.Click += (_, _) => click();
        return b;
    }

    private static Button Link(string text, Action click)
    {
        var b = new Button { Content = text, Classes = { "link" }, HorizontalAlignment = HorizontalAlignment.Left, FontSize = 12.5 };
        b.Click += (_, _) => click();
        return b;
    }

    private static string Pretty(string framework) => framework switch
    {
        "Microsoft.WindowsDesktop.App" => ".NET Desktop Runtime",
        "Microsoft.NETCore.App" => ".NET Runtime",
        "Microsoft.AspNetCore.App" => "ASP.NET Core Runtime",
        _ => framework,
    };

    private static string Shorten(string path)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return path.StartsWith(home, StringComparison.OrdinalIgnoreCase) ? "~" + path[home.Length..] : path;
    }

    private static string Ago(DateTime utc)
    {
        var d = DateTime.UtcNow - utc;
        return d.TotalMinutes < 1 ? "just now"
            : d.TotalHours < 1 ? $"{(int)d.TotalMinutes} min ago"
            : d.TotalDays < 1 ? $"{(int)d.TotalHours} h ago"
            : utc.ToLocalTime().ToString("MMM d");
    }

    private static SolidColorBrush Brush(string hex) => new(Color.Parse(hex));
}
