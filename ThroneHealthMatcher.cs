namespace ClickyBot;

internal static class ThroneHealthMatcher
{
    // Locate the gold HP frame using its rim, excluding the changing fill/text.
    // Missing or unreadable frames are unknown health, never "zero HP".
    internal static double? Read(byte[] frame, int width, int height, byte[] reference, int rw, int rh, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (rw < 40 || rh < 12 || rw > width || rh > height
            || frame.LongLength != (long)width * height * 3 || reference.LongLength != (long)rw * rh * 3) return null;
        var samples = new List<(int X, int Y, int Offset)>();
        for (var y = 0; y < rh; y++)
        for (var x = 0; x < rw; x += 2)
        {
            if (y > 2 && y < rh - 4 && x > 7 && x < rw - 10) continue;
            var i = (y * rw + x) * 3;
            int r = reference[i], g = reference[i + 1], b = reference[i + 2];
            if (r >= 90 && g >= 55 && r >= g - 10 && g >= b) samples.Add((x, y, i));
        }
        if (samples.Count < 20) return null;
        int best = -1, bestX = 0, bestY = 0;
        int required = (int)Math.Ceiling(samples.Count * 0.8);
        for (var y = 0; y <= height - rh; y++)
        {
            token.ThrowIfCancellationRequested();
            for (var x = 0; x <= width - rw; x++)
            {
                int matches = 0, misses = 0;
                foreach (var sample in samples)
                {
                    var i = ((y + sample.Y) * width + x + sample.X) * 3;
                    if (Math.Abs(frame[i] - reference[sample.Offset]) <= 40
                        && Math.Abs(frame[i + 1] - reference[sample.Offset + 1]) <= 40
                        && Math.Abs(frame[i + 2] - reference[sample.Offset + 2]) <= 40) matches++;
                    else if (++misses > samples.Count - required) break;
                }
                if (matches >= required && matches > best) { best = matches; bestX = x; bestY = y; }
            }
        }
        if (best < required) return null;
        int fillWidth = rw - 18, filled = 0;
        for (var x = 8; x < rw - 10; x++)
        {
            token.ThrowIfCancellationRequested();
            int green = 0;
            for (var y = 3; y < rh - 4; y++)
            {
                var i = ((bestY + y) * width + bestX + x) * 3;
                int r = frame[i], g = frame[i + 1], b = frame[i + 2];
                if (g >= 65 && g > r + 20 && g > b + 10) green++;
            }
            if (green >= 3) filled++;
        }
        return filled * 100d / fillWidth;
    }
}
