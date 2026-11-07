using System.Reflection;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using PaxToolkit.Core;
using PaxToolkit.Views;

namespace PaxToolkit;

/// <summary>
/// --self-test DIR: runs the real engine against GitHub with every path pointed at test folders (set PAXTK_DATA,
/// PAXTK_APPS and PAXTK_STARTMENU first), installs and removes one small app, and renders the window off-screen to PNGs.
/// Nothing appears on screen. Extra modes: --icon OUT.ico, --render-icon DLL TYPE OUT.png.
/// </summary>
internal static class SelfTest
{
    public static string? Dir { get; set; }
    public static string[] Args { get; set; } = [];
    public static int ExitCode { get; private set; }

    private static readonly StringBuilder R = new();

    public static async Task RunAsync()
    {
        try
        {
            Directory.CreateDirectory(Dir!);
            if (Arg("--icon") is { } ico)
            {
                AppIcon.WriteIco(ico);
                AppIcon.Render(256).Save(Path.ChangeExtension(ico, ".png"));
                Line("icon written");
            }
            else if (Array.FindIndex(Args, a => a == "--render-icon") is var i and >= 0)
                RenderForeignIcon(Args[i + 1], Args[i + 2], Args[i + 3]);
            else if (Args.Contains("--screenshot"))
                await ScreenshotAsync();
            else
                await FullAsync();
        }
        catch (Exception ex)
        {
            Fail("CRASH: " + ex);
        }

        File.WriteAllText(Path.Combine(Dir!, "report.txt"), R.ToString());
    }

    private static async Task FullAsync()
    {
        foreach (var v in new[] { "PAXTK_DATA", "PAXTK_APPS", "PAXTK_STARTMENU" })
            if (Environment.GetEnvironmentVariable(v) is not { Length: > 0 })
                throw new InvalidOperationException($"{v} must point at a test folder.");

        // PAX App Standard link: the tray switch round-trips through toolkit.json without touching other apps' entries.
        if (Environment.GetEnvironmentVariable("PAX_SHARED_DIR") is not { Length: > 0 })
            throw new InvalidOperationException("PAX_SHARED_DIR must point at a test folder.");
        Check("tray icon on by default", PaxStandard.PaxLink.TrayIconVisible("PaxAudioSwitcher"));
        PaxStandard.PaxLink.SetTrayIconVisible("PaxAudioSwitcher", false);
        PaxStandard.PaxLink.SetTrayIconVisible("PaxChatterGuard", true);
        Check("tray icon switched off", !PaxStandard.PaxLink.TrayIconVisible("PaxAudioSwitcher"));
        Check("other app untouched", PaxStandard.PaxLink.TrayIconVisible("PaxChatterGuard"));
        var fired = new TaskCompletionSource();
        using (PaxStandard.PaxLink.Watch(() => fired.TrySetResult()))
        {
            PaxStandard.PaxLink.SetTrayIconVisible("PaxAudioSwitcher", true);
            Check("watcher fires on change", await Task.WhenAny(fired.Task, Task.Delay(3000)) == fired.Task);
        }

        Check("tray icon back on", PaxStandard.PaxLink.TrayIconVisible("PaxAudioSwitcher"));

        using var kit = new Toolkit();
        Line($"catalog: {kit.Apps.Count} apps ({string.Join(", ", kit.Apps.Select(a => a.App.Id))})");
        Check("built-in catalog parses", Catalog.Parse(Catalog.ReadBuiltIn())?.Count == kit.Apps.Count);
        Check("version parse v0.001-beta", Versions.Parse("v0.001-beta") == new Version(0, 1, 0));
        Check("version parse 1.2.0+abc", Versions.Parse("1.2.0+abc") == new Version(1, 2, 0));

        await kit.CheckAsync();
        Line("check error: " + (kit.LastCheckError ?? "none"));
        foreach (var e in kit.Apps)
            Line($"  {e.App.Id,-18} latest={e.Latest?.Tag ?? (e.NoRelease ? "(no release)" : "?"),-12} asset={e.Download?.Name ?? "-"} {e.CheckError}");

        // Install the smallest real app, check it landed, start-menu shortcut and all, then remove it again.
        var target = kit.Find(Arg("--app") ?? "PaxAutoClicker")!;
        Check($"{target.App.Id} not installed before", !target.IsInstalled);
        await kit.InstallAsync(target);
        Line($"installed: dir={target.InstallDir} exe={target.ExePath} version={Versions.Show(target.InstalledVersion)} runtimes missing={target.MissingRuntimes.Count}");
        Check("exe present after install", target.ExePath is { } exe && File.Exists(exe));
        Check("installed under test root", target.InstallDir?.StartsWith(Paths.DefaultAppsRoot, StringComparison.OrdinalIgnoreCase) == true);
        Check("record saved", Settings.Load().Installed.ContainsKey(target.App.Id));
        Check("version matches release", target.InstalledVersion == target.Latest?.Version);
        Check("no update offered right after install", !target.UpdateAvailable);
        Check("start menu shortcut", File.Exists(Path.Combine(Paths.StartMenu, target.App.Name + ".lnk")));
        Check("not running", !target.IsRunning);

        // Fake an older install: an update must be offered.
        kit.Settings.Installed[target.App.Id].Tag = "v0.0.1";
        kit.Detect(target);
        Check("update offered for older version", target.UpdateAvailable);
        kit.Settings.Installed[target.App.Id].Tag = target.Latest!.Tag;
        kit.Detect(target);

        // A script app installs as a single file.
        if (kit.Find("PaxStartupScanner") is { Download: not null } script)
        {
            await kit.InstallAsync(script);
            Check("script installed", script.ExePath is { } p && File.Exists(p) && p.EndsWith(".ps1"));
            Check("script version read", script.InstalledVersion == script.Latest?.Version);
        }

        // Render the window with one app on.
        RenderWindow(kit, "window");

        await kit.UninstallAsync(target);
        Check("removed", !target.IsInstalled && !Directory.Exists(Path.Combine(Paths.DefaultAppsRoot, target.App.Folder)));
        Check("shortcut removed", !File.Exists(Path.Combine(Paths.StartMenu, target.App.Name + ".lnk")));
        Check("record removed", !Settings.Load().Installed.ContainsKey(target.App.Id));
        if (kit.Find("PaxStartupScanner") is { IsInstalled: true } s2)
            await kit.UninstallAsync(s2);

        RenderWindow(kit, "window-after");
        Line(ExitCode == 0 ? "ALL PASSED" : "SOME CHECKS FAILED");
    }

    /// <summary>README picture: real release info from GitHub, made-up install state (nothing is installed).</summary>
    private static async Task ScreenshotAsync()
    {
        MainWindow.HideSetupBanner = true;
        using var kit = new Toolkit(new Settings { LastCheckUtc = DateTime.UtcNow });
        await kit.CheckAsync();
        var apps = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Apps");
        void Fake(string id, string? version, bool running = false)
        {
            if (kit.Find(id) is not { } e)
                return;
            e.InstallDir = Path.Combine(apps, e.App.Folder);
            e.ExePath = Path.Combine(e.InstallDir, e.App.Exe);
            e.InstalledVersion = Versions.Parse(version) ?? e.Latest?.Version;
            e.IsRunning = running;
            e.MissingRuntimes = [];
        }

        foreach (var e in kit.Apps)
        {
            e.InstallDir = e.ExePath = null;
            e.IsRunning = false;
        }

        Fake("PaxCommander", null, running: true);
        Fake("PaxAudioSwitcher", "1.1.0", running: true);
        Fake("PaxChatterGuard", null, running: true);
        Fake("PaxStartupScanner", null);
        RenderWindow(kit, "screenshot");
    }

    private static void RenderWindow(Toolkit kit, string name)
    {
        var app = (App)Application.Current!;
        var window = new MainWindow(kit, app)
        {
            WindowStartupLocation = WindowStartupLocation.Manual,
            Position = new PixelPoint(-30000, -30000),
            ShowActivated = false,
            ShowInTaskbar = false,
            Width = 1000,
            Height = 680,
        };
        window.Show();

        void Shot(string file)
        {
            window.UpdateLayout();
            using var bmp = new RenderTargetBitmap(new PixelSize(1000, 680));
            bmp.Render(window);
            bmp.Save(Path.Combine(Dir!, file + ".png"));
            Line($"rendered {file}.png");
        }

        // An installed app (one with a tray switch if there is one), an available one, and Settings.
        var installed = kit.Apps.FirstOrDefault(a => a.IsInstalled && a.App.Tray) ?? kit.Apps.FirstOrDefault(a => a.IsInstalled);
        if (installed is not null)
        {
            window.Select(installed.App.Id);
            Shot(name);
        }

        if (kit.Apps.FirstOrDefault(a => !a.IsInstalled && a.Download is not null) is { } available)
        {
            window.Select(available.App.Id);
            Shot(name + "-available");
        }

        window.ShowSettings();
        Shot(name + "-settings");
        window.Close();
    }

    /// <summary>Calls another PAX app's static AppIcon.Create(int) from its dll and saves the picture.</summary>
    private static void RenderForeignIcon(string dll, string type, string output)
    {
        var asm = Assembly.LoadFrom(dll);
        var method = "Create";
        if (type.Split("::") is [var typeName, var methodName])
            (type, method) = (typeName, methodName);
        var t = asm.GetType(type, throwOnError: true)!;
        var create = t.GetMethods(BindingFlags.Public | BindingFlags.Static).First(m => m.Name == method && m.ReturnType == typeof(WindowIcon));
        var args = create.GetParameters().Select(p => p.ParameterType == typeof(int) ? 256 : p.HasDefaultValue ? p.DefaultValue : null).ToArray();
        var icon = (WindowIcon)create.Invoke(null, args)!;
        using var fs = File.Create(output);
        icon.Save(fs);
        Line("rendered " + output);
    }

    private static IEnumerable<T> GetLogicalDescendants<T>(this Control root)
    {
        foreach (var c in Avalonia.LogicalTree.LogicalExtensions.GetLogicalDescendants(root))
            if (c is T t)
                yield return t;
    }

    private static string? Arg(string name)
    {
        var i = Array.FindIndex(Args, a => a.Equals(name, StringComparison.OrdinalIgnoreCase));
        return i >= 0 && i + 1 < Args.Length ? Args[i + 1] : null;
    }

    private static void Check(string what, bool ok)
    {
        Line((ok ? "PASS " : "FAIL ") + what);
        if (!ok)
            ExitCode = 1;
    }

    private static void Fail(string text)
    {
        Line(text);
        ExitCode = 1;
    }

    private static void Line(string text) => R.AppendLine(text);
}
