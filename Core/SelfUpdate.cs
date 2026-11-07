using System.Diagnostics;
using System.IO.Compression;

namespace PaxToolkit.Core;

/// <summary>The toolkit moving itself into its own folder, and replacing itself with a newer release.</summary>
public static class SelfUpdate
{
    public const string AssetPattern = @"^PaxToolkit-.*-win-x64\.zip$";
    private const string ExeName = "PaxToolkit.exe";

    public static string? CurrentExe => Environment.ProcessPath;

    public static string HomeExe => Path.Combine(Paths.ToolkitHome, ExeName);

    /// <summary>Running from its own folder (not Downloads or a build folder).</summary>
    public static bool RunningFromHome =>
        CurrentExe is { } exe && string.Equals(Path.GetFullPath(exe), Path.GetFullPath(HomeExe), StringComparison.OrdinalIgnoreCase);

    /// <summary>A developer build (dotnet run / bin folder): never offer to move or replace it.</summary>
    public static bool IsDevBuild =>
        CurrentExe is not { } exe || exe.Contains(@"\bin\", StringComparison.OrdinalIgnoreCase)
                                  || Path.GetFileNameWithoutExtension(exe).Equals("dotnet", StringComparison.OrdinalIgnoreCase);

    /// <summary>Removes the copy an update left behind.</summary>
    public static void CleanUp()
    {
        try
        {
            if (CurrentExe is { } exe && File.Exists(exe + ".old"))
                File.Delete(exe + ".old");
        }
        catch
        {
            /* still locked: next start */
        }
    }

    /// <summary>Copies this exe to %USERPROFILE%\Apps\PaxToolkit, adds Start menu shortcuts and starts that copy.</summary>
    public static void InstallHere()
    {
        Directory.CreateDirectory(Paths.ToolkitHome);
        File.Copy(CurrentExe!, HomeExe, overwrite: true);
        CreateShortcuts();
        Restart(HomeExe);
    }

    public static void CreateShortcuts()
    {
        try
        {
            Shortcut.Create(Path.Combine(Paths.StartMenu, "PAX Toolkit.lnk"), HomeExe, null, Paths.ToolkitHome, "Install, update and open your PAX apps");
        }
        catch
        {
            /* optional */
        }
    }

    /// <summary>Downloads the release, swaps the exe (a running exe can be renamed, not overwritten) and restarts.</summary>
    public static async Task ApplyAsync(HttpClient http, Release release, Action<double> progress, CancellationToken ct = default)
    {
        var asset = release.Find(AssetPattern) ?? throw new InvalidOperationException("That release has no Windows download.");
        var exe = CurrentExe ?? throw new InvalidOperationException("Can't tell where PAX Toolkit is running from.");
        var stage = Path.Combine(Paths.Downloads, "toolkit-update");
        if (Directory.Exists(stage))
            Directory.Delete(stage, true);
        Directory.CreateDirectory(stage);
        var zip = Path.Combine(stage, asset.Name);

        using (var res = await http.GetAsync(asset.Url, HttpCompletionOption.ResponseHeadersRead, ct))
        {
            res.EnsureSuccessStatusCode();
            var total = res.Content.Headers.ContentLength ?? asset.Size;
            await using var src = await res.Content.ReadAsStreamAsync(ct);
            await using var dst = File.Create(zip);
            var buffer = new byte[1 << 16];
            long done = 0;
            int n;
            while ((n = await src.ReadAsync(buffer, ct)) > 0)
            {
                await dst.WriteAsync(buffer.AsMemory(0, n), ct);
                done += n;
                if (total > 0)
                    progress((double)done / total);
            }
        }

        ZipFile.ExtractToDirectory(zip, stage);
        var fresh = Directory.EnumerateFiles(stage, ExeName, SearchOption.AllDirectories).FirstOrDefault()
                    ?? throw new InvalidOperationException("The download has no PaxToolkit.exe in it.");

        if (File.Exists(exe + ".old"))
            File.Delete(exe + ".old");
        File.Move(exe, exe + ".old");
        try
        {
            File.Copy(fresh, exe);
        }
        catch
        {
            File.Move(exe + ".old", exe);   // put the working copy back
            throw;
        }

        Directory.Delete(stage, true);
        Restart(exe);
    }

    /// <summary>Starts <paramref name="exe"/>, which waits for this process to exit, then quits this one.</summary>
    private static void Restart(string exe)
    {
        Process.Start(new ProcessStartInfo(exe, $"--show --wait-for {Environment.ProcessId}") { UseShellExecute = true })?.Dispose();
        Program.ExitForRestart();
    }
}
