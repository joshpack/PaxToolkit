using System.Diagnostics;
using pxdStandard;

namespace pxdToolkit.Core;

/// <summary>
/// Moves what older versions (from before the pxd rename) left on this PC to the pxd names: the toolkit's own data,
/// Start menu folder, Run entry and install folder at start-up, then every app installed under its former name (the new
/// release goes into the new folder, the old folder is removed, and an app that was running is started again).
/// Former names come from <see cref="pxdMigrate.FormerName"/>; nothing here runs once the old things are gone.
/// </summary>
public static class Migration
{
    private static bool Overridden(string env) => Environment.GetEnvironmentVariable(env) is { Length: > 0 };

    /// <summary>Before the toolkit loads its settings.</summary>
    public static void Own()
    {
        if (!Overridden("PXDTK_DATA"))
            pxdMigrate.Folder(
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), pxdMigrate.FormerName("pxdToolkit")),
                Paths.Data);

        if (!Overridden("PXDTK_STARTMENU"))
            TryDeleteDir(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), pxdMigrate.FormerPrefix.ToUpperInvariant() + " Toolkit"));

        if (SelfUpdate.IsDevBuild)
            return;
        var exe = SelfUpdate.RunningFromHome || !File.Exists(SelfUpdate.HomeExe) ? Environment.ProcessPath! : SelfUpdate.HomeExe;
        pxdMigrate.RunEntry(pxdMigrate.FormerName("pxdToolkit"), "pxdToolkit", exe);

        // The old toolkit's own folder: close that copy and remove it (this copy has taken over).
        if (!Overridden("PXDTK_APPS"))
        {
            var oldHome = Path.Combine(Paths.DefaultAppsRoot, pxdMigrate.FormerName("pxdToolkit"));
            var oldExe = Path.Combine(oldHome, pxdMigrate.FormerName("pxdToolkit") + ".exe");
            if (Directory.Exists(oldHome) && !IsInside(Environment.ProcessPath, oldHome))
            {
                StopFrom(oldExe);
                TryDeleteDir(oldHome);
            }
        }

        if (SelfUpdate.RunningFromHome)
            SelfUpdate.CreateShortcuts();
    }

    /// <summary>Where an app's former install is, if there is one and the new one isn't there yet.</summary>
    public static (string Dir, string Exe)? FormerInstall(Toolkit kit, AppEntry e)
    {
        if (e.IsInstalled)
            return null;
        var oldExeName = pxdMigrate.FormerName(e.App.Exe);
        var candidates = new List<string>();
        if (kit.Settings.Installed.TryGetValue(pxdMigrate.FormerName(e.App.Id), out var rec) && rec.Path is { Length: > 0 })
            candidates.Add(rec.Path);
        candidates.Add(Path.Combine(kit.Settings.EffectiveAppsRoot, pxdMigrate.FormerName(e.App.Folder)));
        foreach (var dir in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var exe = Path.Combine(dir, oldExeName);
            if (File.Exists(exe))
                return (dir, exe);
            try
            {
                if (Directory.Exists(dir) && Directory.EnumerateFiles(dir, oldExeName, new EnumerationOptions { RecurseSubdirectories = true, MaxRecursionDepth = 2 }).FirstOrDefault() is { } nested)
                    return (dir, nested);
            }
            catch
            {
                /* not readable: skip */
            }
        }

        return null;
    }

    /// <summary>
    /// Installs the new release of every app found under its former name, then removes the old copy. Needs release
    /// info (run after a check). Returns how many apps moved.
    /// </summary>
    public static async Task<int> AppsAsync(Toolkit kit, CancellationToken ct = default)
    {
        var moved = 0;
        foreach (var e in kit.Apps.ToList())
        {
            if (e.Busy || FormerInstall(kit, e) is not { } old)
                continue;
            if (e.Latest is not { } rel || rel.Find(e.App.Asset) is null)
                continue; // the pxd release isn't out yet: keep the old copy for now

            var wasRunning = StopFrom(old.Exe);
            try
            {
                await kit.InstallAsync(e, ct);
            }
            catch
            {
                if (wasRunning)
                    StartOld(old.Exe, e);
                continue;
            }

            if (e.ExePath is { } newExe && !e.App.IsScript)
                pxdMigrate.RunEntry(pxdMigrate.FormerName(e.App.Id), e.App.Id, newExe);
            TryDelete(Path.Combine(Paths.StartMenu, pxdMigrate.FormerPrefix.ToUpperInvariant() + e.App.Name[3..] + ".lnk"));
            if (Looks(old.Dir, e))
                TryDeleteDir(old.Dir);
            kit.Settings.Installed.Remove(pxdMigrate.FormerName(e.App.Id));
            kit.Settings.Save();
            kit.Detect(e);
            // Started again the way an update does it; apps that need admin wait for the user rather than prompting.
            if (wasRunning && !e.App.Elevate)
                kit.Launch(e, tray: e.App.TrayArgs is not null);
            moved++;
        }

        return moved;
    }

    /// <summary>Only ever delete a folder that is named like the app's former folder.</summary>
    private static bool Looks(string dir, AppEntry e) =>
        Path.GetFileName(Path.GetFullPath(dir).TrimEnd('\\')).Equals(pxdMigrate.FormerName(e.App.Folder), StringComparison.OrdinalIgnoreCase);

    private static void StartOld(string exe, AppEntry e)
    {
        try
        {
            if (!e.App.IsScript)
                Process.Start(new ProcessStartInfo(exe, e.App.TrayArgs ?? "") { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(exe)! })?.Dispose();
        }
        catch
        {
            /* it stays closed; the toolkit shows it */
        }
    }

    /// <summary>Closes every process started from <paramref name="exe"/>. True if any ran.</summary>
    public static bool StopFrom(string exe)
    {
        var any = false;
        foreach (var p in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(exe)))
        {
            using (p)
            {
                try
                {
                    if (!string.Equals(p.MainModule?.FileName, exe, StringComparison.OrdinalIgnoreCase))
                        continue;
                    any = true;
                    if (!p.CloseMainWindow() || !p.WaitForExit(1500))
                        p.Kill(entireProcessTree: true);
                    p.WaitForExit(5000);
                }
                catch
                {
                    /* elevated or already gone */
                }
            }
        }

        return any;
    }

    private static bool IsInside(string? path, string dir) =>
        path is not null && Path.GetFullPath(path).StartsWith(Path.GetFullPath(dir).TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase);

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            /* harmless leftover */
        }
    }

    private static void TryDeleteDir(string dir)
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            try
            {
                if (Directory.Exists(dir))
                    Directory.Delete(dir, recursive: true);
                return;
            }
            catch
            {
                Thread.Sleep(300);
            }
        }
    }
}
