namespace ClickyBot;

internal static class AionTargetBarMatcher
{
    internal static bool ValidReference(byte[] reference, int width, int height)
        => width is >= 60 and <= 1200 && height is >= 10 and <= 100
            && reference.LongLength == (long)width * height * 3
            && Marker(reference, width, height, false).Length >= 8
            && Marker(reference, width, height, true).Length >= 8;

    // Only the neutral, bright end-marker pixels matter. The centre contains
    // changing health fill, names and scenery and is deliberately excluded.
    internal static (MatchLocation? Location, double Score) Find(byte[] frame, int width, int height,
        byte[] reference, int referenceWidth, int referenceHeight, int threshold, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!ValidReference(reference, referenceWidth, referenceHeight) || width < referenceWidth || height < referenceHeight
            || frame.LongLength != (long)width * height * 3) return (null, 0);
        var left = Marker(reference, referenceWidth, referenceHeight, false);
        var right = Marker(reference, referenceWidth, referenceHeight, true);
        var leftEdges = Edges(reference, referenceWidth, referenceHeight, left);
        var rightEdges = Edges(reference, referenceWidth, referenceHeight, right);
        var best = 0d;
        MatchLocation? location = null;
        for (var y = 0; y <= height - referenceHeight; y++)
        {
            token.ThrowIfCancellationRequested();
            for (var x = 0; x <= width - referenceWidth; x++)
            {
                if ((x & 31) == 0) token.ThrowIfCancellationRequested();
                double Score((int X, int Y)[] points, bool bright)
                {
                    var matches = 0;
                    foreach (var point in points)
                    {
                        var i = ((y + point.Y) * width + x + point.X) * 3;
                        if (BrightNeutral(frame[i], frame[i + 1], frame[i + 2]) == bright) matches++;
                    }
                    return points.Length == 0 ? 0 : matches * 100d / points.Length;
                }
                var score = Math.Min(Score(left, true) * .8 + Score(leftEdges, false) * .2,
                    Score(right, true) * .8 + Score(rightEdges, false) * .2);
                if (score > best) { best = score; location = new(x + referenceWidth / 2, y + referenceHeight / 2); }
                if (best >= 100) return (location, best);
            }
        }
        return (best >= Math.Clamp(threshold, 1, 100) ? location : null, best);
    }

    private static bool BrightNeutral(byte r, byte g, byte b)
        => Math.Min(r, Math.Min(g, b)) >= 85 && Math.Max(r, Math.Max(g, b)) - Math.Min(r, Math.Min(g, b)) <= 60;

    private static (int X, int Y)[] Edges(byte[] reference, int width, int height, (int X, int Y)[] points)
    {
        var edges = new HashSet<(int X, int Y)>();
        foreach (var point in points)
        foreach (var (dx, dy) in new[] { (-1, 0), (1, 0), (0, -1), (0, 1) })
        {
            var x = point.X + dx; var y = point.Y + dy;
            if (x < 0 || x >= width || y < 0 || y >= height) continue;
            var i = (y * width + x) * 3;
            if (!BrightNeutral(reference[i], reference[i + 1], reference[i + 2])) edges.Add((x, y));
        }
        return edges.Where((_, index) => index % Math.Max(1, edges.Count / 64) == 0).ToArray();
    }

    private static (int X, int Y)[] Marker(byte[] reference, int width, int height, bool right)
    {
        var points = new List<(int X, int Y)>();
        var markerWidth = Math.Clamp(width / 16, 8, 24);
        var start = right ? width - markerWidth : 0;
        for (var y = 0; y < height; y++)
        for (var x = start; x < start + markerWidth; x++)
        {
            var i = (y * width + x) * 3;
            if (BrightNeutral(reference[i], reference[i + 1], reference[i + 2])) points.Add((x, y));
        }
        return points.Where((_, index) => index % Math.Max(1, points.Count / 64) == 0).ToArray();
    }
}
