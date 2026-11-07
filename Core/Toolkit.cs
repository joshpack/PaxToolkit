using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace PaxToolkit.Core;

/// <summary>
/// The engine: knows every catalog app, finds what's installed, checks GitHub for new releases, and installs, updates,
/// removes and starts apps. UI-free; <see cref="Changed"/> fires (on any thread) whenever something worth redrawing changes.
/// </summary>
public sealed class Toolkit : IDisposable
{
    private readonly HttpClient _http = GitHub.CreateClient();
    private readonly GitHub _github;
    private readonly SemaphoreSlim _checkLock = new(1, 1);

    public Settings Settings { get; }
    public List<AppEntry> Apps { get; } = [];

    /// <summary>The toolkit's own newest release, when it is newer than this copy.</summary>
    public Release? ToolkitUpdate { get; private set; }

    public string? LastCheckError { get; private set; }
    public bool Checking { get; private set; }

    public event Action<AppEntry?>? Changed;

    public Toolkit(Settings? settings = null)
    {
        _github = new GitHub(_http);
        Settings = settings ?? Settings.Load();
        SetCatalog(Catalog.LoadLocal());
    }

    public HttpClient Http => _http;

    public int UpdateCount => Apps.Count(a => a.UpdateAvailable);

    /// <summary>Applies a (new) app list, keeping the state of apps that stay.</summary>
    public void SetCatalog(List<CatalogApp> catalog)
    {
        lock (Apps)
        {
            var old = Apps.ToDictionary(a => a.App.Id, StringComparer.OrdinalIgnoreCase);
            Apps.Clear();
            foreach (var c in catalog)
            {
                if (old.TryGetValue(c.Id, out var e))
                    e.App = c;
                else
                    e = new AppEntry(c);
                Apps.Add(e);
            }
        }

        foreach (var e in Apps)
            Detect(e);
        Changed?.Invoke(null);
    }

    public AppEntry? Find(string id) => Apps.FirstOrDefault(a => a.App.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    // ───────────────────────────── what's on this PC ─────────────────────────────

    public void DetectAll()
    {
        foreach (var e in Apps)
            Detect(e);
        Changed?.Invoke(null);
    }

    /// <summary>Works out where (and whether) the app is installed, its version, and whether it's running.</summary>
    public void Detect(AppEntry e)
    {
        var app = e.App;
        string? dir = null, exe = null;
        Version? version = null;
        var external = false;

        if (Settings.Installed.TryGetValue(app.Id, out var rec) && FindExe(rec.Path, app.Exe) is { } recorded)
        {
            dir = rec.Path;
            exe = recorded;
            version = Versions.Parse(rec.Tag);
        }
        else if (FindExe(Path.Combine(Settings.EffectiveAppsRoot, app.Folder), app.Exe) is { } inRoot)
        {
            // Put there by hand (or by an older toolkit): it is in our folder, so treat it as ours.
            dir = Path.Combine(Settings.EffectiveAppsRoot, app.Folder);
            exe = inRoot;
        }
        else
        {
            foreach (var known in app.KnownPaths)
            {
                var path = Paths.Expand(known);
                if (File.Exists(path))
                {
                    dir = Path.GetDirectoryName(path);
                    exe = path;
                    external = true;
                    break;
                }
            }
        }

        e.InstallDir = dir;
        e.ExePath = exe;
        e.External = external;
        e.InstalledVersion = exe is null ? null : version ?? FileVersion(exe, app.IsScript);
        e.MissingRuntimes = exe is null || app.IsScript ? [] : Runtimes.Missing(exe);
        e.IsRunning = exe is not null && !app.IsScript && RunningProcesses(e).Count > 0;
    }

    /// <summary>Only refreshes the running flags (cheap; the window polls it).</summary>
    public bool RefreshRunning()
    {
        var changed = false;
        foreach (var e in Apps.Where(a => a.IsInstalled && !a.App.IsScript))
        {
            var running = RunningProcesses(e).Count > 0;
            if (running != e.IsRunning)
            {
                e.IsRunning = running;
                changed = true;
            }
        }

        if (changed)
            Changed?.Invoke(null);
        return changed;
    }

    private static string? FindExe(string? dir, string exe)
    {
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
            return null;
        var direct = Path.Combine(dir, exe);
        if (File.Exists(direct))
            return direct;
        try
        {
            // A zip that nests its files one or two folders down.
            return Directory.EnumerateFiles(dir, exe, new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = 2 })
                .FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }

    private static Version? FileVersion(string path, bool script)
    {
        try
        {
            if (script)
            {
                // PowerShell tools: the first "...Version = '1.2.3'" line.
                foreach (var line in File.ReadLines(path).Take(400))
                    if (Regex.Match(line, @"Version\s*=\s*['""](\d+\.\d+(?:\.\d+)?)['""]") is { Success: true } m)
                        return Versions.Parse(m.Groups[1].Value);
                return null;
            }

            var info = FileVersionInfo.GetVersionInfo(path);
            return Versions.Parse(info.ProductVersion) ?? Versions.Parse(info.FileVersion);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Processes started from this app's folder (a copy elsewhere, like a dev build, is left alone).</summary>
    public static List<Process> RunningProcesses(AppEntry e)
    {
        var found = new List<Process>();
        if (e.ExePath is null || e.App.IsScript)
            return found;
        var name = Path.GetFileNameWithoutExtension(e.ExePath);
        foreach (var p in Process.GetProcessesByName(name))
        {
            string? path = null;
            try
            {
                path = p.MainModule?.FileName;
            }
            catch
            {
                /* elevated or exiting: can't tell where it's from */
            }

            if (path is null || string.Equals(Path.GetFullPath(path), Path.GetFullPath(e.ExePath), StringComparison.OrdinalIgnoreCase))
                found.Add(p);
            else
                p.Dispose();
        }

        return found;
    }

    // ───────────────────────────── update checks ─────────────────────────────

    /// <summary>Fetches the app list and every app's latest release. Safe to call often; overlapping calls wait.</summary>
    public async Task CheckAsync(CancellationToken ct = default)
    {
        await _checkLock.WaitAsync(ct);
        Checking = true;
        Changed?.Invoke(null);
        try
        {
            if (await Catalog.FetchRemoteAsync(_http, ct) is { } remote)
                SetCatalog(remote);

            string? firstError = null;
            await Parallel.ForEachAsync(Apps.ToList(), new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = ct }, async (e, token) =>
            {
                try
                {
                    e.Latest = await _github.LatestAsync(e.App.Repo, token);
                    e.NoRelease = e.Latest is null;
                    e.CheckError = null;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    e.CheckError = Friendly(ex);
                    firstError ??= e.CheckError;
                }

                Changed?.Invoke(e);
            });

            try
            {
                var self = await _github.LatestAsync("joshpack/PaxToolkit", ct);
                ToolkitUpdate = self?.Version is { } v && v > Versions.Current && self.Find(SelfUpdate.AssetPattern) is not null ? self : null;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                firstError ??= Friendly(ex);
            }

            LastCheckError = firstError;
            Settings.LastCheckUtc = DateTime.UtcNow;
            Settings.Save();
        }
        finally
        {
            Checking = false;
            _checkLock.Release();
            Changed?.Invoke(null);
        }
    }

    // ───────────────────────────── install / update ─────────────────────────────

    /// <summary>
    /// Downloads the latest release and puts it in place (fresh install or update). A running copy is closed first and
    /// started again afterwards. Throws with a readable message on failure (also left in <see cref="AppEntry.Error"/>).
    /// </summary>
    public async Task InstallAsync(AppEntry e, CancellationToken ct = default)
    {
        if (e.Busy)
            return;
        e.Busy = true;
        e.Error = null;
        Report(e, "Checking GitHub…", -1);
        string? download = null;
        var stage = Path.Combine(Paths.Downloads, "stage-" + e.App.Id);
        try
        {
            var release = await _github.LatestAsync(e.App.Repo, ct)
                          ?? throw new InvalidOperationException("There's no release on GitHub yet.");
            e.Latest = release;
            var asset = release.Find(e.App.Asset)
                        ?? throw new InvalidOperationException($"The {release.Tag} release has no Windows download.");

            Directory.CreateDirectory(Paths.Downloads);
            download = Path.Combine(Paths.Downloads, asset.Name);
            await DownloadAsync(asset, download, p => Report(e, $"Downloading {Sizes.Show(asset.Size)}…", p * 0.9), ct);

            if (release.ChecksumFor(asset) is { } sumAsset)
            {
                Report(e, "Checking the download…", 0.92);
                var expected = Regex.Match(await _http.GetStringAsync(sumAsset.Url, ct), "[0-9a-fA-F]{64}").Value;
                string actual;
                await using (var fs = File.OpenRead(download))
                    actual = Convert.ToHexString(await SHA256.HashDataAsync(fs, ct));
                if (expected.Length == 64 && !expected.Equals(actual, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("The download is damaged (checksum doesn't match). Try again.");
            }

            var target = e.InstallDir ?? Path.Combine(Settings.EffectiveAppsRoot, e.App.Folder);
            var wasRunning = StopApp(e);

            Report(e, "Installing…", 0.95);
            Directory.CreateDirectory(target);
            if (e.App.IsScript)
                await CopyWithRetryAsync(download, Path.Combine(target, e.App.Exe), ct);
            else
            {
                if (Directory.Exists(stage))
                    Directory.Delete(stage, true);
                ZipFile.ExtractToDirectory(download, stage, overwriteFiles: true);
                var root = SingleTopFolder(stage);
                // Copy over the old files (rather than replacing the folder) so anything the user keeps there survives.
                foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
                {
                    var dest = Path.Combine(target, Path.GetRelativePath(root, file));
                    Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                    await CopyWithRetryAsync(file, dest, ct);
                }
            }

            if (!e.External)
                Settings.Installed[e.App.Id] = new InstallRecord { Path = target, Tag = release.Tag, InstalledUtc = DateTime.UtcNow };
            Settings.Save();
            Detect(e);
            if (e.External)
                e.InstalledVersion = release.Version;   // its own setup's file may not carry a version
            if (Settings.StartMenuShortcuts && !e.External)
                TryCreateShortcut(e);

            if (wasRunning)
                Launch(e, tray: e.App.TrayArgs is not null);
        }
        catch (Exception ex)
        {
            e.Error = Friendly(ex);
            throw new InvalidOperationException(e.Error, ex);
        }
        finally
        {
            TryDelete(download);
            TryDeleteDir(stage);
            e.Busy = false;
            e.BusyText = null;
            e.Progress = -1;
            Changed?.Invoke(e);
        }
    }

    private async Task DownloadAsync(ReleaseAsset asset, string path, Action<double> progress, CancellationToken ct)
    {
        using var res = await _http.GetAsync(asset.Url, HttpCompletionOption.ResponseHeadersRead, ct);
        res.EnsureSuccessStatusCode();
        var total = res.Content.Headers.ContentLength ?? asset.Size;
        await using var src = await res.Content.ReadAsStreamAsync(ct);
        await using var dst = File.Create(path);
        var buffer = new byte[1 << 16];
        long done = 0;
        var last = Stopwatch.StartNew();
        int n;
        while ((n = await src.ReadAsync(buffer, ct)) > 0)
        {
            await dst.WriteAsync(buffer.AsMemory(0, n), ct);
            done += n;
            if (total > 0 && last.ElapsedMilliseconds > 120)
            {
                last.Restart();
                progress((double)done / total);
            }
        }

        progress(1);
    }

    /// <summary>A zip whose whole content sits in one folder: use that folder as the root.</summary>
    private static string SingleTopFolder(string dir)
    {
        var dirs = Directory.GetDirectories(dir);
        return dirs.Length == 1 && Directory.GetFiles(dir).Length == 0 ? dirs[0] : dir;
    }

    private static async Task CopyWithRetryAsync(string from, string to, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                File.Copy(from, to, overwrite: true);
                return;
            }
            catch (IOException) when (attempt < 10)
            {
                // A process that was just closed can hold its files for a moment.
                await Task.Delay(300, ct);
            }
        }
    }

    private void TryCreateShortcut(AppEntry e)
    {
        try
        {
            var lnk = Path.Combine(Paths.StartMenu, e.App.Name + ".lnk");
            if (e.App.IsScript)
            {
                var ps = Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe");
                Shortcut.Create(lnk, ps, ScriptArgs(e, gui: e.App.GuiArgs is not null), e.InstallDir, e.App.Description, ps);
            }
            else
                Shortcut.Create(lnk, e.ExePath!, e.App.OpenArgs, e.InstallDir, e.App.Description);
        }
        catch
        {
            /* a missing shortcut is not worth failing the install for */
        }
    }

    // ───────────────────────────── remove ─────────────────────────────

    /// <summary>Closes the app and removes it (its settings in AppData stay, so turning it on again picks them up).</summary>
    public async Task UninstallAsync(AppEntry e)
    {
        if (e.Busy || !e.IsInstalled)
            return;
        e.Busy = true;
        e.Error = null;
        Report(e, "Turning off…", -1);
        try
        {
            StopApp(e);
            var dir = e.InstallDir!;
            if (e.External && e.App.Uninstall is { Length: > 0 } cmd)
                await RunUninstallerAsync(dir, cmd);
            else
                await DeleteAppFolderAsync(e, dir);

            TryDelete(Path.Combine(Paths.StartMenu, e.App.Name + ".lnk"));
            Settings.Installed.Remove(e.App.Id);
            Settings.Save();
            Detect(e);
            if (e.IsInstalled)
                throw new InvalidOperationException($"Some files are still in {e.InstallDir}. Close the app and try again.");
        }
        catch (Exception ex)
        {
            e.Error = Friendly(ex);
            throw new InvalidOperationException(e.Error, ex);
        }
        finally
        {
            e.Busy = false;
            e.BusyText = null;
            Changed?.Invoke(e);
        }
    }

    private static async Task DeleteAppFolderAsync(AppEntry e, string dir)
    {
        // Never delete anything that doesn't look like this app's own folder.
        var full = Path.GetFullPath(dir).TrimEnd('\\');
        if (full.Length < 8 || Path.GetPathRoot(full)?.TrimEnd('\\') == full || e.ExePath is null
            || !Path.GetFullPath(e.ExePath).StartsWith(full + "\\", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("That folder doesn't look like the app's own, so it was left alone.");

        for (var attempt = 0; ; attempt++)
        {
            try
            {
                Directory.Delete(full, recursive: true);
                return;
            }
            catch (Exception) when (attempt < 10 && Directory.Exists(full))
            {
                await Task.Delay(300);
            }
            catch (DirectoryNotFoundException)
            {
                return;
            }
        }
    }

    private static async Task RunUninstallerAsync(string dir, string cmd)
    {
        var m = Regex.Match(cmd, @"^\s*(""[^""]+""|\S+)\s*(.*)$");
        var file = Path.Combine(dir, m.Groups[1].Value.Trim('"'));
        var psi = file.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase) || file.EndsWith(".bat", StringComparison.OrdinalIgnoreCase)
            ? new ProcessStartInfo("cmd.exe", $"/c \"\"{file}\" {m.Groups[2].Value}\"")
            : new ProcessStartInfo(file, m.Groups[2].Value);
        psi.UseShellExecute = false;
        psi.CreateNoWindow = true;
        psi.WorkingDirectory = Path.GetTempPath();
        using var p = Process.Start(psi) ?? throw new InvalidOperationException("Its uninstaller didn't start.");
        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        await p.WaitForExitAsync(cts.Token);
        await Task.Delay(500);
    }

    // ───────────────────────────── start / stop ─────────────────────────────

    /// <summary>Starts the app (tray = its start-in-tray arguments; gui = a script's window mode).</summary>
    public void Launch(AppEntry e, bool tray = false, bool gui = false)
    {
        if (e.ExePath is null)
            return;
        e.Error = null;
        try
        {
            ProcessStartInfo psi;
            if (e.App.IsScript)
                psi = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe"), ScriptArgs(e, gui));
            else
                psi = new ProcessStartInfo(e.ExePath, (tray ? e.App.TrayArgs : e.App.OpenArgs) ?? "");
            psi.UseShellExecute = true;
            psi.WorkingDirectory = e.InstallDir ?? "";
            if (e.App.Elevate)
                psi.Verb = "runas";
            Process.Start(psi)?.Dispose();
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            e.Error = "Not started: it needs admin rights and the Windows prompt was cancelled.";
        }
        catch (Exception ex)
        {
            e.Error = Friendly(ex);
        }

        Changed?.Invoke(e);
    }

    private static string ScriptArgs(AppEntry e, bool gui)
    {
        var path = e.ExePath ?? Path.Combine(e.InstallDir ?? "", e.App.Exe);
        return gui && e.App.GuiArgs is { } g
            ? $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{path}\" {g}"
            : $"-NoProfile -ExecutionPolicy Bypass -NoExit -File \"{path}\"";
    }

    /// <summary>Closes every copy of the app started from its folder. True if any were running.</summary>
    public bool StopApp(AppEntry e)
    {
        var procs = RunningProcesses(e);
        foreach (var p in procs)
        {
            using (p)
            {
                try
                {
                    if (!p.CloseMainWindow() || !p.WaitForExit(1500))
                        p.Kill(entireProcessTree: true);
                    p.WaitForExit(5000);
                }
                catch
                {
                    /* already gone */
                }
            }
        }

        if (procs.Count > 0)
        {
            e.IsRunning = false;
            Changed?.Invoke(e);
        }

        return procs.Count > 0;
    }

    public void OpenFolder(AppEntry e)
    {
        if (e.InstallDir is { } dir && Directory.Exists(dir))
            OpenUrl(dir);
    }

    public static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })?.Dispose();
        }
        catch
        {
            /* nothing sensible to do */
        }
    }

    // ───────────────────────────── helpers ─────────────────────────────

    private void Report(AppEntry e, string text, double progress)
    {
        e.BusyText = text;
        e.Progress = progress;
        Changed?.Invoke(e);
    }

    public static string Friendly(Exception ex) => ex switch
    {
        HttpRequestException { StatusCode: null } h when h.InnerException is not null => "Can't reach GitHub. Check the internet connection.",
        HttpRequestException h => h.Message,
        TaskCanceledException => "GitHub took too long to answer.",
        UnauthorizedAccessException => "Windows refused access to the app's folder. Close the app and try again.",
        InvalidOperationException i => i.Message,
        _ => ex.Message,
    };

    private static void TryDelete(string? path)
    {
        try
        {
            if (path is not null && File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            /* leftovers in our own downloads folder are harmless */
        }
    }

    private static void TryDeleteDir(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, true);
        }
        catch
        {
            /* as above */
        }
    }

    public void Dispose() => _http.Dispose();
}

public static class Sizes
{
    public static string Show(long bytes) => bytes switch
    {
        <= 0 => "",
        < 1024 * 1024 => $"{Math.Max(1, bytes / 1024)} KB",
        < 1024L * 1024 * 1024 => $"{bytes / (1024.0 * 1024):0.#} MB",
        _ => $"{bytes / (1024.0 * 1024 * 1024):0.##} GB",
    };
}
