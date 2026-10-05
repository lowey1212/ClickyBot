using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ClickyBot;

internal static class Aio2Regression
{
    internal static async Task Run()
    {
        static void Check(bool value, string message)
        {
            if (!value) throw new Exception(message);
        }
        var original = new MacroRule { Name = "Existing skill", Key = "F" };
        var profile = new MacroProfile { Game = "aio2", Rules = [original] };
        var folder = Path.Combine(AppContext.BaseDirectory, "aio2-test-references");
        var rule = Aio2ProfileSetup.Configure(profile, folder);
        Check(profile.Rules.Count == 2 && ReferenceEquals(profile.Rules[1], original), "Setup changed an existing skill.");
        Check(rule.Condition == ConditionType.RegionSnapshotMatches && rule.SearchReference
            && rule.SearchWidth == 1 && rule.SearchHeight == 1 && rule.GateEnabled && !rule.GateAreaSelected,
            "Setup must wait for the manually selected search area.");
        Check(rule.Repeat == RepeatMode.OnRisingEdge && rule.Action == ActionType.RecordedCombo,
            "Alt+1 must fire once per badge appearance.");
        Check(rule.GateCondition == ConditionType.RegionSnapshotDiffers && rule.GateReferenceRgb.Length == 72 * 17 * 3,
            "Auto Move must be an absence gate using only the fixed label, excluding distance.");
        var blocked = Conditions.Invert(ConditionObservation.Percent(100, rule.GateCoverageThreshold, false, "Auto Move"));
        var ready = Conditions.Invert(ConditionObservation.Percent(0, rule.GateCoverageThreshold, false, "Auto Move"));
        var unavailable = Conditions.Invert(ConditionObservation.Percent(-1, rule.GateCoverageThreshold, false, "Auto Move"));
        Check(blocked.Passed == false && ready.Passed == true && unavailable.Passed is null,
            "Visible Auto Move blocks; absence allows; failed reads cannot authorize Alt+1.");
        Check(ReferenceImageService.TryLoadRgb(rule.ReferenceImagePath, rule.WatchWidth, rule.WatchHeight, out var badge)
            && badge.SequenceEqual(rule.ReferenceRgb), "Saved reference differs from the bundled badge.");
        MatchLocation? Find(byte[] frame, int width, int height) => ImageMatcher.Find(frame, width, height,
            badge, rule.WatchWidth, rule.WatchHeight, rule.Tolerance, rule.CoverageThreshold, default);
        Check(Find(new byte[3], 1, 1) is null, "Unconfigured area accepted a prompt.");
        var bitmap = new BitmapImage(new Uri(Path.Combine(AppContext.BaseDirectory, "fixtures", "Aio2-quest-prompt.png")));
        var converted = new FormatConvertedBitmap(bitmap, PixelFormats.Rgb24, null, 0);
        var quest = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 3];
        converted.CopyPixels(quest, bitmap.PixelWidth * 3, 0);
        Check(Find(quest, bitmap.PixelWidth, bitmap.PixelHeight) == new MatchLocation(18, 47),
            "The supplied quest screenshot must match only the Alt+1 badge.");
        for (var y = 41; y < 53; y++)
            Array.Clear(quest, (y * bitmap.PixelWidth + 4) * 3, 29 * 3);
        Check(Find(quest, bitmap.PixelWidth, bitmap.PixelHeight) is null,
            "Quest text without the Alt+1 badge must not trigger.");
        foreach (var (x, y) in new[] { (0, 0), (103, 42), (320 - rule.WatchWidth, 100 - rule.WatchHeight) })
        {
            var frame = new byte[320 * 100 * 3];
            for (var row = 0; row < rule.WatchHeight; row++)
                Array.Copy(badge, row * rule.WatchWidth * 3, frame, ((y + row) * 320 + x) * 3, rule.WatchWidth * 3);
            Check(Find(frame, 320, 100) == new MatchLocation(x + rule.WatchWidth / 2, y + rule.WatchHeight / 2),
                "Badge not found at the selected area's edge or centre.");
        }
        Check(Find(new byte[320 * 100 * 3], 320, 100) is null, "Absent badge triggered.");
        rule.SearchX = 1700; rule.SearchY = 200; rule.SearchWidth = 220; rule.SearchHeight = 150;
        rule.GateAreaSelected = true; rule.GateX = 1200; rule.GateY = 80;
        Check(ReferenceEquals(rule, Aio2ProfileSetup.Configure(profile, folder)) && profile.Rules.Count == 2
            && rule.SearchX == 1700 && rule.SearchY == 200 && rule.SearchWidth == 220 && rule.SearchHeight == 150
            && rule.GateAreaSelected && rule.GateX == 1200 && rule.GateY == 80,
            "Repeated setup duplicated the rule or lost the manually selected area.");
        var options = new JsonSerializerOptions { Converters = { new JsonStringEnumConverter() } };
        var restored = JsonSerializer.Deserialize<MacroProfile>(JsonSerializer.Serialize(profile, options), options)!;
        Check(restored.Game == "aio2" && restored.Rules[0].SearchX == 1700
            && restored.Rules[0].RecordedSteps.Count == 3 && restored.Rules[0].GateAreaSelected
            && restored.Rules[0].GateCondition == ConditionType.RegionSnapshotDiffers, "Macro save/load lost AIO2 configuration.");
        InputSimulator.ReleaseAllHeldInputs();
        NativeMethods.KeyboardInputs.Clear();
        await InputSimulator.ExecuteAsync(rule, default);
        Check(NativeMethods.KeyboardInputs.Select(input => (input.ScanCode, input.Flags)).SequenceEqual(
            new (ushort, uint)[] { (0xA4, 8), (0x31, 8), (0x31, 10), (0xA4, 10) }),
            "Combo must hold LeftAlt around exactly one 1 tap and release it.");
        NativeMethods.KeyboardInputs.Clear();
        using var stop = new CancellationTokenSource();
        var pending = InputSimulator.ExecuteAsync(rule, stop.Token);
        stop.Cancel();
        try { await pending; throw new Exception("Cancelled combo continued."); }
        catch (OperationCanceledException) { }
        InputSimulator.ReleaseAllHeldInputs();
        Check(NativeMethods.KeyboardInputs.Select(input => (input.ScanCode, input.Flags)).SequenceEqual(
            new (ushort, uint)[] { (0xA4, 8), (0xA4, 10) }), "Stopping must release Alt without tapping 1.");
        Console.WriteLine("PASS: supplied AIO2 badge, text without badge, moving/absent prompts, manual area, setup preservation, save/load, Alt+1 and stop cleanup; no real input sent.");
    }
}
