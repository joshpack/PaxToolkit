using System.Reflection;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using pxdToolkit.Core;
using pxdToolkit.Views;

namespace pxdToolkit;

/// <summary>
/// --self-test DIR: runs the real engine against GitHub with every path pointed at test folders (set PXDTK_DATA,
/// PXDTK_APPS and PXDTK_STARTMENU first), installs and removes one small app, and renders the window off-screen to PNGs.
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
        foreach (var v in new[] { "PXDTK_DATA", "PXDTK_APPS", "PXDTK_STARTMENU" })
            if (Environment.GetEnvironmentVariable(v) is not { Length: > 0 })
                throw new InvalidOperationException($"{v} must point at a test folder.");

        // pxd App Standard link: the tray switch round-trips through toolkit.json without touching other apps' entries.
        if (Environment.GetEnvironmentVariable("PXD_SHARED_DIR") is not { Length: > 0 })
            throw new InvalidOperationException("PXD_SHARED_DIR must point at a test folder.");
        Check("tray icon on by default", pxdStandard.pxdLink.TrayIconVisible("pxdAudioSwitcher"));
        pxdStandard.pxdLink.SetTrayIconVisible("pxdAudioSwitcher", false);
        pxdStandard.pxdLink.SetTrayIconVisible("pxdChatterGuard", true);
        Check("tray icon switched off", !pxdStandard.pxdLink.TrayIconVisible("pxdAudioSwitcher"));
        Check("other app untouched", pxdStandard.pxdLink.TrayIconVisible("pxdChatterGuard"));
        var fired = new TaskCompletionSource();
        using (pxdStandard.pxdLink.Watch(() => fired.TrySetResult()))
        {
            pxdStandard.pxdLink.SetTrayIconVisible("pxdAudioSwitcher", true);
            Check("watcher fires on change", await Task.WhenAny(fired.Task, Task.Delay(3000)) == fired.Task);
        }

        Check("tray icon back on", pxdStandard.pxdLink.TrayIconVisible("pxdAudioSwitcher"));

        using var kit = new Toolkit();
        Line($"catalog: {kit.Apps.Count} apps ({string.Join(", ", kit.Apps.Select(a => a.App.Id))})");
        Check("built-in catalog parses", Catalog.Parse(Catalog.ReadBuiltIn())?.Count == kit.Apps.Count);
        Check("Startup Scanner has its one-liner", kit.Find("pxdStartupScanner")?.App.InstallCommand == "irm pxdtek.com/ss | iex");
        Check("Event Viewer has its one-liner", kit.Find("pxdEventViewer")?.App.InstallCommand == "irm pxdtek.com/ev | iex");
        Check("version parse v0.001-beta", Versions.Parse("v0.001-beta") == new Version(0, 1, 0));
        Check("version parse 1.2.0+abc", Versions.Parse("1.2.0+abc") == new Version(1, 2, 0));

        // Icons come from the catalog's iconUrl, so apps added later get theirs in older toolkits too.
        Check("every catalog app has a GitHub icon URL", kit.Apps.All(a => Views.Icons.UsableUrl(a.App.IconUrl)));
        Check("non-GitHub icon URLs are refused", !Views.Icons.UsableUrl("https://example.com/x.png") && !Views.Icons.UsableUrl("http://raw.githubusercontent.com/x.png"));
        var ev = kit.Find("pxdEventViewer")?.App;
        if (ev is not null)
        {
            var got = await Views.Icons.FetchAsync(ev);
            var cached = Path.Combine(Paths.Data, "icons", "pxdEventViewer.png");
            Check($"Event Viewer icon downloads and is cached ({(got is null ? "none" : $"{got.PixelSize.Width}x{got.PixelSize.Height}")})", got is not null && File.Exists(cached));
        }
        var bad = new CatalogApp { Id = "IconTestMissing", Name = "Icon Test", IconUrl = "https://raw.githubusercontent.com/joshpack/pxdToolkit/main/Assets/icons/does-not-exist.png" };
        Check("a missing icon falls back (no picture, no crash)", await Views.Icons.FetchAsync(bad) is null && Views.Icons.For(bad) is not null);
        // Former names: folders move once, Run command arguments survive, old installs are found.
        var mig = Path.Combine(Dir!, "migrate");
        var oldData = Path.Combine(mig, pxdStandard.pxdMigrate.FormerName("pxdWatermark"));
        Directory.CreateDirectory(oldData);
        File.WriteAllText(Path.Combine(oldData, "settings.json"), "{}");
        Check("former name of pxdWatermark", pxdStandard.pxdMigrate.FormerName("pxdWatermark") == pxdStandard.pxdMigrate.FormerPrefix + "Watermark");
        Check("old data folder moves", pxdStandard.pxdMigrate.Folder(oldData, Path.Combine(mig, "pxdWatermark")) && File.Exists(Path.Combine(mig, "pxdWatermark", "settings.json")) && !Directory.Exists(oldData));
        Check("second move is a no-op", !pxdStandard.pxdMigrate.Folder(oldData, Path.Combine(mig, "pxdWatermark")));
        Check("Run arguments kept", pxdStandard.pxdMigrate.ArgsOf("\"C:\\A B\\x.exe\" --minimized") == "--minimized" && pxdStandard.pxdMigrate.ArgsOf("C:\\x.exe") == "");
        var oldInstall = Path.Combine(kit.Settings.EffectiveAppsRoot, pxdStandard.pxdMigrate.FormerName("pxdDiagnostics"));
        Directory.CreateDirectory(oldInstall);
        File.WriteAllText(Path.Combine(oldInstall, pxdStandard.pxdMigrate.FormerName("pxdDiagnostics.exe")), "");
        Check("old install found", Migration.FormerInstall(kit, kit.Find("pxdDiagnostics")!) is { } found && found.Dir == oldInstall);

        await UpdateSelfTestAsync();

        await kit.CheckAsync();
        Line("check error: " + (kit.LastCheckError ?? "none"));
        foreach (var e in kit.Apps)
            Line($"  {e.App.Id,-18} latest={e.Latest?.Tag ?? (e.NoRelease ? "(no release)" : "?"),-12} asset={e.Download?.Name ?? "-"} {e.CheckError}");

        var diag = kit.Find("pxdDiagnostics")!;
        var movedApps = await Migration.AppsAsync(kit);
        if (diag.Latest?.Find(diag.App.Asset) is null)
            Check("old install kept while there's no pxd release", Directory.Exists(oldInstall) && !diag.IsInstalled);
        else
            Check($"old install moved to the pxd folder ({movedApps})", !Directory.Exists(oldInstall) && diag.IsInstalled && diag.InstallDir!.EndsWith("pxdDiagnostics", StringComparison.OrdinalIgnoreCase));
        if (diag.IsInstalled)
            await kit.UninstallAsync(diag);
        if (Directory.Exists(oldInstall))
            Directory.Delete(oldInstall, true);

        // Install the smallest real app, check it landed, start-menu shortcut and all, then remove it again.
        var target = kit.Find(Arg("--app") ?? "pxdAutoClicker")!;
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
        // A self-updating app (Commander) that moved past our record: its exe's own version counts, no update.
        target.App.SelfUpdates = true;
        kit.Detect(target);
        Check("self-updated app: the exe's newer version wins over the recorded tag", !target.UpdateAvailable && target.InstalledVersion == target.Latest?.Version);
        target.App.SelfUpdates = false;
        kit.Settings.Installed[target.App.Id].Tag = target.Latest!.Tag;
        kit.Detect(target);

        // A script app installs as a single file.
        if (kit.Find("pxdStartupScanner") is { Download: not null } script)
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
        if (kit.Find("pxdStartupScanner") is { IsInstalled: true } s2)
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

        Fake("pxdCommander", null, running: true);
        Fake("pxdAudioSwitcher", "1.1.0", running: true);
        Fake("pxdChatterGuard", null, running: true);
        Fake("pxdStartupScanner", null);
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

        if (kit.Apps.FirstOrDefault(a => a.App.InstallCommand is { Length: > 0 }) is { } withCommand)
        {
            window.Select(withCommand.App.Id);
            Shot(name + "-command");
        }

        window.ShowCommands();
        Shot(name + "-oneliners");

        window.ShowSettings();
        Shot(name + "-settings");
        window.Close();
    }

    /// <summary>Calls another pxd app's static AppIcon.Create(int) from its dll and saves the picture.</summary>
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

    /// <summary>pxdUpdate against a fake release served on localhost: found, checksum enforced, files swapped, old ones cleaned up.</summary>
    private static async Task UpdateSelfTestAsync()
    {
        var root = Path.Combine(Dir!, "update");
        var app = Path.Combine(root, "app");
        var pkg = Path.Combine(root, "pkg", "Demo");
        Directory.CreateDirectory(app);
        Directory.CreateDirectory(pkg);
        File.WriteAllText(Path.Combine(app, "Demo.exe"), "old exe");
        File.WriteAllText(Path.Combine(app, "lib.dll"), "old lib");
        File.WriteAllText(Path.Combine(app, "keep.txt"), "user file");
        File.WriteAllText(Path.Combine(pkg, "Demo.exe"), "new exe");
        File.WriteAllText(Path.Combine(pkg, "lib.dll"), "new lib");
        File.WriteAllText(Path.Combine(pkg, "extra.dll"), "new file");
        var zip = Path.Combine(root, "Demo-2.0.0-win-x64.zip");
        System.IO.Compression.ZipFile.CreateFromDirectory(Path.Combine(root, "pkg"), zip);
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(zip))).ToLowerInvariant();

        var port = new Random().Next(41000, 49000);
        var prefix = $"http://127.0.0.1:{port}/";
        using var listener = new System.Net.HttpListener();
        listener.Prefixes.Add(prefix);
        listener.Start();
        var badSum = false;
        var serving = Task.Run(async () =>
        {
            while (listener.IsListening)
            {
                System.Net.HttpListenerContext ctx;
                try { ctx = await listener.GetContextAsync(); } catch { return; }
                var path = ctx.Request.Url!.AbsolutePath;
                byte[] body = path switch
                {
                    "/repos/joshpack/Demo/releases/latest" => Encoding.UTF8.GetBytes(
                        $"{{\"tag_name\":\"v2.0.0\",\"assets\":[{{\"name\":\"Demo-2.0.0-win-x64.zip\",\"browser_download_url\":\"{prefix}zip\"}},{{\"name\":\"Demo-2.0.0-win-x64.zip.sha256\",\"browser_download_url\":\"{prefix}sum\"}}]}}"),
                    "/zip" => File.ReadAllBytes(zip),
                    "/sum" => Encoding.ASCII.GetBytes((badSum ? new string('0', 64) : hash) + "  Demo-2.0.0-win-x64.zip"),
                    _ => [],
                };
                ctx.Response.StatusCode = body.Length == 0 ? 404 : 200;
                await ctx.Response.OutputStream.WriteAsync(body);
                ctx.Response.Close();
            }
        });

        Environment.SetEnvironmentVariable("PXD_UPDATE_API", prefix.TrimEnd('/'));
        try
        {
            var plan = await pxdStandard.pxdUpdate.FindUpdateAsync("joshpack/Demo", @"^Demo-.*-win-x64\.zip$", new Version(1, 0, 0));
            Check("update: newer release found", plan is { Sum: not null });
            Check("update: current copy left alone", await pxdStandard.pxdUpdate.FindUpdateAsync("joshpack/Demo", @"^Demo-.*-win-x64\.zip$", new Version(2, 0, 0)) is null);

            badSum = true;
            var refused = !await pxdStandard.pxdUpdate.ApplyAsync(plan!.Value.Zip, plan.Value.Sum, app, Path.Combine(app, "Demo.exe"), [], start: false);
            Check("update: a bad checksum changes nothing", refused && File.ReadAllText(Path.Combine(app, "Demo.exe")) == "old exe" && !File.Exists(Path.Combine(app, "extra.dll")));

            badSum = false;
            var ok = await pxdStandard.pxdUpdate.ApplyAsync(plan.Value.Zip, plan.Value.Sum, app, Path.Combine(app, "Demo.exe"), [], start: false);
            Check("update: files swapped", ok && File.ReadAllText(Path.Combine(app, "Demo.exe")) == "new exe" && File.ReadAllText(Path.Combine(app, "lib.dll")) == "new lib"
                                         && File.Exists(Path.Combine(app, "extra.dll")) && File.ReadAllText(Path.Combine(app, "keep.txt")) == "user file");
            Check("update: old files set aside", File.Exists(Path.Combine(app, "Demo.exe.pxdold")) && File.Exists(Path.Combine(app, "lib.dll.pxdold")));
            pxdStandard.pxdUpdate.CleanUp(app);
            Check("update: set-aside files removed at next start", Directory.GetFiles(app, "*.pxdold").Length == 0);
            Check("update: version parse", pxdStandard.pxdUpdate.ParseVersion("1.2.1+abc") == new Version(1, 2, 1) && pxdStandard.pxdUpdate.ParseVersion("v1.4") == new Version(1, 4, 0));
        }
        finally
        {
            Environment.SetEnvironmentVariable("PXD_UPDATE_API", null);
            listener.Stop();
            await Task.WhenAny(serving, Task.Delay(2000));
        }
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
