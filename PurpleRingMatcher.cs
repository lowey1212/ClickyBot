namespace ClickyBot;

internal static class PurpleRingMatcher
{
    // Detect a circular purple arc at multiple radii. The centre artwork and
    // background are deliberately excluded: the outer ring shrinks over time.
    internal static MatchLocation? Find(byte[] rgb, int width, int height, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (width < 25 || height < 25 || rgb.LongLength != (long)width * height * 3) return null;
        var purple = new bool[width * height];
        for (var i = 0; i < purple.Length; i++)
        {
            if ((i & 8191) == 0) token.ThrowIfCancellationRequested();
            int r = rgb[i * 3], g = rgb[i * 3 + 1], b = rgb[i * 3 + 2];
            purple[i] = r >= 200 && b >= 230 && r >= g + 20 && b >= g + 30;
        }
        // Dilate by three pixels to allow glow, interrupted arcs and grid error.
        var mask = new bool[purple.Length];
        for (var y = 0; y < height; y++)
        {
            token.ThrowIfCancellationRequested();
            for (var x = 0; x < width; x++)
                if (purple[y * width + x])
                    for (var dy = -3; dy <= 3; dy++)
                    for (var dx = -3; dx <= 3; dx++)
                        if (x + dx >= 0 && x + dx < width && y + dy >= 0 && y + dy < height)
                            mask[(y + dy) * width + x + dx] = true;
        }
        var maxRadius = Math.Min(120, Math.Min(width, height) / 2 - 1);
        for (var radius = 12; radius <= maxRadius; radius += 2)
        {
            var offsets = Enumerable.Range(0, 32).Select(i =>
                ((int)Math.Round(radius * Math.Cos(i * Math.PI / 16)),
                 (int)Math.Round(radius * Math.Sin(i * Math.PI / 16)))).ToArray();
            for (var y = radius; y < height - radius; y += 4)
            {
                token.ThrowIfCancellationRequested();
                for (var x = radius; x < width - radius; x += 4)
                {
                    int matches = 0, misses = 0;
                    foreach (var (dx, dy) in offsets)
                    {
                        if (mask[(y + dy) * width + x + dx]) matches++;
                        else if (++misses > 4) break;
                    }
                    if (matches < 28) continue;
                    // Reject solid purple patches: a prompt has an open interior.
                    int inside = 0;
                    for (var i = 0; i < 16; i++)
                    {
                        var dx = (int)Math.Round(radius * 0.5 * Math.Cos(i * Math.PI / 8));
                        var dy = (int)Math.Round(radius * 0.5 * Math.Sin(i * Math.PI / 8));
                        var iRgb = ((y + dy) * width + x + dx) * 3;
                        int r = rgb[iRgb], g = rgb[iRgb + 1], b = rgb[iRgb + 2];
                        // Include the white core of a violet glow so a bright
                        // beam cannot masquerade as a hollow ring.
                        if (r >= 140 && b >= 170 && b >= g && r >= g - 15) inside++;
                    }
                    if (inside > 4) continue;
                    // Check a second contour outside the ring. A horizontal
                    // beam or a wide glow patch has no bounded circular edge.
                    int outside = 0;
                    for (var i = 0; i < 32; i++)
                    {
                        var dx = (int)Math.Round((radius + 9) * Math.Cos(i * Math.PI / 16));
                        var dy = (int)Math.Round((radius + 9) * Math.Sin(i * Math.PI / 16));
                        if (x + dx >= 0 && x + dx < width && y + dy >= 0 && y + dy < height
                            && purple[(y + dy) * width + x + dx]) outside++;
                    }
                    if (outside <= 10) return new MatchLocation(x, y);
                }
            }
        }
        return null;
    }
}
