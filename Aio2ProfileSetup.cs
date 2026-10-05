using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ClickyBot;

internal static class Aio2ProfileSetup
{
    internal const string RuleName = "Alt+1 — quest prompt under minimap";

    internal static MacroRule Configure(MacroProfile profile, string referenceFolder)
    {
        // Load the bundled badge, excluding changing quest text and artwork.
        using var stream = typeof(Aio2ProfileSetup).Assembly.GetManifestResourceStream("ClickyBot.Aio2.Alt1.png")
            ?? throw new IOException("The bundled Alt+1 reference is missing.");
        var bitmap = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
        var converted = new FormatConvertedBitmap(bitmap, PixelFormats.Rgb24, null, 0);
        var rgb = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 3];
        converted.CopyPixels(rgb, bitmap.PixelWidth * 3, 0);
        var path = ReferenceImageService.CreateNextPath(referenceFolder, "aio2-Alt-1-prompt", false);
        if (!ReferenceImageService.TrySavePng(path, bitmap.PixelWidth, bitmap.PixelHeight, rgb, out var error))
            throw new IOException(error);

        var rule = profile.Rules.FirstOrDefault(rule => rule.Name == RuleName);
        if (rule is null)
        {
            // No assumed screen coordinates: a 1x1 search cannot match the badge.
            // The user selects the area under their minimap in the rule editor.
            rule = new MacroRule { Name = RuleName, SearchWidth = 1, SearchHeight = 1 };
            profile.Rules.Insert(0, rule);
        }
        rule.Enabled = true;
        rule.Condition = ConditionType.RegionSnapshotMatches;
        rule.WatchWidth = bitmap.PixelWidth;
        rule.WatchHeight = bitmap.PixelHeight;
        rule.ReferenceImagePath = path;
        rule.ReferenceRgb = rgb;
        rule.SearchReference = true;
        rule.ImageMatchMethod = ImageMatchMethod.PixelColors;
        rule.Tolerance = 25;
        rule.CoverageThreshold = 90;
        if (!rule.GateEnabled || rule.GateCondition != ConditionType.RegionSnapshotDiffers)
        {
            using var moveStream = typeof(Aio2ProfileSetup).Assembly.GetManifestResourceStream("ClickyBot.Aio2.AutoMove.png")
                ?? throw new IOException("The bundled Auto Move reference is missing.");
            var moveBitmap = BitmapDecoder.Create(moveStream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
            var moveConverted = new FormatConvertedBitmap(moveBitmap, PixelFormats.Rgb24, null, 0);
            var moveRgb = new byte[moveBitmap.PixelWidth * moveBitmap.PixelHeight * 3];
            moveConverted.CopyPixels(moveRgb, moveBitmap.PixelWidth * 3, 0);
            var movePath = ReferenceImageService.CreateNextPath(referenceFolder, "aio2-Auto-Move", true);
            if (!ReferenceImageService.TrySavePng(movePath, moveBitmap.PixelWidth, moveBitmap.PixelHeight, moveRgb, out var moveError))
                throw new IOException(moveError);
            rule.GateEnabled = true;
            rule.GateAreaSelected = false;
            rule.GateCondition = ConditionType.RegionSnapshotDiffers;
            rule.GateWidth = moveBitmap.PixelWidth;
            rule.GateHeight = moveBitmap.PixelHeight;
            rule.GateReferenceImagePath = movePath;
            rule.GateReferenceRgb = moveRgb;
            rule.GateTolerance = 25;
            rule.GateCoverageThreshold = 90;
        }
        rule.Action = ActionType.RecordedCombo;
        rule.Repeat = RepeatMode.OnRisingEdge;
        rule.CooldownMs = 0;
        rule.DelayAfterActionMs = 20;
        rule.RecordedSteps =
        [
            new() { Type = RecordedStepType.KeyDown, Key = "LeftAlt" },
            new() { Type = RecordedStepType.KeyPress, Key = "1", DelayBeforeMs = 50 },
            new() { Type = RecordedStepType.KeyUp, Key = "LeftAlt" }
        ];
        return rule;
    }
}
