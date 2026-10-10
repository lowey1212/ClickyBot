namespace ClickyBot;

internal static class AionTargetBarMatcher
{
    internal static int MatchThreshold(int threshold) => Math.Clamp(threshold, 70, 100);
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
        if (first.Length < 8 || Outline(reference, width, height, first).Length < 8)
            return "The reference has no clear target arrow or left end marker. Capture the arrow tightly, or include both HP bar ends.";
        if (!markerOnly)
        {
            var last = Marker(reference, width, height, true);
            if (last.Length < 8 || Outline(reference, width, height, last).Length < 8)
                return "The whole-bar reference is missing its right end marker. Include both ends, or capture one target arrow tightly.";
        }
        return null;
    }

    // Reference colors isolate the captured outline only. Live detection uses
    // local contrast along that outline, without a white/cyan RGB cutoff.
    internal static (MatchLocation? Location, double Score) Find(byte[] frame, int width, int height,
        byte[] reference, int referenceWidth, int referenceHeight, int threshold, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!ValidReference(reference, referenceWidth, referenceHeight) || width < referenceWidth || height < referenceHeight
            || frame.LongLength != (long)width * height * 3) return (null, 0);
        var markerOnly = IsMarkerReference(referenceWidth, referenceHeight);
        var left = Marker(reference, referenceWidth, referenceHeight, markerOnly ? null : false);
        var leftOutline = Outline(reference, referenceWidth, referenceHeight, left);
        var right = markerOnly ? [] : Marker(reference, referenceWidth, referenceHeight, true);
        var rightOutline = markerOnly ? [] : Outline(reference, referenceWidth, referenceHeight, right);
        // Even a very low user threshold must retain a recognisable outline.
        var required = MatchThreshold(threshold);
        var gray = new byte[width * height];
        for (var i = 0; i < gray.Length; i++)
        {
            if ((i & 8191) == 0) token.ThrowIfCancellationRequested();
            gray[i] = Luminance(frame, i * 3);
        }
        var leftOffsets = Offsets(leftOutline, width);
        var rightOffsets = Offsets(rightOutline, width);
        double Score(int origin, (int Inside, int Outside)[] outline)
        {
            var misses = 0;
            var allowedMisses = outline.Length * (100 - required) / 100;
            foreach (var pair in outline)
            {
                // A visible local boundary is required. Uniform light, cyan,
                // orange and dark patches cannot pass regardless of threshold.
                if (gray[origin + pair.Inside] - gray[origin + pair.Outside] < 8
                    && ++misses > allowedMisses) return 0;
            }
            return (outline.Length - misses) * 100d / outline.Length;
        }
        for (var y = 0; y <= height - referenceHeight; y++)
        {
            token.ThrowIfCancellationRequested();
            for (var x = 0; x <= width - referenceWidth; x++)
            {
                if ((x & 31) == 0) token.ThrowIfCancellationRequested();
                var origin = y * width + x;
                var score = Score(origin, leftOffsets);
                if (score < required) continue;
                if (!markerOnly) score = Math.Min(score, Score(origin, rightOffsets));
                if (score >= required) return (new(x + referenceWidth / 2, y + referenceHeight / 2), score);
            }
        }
        return (null, 0);
    }

    private static bool MarkerColor(byte r, byte g, byte b)
        => (Math.Min(r, Math.Min(g, b)) >= 150 && Math.Max(r, Math.Max(g, b)) - Math.Min(r, Math.Min(g, b)) <= 60)
            || (r >= 70 && g >= 150 && b >= 170 && b >= r + 20 && g >= r + 10 && Math.Abs(b - g) <= 80);

    private readonly record struct Boundary(int X, int Y, int OutsideX, int OutsideY);

    private static byte Luminance(byte[] rgb, int i) => (byte)((rgb[i] + rgb[i + 1] + rgb[i + 2]) / 3);

    private static (int Inside, int Outside)[] Offsets(Boundary[] outline, int width)
        => outline.Select(pair => (pair.Y * width + pair.X, pair.OutsideY * width + pair.OutsideX)).ToArray();

    private static Boundary[] Outline(byte[] reference, int width, int height, (int X, int Y)[] points)
    {
        var edges = new List<Boundary>();
        foreach (var point in points)
        foreach (var (dx, dy) in new[] { (-1, 0), (1, 0), (0, -1), (0, 1) })
        {
            var adjacentX = point.X + dx; var adjacentY = point.Y + dy;
            if (adjacentX < 0 || adjacentX >= width || adjacentY < 0 || adjacentY >= height) continue;
            var adjacent = (adjacentY * width + adjacentX) * 3;
            if (MarkerColor(reference[adjacent], reference[adjacent + 1], reference[adjacent + 2])) continue;
            // Look beyond antialiasing/glow, but remain inside the capture.
            // Only clear reference boundaries vote on the live arrow shape.
            var bestContrast = 31;
            Boundary? best = null;
            for (var distance = 1; distance <= 4; distance++)
            {
                var x = point.X + dx * distance; var y = point.Y + dy * distance;
                if (x < 0 || x >= width || y < 0 || y >= height) break;
                var contrast = Luminance(reference, (point.Y * width + point.X) * 3)
                    - Luminance(reference, (y * width + x) * 3);
                if (contrast <= bestContrast) continue;
                bestContrast = contrast;
                best = new(point.X, point.Y, x, y);
            }
            if (best is { } pair) edges.Add(pair);
        }
        // Interleave distant parts of the outline so early rejection does not
        // depend solely on the top edge or one arm of an arrow.
        var sampled = edges.Count <= 96 ? edges.ToArray()
            : Enumerable.Range(0, 96).Select(i => edges[i * (edges.Count - 1) / 95]).ToArray();
        return Enumerable.Range(0, 4).SelectMany(group => sampled.Where((_, i) => i % 4 == group)).ToArray();
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
            if (MarkerColor(reference[i], reference[i + 1], reference[i + 2])) points.Add((x, y));
        }
        return points.ToArray();
    }

}
