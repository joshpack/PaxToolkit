using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using PaxToolkit.Core;

namespace PaxToolkit.Views;

/// <summary>One app: its picture, status, the on/off switch and what you can do with it.</summary>
public sealed class AppCard : Border
{
    private static readonly IBrush Muted = Brush("#8A92A6");
    private static readonly IBrush Good = Brush("#19F58C");
    private static readonly IBrush Info = Brush("#00C8FF");
    private static readonly IBrush Warn = Brush("#F5B14C");
    private static readonly IBrush Bad = Brush("#F2666A");

    private readonly Toolkit _kit;
    private readonly AppEntry _e;
    private readonly Image _icon = new() { Width = 52, Height = 52 };
    private readonly TextBlock _name = new() { FontSize = 16, FontWeight = FontWeight.Bold };
    private readonly TextBlock _tagline = new() { FontSize = 12, Foreground = Muted };
    private readonly Border _pill = new() { Classes = { "pill" } };
    private readonly TextBlock _pillText = new();
    private readonly ToggleSwitch _switch = new() { OnContent = "", OffContent = "", VerticalAlignment = VerticalAlignment.Center, MinWidth = 0 };
    private readonly TextBlock _desc = new() { TextWrapping = TextWrapping.Wrap, Foreground = Brush("#C3C8D4"), Margin = new Thickness(0, 12, 0, 0) };
    private readonly TextBlock _meta = new() { FontSize = 12, Foreground = Muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
    private readonly ProgressBar _progress = new() { Margin = new Thickness(0, 10, 0, 0), Minimum = 0, Maximum = 1 };
    private readonly TextBlock _busy = new() { FontSize = 12, Foreground = Info, Margin = new Thickness(0, 6, 0, 0) };
    private readonly TextBlock _error = new() { FontSize = 12, Foreground = Bad, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
    private readonly StackPanel _runtime = new() { Orientation = Orientation.Horizontal, Spacing = 10, Margin = new Thickness(0, 8, 0, 0) };
    private readonly TextBlock _runtimeText = new() { FontSize = 12, Foreground = Warn, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
    private readonly Border _confirm = new() { Classes = { "banner" }, Margin = new Thickness(0, 12, 0, 0), Background = Brush("#2A1518"), BorderBrush = Brush("#6B2D33") };
    private readonly WrapPanel _buttons = new() { Orientation = Orientation.Horizontal, ItemSpacing = 8, LineSpacing = 8, Margin = new Thickness(0, 14, 0, 0) };
    private bool _updating;

    public AppCard(Toolkit kit, AppEntry entry)
    {
        _kit = kit;
        _e = entry;
        Classes.Add("card");
        Margin = new Thickness(0, 0, 0, 0);

        _pill.Child = _pillText;
        var titles = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 10, 0) };
        titles.Children.Add(_name);
        titles.Children.Add(_tagline);

        var top = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto") };
        top.Children.Add(_icon);
        Grid.SetColumn(titles, 1);
        top.Children.Add(titles);
        Grid.SetColumn(_pill, 2);
        _pill.Margin = new Thickness(0, 0, 12, 0);
        top.Children.Add(_pill);
        Grid.SetColumn(_switch, 3);
        top.Children.Add(_switch);
        _switch.IsCheckedChanged += (_, _) => OnSwitch();

        var installRuntime = new Button { Content = "Install it", Classes = { "small" } };
        installRuntime.Click += async (_, _) => await InstallRuntimeAsync(installRuntime);
        _runtime.Children.Add(_runtimeText);
        _runtime.Children.Add(installRuntime);

        var confirmText = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = Brush("#FFD6D7") };
        var yes = new Button { Content = "Turn off", Classes = { "small", "warn" } };
        var no = new Button { Content = "Keep it", Classes = { "small" } };
        yes.Click += async (_, _) =>
        {
            _confirm.IsVisible = false;
            await Run(() => _kit.UninstallAsync(_e));
        };
        no.Click += (_, _) =>
        {
            _confirm.IsVisible = false;
            Update();
        };
        var confirmButtons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 10, 0, 0) };
        confirmButtons.Children.Add(yes);
        confirmButtons.Children.Add(no);
        var confirmStack = new StackPanel();
        confirmStack.Children.Add(confirmText);
        confirmStack.Children.Add(confirmButtons);
        _confirm.Child = confirmStack;
        _confirm.IsVisible = false;
        _confirm.Tag = confirmText;

        var body = new StackPanel();
        body.Children.Add(top);
        body.Children.Add(_desc);
        body.Children.Add(_meta);
        body.Children.Add(_progress);
        body.Children.Add(_busy);
        body.Children.Add(_runtime);
        body.Children.Add(_error);
        body.Children.Add(_confirm);
        body.Children.Add(_buttons);
        Child = body;

        _icon.Source = LoadIcon(entry.App);
        Update();
    }

    public AppEntry Entry => _e;

    /// <summary>Redraws everything from the entry's current state.</summary>
    public void Update()
    {
        _updating = true;
        var app = _e.App;
        _name.Text = app.Name;
        _tagline.Text = app.Tagline;
        _desc.Text = app.Description;

        _switch.IsChecked = _e.IsInstalled;
        _switch.IsEnabled = !_e.Busy && (_e.IsInstalled || _e.Download is not null || _e.Latest is null && !_e.NoRelease);

        var (pill, color) = _e switch
        {
            { Busy: true } => ("WORKING", Info),
            { IsInstalled: true, UpdateAvailable: true } => ("UPDATE", Warn),
            { IsRunning: true } => ("RUNNING", Good),
            { IsInstalled: true } => ("ON", Info),
            { NoRelease: true } => ("NOT RELEASED", Muted),
            _ => ("OFF", Muted),
        };
        _pillText.Text = pill;
        _pillText.Foreground = color;
        _pill.BorderBrush = color;
        _pill.Background = new SolidColorBrush(((ISolidColorBrush)color).Color, 0.12);

        _meta.Text = MetaText();

        _progress.IsVisible = _e.Busy;
        _progress.IsIndeterminate = _e.Progress < 0;
        if (_e.Progress >= 0)
            _progress.Value = _e.Progress;
        _busy.IsVisible = _e.Busy && _e.BusyText is not null;
        _busy.Text = _e.BusyText;

        _error.IsVisible = _e.Error is not null;
        _error.Text = _e.Error;

        _runtime.IsVisible = _e.IsInstalled && _e.MissingRuntimes.Count > 0 && !_e.Busy;
        if (_runtime.IsVisible)
            _runtimeText.Text = "Needs " + string.Join(" and ", _e.MissingRuntimes.Select(r => $"{Pretty(r.Name)} {r.Major}")) + " to run.";

        BuildButtons();
        _updating = false;
    }

    private string MetaText()
    {
        if (_e.IsInstalled)
        {
            var where = _e.External ? "installed with its own setup" : "in " + Shorten(_e.InstallDir);
            var text = $"v{Versions.Show(_e.InstalledVersion)} · {where}";
            if (_e.UpdateAvailable)
                text += $" · v{Versions.Show(_e.Latest!.Version)} is out ({Sizes.Show(_e.Download!.Size)})";
            else if (_e.Latest is not null)
                text += " · up to date";
            return text;
        }

        if (_e.NoRelease)
            return "No download on GitHub yet.";
        if (_e.Latest is { } latest)
            return _e.Download is { } d ? $"v{Versions.Show(latest.Version)} · {Sizes.Show(d.Size)} download" : $"The {latest.Tag} release has no Windows download yet.";
        return _e.CheckError ?? "Checking GitHub…";
    }

    private void BuildButtons()
    {
        _buttons.Children.Clear();
        if (_e.Busy)
            return;

        if (!_e.IsInstalled)
        {
            if (_e.Download is not null || _e.Latest is null && !_e.NoRelease)
                _buttons.Children.Add(Btn("Turn on", () => _ = Run(() => _kit.InstallAsync(_e)), primary: true));
            _buttons.Children.Add(Btn("View on GitHub", () => Toolkit.OpenUrl(_e.App.RepoUrl), ghost: true));
            return;
        }

        if (_e.UpdateAvailable)
            _buttons.Children.Add(Btn($"Update to v{Versions.Show(_e.Latest!.Version)}", () => _ = Run(() => _kit.InstallAsync(_e)), primary: true));

        _buttons.Children.Add(Btn(_e.App.IsScript ? "Run" : "Open", () => _kit.Launch(_e), primary: !_e.UpdateAvailable));
        if (_e.App.GuiArgs is not null)
            _buttons.Children.Add(Btn("Open window", () => _kit.Launch(_e, gui: true)));
        if (_e.App.TrayArgs is not null && !_e.IsRunning)
            _buttons.Children.Add(Btn("Start in tray", () => _kit.Launch(_e, tray: true)));
        if (_e.IsRunning)
            _buttons.Children.Add(Btn("Close", () => _kit.StopApp(_e)));

        var more = new Button { Content = "More ▾", Classes = { "small", "ghost" } };
        var menu = new MenuFlyout();
        menu.Items.Add(Item("Open folder", () => _kit.OpenFolder(_e)));
        if (_e.Latest is { Url.Length: > 0 } rel)
            menu.Items.Add(Item("What's new", () => Toolkit.OpenUrl(rel.Url)));
        menu.Items.Add(Item("View on GitHub", () => Toolkit.OpenUrl(_e.App.RepoUrl)));
        if (_e.Download is not null && !_e.UpdateAvailable)
            menu.Items.Add(Item("Reinstall", () => _ = Run(() => _kit.InstallAsync(_e))));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Turn off…", AskTurnOff));
        more.Flyout = menu;
        _buttons.Children.Add(more);
    }

    private void OnSwitch()
    {
        if (_updating)
            return;
        if (_switch.IsChecked == true && !_e.IsInstalled)
            _ = Run(() => _kit.InstallAsync(_e));
        else if (_switch.IsChecked == false && _e.IsInstalled)
            AskTurnOff();
    }

    private void AskTurnOff()
    {
        var where = _e.External ? "Its own uninstaller is run" : "Its files are removed";
        ((TextBlock)_confirm.Tag!).Text = $"Turn off {_e.App.Name}? It's closed if it's running. {where}; your settings stay, so turning it back on picks up where you left off.";
        _confirm.IsVisible = true;
        _updating = true;
        _switch.IsChecked = true;
        _updating = false;
    }

    private async Task Run(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch
        {
            /* the error is on the entry and shows on the card */
        }
    }

    private async Task InstallRuntimeAsync(Button button)
    {
        button.IsEnabled = false;
        button.Content = "Installing…";
        var ok = true;
        foreach (var (name, major) in _e.MissingRuntimes.ToList())
            ok &= await Runtimes.InstallAsync(Runtimes.WingetId(name, major));
        if (!ok)
            Toolkit.OpenUrl($"{Runtimes.DownloadPage}/{_e.MissingRuntimes.FirstOrDefault().Major}.0");
        button.IsEnabled = true;
        button.Content = "Install it";
        _kit.Detect(_e);
        Update();
    }

    private static string Pretty(string framework) => framework switch
    {
        "Microsoft.WindowsDesktop.App" => ".NET Desktop Runtime",
        "Microsoft.NETCore.App" => ".NET Runtime",
        "Microsoft.AspNetCore.App" => "ASP.NET Core Runtime",
        _ => framework,
    };

    private static string Shorten(string? path)
    {
        if (path is null)
            return "";
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return path.StartsWith(home, StringComparison.OrdinalIgnoreCase) ? "~" + path[home.Length..] : path;
    }

    private static Button Btn(string text, Action click, bool primary = false, bool ghost = false)
    {
        var b = new Button { Content = text, Classes = { "small" } };
        if (primary)
            b.Classes.Add("primary");
        if (ghost)
            b.Classes.Add("ghost");
        b.Click += (_, _) => click();
        return b;
    }

    private static MenuItem Item(string text, Action click)
    {
        var m = new MenuItem { Header = text };
        m.Click += (_, _) => click();
        return m;
    }

    private static readonly Dictionary<string, Bitmap> IconCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The built-in picture for the app, or a tile with its initials.</summary>
    public static Bitmap LoadIcon(CatalogApp app)
    {
        if (IconCache.TryGetValue(app.Id, out var cached))
            return cached;
        Bitmap bmp;
        var uri = new Uri($"avares://PaxToolkit/Assets/icons/{app.Id}.png");
        try
        {
            bmp = AssetLoader.Exists(uri) ? new Bitmap(AssetLoader.Open(uri)) : AppIcon.Monogram(app.Name, app.Color);
        }
        catch
        {
            bmp = AppIcon.Monogram(app.Name, app.Color);
        }

        IconCache[app.Id] = bmp;
        return bmp;
    }

    private static SolidColorBrush Brush(string hex) => new(Color.Parse(hex));
}
