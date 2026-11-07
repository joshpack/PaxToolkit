using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using ShapePath = Avalonia.Controls.Shapes.Path;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace PaxToolkit;

/// <summary>
/// The toolkit's mark: three app tiles and a "+" slot on a cyan-to-green tile. Drawn from shapes so it is crisp at any
/// size; <see cref="WriteIco"/> turns it into the exe's .ico.
/// </summary>
public static class AppIcon
{
    private static readonly Color Dark = Color.Parse("#04140C");

    public static Bitmap Render(int size, bool badge = false)
    {
        var tile = new Grid { Width = size, Height = size };
        tile.Children.Add(new Border
        {
            CornerRadius = new CornerRadius(size * 0.22),
            Background = Gradient("#00C8FF", "#19F58C"),
        });

        // 100x100 artwork: 2x2 grid of rounded squares, the last one an outlined "+" slot.
        var art = new Canvas { Width = 100, Height = 100 };
        void Square(double x, double y)
        {
            var r = new Rectangle { Width = 34, Height = 34, RadiusX = 8, RadiusY = 8, Fill = new SolidColorBrush(Dark) };
            Canvas.SetLeft(r, x);
            Canvas.SetTop(r, y);
            art.Children.Add(r);
        }

        Square(12, 12);
        Square(54, 12);
        Square(12, 54);
        var slot = new Rectangle
        {
            Width = 30, Height = 30, RadiusX = 7, RadiusY = 7, Stroke = new SolidColorBrush(Dark), StrokeThickness = 5,
        };
        Canvas.SetLeft(slot, 56);
        Canvas.SetTop(slot, 56);
        art.Children.Add(slot);
        art.Children.Add(new ShapePath
        {
            Data = Geometry.Parse("M71,63 L71,79 M63,71 L79,71"),
            Stroke = new SolidColorBrush(Dark),
            StrokeThickness = 5,
            StrokeLineCap = PenLineCap.Round,
        });
        tile.Children.Add(new Viewbox { Margin = new Thickness(size * 0.08), Child = art });

        if (badge)
        {
            // An update is waiting: an orange dot in the corner.
            tile.Children.Add(new Ellipse
            {
                Width = size * 0.42, Height = size * 0.42,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
                Fill = new SolidColorBrush(Color.Parse("#FF9F1C")),
                Stroke = new SolidColorBrush(Color.Parse("#0E1015")),
                StrokeThickness = size * 0.06,
            });
        }

        return Draw(tile, size);
    }

    public static WindowIcon Window(bool badge = false) => new(Render(64, badge));

    /// <summary>A rounded tile with the app's initials, for apps that have no picture.</summary>
    public static Bitmap Monogram(string name, string color, int size = 96)
    {
        var words = name.Replace("PAX ", "", StringComparison.OrdinalIgnoreCase).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var letters = string.Concat(words.Take(2).Select(w => char.ToUpperInvariant(w[0])));
        Color c;
        try
        {
            c = Color.Parse(color);
        }
        catch
        {
            c = Color.Parse("#19F58C");
        }

        var lighter = Color.FromRgb((byte)Math.Min(255, c.R + 60), (byte)Math.Min(255, c.G + 60), (byte)Math.Min(255, c.B + 60));
        var tile = new Grid { Width = size, Height = size };
        tile.Children.Add(new Border
        {
            CornerRadius = new CornerRadius(size * 0.22),
            Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                GradientStops = { new GradientStop(lighter, 0), new GradientStop(c, 1) },
            },
        });
        tile.Children.Add(new TextBlock
        {
            Text = letters,
            FontSize = size * 0.38,
            FontWeight = FontWeight.Bold,
            Foreground = new SolidColorBrush(Dark),
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
        });
        return Draw(tile, size);
    }

    /// <summary>Writes a multi-size .ico (PNG entries, Vista+).</summary>
    public static void WriteIco(string path)
    {
        int[] sizes = [16, 20, 24, 32, 40, 48, 64, 128, 256];
        var images = sizes.Select(s =>
        {
            using var ms = new MemoryStream();
            Render(s).Save(ms);
            return ms.ToArray();
        }).ToList();

        using var w = new BinaryWriter(File.Create(path));
        w.Write((short)0);
        w.Write((short)1);
        w.Write((short)sizes.Length);
        var offset = 6 + 16 * sizes.Length;
        for (var i = 0; i < sizes.Length; i++)
        {
            w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
            w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
            w.Write((byte)0);
            w.Write((byte)0);
            w.Write((short)1);
            w.Write((short)32);
            w.Write(images[i].Length);
            w.Write(offset);
            offset += images[i].Length;
        }

        foreach (var img in images)
            w.Write(img);
    }

    private static LinearGradientBrush Gradient(string from, string to) => new()
    {
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
        EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
        GradientStops = { new GradientStop(Color.Parse(from), 0), new GradientStop(Color.Parse(to), 1) },
    };

    private static Bitmap Draw(Control c, int size)
    {
        c.Measure(new Size(size, size));
        c.Arrange(new Rect(0, 0, size, size));
        var bitmap = new RenderTargetBitmap(new PixelSize(size, size), new Vector(96, 96));
        bitmap.Render(c);
        return bitmap;
    }
}
