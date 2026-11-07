namespace PaxToolkit.Core;

/// <summary>Live state of one catalog app on this PC: installed or not, which version, running, busy.</summary>
public sealed class AppEntry(CatalogApp app)
{
    public CatalogApp App { get; set; } = app;

    /// <summary>Folder the app lives in, when it is installed.</summary>
    public string? InstallDir { get; set; }

    /// <summary>The file "Open" starts.</summary>
    public string? ExePath { get; set; }

    /// <summary>Installed some other way (its own setup), found in one of the catalog's known places.</summary>
    public bool External { get; set; }

    public Version? InstalledVersion { get; set; }

    public Release? Latest { get; set; }

    /// <summary>Repo has no release yet (checked).</summary>
    public bool NoRelease { get; set; }

    /// <summary>Why the last update check for this app failed.</summary>
    public string? CheckError { get; set; }

    public bool IsRunning { get; set; }

    public List<(string Name, int Major)> MissingRuntimes { get; set; } = [];

    public bool Busy { get; set; }

    /// <summary>0..1 while busy, or -1 for "working, no percentage".</summary>
    public double Progress { get; set; } = -1;

    public string? BusyText { get; set; }

    /// <summary>Last install/uninstall/launch problem, shown on the card until the next action.</summary>
    public string? Error { get; set; }

    public bool IsInstalled => ExePath is not null;

    public ReleaseAsset? Download => Latest?.Find(App.Asset);

    public bool UpdateAvailable =>
        IsInstalled && Download is not null && Latest?.Version is { } latest && (InstalledVersion is null || latest > InstalledVersion);
}
