using System.Net.Http;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using PaxToolkit.Core;

namespace PaxToolkit.Views;

/// <summary>
/// App pictures. The catalog's <c>iconUrl</c> wins (downloaded once, cached in the data folder, refreshed weekly), so an
/// app added to the online catalog gets its own icon even in a toolkit built before it existed. Otherwise the picture
/// built into the toolkit, otherwise a tile with the app's initials.
/// </summary>
public static class Icons
{
    private static readonly Dictionary<string, Bitmap> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, Task<Bitmap?>> Fetches = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HttpClient Http = GitHub.CreateClient();
    private static readonly TimeSpan Refresh = TimeSpan.FromDays(7);
    private const int MaxBytes = 1024 * 1024;

    /// <summary>The cached or built-in picture right now (may change once a download finishes; see <see cref="Image"/>).</summary>
    public static Bitmap For(CatalogApp app)
    {
        if (Cache.TryGetValue(app.Id, out var cached))
            return cached;
        var bmp = LoadCachedFile(app) ?? BuiltIn(app) ?? AppIcon.Monogram(app.Name, app.Color);
        Cache[app.Id] = bmp;
        return bmp;
    }

    /// <summary>An image of the app's icon that swaps in the online picture when it arrives.</summary>
    public static Image Image(CatalogApp app, double size)
    {
        var img = new Image { Source = For(app), Width = size, Height = size };
        if (NeedsFetch(app))
        {
            var task = FetchAsync(app);
            if (!task.IsCompleted)
                task.ContinueWith(t =>
                {
                    if (t.Status == TaskStatus.RanToCompletion && t.Result is { } b)
                        Dispatcher.UIThread.Post(() => img.Source = b);
                }, TaskScheduler.Default);
        }

        return img;
    }

    private static string CacheFile(CatalogApp app) => Path.Combine(Paths.Data, "icons", app.Id + ".png");

    private static Bitmap? BuiltIn(CatalogApp app)
    {
        try
        {
            var uri = new Uri($"avares://PaxToolkit/Assets/icons/{app.Id}.png");
            return AssetLoader.Exists(uri) ? new Bitmap(AssetLoader.Open(uri)) : null;
        }
        catch
        {
            return null;
        }
    }

    private static Bitmap? LoadCachedFile(CatalogApp app)
    {
        if (!UsableUrl(app.IconUrl)) return null;
        try
        {
            var f = CacheFile(app);
            return File.Exists(f) ? new Bitmap(f) : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Only https pictures from GitHub: the catalog lives there, and nothing else needs to be trusted.</summary>
    public static bool UsableUrl(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttps
        && (u.Host.Equals("raw.githubusercontent.com", StringComparison.OrdinalIgnoreCase) || u.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase));

    private static bool NeedsFetch(CatalogApp app)
    {
        if (!UsableUrl(app.IconUrl)) return false;
        if (Fetches.ContainsKey(app.Id)) return true;
        var f = CacheFile(app);
        return !File.Exists(f) || DateTime.UtcNow - File.GetLastWriteTimeUtc(f) > Refresh;
    }

    /// <summary>Downloads and caches the catalog picture (one download per app per run); null when it can't.</summary>
    public static Task<Bitmap?> FetchAsync(CatalogApp app)
    {
        if (Fetches.TryGetValue(app.Id, out var running)) return running;
        if (!UsableUrl(app.IconUrl)) return Task.FromResult<Bitmap?>(null);
        var task = Task.Run(async () =>
        {
            try
            {
                using var resp = await Http.GetAsync(app.IconUrl, HttpCompletionOption.ResponseHeadersRead);
                if (!resp.IsSuccessStatusCode || resp.Content.Headers.ContentLength > MaxBytes) return null;
                var bytes = await resp.Content.ReadAsByteArrayAsync();
                // A PNG, of sensible size, that actually decodes; anything else keeps the current picture.
                if (bytes.Length is < 8 or > MaxBytes || bytes[0] != 0x89 || bytes[1] != (byte)'P' || bytes[2] != (byte)'N' || bytes[3] != (byte)'G') return null;
                Bitmap bmp;
                using (var ms = new MemoryStream(bytes)) bmp = new Bitmap(ms);
                var f = CacheFile(app);
                Directory.CreateDirectory(Path.GetDirectoryName(f)!);
                var tmp = f + ".tmp";
                await File.WriteAllBytesAsync(tmp, bytes);
                File.Move(tmp, f, true);
                Dispatcher.UIThread.Post(() => Cache[app.Id] = bmp);
                return bmp;
            }
            catch
            {
                return null; // offline or GitHub unhappy: try again next start
            }
        });
        Fetches[app.Id] = task;
        return task;
    }
}
