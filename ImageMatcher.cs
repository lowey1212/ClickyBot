namespace ClickyBot;

internal static class ImageMatcher
{
    // Zero-mean normalized correlation on luminance. Compare the pattern
    // rather than requiring near-identical RGB values at every sample.
    internal static (MatchLocation? Location, double Score) FindSimilar(
        byte[] frame, int width, int height, byte[] reference, int referenceWidth, int referenceHeight,
        CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (width < 1 || height < 1 || referenceWidth < 1 || referenceHeight < 1
            || referenceWidth > width || referenceHeight > height
            || frame.LongLength != (long)width * height * 3
            || reference.LongLength != (long)referenceWidth * referenceHeight * 3)
            return (null, 0);

        var gray = new byte[width * height];
        for (int i = 0; i < gray.Length; i++)
        {
            if ((i & 8191) == 0) token.ThrowIfCancellationRequested();
            gray[i] = (byte)((frame[i * 3] + frame[i * 3 + 1] + frame[i * 3 + 2]) / 3);
        }
        int columns = Math.Min(referenceWidth, 16), rows = Math.Min(referenceHeight, 16);
        int count = columns * rows;
        var offsets = new int[count];
        var values = new int[count];
        var referenceRed = new byte[count];
        var referenceGreen = new byte[count];
        var referenceBlue = new byte[count];
        int referenceSum = 0, referenceSquares = 0, index = 0;
        for (int row = 0; row < rows; row++)
        for (int column = 0; column < columns; column++)
        {
            int px = columns == 1 ? 0 : column * (referenceWidth - 1) / (columns - 1);
            int py = rows == 1 ? 0 : row * (referenceHeight - 1) / (rows - 1);
            int source = (py * referenceWidth + px) * 3;
            int value = (reference[source] + reference[source + 1] + reference[source + 2]) / 3;
            offsets[index] = py * width + px;
            values[index++] = value;
            referenceRed[index - 1] = reference[source];
            referenceGreen[index - 1] = reference[source + 1];
            referenceBlue[index - 1] = reference[source + 2];
            referenceSum += value;
            referenceSquares += value * value;
        }
        double referenceVariance = (double)count * referenceSquares - (double)referenceSum * referenceSum;
        if (referenceVariance <= count * count) return (null, 0); // Flat images have no reliable shape.
        double best = 0;
        MatchLocation? location = null;
        for (int y = 0; y <= height - referenceHeight; y++)
        {
            token.ThrowIfCancellationRequested();
            for (int x = 0; x <= width - referenceWidth; x++)
            {
                if ((x & 31) == 0) token.ThrowIfCancellationRequested();
                int origin = y * width + x;
                int sum = 0, squares = 0, cross = 0;
                for (int i = 0; i < count; i++)
                {
                    int value = gray[origin + offsets[i]];
                    sum += value;
                    squares += value * value;
                    cross += value * values[i];
                }
                double covariance = (double)count * cross - (double)sum * referenceSum;
                double variance = (double)count * squares - (double)sum * sum;
                double product = variance * referenceVariance;
                if (variance <= count * count || covariance <= 0)
                    continue;
                var luminanceScore = Math.Clamp(covariance / Math.Sqrt(product), 0, 1);
                double colorDistance = 0;
                var colorStep = Math.Max(1, count / 64);
                var colorSamples = 0;
                for (int i = 0; i < count; i += colorStep)
                {
                    var frameOffset = origin + offsets[i];
                    var frameTotal = Math.Max(1, gray[frameOffset] * 3);
                    var referenceTotal = Math.Max(1, referenceRed[i] + referenceGreen[i] + referenceBlue[i]);
                    var rgbOffset = frameOffset * 3;
                    var redDelta = frame[rgbOffset] / (double)frameTotal - referenceRed[i] / (double)referenceTotal;
                    var greenDelta = frame[rgbOffset + 1] / (double)frameTotal - referenceGreen[i] / (double)referenceTotal;
                    var blueDelta = frame[rgbOffset + 2] / (double)frameTotal - referenceBlue[i] / (double)referenceTotal;
                    colorDistance += redDelta * redDelta + greenDelta * greenDelta + blueDelta * blueDelta;
                    colorSamples++;
                }
                var colorScore = Math.Clamp(1d - Math.Sqrt(colorDistance / colorSamples / 2d), 0, 1);
                var score = luminanceScore * 0.7 + colorScore * 0.3;
                if (score <= best) continue;
                best = score;
                location = new MatchLocation(x + referenceWidth / 2, y + referenceHeight / 2);
                if (best >= 0.999999999) return (location, 100);
            }
        }
        return (location, best * 100);
    }

    // Scan every possible position, comparing up to 256 evenly spaced samples
    // per candidate. Return the centre of the first qualifying match.
    internal static MatchLocation? Find(byte[] frame, int width, int height,
        byte[] reference, int referenceWidth, int referenceHeight,
        int tolerance, int threshold, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (width < 1 || height < 1 || referenceWidth < 1 || referenceHeight < 1
            || referenceWidth > width || referenceHeight > height
            || frame.LongLength != (long)width * height * 3
            || reference.LongLength != (long)referenceWidth * referenceHeight * 3)
            return null;

        tolerance = Math.Clamp(tolerance, 0, 255);
        threshold = Math.Clamp(threshold, 1, 100);
        var samples = new List<(int FrameOffset, int ReferenceOffset)>();
        var columns = Math.Min(referenceWidth, 16);
        var rows = Math.Min(referenceHeight, 16);
        for (int row = 0; row < rows; row++)
        for (int column = 0; column < columns; column++)
        {
            int px = columns == 1 ? 0 : column * (referenceWidth - 1) / (columns - 1);
            int py = rows == 1 ? 0 : row * (referenceHeight - 1) / (rows - 1);
            samples.Add(((py * width + px) * 3, (py * referenceWidth + px) * 3));
        }
        int allowedMisses = samples.Count * (100 - threshold) / 100;
        for (int y = 0; y <= height - referenceHeight; y++)
        {
            token.ThrowIfCancellationRequested();
            for (int x = 0; x <= width - referenceWidth; x++)
            {
                if ((x & 31) == 0) token.ThrowIfCancellationRequested();
                int origin = (y * width + x) * 3;
                int misses = 0;
                foreach (var sample in samples)
                {
                    int a = origin + sample.FrameOffset;
                    int b = sample.ReferenceOffset;
                    if (Math.Abs(frame[a] - reference[b]) > tolerance
                        || Math.Abs(frame[a + 1] - reference[b + 1]) > tolerance
                        || Math.Abs(frame[a + 2] - reference[b + 2]) > tolerance)
                    {
                        if (++misses > allowedMisses) break;
                    }
                }
                if (misses <= allowedMisses)
                    return new MatchLocation(x + referenceWidth / 2, y + referenceHeight / 2);
            }
        }
        return null;
    }
}
