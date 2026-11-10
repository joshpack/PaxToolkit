using System.Diagnostics;
using System.IO.Pipes;
using Avalonia;
using Avalonia.Headless;

namespace pxdToolkit;

internal static class Program
{
    public const string PipeName = "pxdToolkit.commands";
    private static Mutex? _instance;

    /// <summary>Started at sign-in (or with --minimized): stay in the tray.</summary>
    public static bool StartMinimized { get; private set; }

    [STAThread]
    public static int Main(string[] args)
    {
        StartMinimized = Has(args, "--minimized");

        // pxd App Standard: a newer release is installed before the toolkit opens (skipped for dev builds and self-tests).
        if (pxdStandard.pxdUpdate.BeforeStart("joshpack/pxdToolkit", Core.SelfUpdate.AssetPattern, args))
            return 0;

        var test = Array.FindIndex(args, a => a.Equals("--self-test", StringComparison.OrdinalIgnoreCase));
        if (test >= 0 && test + 1 < args.Length)
        {
            SelfTest.Dir = args[test + 1];
            SelfTest.Args = args;
            // Headless: windows given a far-off position still flash on screen for a moment on the real platform.
            AppBuilder.Configure<App>()
                .UseSkia()
                .UseHeadless(new Avalonia.Headless.AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .StartWithClassicDesktopLifetime(args);
            return SelfTest.ExitCode;
        }

        // After an update or a move, the old copy is still closing: give it a moment to let go.
        var wait = Array.FindIndex(args, a => a.Equals("--wait-for", StringComparison.OrdinalIgnoreCase));
        if (wait >= 0 && wait + 1 < args.Length && int.TryParse(args[wait + 1], out var pid))
        {
            try
            {
                using var old = Process.GetProcessById(pid);
                old.WaitForExit(15000);
            }
            catch
            {
                /* already gone */
            }
        }

        _instance = new Mutex(true, @"Local\pxdToolkit.instance", out var first);
        if (!first)
            return StartMinimized ? 0 : Forward(["--show"]) ? 0 : 1;   // already running: a tray start changes nothing

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        return 0;
    }

    public static bool Has(string[] args, string flag) => args.Any(a => a.Equals(flag, StringComparison.OrdinalIgnoreCase));

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .With(new Win32PlatformOptions { RenderingMode = [Win32RenderingMode.Software] })
            .LogToTrace();

    /// <summary>Lets go of the single-instance lock and quits, so a freshly started copy can take over.</summary>
    public static void ExitForRestart()
    {
        try
        {
            _instance?.ReleaseMutex();
            _instance?.Dispose();
            _instance = null;
        }
        catch
        {
            /* not ours any more */
        }

        (Application.Current as App)?.Quit();
    }

    private static bool Forward(string[] args)
    {
        try
        {
            using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            pipe.Connect(2000);
            using var writer = new StreamWriter(pipe);
            writer.Write(string.Join("\n", args));
            return true;
        }
        catch
        {
            return false;
        }
    }
}
