using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ClickyBot;

internal static class ImageFileDiagnostic
{
    internal static void Run(string framePath, string referencePath)
    {
        static (byte[] Rgb, int Width, int Height) Read(string path)
        {
            using var stream = File.OpenRead(path);
            var decoded = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
            var rgb = new FormatConvertedBitmap(decoded, PixelFormats.Rgb24, null, 0);
            var data = new byte[rgb.PixelWidth * rgb.PixelHeight * 3];
            rgb.CopyPixels(data, rgb.PixelWidth * 3, 0);
            return (data, rgb.PixelWidth, rgb.PixelHeight);
        }
        var frame = Read(framePath);
        var reference = Read(referencePath);
        Console.WriteLine($"Frame {frame.Width}x{frame.Height}; reference {reference.Width}x{reference.Height}");
        var timer = System.Diagnostics.Stopwatch.StartNew();
        var similar = ImageMatcher.FindSimilar(frame.Rgb, frame.Width, frame.Height,
            reference.Rgb, reference.Width, reference.Height, default);
        Console.WriteLine($"Production similarity: {similar.Score:F2}% at {similar.Location}; {timer.ElapsedMilliseconds} ms");
        foreach (var threshold in new[] { 100, 90, 75, 50 })
        {
            var point = ImageMatcher.Find(frame.Rgb, frame.Width, frame.Height, reference.Rgb,
                reference.Width, reference.Height, 15, threshold, default);
            Console.WriteLine($"Tolerance 15, threshold {threshold}: {point?.ToString() ?? "NO MATCH"}");
        }
        var samples = new List<(int FrameOffset, double Reference)>();
        double sumReference = 0, squareReference = 0;
        for (int row = 0; row < Math.Min(reference.Height, 16); row++)
        for (int column = 0; column < Math.Min(reference.Width, 16); column++)
        {
            var px = column * (reference.Width - 1) / Math.Max(1, Math.Min(reference.Width, 16) - 1);
            var py = row * (reference.Height - 1) / Math.Max(1, Math.Min(reference.Height, 16) - 1);
            int offset = (py * reference.Width + px) * 3;
            double value = (reference.Rgb[offset] + reference.Rgb[offset + 1] + reference.Rgb[offset + 2]) / 3d;
            samples.Add(((py * frame.Width + px) * 3, value));
            sumReference += value;
            squareReference += value * value;
        }
        double best = -1;
        int bestX = 0, bestY = 0;
        double bestMean = 0;
        for (int y = 0; y <= frame.Height - reference.Height; y++)
        for (int x = 0; x <= frame.Width - reference.Width; x++)
        {
            double sum = 0, square = 0, cross = 0;
            int origin = (y * frame.Width + x) * 3;
            foreach (var sample in samples)
            {
                int i = origin + sample.FrameOffset;
                double value = (frame.Rgb[i] + frame.Rgb[i + 1] + frame.Rgb[i + 2]) / 3d;
                sum += value;
                square += value * value;
                cross += value * sample.Reference;
            }
            double n = samples.Count;
            double denominator = Math.Sqrt(Math.Max(0, (square - sum * sum / n) * (squareReference - sumReference * sumReference / n)));
            double score = denominator > 0 ? (cross - sum * sumReference / n) / denominator : 0;
            if (score > best) { best = score; bestX = x; bestY = y; bestMean = sum / n; }
        }
        Console.WriteLine($"Best brightness-normalized similarity: {best:P2}, top-left {bestX},{bestY}; reference mean {sumReference / samples.Count:F1}, match mean {bestMean:F1}");
    }
}
