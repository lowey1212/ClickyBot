namespace ClickyBot;

internal static class AionTargetBarMatcher
{
    internal static bool IsMarkerReference(int width, int height) => width < 60 || width <= height * 2;
    internal static bool ValidReference(byte[] reference, int width, int height) => ReferenceProblem(reference, width, height) is null;

    internal static string? ReferenceProblem(byte[] reference, int width, int height)
    {
        if (width is < 8 or > 1200 || height is < 8 or > 240)
            return $"Reference is {width}×{height}. Capture one target arrow, or the HP bar with its end markers (8–1200 px wide, 8–240 px high).";
        if (reference.LongLength != (long)width * height * 3)
            return "The target reference image is not loaded. Re-capture it or check its saved image path.";
        var markerOnly = IsMarkerReference(width, height);
        var first = Marker(reference, width, height, markerOnly ? null : false);
        if (first.Length < 8 || Edges(reference, width, height, first).Length < 4)
            return "The reference has no clear target arrow or left end marker. Capture the arrow tightly, or include both HP bar ends.";
        if (!markerOnly)
        {
            var last = Marker(reference, width, height, true);
            if (last.Length < 8 || Edges(reference, width, height, last).Length < 4)
                return "The whole-bar reference is missing its right end marker. Include both ends, or capture one target arrow tightly.";
        }
        return null;
    }

    // Tight arrow captures track that marker; wider bars check both end shapes.
    internal static (MatchLocation? Location, double Score) Find(byte[] frame, int width, int height,
        byte[] reference, int referenceWidth, int referenceHeight, int threshold, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!ValidReference(reference, referenceWidth, referenceHeight) || width < referenceWidth || height < referenceHeight
            || frame.LongLength != (long)width * height * 3) return (null, 0);
        var markerOnly = IsMarkerReference(referenceWidth, referenceHeight);
        var left = Marker(reference, referenceWidth, referenceHeight, markerOnly ? null : false);
        var leftEdges = Edges(reference, referenceWidth, referenceHeight, left);
        var right = markerOnly ? [] : Marker(reference, referenceWidth, referenceHeight, true);
        var rightEdges = markerOnly ? [] : Edges(reference, referenceWidth, referenceHeight, right);
        var required = Math.Clamp(threshold, 1, 100);
        for (var y = 0; y <= height - referenceHeight; y++)
        {
            token.ThrowIfCancellationRequested();
            for (var x = 0; x <= width - referenceWidth; x++)
            {
                if ((x & 31) == 0) token.ThrowIfCancellationRequested();
                double Score((int X, int Y)[] points, (int X, int Y)[] edges)
                {
                    var matches = points.Length;
                    foreach (var point in points)
                    {
                        var i = ((y + point.Y) * width + x + point.X) * 3;
                        if (!BrightNeutral(frame[i], frame[i + 1], frame[i + 2])) matches--;
                        // Reject unlikely positions early so broad user watch
                        // areas stay responsive to Stop and cancellation.
                        if (matches * 80d / points.Length + 20 < required) return 0;
                    }
                    var edgeMatches = edges.Length;
                    foreach (var point in edges)
                    {
                        var i = ((y + point.Y) * width + x + point.X) * 3;
                        if (BrightNeutral(frame[i], frame[i + 1], frame[i + 2])) edgeMatches--;
                        if (matches * 80d / points.Length + edgeMatches * 20d / edges.Length < required) return 0;
                    }
                    return matches * 80d / points.Length + edgeMatches * 20d / edges.Length;
                }
                var score = Score(left, leftEdges);
                if (score < required) continue;
                if (!markerOnly) score = Math.Min(score, Score(right, rightEdges));
                if (score >= required) return (new(x + referenceWidth / 2, y + referenceHeight / 2), score);
            }
        }
        return (null, 0);
    }

    private static bool BrightNeutral(byte r, byte g, byte b)
        => Math.Min(r, Math.Min(g, b)) >= 150 && Math.Max(r, Math.Max(g, b)) - Math.Min(r, Math.Min(g, b)) <= 60;

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
        return Sample(edges.ToArray());
    }

    private static (int X, int Y)[] Marker(byte[] reference, int width, int height, bool? right)
    {
        var points = new List<(int X, int Y)>();
        // A quarter at each end allows normal padding around captured arrows.
        var markerWidth = right is null ? width : Math.Clamp(width / 4, 8, 160);
        var start = right == true ? width - markerWidth : 0;
        for (var y = 0; y < height; y++)
        for (var x = start; x < start + markerWidth; x++)
        {
            var i = (y * width + x) * 3;
            if (BrightNeutral(reference[i], reference[i + 1], reference[i + 2])) points.Add((x, y));
        }
        return Sample(points.ToArray());
    }

    private static (int X, int Y)[] Sample((int X, int Y)[] points)
        => points.Length <= 64 ? points : Enumerable.Range(0, 64).Select(i => points[i * (points.Length - 1) / 63]).ToArray();
}
