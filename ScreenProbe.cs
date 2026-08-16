using System.Buffers;

namespace ClickyBot;

internal static class ScreenProbe
{
    public const int MaxReferencePixels = 100_000;

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

    public static bool TryCaptureRegion(int x, int y, int width, int height, out byte[] rgb)
    {
        rgb = [];
        width = Math.Clamp(width, 1, 1200);
        height = Math.Clamp(height, 1, 800);
        var pixelCount = (long)width * height;
        if (pixelCount > MaxReferencePixels)
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
            if (previousObject == IntPtr.Zero || !NativeMethods.BitBlt(memoryDc, 0, 0, width, height, screenDc, x, y, NativeMethods.Srccopy | NativeMethods.CaptureBlt))
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
