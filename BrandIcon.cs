using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using ShapePath = Avalonia.Controls.Shapes.Path;

namespace pxdToolkit;

/// <summary>
/// pxd Toolkit's icon: the PD monogram from pxdtek.com (public/logo.svg in the pxdtek site), in black on a white disc
/// with a black ring: the site's colours swapped. Same geometry as the SVG (its 1320-unit viewBox starts at 142,52):
/// a "p" (stem into a round bowl), the same shape turned half a circle for the "d", linked like two rings.
/// Drawn from vector shapes so it stays crisp at every size; used for the tray, windows and the exe's .ico.
/// </summary>
public static class BrandIcon
{
    private const string Ink = "#000000";
    private const string Paper = "#FFFFFF";

    /// <summary>The icon as a control (<paramref name="alt"/>: unused).</summary>
    public static Control Build(double size, bool alt = false)
    {
        // Small sizes get bolder strokes so the letters don't blur into grey (16 px is the tray and title bar).
        var k = size <= 20 ? 2.2 : size <= 32 ? 1.5 : size <= 48 ? 1.2 : 1.0;

        // Everything in the SVG's own coordinates, shifted so its viewBox (142,52 .. 1462,1372) starts at 0,0.
        var art = new Canvas { Width = 1604, Height = 1604, RenderTransform = new TranslateTransform(-142, -52), RenderTransformOrigin = RelativePoint.TopLeft };
        art.Children.Add(Disc(k));
        art.Children.Add(Letter(0, k));
        art.Children.Add(Letter(180, k));
        // Where the p crosses over the d: a paper-coloured edge opens a gap in the d, then the p's bowl runs on top.
        art.Children.Add(Stroke("M 698.3 523.3 A 211 211 0 0 1 836.6 597.0", Paper, 99 * k));
        art.Children.Add(Stroke("M 667.6 522.1 A 211 211 0 0 1 855.9 624.3", Ink, 63 * k));

        var frame = new Canvas { Width = 1320, Height = 1320, ClipToBounds = true };
        frame.Children.Add(art);
        // A little room around the ring so its edge isn't cut at the image border.
        return new Viewbox { Width = size, Height = size, Child = new Border { Padding = new Thickness(40), Child = frame } };
    }

    private static Control Disc(double k) => new Avalonia.Controls.Shapes.Ellipse
    {
        Width = 1258, Height = 1258, Fill = new SolidColorBrush(Color.Parse(Paper)),
        Stroke = new SolidColorBrush(Color.Parse(Ink)), StrokeThickness = 42 * k,
        [Canvas.LeftProperty] = 802 - 629.0, [Canvas.TopProperty] = 712 - 629.0,
    };

    /// <summary>One letter: a stem running into a round bowl, cut flat level with the ground, turned by <paramref name="angle"/>.</summary>
    private static Control Letter(double angle, double k)
    {
        var letter = new Canvas { Width = 1604, Height = 1604, Clip = new RectangleGeometry(new Rect(0, 0, 1604, 1135)) };
        letter.Children.Add(Stroke("M 796 1155 L 524.5 883.5", Ink, 63 * k));
        letter.Children.Add(Stroke("M 464 733 A 211 211 0 1 1 886 733 A 211 211 0 1 1 464 733 Z", Ink, 63 * k));
        if (angle == 0)
            return letter;
        // Turned around the logo's centre; the cut happens before the turn, as in the SVG.
        return new Canvas
        {
            Width = 1604, Height = 1604, Children = { letter },
            RenderTransform = new RotateTransform(angle, 802, 712), RenderTransformOrigin = RelativePoint.TopLeft,
        };
    }

    private static ShapePath Stroke(string data, string color, double width) => new()
    {
        Data = Geometry.Parse(data), Stroke = new SolidColorBrush(Color.Parse(color)), StrokeThickness = width, StrokeLineCap = PenLineCap.Flat,
    };

    public static RenderTargetBitmap Render(int size, bool alt = false)
    {
        var c = Build(size, alt);
        c.Measure(new Size(size, size));
        c.Arrange(new Rect(0, 0, size, size));
        var bitmap = new RenderTargetBitmap(new PixelSize(size, size), new Vector(96, 96));
        bitmap.Render(c);
        return bitmap;
    }

    public static WindowIcon Window(int size = 64, bool alt = false) => new(Render(size, alt));

    /// <summary>Writes a multi-size .ico (PNG entries) for the exe, or a 256 px PNG when the path ends in .png.</summary>
    public static void Write(string path)
    {
        if (path.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
        {
            using var png = File.Create(path);
            Render(256).Save(png);
            return;
        }

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
            var b = (byte)(sizes[i] >= 256 ? 0 : sizes[i]);
            w.Write(b);
            w.Write(b);
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

    private static Grid Tile(double size, string from, string to, Canvas art, double margin)
    {
        var g = new Grid { Width = size, Height = size };
        g.Children.Add(new Border
        {
            CornerRadius = new CornerRadius(size * 0.22),
            Background = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                GradientStops = { new GradientStop(Color.Parse(from), 0), new GradientStop(Color.Parse(to), 1) },
            },
        });
        g.Children.Add(new Viewbox { Margin = new Thickness(size * margin), Child = art });
        return g;
    }

    private static string RoundRect(double x1, double y1, double x2, double y2, double r) =>
        FormattableString.Invariant($"M{x1 + r},{y1} L{x2 - r},{y1} Q{x2},{y1} {x2},{y1 + r} L{x2},{y2 - r} Q{x2},{y2} {x2 - r},{y2} L{x1 + r},{y2} Q{x1},{y2} {x1},{y2 - r} L{x1},{y1 + r} Q{x1},{y1} {x1 + r},{y1} Z");
}