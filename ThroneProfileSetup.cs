using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ClickyBot;

internal static class ThroneProfileSetup
{
    internal static void Configure(MacroProfile profile, string referenceFolder, ScreenSelection skillArea, ScreenSelection promptArea)
    {
        foreach (var key in new[] { "Q", "V", "1" })
        {
            var existing = profile.Rules.Where(rule => rule.Action == ActionType.KeyPress
                && rule.Key.Equals(key, StringComparison.OrdinalIgnoreCase)).ToList();
            var rule = existing.FirstOrDefault();
            if (rule is null)
            {
                rule = new MacroRule { Key = key };
                profile.Rules.Add(rule);
            }
            foreach (var duplicate in existing.Skip(1)) duplicate.Enabled = false;
            rule.Key = key;
            rule.Name = key switch
            {
                "Q" => "Q — purple defence circle (any size)",
                "V" => "V — chain prompt",
                _ => "1 — continuous attack"
            };
            rule.Repeat = key == "1" ? RepeatMode.WhileTrue : RepeatMode.OnRisingEdge;
            rule.CooldownMs = key == "1" ? 100 : 200;
            rule.DelayAfterActionMs = 20;
            rule.Enabled = true;
            rule.GateEnabled = false;
            if (key == "Q")
            {
                rule.Condition = ConditionType.PurpleRingMatches;
                rule.WatchX = promptArea.X; rule.WatchY = promptArea.Y;
                rule.WatchWidth = promptArea.Width; rule.WatchHeight = promptArea.Height;
            }
            else if (key == "V")
            {
                var hadReference = rule.Condition == ConditionType.RegionSnapshotMatches && rule.ReferenceRgb.Length > 0;
                rule.Condition = ConditionType.RegionSnapshotMatches;
                rule.SearchReference = true;
                rule.ImageMatchMethod = ImageMatchMethod.PixelColors;
                rule.Tolerance = 20;
                rule.CoverageThreshold = 85;
                rule.SearchX = skillArea.X; rule.SearchY = skillArea.Y;
                rule.SearchWidth = skillArea.Width; rule.SearchHeight = skillArea.Height;
                if (!hadReference) LoadSample(rule, referenceFolder);
            }
            else rule.Condition = ConditionType.Always;
        }
        profile.ThroneCombatMode = true;
        profile.PollIntervalMs = 80;
    }

    private static void LoadSample(MacroRule rule, string folder)
    {
        var asset = "V-chain.png";
        using var resource = Application.GetResourceStream(new Uri($"/ClickyBot;component/Assets/Throne/{asset}", UriKind.Relative))!.Stream;
        var original = BitmapDecoder.Create(resource, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
        // Match the V key badge rather than this particular skill's artwork,
        // so a different chained skill can use the same prompt.
        var bitmap = new CroppedBitmap(original, new Int32Rect(36, 64, 18, 18));
        var converted = new FormatConvertedBitmap(bitmap, PixelFormats.Rgb24, null, 0);
        var rgb = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 3];
        converted.CopyPixels(rgb, bitmap.PixelWidth * 3, 0);
        var path = ReferenceImageService.CreateNextPath(folder, rule.Name, false);
        if (!ReferenceImageService.TrySavePng(path, bitmap.PixelWidth, bitmap.PixelHeight, rgb, out var error))
            throw new IOException(error);
        rule.WatchWidth = bitmap.PixelWidth; rule.WatchHeight = bitmap.PixelHeight;
        rule.ReferenceImagePath = path;
        rule.ReferenceRgb = rgb;
    }
}
