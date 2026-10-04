using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ClickyBot;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
        foreach (var text in new[] { "7s", "35s", "0.8", "1.2s", "123s", "1:30", "3m", "Is", "Os" })
            Check(CooldownTimerReader.IsTimerText(text), $"Timer syntax missed: {text}");
        foreach (var text in new[] { "", "ready", "Timer", "attack", "Q", "1ready" })
            Check(!CooldownTimerReader.IsTimerText(text), $"Non-timer text accepted: {text}");
        Check(new TimerReading(null, "Unknown").Observe(true).Passed is null, "Unknown must not pass absence.");
        Check(Conditions.Invert(new(null, "Missing capture")).Passed is null, "Missing reference must not pass inversion.");
        Check(Conditions.Invert(new(true, "Match")).Passed == false && Conditions.Invert(new(false, "No match")).Passed == true,
            "Inverted reference matching must require a valid non-match.");
        var invalid = new MacroRule { Condition = ConditionType.RegionSnapshotDiffers, SearchReference = true,
            WatchWidth = 4, WatchHeight = 4, ReferenceRgb = [], SearchWidth = 20, SearchHeight = 20 };
        Check(!new MacroEngine().EvaluateNow(invalid) && !invalid.ObservationValid && invalid.CurrentMatch is null,
            "The real inverted image-search engine must block missing references and expose no mouse target.");
        invalid.Condition = ConditionType.CooldownTimerAbsent;
        invalid.WatchWidth = 1; invalid.WatchHeight = 1;
        Check(!new MacroEngine().EvaluateNow(invalid) && !invalid.ObservationValid,
            "A malformed timer rectangle must not allow casting in the real engine.");
        Check(CooldownTimerReader.Read(new byte[36 * 18 * 3], 36, 18, CancellationToken.None).Present is null,
            "Blank captures must be unknown.");
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        try { CooldownTimerReader.Read([], 36, 18, cancelled.Token); throw new Exception("Cancellation was ignored."); }
        catch (OperationCanceledException) { }

        var clock = System.Diagnostics.Stopwatch.StartNew();
        foreach (var path in Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "fixtures"), "*.png"))
        {
            var source = new BitmapImage(new Uri(path));
            var bitmap = new FormatConvertedBitmap(source, PixelFormats.Rgb24, null, 0);
            var rgb = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 3]; bitmap.CopyPixels(rgb, bitmap.PixelWidth * 3, 0);
            var reading = CooldownTimerReader.Read(rgb, bitmap.PixelWidth, bitmap.PixelHeight, CancellationToken.None);
            Console.WriteLine($"{Path.GetFileName(path)}: {reading.Present} {reading.Detail}");
            Check(reading.Present == !Path.GetFileName(path).StartsWith("ready-"), $"Wrong timer result: {path} {reading.Detail}");
        }
        foreach (var text in new[] { "1s", "2s", "3s", "4s", "5s", "6s", "7s", "8s", "9s", "0s", "10s", "59s", "125s", "0.8s", "1.2", "2m" })
        {
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(38, 25, 60)), null, new Rect(0,0,48,18));
                dc.DrawText(new FormattedText(text, System.Globalization.CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight, new Typeface("Arial"), 14, Brushes.White, 1), new Point(5,0));
            }
            var rendered = new RenderTargetBitmap(48, 18, 96, 96, PixelFormats.Pbgra32); rendered.Render(visual);
            var bitmap = new FormatConvertedBitmap(rendered, PixelFormats.Rgb24, null, 0);
            var rgb = new byte[48 * 18 * 3]; bitmap.CopyPixels(rgb, 48*3, 0);
            var reading = CooldownTimerReader.Read(rgb, 48, 18, CancellationToken.None);
            Console.WriteLine($"Generated {text}: {reading.Present} {reading.Detail}");
            Check(reading.Present == true, $"Changing countdown {text} was missed.");
        }
        Console.WriteLine($"PASS: supplied timers, ready artwork, all digits and decimal timers; invalid/blank reads block casting; cancellation, inversion and real engine. Elapsed {clock.ElapsedMilliseconds} ms.");
    }
}
