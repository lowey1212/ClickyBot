using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ClickyBot;

internal static class Aio2ProfileSetup
{
    internal const string RuleName = "Alt+1 — quest prompt under minimap";
    internal const string InteractionRuleName = "F — tap while interaction prompt is visible";
    internal const string SkipRuleName = "Esc — skip when SKIP prompt is visible";
    internal const string GatherRuleName = "F — Gather with random 0–1 second reaction";

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
        ConfigureAutoMoveGate(rule, referenceFolder);
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

    internal static MacroRule ConfigureInteraction(MacroProfile profile, string referenceFolder)
        => ConfigureKeyPrompt(profile, referenceFolder, InteractionRuleName, "ClickyBot.Aio2.F.png", "aio2-F-prompt",
            "F", RepeatMode.WhileTrue, 500, ImageMatchMethod.PixelColors);

    internal static MacroRule ConfigureSkip(MacroProfile profile, string referenceFolder)
        => ConfigureKeyPrompt(profile, referenceFolder, SkipRuleName, "ClickyBot.Aio2.Skip.png", "aio2-Skip-prompt",
            "Escape", RepeatMode.OnRisingEdge, 0, ImageMatchMethod.ImageSimilarity);

    internal static MacroRule ConfigureGather(MacroProfile profile, string referenceFolder)
    {
        var rule = ConfigureKeyPrompt(profile, referenceFolder, GatherRuleName, "ClickyBot.Aio2.Gather.png", "aio2-F-Gather",
            "F", RepeatMode.WhileTrue, 500, ImageMatchMethod.ImageSimilarity, useAutoMoveGate: false);
        rule.RandomizeReactionDelay = true;
        rule.ReactionDelayMinMs = 0;
        rule.ReactionDelayMaxMs = 1000;
        rule.KeyboardInputMode = KeyboardInputMode.FakerInput;
        rule.GatherInputRevision = 1;
        rule.DelayAfterActionMs = 0;
        return rule;
    }

    internal static bool UpgradeGatherInput(MacroProfile profile)
    {
        var changed = false;
        foreach (var rule in profile.Rules.Where(rule => rule.Name == GatherRuleName && string.Equals(rule.Key, "F", StringComparison.OrdinalIgnoreCase) && rule.GatherInputRevision < 1))
        {
            rule.KeyboardInputMode = KeyboardInputMode.FakerInput;
            rule.GatherInputRevision = 1;
            changed = true;
        }
        return changed;
    }

    private static MacroRule ConfigureKeyPrompt(MacroProfile profile, string referenceFolder, string name,
        string resource, string referenceName, string key, RepeatMode repeat, int cooldown, ImageMatchMethod method, bool useAutoMoveGate = true)
    {
        using var stream = typeof(Aio2ProfileSetup).Assembly.GetManifestResourceStream(resource)
            ?? throw new IOException($"The bundled {key} reference is missing.");
        var bitmap = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
        var converted = new FormatConvertedBitmap(bitmap, PixelFormats.Rgb24, null, 0);
        var rgb = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 3];
        converted.CopyPixels(rgb, bitmap.PixelWidth * 3, 0);
        var path = ReferenceImageService.CreateNextPath(referenceFolder, referenceName, false);
        if (!ReferenceImageService.TrySavePng(path, bitmap.PixelWidth, bitmap.PixelHeight, rgb, out var error))
            throw new IOException(error);

        var rule = profile.Rules.FirstOrDefault(rule => rule.Name == name);
        if (rule is null)
        {
            rule = new MacroRule { Name = name, SearchWidth = 1, SearchHeight = 1 };
            var questRule = useAutoMoveGate ? profile.Rules.Where(rule => (rule.Name == RuleName || rule.Name == InteractionRuleName)
                && rule.GateEnabled && rule.GateCondition == ConditionType.RegionSnapshotDiffers)
                .OrderByDescending(rule => rule.GateAreaSelected && rule.GateReferenceRgb.Length > 0).FirstOrDefault() : null;
            if (questRule is not null)
            {
                // Reuse the user's existing Auto Move calibration for new prompts.
                rule.GateEnabled = true;
                rule.GateAreaSelected = questRule.GateAreaSelected;
                rule.GateCondition = questRule.GateCondition;
                rule.GateX = questRule.GateX; rule.GateY = questRule.GateY;
                rule.GateWidth = questRule.GateWidth; rule.GateHeight = questRule.GateHeight;
                rule.GateTolerance = questRule.GateTolerance;
                rule.GateCoverageThreshold = questRule.GateCoverageThreshold;
                rule.GateReferenceImagePath = questRule.GateReferenceImagePath;
                rule.GateReferenceRgb = questRule.GateReferenceRgb.ToArray();
            }
            profile.Rules.Insert(0, rule);
        }
        if (useAutoMoveGate) ConfigureAutoMoveGate(rule, referenceFolder);
        rule.Enabled = true;
        rule.Condition = ConditionType.RegionSnapshotMatches;
        rule.WatchWidth = bitmap.PixelWidth; rule.WatchHeight = bitmap.PixelHeight;
        rule.ReferenceImagePath = path; rule.ReferenceRgb = rgb;
        rule.SearchReference = true;
        rule.ImageMatchMethod = method;
        rule.Tolerance = 25; rule.CoverageThreshold = 90;
        rule.Action = ActionType.KeyPress;
        rule.Key = key;
        rule.Repeat = repeat;
        rule.CooldownMs = cooldown;
        rule.DelayAfterActionMs = 20;
        rule.RecordedSteps = [];
        return rule;
    }

    private static void ConfigureAutoMoveGate(MacroRule rule, string referenceFolder)
    {
        if (rule.GateEnabled && rule.GateCondition == ConditionType.RegionSnapshotDiffers) return;
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
}
