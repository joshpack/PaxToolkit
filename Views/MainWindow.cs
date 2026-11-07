using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using PaxToolkit.Core;

namespace PaxToolkit.Views;

/// <summary>The toolkit window: every PAX app as a card on the Apps tab, and the toolkit's own options.</summary>
public sealed class MainWindow : Window
{
    private readonly Toolkit _kit;
    private readonly App _app;
    private readonly Dictionary<string, AppCard> _cards = new(StringComparer.OrdinalIgnoreCase);
    private readonly UniformGrid _grid = new() { Columns = 2 };
    private readonly TextBlock _status = new()
    {
        Classes = { "muted" }, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap,
        MaxWidth = 220, TextAlignment = TextAlignment.Right,
    };
    private readonly Button _check = new() { Content = "Check for updates" };
    private readonly Button _updateAll = new() { Classes = { "primary" } };
    private readonly StackPanel _banners = new() { Spacing = 10 };
    private readonly TextBlock _summary = new() { Classes = { "muted" }, Margin = new Thickness(2, 0, 0, 12) };
    private bool _pending;

    /// <summary>README screenshots: no banner showing where this copy happens to run from.</summary>
    public static bool HideSetupBanner { get; set; }

    public MainWindow(Toolkit kit, App app)
    {
        _kit = kit;
        _app = app;
        Title = "PAX Toolkit";
        Icon = AppIcon.Window();
        Width = 1040;
        Height = 760;
        MinWidth = 560;
        MinHeight = 480;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        _check.Click += async (_, _) => await _app.CheckAsync(auto: false);
        _updateAll.Click += async (_, _) => await _app.UpdateAllAsync();

        var tabs = new TabControl { Margin = new Thickness(0, 14, 0, 0) };
        tabs.Items.Add(new TabItem { Header = "Apps", Content = BuildAppsTab() });
        tabs.Items.Add(new TabItem { Header = "Settings", Content = BuildSettingsTab() });

        var root = new DockPanel { Margin = new Thickness(26, 22, 26, 10) };
        var header = BuildHeader();
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);
        DockPanel.SetDock(_banners, Dock.Top);
        _banners.Margin = new Thickness(0, 16, 0, 0);
        root.Children.Add(_banners);
        root.Children.Add(tabs);
        Content = root;

        SizeChanged += (_, e) => _grid.Columns = e.NewSize.Width >= 900 ? 2 : 1;
        _kit.Changed += OnChanged;
        Closed += (_, _) => _kit.Changed -= OnChanged;
        Refresh();
    }

    private Control BuildHeader()
    {
        var logo = new Image { Source = AppIcon.Render(96), Width = 48, Height = 48, VerticalAlignment = VerticalAlignment.Center };
        var title = new StackPanel { Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        title.Children.Add(new TextBlock { Text = "PAX TOOLKIT", FontSize = 26, FontWeight = FontWeight.Bold, LetterSpacing = 2, Classes = { "neon" } });
        title.Children.Add(new TextBlock { Text = "Turn your PAX apps on, keep them up to date and open them from one place.", Classes = { "muted" }, TextWrapping = TextWrapping.Wrap });

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, VerticalAlignment = VerticalAlignment.Center };
        actions.Children.Add(_status);
        actions.Children.Add(_check);
        actions.Children.Add(_updateAll);

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        grid.Children.Add(logo);
        Grid.SetColumn(title, 1);
        grid.Children.Add(title);
        Grid.SetColumn(actions, 2);
        grid.Children.Add(actions);
        return grid;
    }

    private Control BuildAppsTab()
    {
        var stack = new StackPanel { Margin = new Thickness(0, 14, 0, 14) };
        stack.Children.Add(_summary);
        stack.Children.Add(_grid);
        return new ScrollViewer { Content = stack, Padding = new Thickness(0, 0, 12, 0) };
    }

    private Control BuildSettingsTab()
    {
        var s = _kit.Settings;
        var panel = new StackPanel { Spacing = 14, Margin = new Thickness(0, 14, 12, 14), MaxWidth = 720, HorizontalAlignment = HorizontalAlignment.Left };

        var startup = new CheckBox { Content = "Start PAX Toolkit with Windows (in the tray)", IsChecked = Autostart.IsEnabled() };
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

        var check = new CheckBox { Content = "Check GitHub for updates every 6 hours", IsChecked = s.CheckForUpdates };
        var auto = new CheckBox { Content = "Install app updates automatically (running apps are restarted)", IsChecked = s.AutoUpdate };
        var shortcuts = new CheckBox { Content = "Add Start menu shortcuts (Start → PAX Toolkit) for apps I turn on", IsChecked = s.StartMenuShortcuts };
        check.IsCheckedChanged += (_, _) => { s.CheckForUpdates = check.IsChecked == true; s.Save(); };
        auto.IsCheckedChanged += (_, _) => { s.AutoUpdate = auto.IsChecked == true; s.Save(); };
        shortcuts.IsCheckedChanged += (_, _) => { s.StartMenuShortcuts = shortcuts.IsChecked == true; s.Save(); };

        var folder = new TextBox { Text = s.EffectiveAppsRoot, Width = 460 };
        var browse = new Button { Content = "Browse…" };
        browse.Click += async (_, _) =>
        {
            var picked = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Where should PAX apps go?", AllowMultiple = false });
            if (picked.FirstOrDefault()?.TryGetLocalPath() is { } path)
                folder.Text = path;
        };
        folder.LostFocus += (_, _) => SaveRoot(folder);
        folder.TextChanged += (_, _) => DispatcherTimer.RunOnce(() => SaveRoot(folder), TimeSpan.FromMilliseconds(800));
        var folderRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        folderRow.Children.Add(folder);
        folderRow.Children.Add(browse);

        panel.Children.Add(Section("STARTUP", startup));
        panel.Children.Add(Section("UPDATES", check, auto));
        panel.Children.Add(Section("INSTALLING", shortcuts,
            new TextBlock { Text = "Install folder", Margin = new Thickness(0, 6, 0, 0) },
            folderRow,
            new TextBlock { Text = "New apps go into their own folder in here. Apps that are already on stay where they are.", Classes = { "muted" }, FontSize = 12, TextWrapping = TextWrapping.Wrap }));

        var about = new TextBlock { Text = $"PAX Toolkit v{Versions.Show(Versions.Current)} · settings in {Paths.Data}", Classes = { "muted" }, TextWrapping = TextWrapping.Wrap };
        var gh = new Button { Content = "PAX Toolkit on GitHub", Classes = { "ghost" }, HorizontalAlignment = HorizontalAlignment.Left };
        gh.Click += (_, _) => Toolkit.OpenUrl("https://github.com/joshpack/PaxToolkit");
        panel.Children.Add(Section("ABOUT", about, gh));
        return new ScrollViewer { Content = panel };
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

    private static Control Section(string label, params Control[] items)
    {
        var stack = new StackPanel { Spacing = 10 };
        stack.Children.Add(new TextBlock { Text = label, Classes = { "label" } });
        foreach (var i in items)
            stack.Children.Add(i);
        return new Border { Classes = { "card" }, Child = stack };
    }

    private void OnChanged(AppEntry? e)
    {
        // Engine events come from worker threads and in bursts: coalesce into one redraw per frame.
        if (_pending)
            return;
        _pending = true;
        Dispatcher.UIThread.Post(() =>
        {
            _pending = false;
            Refresh();
        }, DispatcherPriority.Background);
    }

    /// <summary>Brings cards, header and banners in line with the engine.</summary>
    public void Refresh()
    {
        var ids = _kit.Apps.Select(a => a.App.Id).ToList();
        if (!ids.SequenceEqual(_cards.Keys) || _grid.Children.Count != ids.Count)
        {
            _grid.Children.Clear();
            foreach (var e in _kit.Apps)
            {
                if (!_cards.TryGetValue(e.App.Id, out var card) || card.Entry != e)
                    _cards[e.App.Id] = card = new AppCard(_kit, e);
                card.Margin = new Thickness(0, 0, 14, 14);
                _grid.Children.Add(card);
            }

            foreach (var gone in _cards.Keys.Except(ids, StringComparer.OrdinalIgnoreCase).ToList())
                _cards.Remove(gone);
        }

        foreach (var card in _cards.Values)
            card.Update();

        var on = _kit.Apps.Count(a => a.IsInstalled);
        var running = _kit.Apps.Count(a => a.IsRunning);
        _summary.Text = $"{on} of {_kit.Apps.Count} apps on" + (running > 0 ? $" · {running} running" : "");

        var updates = _kit.UpdateCount;
        _updateAll.IsVisible = updates > 0;
        _updateAll.Content = $"Update all ({updates})";
        _check.IsEnabled = !_kit.Checking;
        _status.Text = _kit.Checking
            ? "Checking GitHub…"
            : _kit.LastCheckError ?? (_kit.Settings.LastCheckUtc is { } t ? "Checked " + Ago(t) : "");

        BuildBanners();
    }

    private void BuildBanners()
    {
        _banners.Children.Clear();
        if (_kit.ToolkitUpdate is { } upd)
        {
            var btn = new Button { Content = $"Update to v{Versions.Show(upd.Version)}", Classes = { "primary", "small" } };
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
            _banners.Children.Add(Banner($"A new PAX Toolkit is out (v{Versions.Show(upd.Version)}).", btn));
        }

        if (!SelfUpdate.RunningFromHome && !SelfUpdate.IsDevBuild && !HideSetupBanner)
        {
            var btn = new Button { Content = "Move it there", Classes = { "small" } };
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
            _banners.Children.Add(Banner($"PAX Toolkit is running from {Path.GetDirectoryName(SelfUpdate.CurrentExe)}. Keep it in {Paths.ToolkitHome} with a Start menu shortcut?", btn));
        }
    }

    private static Border Banner(string text, Control action)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        grid.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) });
        Grid.SetColumn(action, 1);
        grid.Children.Add(action);
        return new Border { Classes = { "banner" }, Child = grid };
    }

    private static string Ago(DateTime utc)
    {
        var d = DateTime.UtcNow - utc;
        return d.TotalMinutes < 1 ? "just now"
            : d.TotalHours < 1 ? $"{(int)d.TotalMinutes} min ago"
            : d.TotalDays < 1 ? $"{(int)d.TotalHours} h ago"
            : utc.ToLocalTime().ToString("MMM d");
    }
}
