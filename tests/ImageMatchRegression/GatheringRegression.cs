using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ClickyBot;

internal static class GatheringRegression
{
    internal static void Run()
    {
        static void Check(bool passed, string message) { if (!passed) throw new Exception(message); }
        static (byte[] Rgb, int Width, int Height) Load(string name)
        {
            using var stream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "fixtures", name));
            var bitmap = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
            var converted = new FormatConvertedBitmap(bitmap, PixelFormats.Rgb24, null, 0);
            var rgb = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 3];
            converted.CopyPixels(rgb, bitmap.PixelWidth * 3, 0);
            return (rgb, bitmap.PixelWidth, bitmap.PixelHeight);
        }
        static byte[] Crop((byte[] Rgb, int Width, int Height) frame, int x, int y, int width, int height)
        {
            var rgb = new byte[width * height * 3];
            for (var row = 0; row < height; row++)
                Array.Copy(frame.Rgb, ((y + row) * frame.Width + x) * 3, rgb, row * width * 3, width * 3);
            return rgb;
        }
        var active = Load("Aio2-Gather-active.png");
        var idle = Load("Aio2-Gather-idle.png");
        var bars = Load("Aio2-Gather-bars.png");
        var icon = Load("Aio2-Gather-icon.png");
        var activeIcon = Crop(active, 850, 360, 250, 200);
        var activeBars = Crop(active, 730, 790, 470, 120);
        var idleIcon = Crop(idle, 850, 360, 250, 200);
        var idleBars = Crop(idle, 730, 790, 470, 120);
        Check(AionGatherMatcher.Observe(icon.Rgb, icon.Width, icon.Height, false, default).Passed == true,
            "Original supplied gathering hand must match.");
        Check(AionGatherMatcher.Observe(activeIcon, 250, 200, false, default).Passed == true,
            "Real hand over changed terrain must match at the supplied HUD scale.");
        Check(AionGatherMatcher.Observe(idleIcon, 250, 200, false, default).Passed == false,
            "Real river frame without the hand must not authorize F.");
        Check(AionGatherMatcher.Observe(activeBars, 470, 120, true, default).Passed == false,
            "Real Mela Success/Fail panel must block F.");
        Check(AionGatherMatcher.Observe(bars.Rgb, bars.Width, bars.Height, true, default).Passed == false,
            "Real Orichalcum panel with different fills and node name must block F.");
        Check(AionGatherMatcher.Observe(idleBars, 470, 120, true, default).Passed == true,
            "Real river screenshot where the panel is absent must allow the gate.");

        // Preserve only text strokes while changing all other pixels, modelling
        // empty/partial/full progress backgrounds independently of the reference.
        var labelRegion = Crop(bars, 96, 70, 70, 43);
        var white = new bool[70 * 43];
        for (var i = 0; i < white.Length; i++)
            white[i] = Math.Min(labelRegion[i * 3], Math.Min(labelRegion[i * 3 + 1], labelRegion[i * 3 + 2])) >= 150
                && Math.Max(labelRegion[i * 3], Math.Max(labelRegion[i * 3 + 1], labelRegion[i * 3 + 2]))
                    - Math.Min(labelRegion[i * 3], Math.Min(labelRegion[i * 3 + 1], labelRegion[i * 3 + 2])) <= 60;
        foreach (var background in new[] { (R: 20, G: 25, B: 20), (R: 45, G: 130, B: 235), (R: 255, G: 145, B: 0) })
        {
            var changingFill = labelRegion.ToArray();
            for (var i = 0; i < white.Length; i++) if (!white[i])
            { changingFill[i * 3] = (byte)background.R; changingFill[i * 3 + 1] = (byte)background.G; changingFill[i * 3 + 2] = (byte)background.B; }
            Check(AionGatherMatcher.Observe(changingFill, 70, 43, true, default).Passed == false,
                "Label detector must block F over empty, blue and orange fill backgrounds.");
        }
        foreach (var (start, height) in new[] { (0, 20), (23, 20) })
        {
            var oneLabel = labelRegion.ToArray();
            for (var row = start; row < start + height; row++) Array.Clear(oneLabel, row * 70 * 3, 70 * 3);
            Check(AionGatherMatcher.Observe(oneLabel, 70, 43, true, default).Passed == false,
                "Either visible label must block F even if the other label is obscured.");
        }
        foreach (var flat in new byte[] { 0, 120, 255 })
            Check(AionGatherMatcher.Observe(Enumerable.Repeat(flat, 70 * 43 * 3).ToArray(), 70, 43, true, default).Passed == true,
                "Flat dark, bright and gray areas are not gathering bars.");
        Check(AionGatherMatcher.Observe([], 70, 43, true, default).Passed is null
            && AionGatherMatcher.Observe(new byte[30 * 20 * 3], 30, 20, true, default).Passed is null,
            "Unavailable or too-small captures must not count as absent bars.");
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        try { AionGatherMatcher.Observe(activeBars, 470, 120, true, cancelled.Token); throw new Exception("Cancellation ignored."); }
        catch (OperationCanceledException) { }
        foreach (var (hand, absent, allowed) in new[] { (true, true, true), (true, false, false), (false, true, false), (false, false, false) })
        {
            var main = AionGatherMatcher.Observe(hand ? activeIcon : idleIcon, 250, 200, false, default);
            var gate = AionGatherMatcher.Observe(absent ? idleBars : activeBars, 470, 120, true, default);
            Check(new RuleObservation(Guid.NewGuid(), DateTime.UtcNow, main, gate, "").Passed == allowed,
                "F must require hand visible AND gathering bars absent.");
        }
        Console.WriteLine("PASS: actual gathering icon/panel/absent river screenshots, changed terrain and progress fills, either label blocks F, hand AND no bars, invalid captures and cancellation; no real input sent.");
    }
}
