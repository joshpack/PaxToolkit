using Avalonia.Media.Imaging;
using Avalonia.Platform;
using PaxToolkit.Core;

namespace PaxToolkit.Views;

/// <summary>App pictures: the built-in one for each app, or a tile with its initials.</summary>
public static class Icons
{
    private static readonly Dictionary<string, Bitmap> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static Bitmap For(CatalogApp app)
    {
        if (Cache.TryGetValue(app.Id, out var cached))
            return cached;
        Bitmap bmp;
        var uri = new Uri($"avares://PaxToolkit/Assets/icons/{app.Id}.png");
        try
        {
            bmp = AssetLoader.Exists(uri) ? new Bitmap(AssetLoader.Open(uri)) : AppIcon.Monogram(app.Name, app.Color);
        }
        catch
        {
            bmp = AppIcon.Monogram(app.Name, app.Color);
        }

        Cache[app.Id] = bmp;
        return bmp;
    }
}
