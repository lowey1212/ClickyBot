namespace ClickyBot;

internal static class ImageMatcher
{
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
