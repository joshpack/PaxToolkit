namespace pxdToolkit.Core;

/// <summary>Where the toolkit keeps its own files and puts the apps. Environment variables override them for tests.</summary>
public static class Paths
{
    /// <summary>Settings, install records and downloads.</summary>
    public static string Data =>
        Environment.GetEnvironmentVariable("PXDTK_DATA") is { Length: > 0 } d
            ? d
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "pxdToolkit");

    /// <summary>Default home for installed apps: %USERPROFILE%\Apps\&lt;App&gt;.</summary>
    public static string DefaultAppsRoot =>
        Environment.GetEnvironmentVariable("PXDTK_APPS") is { Length: > 0 } a
            ? a
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Apps");

    /// <summary>Start menu folder the toolkit puts its shortcuts in.</summary>
    public static string StartMenu =>
        Environment.GetEnvironmentVariable("PXDTK_STARTMENU") is { Length: > 0 } s
            ? s
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "pxd Toolkit");

    public static string Downloads => Path.Combine(Data, "downloads");

    /// <summary>Where the toolkit itself lives once it has installed itself.</summary>
    public static string ToolkitHome => Path.Combine(DefaultAppsRoot, "pxdToolkit");

    public static string Expand(string path) => Environment.ExpandEnvironmentVariables(path);
}
