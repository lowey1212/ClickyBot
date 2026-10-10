using System.Globalization;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;

namespace ClickyBot;

internal sealed record TimerReading(bool? Present, string Detail, bool Retryable = false)
{
    internal ConditionObservation Observe(bool requireAbsent) => new(
        Present.HasValue ? requireAbsent ? !Present.Value : Present.Value : null, Detail, Retryable);
}

// Windows OCR runs locally. No screenshots or recognised text leave the PC.
internal static class CooldownTimerReader
{
    internal const int MaxWidth = 160, MaxHeight = 64;
    private const int Scale = 6, Padding = 24, PrefixWidth = 220;
    private static readonly SemaphoreSlim ReaderLock = new(1);
    private static OcrEngine? _engine;
    private static byte[]? _prefix;
    private static readonly Regex NumericTimer = new(@"^\d{1,4}(?:[.,:]\d{1,3})?(?:[smh]|ms)?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    internal static TimerReading Read(byte[] rgb, int width, int height, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (width is < 16 or > MaxWidth || height is < 10 or > MaxHeight || rgb.Length != width * height * 3)
            return new(null, $"Select only the central timer line (16–{MaxWidth} × 10–{MaxHeight} pixels), excluding the hotkey and corner badges.");
        var min = 255; var max = 0;
        for (var i = 0; i < rgb.Length; i += 3)
        {
            var value = (rgb[i] + rgb[i + 1] + rgb[i + 2]) / 3;
            min = Math.Min(min, value); max = Math.Max(max, value);
        }
        if (max - min < 20) return new(null, "Timer area is blank or has too little contrast; readiness is unavailable.", Retryable: true);

        ReaderLock.Wait(token);
        try
        {
            _engine ??= OcrEngine.TryCreateFromLanguage(new Windows.Globalization.Language("en-US"));
            if (_engine is null) return new(null, "Windows English OCR is unavailable. Install the Windows English language OCR feature.");
            var uncertain = false;
            for (var mask = 0; mask < 2; mask++)
            {
                token.ThrowIfCancellationRequested();
                var data = Prepare(rgb, width, height, mask == 1, out var imageWidth, out var imageHeight);
                using var bitmap = SoftwareBitmap.CreateCopyFromBuffer(data.AsBuffer(), BitmapPixelFormat.Bgra8,
                    imageWidth, imageHeight, BitmapAlphaMode.Ignore);
                var result = _engine.RecognizeAsync(bitmap).AsTask(token).GetAwaiter().GetResult();
                token.ThrowIfCancellationRequested();
                // The synthetic word gives Windows OCR enough line context to
                // recognise short timers such as 7s. Only words in the captured
                // part count; the prefix itself cannot produce a timer.
                var words = result.Lines.SelectMany(line => line.Words)
                    .Where(word => word.BoundingRect.X + word.BoundingRect.Width / 2 >= PrefixWidth + Padding)
                    // A few bright pixels in icon artwork can be called "1"
                    // by OCR. Real HUD countdown letters occupy at least six
                    // native pixels vertically; ignore smaller decorative marks.
                    .Where(word => word.BoundingRect.Height >= 6 * Scale)
                    .Select(word => word.Text).ToArray();
                var text = string.Concat(words).Trim().Trim('!', '\'', '"', '|', '(', ')', '[', ']');
                if (IsTimerText(text)) return new(true, $"Cooldown timer detected: {text}. The value is not compared with a reference number.");
                if (words.Length > 0) uncertain = true;
            }
            return uncertain ? new(null, "Text in the timer area could not be identified reliably; readiness is blocked. Tighten the timer area.", Retryable: true)
                : new(false, "No cooldown timer detected in the selected central area.");
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or InvalidOperationException or TypeInitializationException or DllNotFoundException)
        {
            return new(null, $"Timer reader unavailable: {ex.Message}");
        }
        finally { ReaderLock.Release(); }
    }

    internal static bool IsTimerText(string text)
    {
        text = string.Concat(text.Where(c => !char.IsWhiteSpace(c)));
        if (NumericTimer.IsMatch(text)) return true;
        // OCR sometimes reads a narrow 1 as I/l or zero as O. Such timer-shaped
        // text blocks casting too, without requiring the exact digit value.
        return Regex.IsMatch(text, @"^[0-9IlOo]{1,4}(?:[.,:][0-9IlOo]{1,3})?[smh]$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static byte[] Prepare(byte[] rgb, int width, int height, bool whiteTextMask, out int w, out int h)
    {
        w = PrefixWidth + width * Scale + Padding * 2;
        h = height * Scale + Padding * 2;
        var pixels = Enumerable.Repeat((byte)255, w * h * 4).ToArray();
        _prefix ??= CreatePrefix();
        for (var row = 0; row < 120 && row < h; row++)
            Array.Copy(_prefix, row * PrefixWidth * 4, pixels, row * w * 4, PrefixWidth * 4);
        for (var y = 0; y < height; y++) for (var x = 0; x < width; x++)
        {
            var index = (y * width + x) * 3;
            var r = rgb[index]; var g = rgb[index + 1]; var b = rgb[index + 2];
            var min = Math.Min(r, Math.Min(g, b)); var max = Math.Max(r, Math.Max(g, b));
            var ink = min >= 145 && max - min <= 60;
            for (var yy = 0; yy < Scale; yy++) for (var xx = 0; xx < Scale; xx++)
            {
                var destination = ((y * Scale + Padding + yy) * w + PrefixWidth + x * Scale + Padding + xx) * 4;
                pixels[destination] = whiteTextMask ? (byte)(ink ? 0 : 255) : b;
                pixels[destination + 1] = whiteTextMask ? (byte)(ink ? 0 : 255) : g;
                pixels[destination + 2] = whiteTextMask ? (byte)(ink ? 0 : 255) : r;
            }
        }
        return pixels;
    }

    private static byte[] CreatePrefix()
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, PrefixWidth, 120));
            dc.DrawText(new FormattedText("Timer", CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface("Arial"), 60, Brushes.Black, 1), new Point(0, Padding));
        }
        var bitmap = new RenderTargetBitmap(PrefixWidth, 120, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var pixels = new byte[PrefixWidth * 120 * 4];
        bitmap.CopyPixels(pixels, PrefixWidth * 4, 0);
        return pixels;
    }
}
