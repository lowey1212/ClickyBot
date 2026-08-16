using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ClickyBot;

internal static class ReferenceImageService
{
    public static string CreateNextPath(string folder, string ruleName, bool gate)
    {
        Directory.CreateDirectory(folder);
        var nextNumber = 1;
        foreach (var file in Directory.EnumerateFiles(folder, "*.png"))
        {
            var name = Path.GetFileNameWithoutExtension(file);
            var separator = name.IndexOf('_');
            if (separator > 0 && int.TryParse(name[..separator], out var number))
            {
                nextNumber = Math.Max(nextNumber, number + 1);
            }
        }

        var safeName = SanitizeFileName(ruleName);
        if (gate)
        {
            safeName += "-gate";
        }

        return Path.Combine(folder, $"{nextNumber:000}_{safeName}.png");
    }

    public static bool TrySavePng(string path, int width, int height, byte[] rgb, out string error)
    {
        error = "";
        if (rgb.Length != width * height * 3)
        {
            error = "The captured image data did not match its selected dimensions.";
            return false;
        }

        try
        {
            var bgr = new byte[rgb.Length];
            for (var index = 0; index < rgb.Length; index += 3)
            {
                bgr[index] = rgb[index + 2];
                bgr[index + 1] = rgb[index + 1];
                bgr[index + 2] = rgb[index];
            }

            var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgr24, null, bgr, width * 3);
            bitmap.Freeze();
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = File.Create(path);
            encoder.Save(stream);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            error = ex.Message;
            return false;
        }
    }

    public static bool TryLoadRgb(string path, int expectedWidth, int expectedHeight, out byte[] rgb)
    {
        rgb = [];
        if (!File.Exists(path))
        {
            return false;
        }

        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.UriSource = new Uri(path, UriKind.Absolute);
            bitmap.EndInit();
            bitmap.Freeze();

            if (bitmap.PixelWidth != expectedWidth || bitmap.PixelHeight != expectedHeight)
            {
                return false;
            }

            var converted = new FormatConvertedBitmap(bitmap, PixelFormats.Bgr24, null, 0);
            converted.Freeze();
            var bgr = new byte[expectedWidth * expectedHeight * 3];
            converted.CopyPixels(bgr, expectedWidth * 3, 0);
            rgb = new byte[bgr.Length];
            for (var index = 0; index < bgr.Length; index += 3)
            {
                rgb[index] = bgr[index + 2];
                rgb[index + 1] = bgr[index + 1];
                rgb[index + 2] = bgr[index];
            }
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public static bool TryLoadFromRule(MacroRule rule, string folder, bool gate, out byte[] rgb, out string resolvedPath)
    {
        rgb = [];
        resolvedPath = gate ? rule.GateReferenceImagePath : rule.ReferenceImagePath;
        if (string.IsNullOrWhiteSpace(resolvedPath))
        {
            return false;
        }

        var candidates = new[]
        {
            resolvedPath,
            Path.Combine(folder, Path.GetFileName(resolvedPath))
        }.Distinct(StringComparer.OrdinalIgnoreCase);

        foreach (var candidate in candidates)
        {
            if (TryLoadRgb(candidate, gate ? rule.GateWidth : rule.WatchWidth, gate ? rule.GateHeight : rule.WatchHeight, out rgb))
            {
                resolvedPath = candidate;
                return true;
            }
        }

        return false;
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = name.Trim().Select(character =>
            character switch
            {
                ' ' => '-',
                _ when invalid.Contains(character) => '_',
                _ => character
            }).ToArray();
        var sanitized = new string(chars).Trim('.', '-');
        return string.IsNullOrWhiteSpace(sanitized) ? "reference" : sanitized;
    }
}
