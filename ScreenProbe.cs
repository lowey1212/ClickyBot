using System.Buffers;
using System.Runtime.CompilerServices;

namespace ClickyBot;

internal static class ScreenProbe
{
    private static readonly ConditionalWeakTable<MacroRule, PurpleRingTiming> RingTimings = new();
    internal static void ResetPurpleRingTiming(MacroRule rule) => RingTimings.Remove(rule);
    internal static double? ReadThroneHealth(ThroneHealingSettings settings, CancellationToken token)
    {
        if (settings.SearchWidth is < 1 or > 1200 || settings.SearchHeight is < 1 or > 800
            || settings.LowHpPercent is < 1 or > 100 || settings.HealthReferenceRgb.Length == 0) return null;
        if (!TryCaptureRegion(settings.SearchX, settings.SearchY, settings.SearchWidth, settings.SearchHeight,
            out var rgb, 1200 * 800)) return null;
        return ThroneHealthMatcher.Read(rgb, settings.SearchWidth, settings.SearchHeight,
            settings.HealthReferenceRgb, settings.HealthReferenceWidth, settings.HealthReferenceHeight, token);
    }
    public const int MaxReferencePixels = 100_000;
    public const int MaxSearchWidth = 3840;
    public const int MaxSearchHeight = 2160;

    public static bool TryReadPixel(int x, int y, out RgbColor color)
    {
        var dc = NativeMethods.GetDC(IntPtr.Zero);
        if (dc == IntPtr.Zero)
        {
            color = default;
            return false;
        }

        try
        {
            var value = NativeMethods.GetPixel(dc, x, y);
            if (value == uint.MaxValue)
            {
                color = default;
                return false;
            }

            color = new RgbColor(
                (byte)(value & 0xFF),
                (byte)((value >> 8) & 0xFF),
                (byte)((value >> 16) & 0xFF));
            return true;
        }
        finally
        {
            NativeMethods.ReleaseDC(IntPtr.Zero, dc);
        }
    }

    public static int Coverage(int x, int y, int width, int height, RgbColor target, int tolerance, CancellationToken token)
    {
        width = Math.Clamp(width, 1, 1200);
        height = Math.Clamp(height, 1, 800);
        var area = (long)width * height;
        var step = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(area / 1200d)));
        tolerance = Math.Clamp(tolerance, 0, 255);

        // A small region is cheaper to capture once than to query with a native
        // GetPixel call for every sample. Keep the existing GetPixel fallback
        // for large regions so a coverage check never allocates a large frame.
        if (area is >= 64 and <= MaxReferencePixels
            && TryCaptureRegion(x, y, width, height, out var captured))
        {
            var samplesFromCapture = 0;
            var matchesFromCapture = 0;
            for (var row = 0; row < height; row += step)
            {
                token.ThrowIfCancellationRequested();
                for (var column = 0; column < width; column += step)
                {
                    var index = ((row * width) + column) * 3;
                    var redDelta = captured[index] - target.R;
                    var greenDelta = captured[index + 1] - target.G;
                    var blueDelta = captured[index + 2] - target.B;
                    samplesFromCapture++;
                    if (redDelta >= -tolerance && redDelta <= tolerance
                        && greenDelta >= -tolerance && greenDelta <= tolerance
                        && blueDelta >= -tolerance && blueDelta <= tolerance)
                    {
                        matchesFromCapture++;
                    }
                }
            }

            return samplesFromCapture == 0
                ? -1
                : (int)Math.Round(matchesFromCapture * 100d / samplesFromCapture);
        }

        var samples = 0;
        var matches = 0;
        var dc = NativeMethods.GetDC(IntPtr.Zero);
        if (dc == IntPtr.Zero)
        {
            return -1;
        }

        try
        {
            for (var row = 0; row < height; row += step)
            {
                token.ThrowIfCancellationRequested();
                for (var column = 0; column < width; column += step)
                {
                    var value = NativeMethods.GetPixel(dc, x + column, y + row);
                    if (value == uint.MaxValue)
                    {
                        continue;
                    }

                    var sample = new RgbColor(
                        (byte)(value & 0xFF),
                        (byte)((value >> 8) & 0xFF),
                        (byte)((value >> 16) & 0xFF));
                    samples++;
                    if (sample.IsCloseTo(target, tolerance))
                    {
                        matches++;
                    }
                }
            }
        }
        finally
        {
            NativeMethods.ReleaseDC(IntPtr.Zero, dc);
        }

        return samples == 0 ? -1 : (int)Math.Round(matches * 100d / samples);
    }

    public static bool TryCaptureRegion(int x, int y, int width, int height, out byte[] rgb, int maxPixels = MaxReferencePixels)
        => TryCaptureScaledRegion(x, y, width, height, width, height, out rgb, maxPixels);

    internal static bool TryCapturePreview(int x, int y, int width, int height, out byte[] rgb, out int previewWidth, out int previewHeight)
    {
        var scale = Math.Min(1d, Math.Min(320d / Math.Max(1, width), 180d / Math.Max(1, height)));
        previewWidth = Math.Max(1, (int)Math.Round(width * scale));
        previewHeight = Math.Max(1, (int)Math.Round(height * scale));
        if (width is < 1 or > MaxSearchWidth || height is < 1 or > MaxSearchHeight)
        {
            rgb = [];
            return false;
        }
        return TryCaptureScaledRegion(x, y, width, height, previewWidth, previewHeight, out rgb, 320 * 180);
    }

    private static bool TryCaptureScaledRegion(int x, int y, int sourceWidth, int sourceHeight,
        int width, int height, out byte[] rgb, int maxPixels)
    {
        rgb = [];
        var pixelCount = (long)width * height;
        if (width < 1 || height < 1 || pixelCount > maxPixels)
        {
            return false;
        }

        var screenDc = NativeMethods.GetDC(IntPtr.Zero);
        if (screenDc == IntPtr.Zero)
        {
            return false;
        }

        var memoryDc = IntPtr.Zero;
        var bitmap = IntPtr.Zero;
        var previousObject = IntPtr.Zero;
        try
        {
            memoryDc = NativeMethods.CreateCompatibleDC(screenDc);
            bitmap = NativeMethods.CreateCompatibleBitmap(screenDc, width, height);
            if (memoryDc == IntPtr.Zero || bitmap == IntPtr.Zero)
            {
                return false;
            }

            previousObject = NativeMethods.SelectObject(memoryDc, bitmap);
            var copied = previousObject != IntPtr.Zero && (width == sourceWidth && height == sourceHeight
                ? NativeMethods.BitBlt(memoryDc, 0, 0, width, height, screenDc, x, y, NativeMethods.Srccopy | NativeMethods.CaptureBlt)
                : NativeMethods.StretchBlt(memoryDc, 0, 0, width, height, screenDc, x, y, sourceWidth, sourceHeight, NativeMethods.Srccopy));
            if (!copied)
            {
                return false;
            }

            var bgr = ArrayPool<byte>.Shared.Rent(checked((int)pixelCount * 4));
            var info = new NativeMethods.BITMAPINFO
            {
                Header = new NativeMethods.BITMAPINFOHEADER
                {
                    Size = (uint)System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.BITMAPINFOHEADER>(),
                    Width = width,
                    Height = -height,
                    Planes = 1,
                    BitCount = 32,
                    Compression = 0
                }
            };
            try
            {
                // GetDIBits requires the bitmap not to be selected into a DC.
                NativeMethods.SelectObject(memoryDc, previousObject);
                previousObject = IntPtr.Zero;
                if (NativeMethods.GetDIBits(memoryDc, bitmap, 0, (uint)height, bgr, ref info, NativeMethods.DibRgbColors) != height)
                {
                    return false;
                }

                rgb = new byte[checked((int)pixelCount * 3)];
                for (var pixel = 0; pixel < pixelCount; pixel++)
                {
                    var source = pixel * 4;
                    var destination = pixel * 3;
                    rgb[destination] = bgr[source + 2];
                    rgb[destination + 1] = bgr[source + 1];
                    rgb[destination + 2] = bgr[source];
                }

                return true;
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(bgr);
            }
        }
        finally
        {
            if (memoryDc != IntPtr.Zero && previousObject != IntPtr.Zero)
            {
                NativeMethods.SelectObject(memoryDc, previousObject);
            }
            if (bitmap != IntPtr.Zero)
            {
                NativeMethods.DeleteObject(bitmap);
            }
            if (memoryDc != IntPtr.Zero)
            {
                NativeMethods.DeleteDC(memoryDc);
            }
            NativeMethods.ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    public static MatchLocation? FindReference(MacroRule rule, CancellationToken token)
    {
        rule.ObservationValid = false;
        rule.LastImageScore = null;
        rule.ImageSearchDiagnostic = "";
        var x = rule.SearchX;
        var y = rule.SearchY;
        var width = rule.SearchWidth;
        var height = rule.SearchHeight;
        if (width is < 1 or > MaxSearchWidth || height is < 1 or > MaxSearchHeight)
        {
            rule.ImageSearchDiagnostic = $"Select an area between 1×1 and {MaxSearchWidth}×{MaxSearchHeight} pixels.";
            return null;
        }
        if (rule.ReferenceRgb.LongLength != (long)rule.WatchWidth * rule.WatchHeight * 3 || rule.ReferenceRgb.Length == 0)
        {
            rule.ImageSearchDiagnostic = "The reference is missing or its dimensions changed. Capture the reference again.";
            return null;
        }
        if (rule.WatchWidth > width || rule.WatchHeight > height)
        {
            rule.ImageSearchDiagnostic = "The reference is larger than the area to watch. Select a larger area.";
            return null;
        }
        token.ThrowIfCancellationRequested();
        if (!TryCaptureRegion(x, y, width, height, out var frame, MaxSearchWidth * MaxSearchHeight))
        {
            rule.ImageSearchDiagnostic = "Windows could not capture the selected area.";
            return null;
        }
        rule.ObservationValid = true;
        MatchLocation? match;
        if (rule.ImageMatchMethod == ImageMatchMethod.ImageSimilarity)
        {
            var result = ImageMatcher.FindSimilar(frame, width, height, rule.ReferenceRgb, rule.WatchWidth, rule.WatchHeight, token);
            rule.LastImageScore = result.Score;
            rule.ImageSearchDiagnostic = result.Location is null
                ? "No distinctive pattern found. Capture a detailed reference or choose PixelColors for flat colors."
                : $"Best image similarity {result.Score:F1}% at {x + result.Location.Value.X},{y + result.Location.Value.Y} (requires {rule.CoverageThreshold}%).";
            match = result.Score + 0.000001 >= Math.Clamp(rule.CoverageThreshold, 1, 100) ? result.Location : null;
        }
        else
        {
            match = ImageMatcher.Find(frame, width, height, rule.ReferenceRgb,
                rule.WatchWidth, rule.WatchHeight, rule.Tolerance, rule.CoverageThreshold, token);
            rule.ImageSearchDiagnostic = match.HasValue ? "Pixel colors matched." : "No location met the pixel-color threshold and tolerance.";
        }
        return match is { } point ? new MatchLocation(x + point.X, y + point.Y) : null;
    }

    public static MatchLocation? FindPurpleRing(MacroRule rule, CancellationToken token, bool applyTiming = true)
    {
        rule.ObservationValid = false;
        if (rule.WatchWidth is < 25 or > MaxSearchWidth || rule.WatchHeight is < 25 or > MaxSearchHeight)
        {
            if (applyTiming) RingTimings.GetValue(rule, _ => new PurpleRingTiming()).Unknown();
            rule.ImageSearchDiagnostic = $"Select the area where the purple Q prompt appears (25–{MaxSearchWidth} by 25–{MaxSearchHeight} pixels).";
            return null;
        }
        if (!TryCaptureRegion(rule.WatchX, rule.WatchY, rule.WatchWidth, rule.WatchHeight, out var rgb, MaxSearchWidth * MaxSearchHeight))
        {
            if (applyTiming) RingTimings.GetValue(rule, _ => new PurpleRingTiming()).Unknown();
            rule.ImageSearchDiagnostic = "Windows could not capture the purple-ring area.";
            return null;
        }
        rule.ObservationValid = true;
        var ring = PurpleRingMatcher.Find(rgb, rule.WatchWidth, rule.WatchHeight, token);
        var ready = !applyTiming ? ring.HasValue
            : RingTimings.GetValue(rule, _ => new PurpleRingTiming()).Observe(ring.HasValue, rule.PurpleRingDelayMs, Environment.TickCount64);
        rule.ImageSearchDiagnostic = ring.HasValue
            ? !applyTiming ? "Purple defence circle detected. START applies the configured delay."
                : ready ? "Purple defence circle detected; delay elapsed." : $"Purple defence circle detected; waiting {rule.PurpleRingDelayMs} ms before Q."
            : "Waiting for the purple defence circle.";
        return ready && ring is { } match
            ? new MatchLocation(rule.WatchX + match.X, rule.WatchY + match.Y) : null;
    }

    public static int ReferenceMatchPercent(int x, int y, int width, int height, byte[] referenceRgb, int tolerance, CancellationToken token)
    {
        width = Math.Clamp(width, 1, 1200);
        height = Math.Clamp(height, 1, 800);
        var pixelCount = (long)width * height;
        if (pixelCount > MaxReferencePixels || referenceRgb.Length != pixelCount * 3)
        {
            return -1;
        }

        // Capture the current watch rectangle as one bitmap, then compare it
        // against the stored reference image. This keeps the runtime model
        // consistent with what the user captured and avoids thousands of
        // individual GetPixel calls on every poll.
        if (!TryCaptureRegion(x, y, width, height, out var currentRgb))
        {
            return -1;
        }

        var step = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(pixelCount / 2500d)));
        tolerance = Math.Clamp(tolerance, 0, 255);
        var samples = 0;
        var matches = 0;
        for (var row = 0; row < height; row += step)
        {
            token.ThrowIfCancellationRequested();
            for (var column = 0; column < width; column += step)
            {
                var referenceIndex = ((row * width) + column) * 3;
                samples++;
                var redDelta = currentRgb[referenceIndex] - referenceRgb[referenceIndex];
                var greenDelta = currentRgb[referenceIndex + 1] - referenceRgb[referenceIndex + 1];
                var blueDelta = currentRgb[referenceIndex + 2] - referenceRgb[referenceIndex + 2];
                if (redDelta >= -tolerance && redDelta <= tolerance
                    && greenDelta >= -tolerance && greenDelta <= tolerance
                    && blueDelta >= -tolerance && blueDelta <= tolerance)
                {
                    matches++;
                }
            }
        }

        return samples == 0 ? -1 : (int)Math.Round(matches * 100d / samples);
    }
}
