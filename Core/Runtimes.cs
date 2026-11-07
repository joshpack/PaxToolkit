using System.Diagnostics;
using System.Text.Json;

namespace PaxToolkit.Core;

/// <summary>Checks whether the .NET runtimes an app was built against are on this PC.</summary>
public static class Runtimes
{
    public const string DownloadPage = "https://dotnet.microsoft.com/download/dotnet";

    private static string DotnetRoot =>
        Environment.GetEnvironmentVariable("DOTNET_ROOT") is { Length: > 0 } r
            ? r
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet");

    /// <summary>Frameworks named in the app's runtimeconfig.json that aren't installed, e.g. "Microsoft.WindowsDesktop.App 10".</summary>
    public static List<(string Name, int Major)> Missing(string exePath)
    {
        var missing = new List<(string, int)>();
        try
        {
            var config = Path.ChangeExtension(exePath, ".runtimeconfig.json");
            if (!File.Exists(config))
                return missing;   // self-contained or not .NET: nothing to check
            using var doc = JsonDocument.Parse(File.ReadAllText(config));
            var opts = doc.RootElement.GetProperty("runtimeOptions");
            var list = new List<JsonElement>();
            if (opts.TryGetProperty("framework", out var one))
                list.Add(one);
            if (opts.TryGetProperty("frameworks", out var many))
                list.AddRange(many.EnumerateArray());
            foreach (var f in list)
            {
                var name = f.GetProperty("name").GetString() ?? "";
                var major = Versions.Parse(f.GetProperty("version").GetString())?.Major ?? 0;
                if (!Has(name, major))
                    missing.Add((name, major));
            }
        }
        catch
        {
            /* unreadable config: let the app report it itself */
        }

        return missing;
    }

    public static bool Has(string framework, int major)
    {
        var dir = Path.Combine(DotnetRoot, "shared", framework);
        if (!Directory.Exists(dir))
            return false;
        return Directory.EnumerateDirectories(dir).Any(d => Versions.Parse(Path.GetFileName(d)) is { } v && v.Major == major);
    }

    /// <summary>winget package for a missing framework (the desktop runtime brings the base one with it).</summary>
    public static string WingetId(string framework, int major) =>
        framework.Contains("WindowsDesktop", StringComparison.OrdinalIgnoreCase) || framework.Contains("NETCore", StringComparison.OrdinalIgnoreCase)
            ? $"Microsoft.DotNet.DesktopRuntime.{major}"
            : framework.Contains("AspNetCore", StringComparison.OrdinalIgnoreCase)
                ? $"Microsoft.DotNet.AspNetCore.{major}"
                : $"Microsoft.DotNet.Runtime.{major}";

    /// <summary>Installs a runtime with winget (Windows shows its own UAC prompt). False if winget isn't there.</summary>
    public static async Task<bool> InstallAsync(string wingetId)
    {
        try
        {
            var psi = new ProcessStartInfo("winget",
                $"install --id {wingetId} -e --silent --accept-package-agreements --accept-source-agreements --disable-interactivity")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var p = Process.Start(psi);
            if (p is null)
                return false;
            await p.WaitForExitAsync();
            return p.ExitCode == 0 || p.ExitCode == -1978335189;   // 0x8A15002B: already installed
        }
        catch
        {
            return false;
        }
    }
}
