using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PaxToolkit.Core;

/// <summary>One app the toolkit can turn on: where its releases live and how to run it.</summary>
public sealed class CatalogApp
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Tagline { get; set; } = "";
    public string Description { get; set; } = "";

    /// <summary>GitHub "owner/name".</summary>
    public string Repo { get; set; } = "";

    /// <summary>"zip" (extract into the app folder) or "script" (a PowerShell script run with powershell.exe).</summary>
    public string Kind { get; set; } = "zip";

    /// <summary>Regex for the release asset to download.</summary>
    public string Asset { get; set; } = "";

    /// <summary>File to start, relative to the app folder (found anywhere inside it if the zip nests it).</summary>
    public string Exe { get; set; } = "";

    /// <summary>Folder name under the install root.</summary>
    public string Folder { get; set; } = "";

    /// <summary>Arguments for "Open". Null = none.</summary>
    public string? OpenArgs { get; set; }

    /// <summary>Arguments for "Start in tray". Null = the app has no tray start.</summary>
    public string? TrayArgs { get; set; }

    /// <summary>Script apps: extra arguments for the GUI mode. Null = no GUI.</summary>
    public string? GuiArgs { get; set; }

    /// <summary>The app has a tray icon the toolkit can turn on and off (PAX App Standard link).</summary>
    public bool Tray { get; set; }

    /// <summary>Ask for admin rights when starting it.</summary>
    public bool Elevate { get; set; }

    /// <summary>Tile colour for the generated icon when there is no picture.</summary>
    public string Color { get; set; } = "#19F58C";

    /// <summary>Where a copy installed some other way (its own setup) lives. Environment variables are expanded.</summary>
    public List<string> KnownPaths { get; set; } = [];

    /// <summary>Uninstall command for a copy found in KnownPaths, relative to its folder ("app.exe --uninstall").</summary>
    public string? Uninstall { get; set; }

    /// <summary>Picture to show; a URL, or empty to use the built-in icon (or a generated one).</summary>
    public string? IconUrl { get; set; }

    [JsonIgnore] public bool IsScript => Kind.Equals("script", StringComparison.OrdinalIgnoreCase);
    [JsonIgnore] public string RepoUrl => $"https://github.com/{Repo}";
}

public sealed class CatalogFile
{
    public int Schema { get; set; } = 1;
    public List<CatalogApp> Apps { get; set; } = [];
}

/// <summary>
/// The app list ships inside the toolkit, and a newer copy is fetched from the PaxToolkit repo, so new apps show up
/// without a toolkit update.
/// </summary>
public static class Catalog
{
    public const string RemoteUrl = "https://raw.githubusercontent.com/joshpack/PaxToolkit/main/catalog.json";

    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private static string CachePath => Path.Combine(Paths.Data, "catalog-cache.json");

    /// <summary>The cached remote list if there is a good one, otherwise the built-in one.</summary>
    public static List<CatalogApp> LoadLocal()
    {
        if (Parse(TryRead(CachePath)) is { } cached)
            return cached;
        return Parse(ReadBuiltIn()) ?? [];
    }

    public static string ReadBuiltIn()
    {
        using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("catalog.json")!;
        using var r = new StreamReader(s);
        return r.ReadToEnd();
    }

    /// <summary>Fetches the list from GitHub and caches it; null when offline or the file is bad.</summary>
    public static async Task<List<CatalogApp>?> FetchRemoteAsync(HttpClient http, CancellationToken ct = default)
    {
        if (Environment.GetEnvironmentVariable("PAXTK_NO_REMOTE_CATALOG") is not null)
            return null;
        try
        {
            var text = await http.GetStringAsync(RemoteUrl, ct);
            var apps = Parse(text);
            if (apps is null)
                return null;
            Directory.CreateDirectory(Paths.Data);
            await File.WriteAllTextAsync(CachePath, text, ct);
            return apps;
        }
        catch
        {
            return null;
        }
    }

    public static List<CatalogApp>? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        try
        {
            var file = JsonSerializer.Deserialize<CatalogFile>(text, Json);
            var apps = file?.Apps.Where(a => a.Id.Length > 0 && a.Repo.Contains('/') && a.Exe.Length > 0 && a.Asset.Length > 0).ToList();
            if (apps is null || apps.Count == 0)
                return null;
            foreach (var a in apps)
                if (a.Folder.Length == 0)
                    a.Folder = a.Id;
            return apps;
        }
        catch
        {
            return null;
        }
    }

    private static string? TryRead(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }
        catch
        {
            return null;
        }
    }
}
