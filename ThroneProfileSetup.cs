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
                "Q" => "Q — delayed purple-circle defence",
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

    internal static void ConfigureHealing(MacroProfile profile, string folder)
    {
        profile.ThroneHealing ??= new ThroneHealingSettings();
        var settings = profile.ThroneHealing;
        var hp = SaveAsset("HP-bar.png", folder, "Throne-HP-bar");
        settings.HealthReferenceImagePath = hp.Path;
        settings.HealthReferenceWidth = hp.Width; settings.HealthReferenceHeight = hp.Height;
        settings.HealthReferenceRgb = hp.Rgb;
        foreach (var key in new[] { "7", "8" })
        {
            var existing = profile.Rules.Where(rule => rule.Action == ActionType.KeyPress && rule.Key == key).ToList();
            var rule = existing.FirstOrDefault();
            if (rule is null) { rule = new MacroRule { Key = key }; profile.Rules.Add(rule); }
            foreach (var duplicate in existing.Skip(1)) duplicate.Enabled = false;
            var ready = SaveAsset($"{key}-ready.png", folder, $"Throne-{key}-ready");
            rule.Name = $"{key} — low HP, only when ready";
            rule.ThroneHealingOnly = true;
            rule.Enabled = true; rule.GateEnabled = false;
            rule.Condition = ConditionType.RegionSnapshotMatches; rule.SearchReference = true;
            rule.WatchWidth = ready.Width; rule.WatchHeight = ready.Height;
            rule.ReferenceImagePath = ready.Path; rule.ReferenceRgb = ready.Rgb;
            rule.SearchX = key == "7" ? 984 : 1042; rule.SearchY = 990;
            rule.SearchWidth = 58; rule.SearchHeight = 58;
            rule.ImageMatchMethod = ImageMatchMethod.PixelColors;
            rule.Tolerance = 20; rule.CoverageThreshold = 90;
            rule.Repeat = RepeatMode.WhileTrue;
            // A visual cooldown is authoritative. This debounce gives the game
            // time to show it, while allowing a retry if the tap was rejected.
            rule.CooldownMs = 1000; rule.DelayAfterActionMs = 20;
        }
        settings.Enabled = true;
    }

    private static (string Path, int Width, int Height, byte[] Rgb) SaveAsset(string asset, string folder, string name)
    {
        using var resource = Application.GetResourceStream(new Uri($"/ClickyBot;component/Assets/Throne/{asset}", UriKind.Relative))!.Stream;
        var bitmap = BitmapDecoder.Create(resource, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
        var converted = new FormatConvertedBitmap(bitmap, PixelFormats.Rgb24, null, 0);
        var rgb = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 3];
        converted.CopyPixels(rgb, bitmap.PixelWidth * 3, 0);
        var path = ReferenceImageService.CreateNextPath(folder, name, false);
        if (!ReferenceImageService.TrySavePng(path, bitmap.PixelWidth, bitmap.PixelHeight, rgb, out var error)) throw new IOException(error);
        return (path, bitmap.PixelWidth, bitmap.PixelHeight, rgb);
    }
}
