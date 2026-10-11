using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ClickyBot;

internal static class AionGatherMatcher
{
    private sealed record Pattern(byte[] Rgb, int Width, int Height);
    private static readonly Lazy<Pattern[]> Icons = new(() => [Load("Icon"), Load("IconLive")]);
    private static readonly Lazy<Pattern[]> Bars = new(() =>
    {
        var pair = Load("Bars");
        var success = pair.Rgb.ToArray();
        var fail = pair.Rgb.ToArray();
        Array.Clear(success, 21 * pair.Width * 3, (pair.Height - 21) * pair.Width * 3);
        Array.Clear(fail, 0, 21 * pair.Width * 3);
        // Either label means gathering may still be active. Requiring both
        // would allow another F if one label were temporarily obscured.
        return [new(success, pair.Width, pair.Height), new(fail, pair.Width, pair.Height)];
    });

    private static Pattern Load(string name)
    {
        using var stream = typeof(AionGatherMatcher).Assembly.GetManifestResourceStream($"ClickyBot.Aio2.Gather{name}.png")
            ?? throw new IOException("The bundled gathering detector reference is unavailable.");
        var bitmap = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
        var converted = new FormatConvertedBitmap(bitmap, PixelFormats.Rgb24, null, 0);
        var rgb = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 3];
        converted.CopyPixels(rgb, bitmap.PixelWidth * 3, 0);
        // Both references are single patterns: the hand icon or the two label
        // outlines at their captured spacing. Bar fill and node names are omitted.
        if (!AionTargetBarMatcher.IsMarkerReference(bitmap.PixelWidth, bitmap.PixelHeight)
            || !AionTargetBarMatcher.ValidReference(rgb, bitmap.PixelWidth, bitmap.PixelHeight))
            throw new IOException("The bundled gathering detector has no readable outline.");
        return new(rgb, bitmap.PixelWidth, bitmap.PixelHeight);
    }

    internal static ConditionObservation Observe(byte[] frame, int width, int height, bool barsAbsent, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        try
        {
            var patterns = barsAbsent ? Bars.Value : Icons.Value;
            if (width is < 1 or > 1200 || height is < 1 or > 800
                || frame.LongLength != (long)width * height * 3
                || patterns.Any(pattern => width < pattern.Width || height < pattern.Height))
                return new(null, "Gathering area is unavailable or too small. Select an area containing the full icon or both Success/Fail labels.");
            // Reuse the local-contrast outline matcher. Only white strokes from
            // the reference vote, so terrain, blue/orange fill and the amount of
            // progress are not compared with the screenshot.
            var present = patterns.Any(pattern => AionTargetBarMatcher.Find(frame, width, height,
                pattern.Rgb, pattern.Width, pattern.Height, 90, token).Location is not null);
            return new(barsAbsent ? !present : present, barsAbsent
                ? present ? "Success/Fail gathering bars visible; F is blocked." : "No Success/Fail gathering bars in the selected area."
                : present ? "Gathering hand icon outline visible." : "No gathering hand icon in the selected area.");
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or NotSupportedException)
        {
            return new(null, "Gathering detection unavailable: " + ex.Message);
        }
    }
}
