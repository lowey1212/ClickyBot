using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ClickyBot;

internal static class AionRangeMessageMatcher
{
    private readonly record struct Edge(int X, int Y, int OutsideX, int OutsideY);
    private sealed record Pattern(int Width, int Height, Edge[][] Sections);
    private static readonly Lazy<Pattern> Message = new(Load);

    private static int RedStrength(byte[] rgb, int pixel)
    {
        var i = pixel * 3;
        return rgb[i] - Math.Max(rgb[i + 1], rgb[i + 2]);
    }

    private static Pattern Load()
    {
        using var stream = typeof(AionRangeMessageMatcher).Assembly.GetManifestResourceStream("ClickyBot.Aio2.OutOfRange.png")
            ?? throw new IOException("The bundled out-of-range message reference is missing.");
        var bitmap = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
        var converted = new FormatConvertedBitmap(bitmap, PixelFormats.Rgb24, null, 0);
        var width = bitmap.PixelWidth; var height = bitmap.PixelHeight;
        var rgb = new byte[width * height * 3];
        converted.CopyPixels(rgb, width * 3, 0);
        var sections = new List<Edge>[] { [], [], [], [] };
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var pixel = y * width + x;
            if (rgb[pixel * 3] < 180 || RedStrength(rgb, pixel) < 75) continue;
            foreach (var (dx, dy) in new[] { (-1, 0), (1, 0), (0, -1), (0, 1) })
            {
                Edge? edge = null; var bestContrast = 49;
                for (var distance = 1; distance <= 3; distance++)
                {
                    var ox = x + dx * distance; var oy = y + dy * distance;
                    if (ox < 0 || ox >= width || oy < 0 || oy >= height) break;
                    var contrast = RedStrength(rgb, pixel) - RedStrength(rgb, oy * width + ox);
                    if (contrast <= bestContrast) continue;
                    bestContrast = contrast; edge = new(x, y, ox, oy);
                }
                if (edge is { } found) sections[Math.Min(3, x * 4 / width)].Add(found);
            }
        }
        if (sections.Any(section => section.Count < 32)) throw new IOException("The range message reference has no clear text outline.");
        return new(width, height, sections.Select(section => Enumerable.Range(0, Math.Min(64, section.Count))
            .Select(i => section[i * (section.Count - 1) / (Math.Min(64, section.Count) - 1)]).ToArray()).ToArray());
    }

    internal static ConditionObservation Observe(byte[] frame, int width, int height, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        try
        {
            var pattern = Message.Value;
            if (width < pattern.Width || width > 1200 || height < pattern.Height || height > 240
                || frame.LongLength != (long)width * height * 3)
                return new(null, "Range message capture is unavailable or smaller than the full message.");
            var red = new short[width * height];
            for (var i = 0; i < red.Length; i++)
            {
                if ((i & 8191) == 0) token.ThrowIfCancellationRequested();
                red[i] = (short)RedStrength(frame, i);
            }
            var sections = pattern.Sections.Select(section => section.Select(edge =>
                (Inside: edge.Y * width + edge.X, Outside: edge.OutsideY * width + edge.OutsideX)).ToArray()).ToArray();
            bool Matches(int origin)
            {
                // Every quarter of the complete phrase must have its red text
                // outline. Red terrain, solid bars and other toast text fail.
                foreach (var section in sections)
                {
                    var misses = 0;
                    foreach (var edge in section)
                        if ((red[origin + edge.Inside] < 20 || red[origin + edge.Inside] - red[origin + edge.Outside] < 12)
                            && ++misses > section.Length / 10) return false;
                }
                return true;
            }
            for (var y = 0; y <= height - pattern.Height; y++)
            {
                token.ThrowIfCancellationRequested();
                for (var x = 0; x <= width - pattern.Width; x++)
                {
                    if ((x & 31) == 0) token.ThrowIfCancellationRequested();
                    if (Matches(y * width + x)) return new(true, "Target is out of reach. Move into skill range.");
                }
            }
            return new(false, "No out-of-range message in the configured area.");
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or NotSupportedException)
        {
            return new(null, "Range message detection unavailable: " + ex.Message);
        }
    }
}
