using System.Text.Json;

namespace pxdToolkit.Core;

/// <summary>What the toolkit remembers about one installed app.</summary>
public sealed class InstallRecord
{
    /// <summary>Folder the app was installed into.</summary>
    public string Path { get; set; } = "";

    /// <summary>Release tag that was installed ("v1.2.0").</summary>
    public string Tag { get; set; } = "";

    public DateTime InstalledUtc { get; set; }
}

public sealed class Settings
{
    public bool CheckForUpdates { get; set; } = true;

    /// <summary>Install app updates on their own when a check finds them.</summary>
    public bool AutoUpdate { get; set; }

    public bool StartMenuShortcuts { get; set; } = true;

    /// <summary>Install root; empty = %USERPROFILE%\Apps.</summary>
    public string AppsRoot { get; set; } = "";

    /// <summary>Apps the toolkit installed (or adopted), by catalog id.</summary>
    public Dictionary<string, InstallRecord> Installed { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public DateTime? LastCheckUtc { get; set; }

    public string EffectiveAppsRoot => string.IsNullOrWhiteSpace(AppsRoot) ? Paths.DefaultAppsRoot : Paths.Expand(AppsRoot);

    private static string FilePath => System.IO.Path.Combine(Paths.Data, "settings.json");

    public static Settings Load()
    {
        try
        {
            if (File.Exists(FilePath) && JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath), Catalog.Json) is { } s)
            {
                s.Installed = new Dictionary<string, InstallRecord>(s.Installed, StringComparer.OrdinalIgnoreCase);
                return s;
            }
        }
        catch
        {
            /* a damaged file: start fresh rather than refuse to run */
        }

        return new Settings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Paths.Data);
            var tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(this, Catalog.Json));
            File.Move(tmp, FilePath, overwrite: true);
        }
        catch
        {
            /* not fatal: the next save tries again */
        }
    }
}
