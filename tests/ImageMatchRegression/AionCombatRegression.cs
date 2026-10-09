using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ClickyBot;

internal static class AionCombatRegression
{
    internal static async Task Run()
    {
        static void Check(bool passed, string message) { if (!passed) throw new Exception(message); }
        var profile = new MacroProfile { Game = "Aion 2", PollIntervalMs = 100 };
        var unrelated = new MacroRule { Name = "Existing Gather", Key = "F" };
        profile.Rules.Add(unrelated);
        var target = Aio2ProfileSetup.ConfigureCombat(profile, Path.Combine(AppContext.BaseDirectory, "aion-combat-reference"));
        try { AionCombatRunner.Validate(profile); throw new Exception("Uncalibrated combat accepted."); }
        catch (InvalidOperationException) { }
        target.SearchX = 90; target.SearchY = 20; target.SearchWidth = 600; target.SearchHeight = 110;
        profile.AionCombat.TurnPixels = -50;
        Aio2ProfileSetup.ConfigureCombat(profile, Path.GetDirectoryName(target.ReferenceImagePath)!);
        Check(profile.Rules.Count == 2 && ReferenceEquals(profile.Rules[1], unrelated) && target.SearchX == 90
            && profile.AionCombat.TurnPixels == -50, "Combat setup must preserve existing rules, calibration and camera settings.");
        var options = new JsonSerializerOptions { Converters = { new JsonStringEnumConverter() } };
        var reloaded = JsonSerializer.Deserialize<MacroProfile>(JsonSerializer.Serialize(profile, options), options)!;
        Check(reloaded.AionCombat.TargetRuleId == target.Id && reloaded.AionCombat.TurnPixels == -50
            && reloaded.Rules[0].Condition == ConditionType.AionTargetBarMatches, "Combat settings must survive save/load.");

        static (byte[] Rgb, int Width, int Height) Load(string name)
        {
            using var stream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "fixtures", name));
            var bitmap = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
            var converted = new FormatConvertedBitmap(bitmap, PixelFormats.Rgb24, null, 0);
            var rgb = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 3];
            converted.CopyPixels(rgb, bitmap.PixelWidth * 3, 0);
            return (rgb, bitmap.PixelWidth, bitmap.PixelHeight);
        }
        var shot = Load("Aio2-target-hp.png");
        var world = Load("Aio2-world-hp.png");
        (MatchLocation? Location, double Score) Find(byte[] rgb, int width, int height)
            => AionTargetBarMatcher.Find(rgb, width, height, target.ReferenceRgb, target.WatchWidth, target.WatchHeight, 90, default);
        var result = Find(shot.Rgb, shot.Width, shot.Height);
        Check(result.Location == new MatchLocation(294, 61), $"Supplied target HP markers should match at 294,61; got {result}.");
        // The health fill and monster-name area can completely change.
        for (var y = 0; y < shot.Height; y++)
        for (var x = 140; x < 450; x++)
        {
            var i = (y * shot.Width + x) * 3;
            shot.Rgb[i] = 6; shot.Rgb[i + 1] = 28; shot.Rgb[i + 2] = 9;
        }
        Check(Find(shot.Rgb, shot.Width, shot.Height).Location is not null, "Health depletion and name changes must retain the target.");
        for (var y = 49; y < 74; y++) Array.Clear(shot.Rgb, (y * shot.Width + 112) * 3, 23 * 3);
        Check(Find(shot.Rgb, shot.Width, shot.Height).Location is null, "Both target end markers are required.");
        Check(Find(world.Rgb, world.Width, world.Height).Location is null, "Overhead monster/player bars must not match the top-centre target HUD.");
        Check(Find(Enumerable.Repeat((byte)180, 700 * 110 * 3).ToArray(), 700, 110).Location is null
            && Find(new byte[700 * 110 * 3], 700, 110).Location is null, "Flat bright and dark regions must not become targets.");
        using (var cancelled = new CancellationTokenSource())
        {
            cancelled.Cancel();
            try { AionTargetBarMatcher.Find(shot.Rgb, shot.Width, shot.Height, target.ReferenceRgb, target.WatchWidth, target.WatchHeight, 90, cancelled.Token); throw new Exception("Cancelled marker search continued."); }
            catch (OperationCanceledException) { }
        }
        Console.WriteLine("PASS: supplied target HUD, changing names/health, both markers required, overhead bars/flat areas rejected, calibration/persistence and cancelled detection.");

        foreach (var file in new[] { "Aio2-captured-marker.png", "Aio2-captured-114.png", "Aio2-captured-118.png", "Aio2-captured-119.png" })
        {
            var capture = Load(file);
            var capturedRule = new MacroRule { Condition = ConditionType.AionTargetBarMatches, SearchReference = true,
                WatchWidth = capture.Width, WatchHeight = capture.Height, ReferenceRgb = capture.Rgb,
                SearchX = 0, SearchY = 0, SearchWidth = 1911, SearchHeight = 1058 };
            var capturedProfile = new MacroProfile { Rules = [capturedRule], AionCombat = new() { Enabled = true, TargetRuleId = capturedRule.Id } };
            Check(ReferenceEquals(AionCombatRunner.Validate(capturedProfile), capturedRule)
                && capturedRule.SearchWidth == 1911 && capturedRule.SearchHeight == 1058,
                $"User reference {file} must start combat without resetting the saved watch area.");
            var frame = new byte[1911 * 1058 * 3];
            for (var row = 0; row < capture.Height; row++)
                Array.Copy(capture.Rgb, row * capture.Width * 3, frame, ((436 + row) * 1911 + 1021) * 3, capture.Width * 3);
            var found = AionTargetBarMatcher.Find(frame, 1911, 1058, capture.Rgb, capture.Width, capture.Height, 90, default);
            Check(found.Location is { } marker && Math.Abs(marker.X - (1021 + capture.Width / 2)) <= 2
                && Math.Abs(marker.Y - (436 + capture.Height / 2)) <= 2, $"Captured reference {file} must match in the saved full-screen watch area; got {found}.");
            Check(AionTargetBarMatcher.Find(new byte[1911 * 1058 * 3], 1911, 1058, capture.Rgb, capture.Width, capture.Height, 90, default).Location is null,
                $"Absent user reference {file} must release the target condition.");
            capturedRule.SearchWidth = capture.Width - 1;
            try { AionCombatRunner.Validate(capturedProfile); throw new Exception("Undersized watch area accepted."); }
            catch (InvalidOperationException ex) { Check(ex.Message.Contains("smaller than reference"), "Watch area failure must explain its actual dimensions."); }
            capturedRule.SearchWidth = 1911; capturedRule.ReferenceRgb = [];
            try { AionCombatRunner.Validate(capturedProfile); throw new Exception("Missing reference accepted."); }
            catch (InvalidOperationException ex) { Check(ex.Message.Contains("not loaded"), "Missing reference failure must not incorrectly ask to reselect the watch area."); }
        }
        Console.WriteLine("PASS: all four real user arrow/whole-bar captures, broad saved watch area, target presence/absence and specific startup errors; no real input sent.");

        var previous = FakerInputKeyboard.Shared;
        var reports = new List<byte[]>();
        NativeMethods.KeyboardInputs.Clear(); NativeMethods.MouseInputs.Clear();
        try
        {
            using var stop = new CancellationTokenSource();
            var mouseDowns = 0;
            FakerInputKeyboard.Shared = new(() => new CombatTransport(report =>
            {
                reports.Add(report.ToArray());
                if (report[2] == 3 && report[3] == 1 && ++mouseDowns == 2) stop.Cancel();
            }));
            long clock = 0; var calls = 0;
            var observations = new bool[] { false, true, true, true, true, false, false, false, false, true, true, true };
            var runner = new AionCombatRunner(profile, (rule, _) => new(rule.Id, DateTime.UtcNow,
                new(observations[Math.Min(calls++, observations.Length - 1)], "Simulated target"), null, ""),
                () => true, _ => { }, (ms, token) => { token.ThrowIfCancellationRequested(); clock += ms; return Task.CompletedTask; }, () => clock);
            try { await runner.RunAsync(stop.Token); } catch (OperationCanceledException) { }
            var firstOne = reports.FindIndex(report => report[2] == 1 && report[5] == 0x1E);
            var firstLeft = reports.FindIndex(report => report[2] == 3 && report[3] == 1);
            Check(reports.Any(report => report[2] == 1 && report[5] == 0x2B) && firstOne >= 0 && firstLeft > firstOne,
                "Search must Tab; confirmed target must press 1 before holding left mouse.");
            Check(reports.Count(report => report[2] == 1 && report[5] == 0x1E) == 2 && mouseDowns == 2
                && reports.Last()[2] == 3 && reports.Last()[3] == 0,
                "Target disappearance must release attack and rearm exactly one 1 tap for a new appearance; cancellation releases left mouse.");
            var moves = reports.Where(report => report[2] == 3 && report[4] != 0).ToArray();
            Check(moves.Length == profile.AionCombat.TurnSteps && moves.All(report => report[3] == 2
                && System.Buffers.Binary.BinaryPrimitives.ReadInt16LittleEndian(report.AsSpan(4, 2)) == -50),
                "Camera movement must preserve held right mouse and encode signed relative movement.");
            Check(NativeMethods.KeyboardInputs.Count == 0 && NativeMethods.MouseInputs.Count == 0,
                "Combat must use only driver reports, with no software keyboard or mouse input fallback.");

            async Task Scenario(Func<int, bool?> reading, Func<int, bool> focused, Action<List<byte[]>> check, int? maxAttack = null)
            {
                reports.Clear(); var count = 0; long elapsed = 0;
                FakerInputKeyboard.Shared = new(() => new CombatTransport(report => reports.Add(report.ToArray())));
                if (maxAttack.HasValue) profile.AionCombat.MaxAttackMs = maxAttack.Value;
                profile.AionCombat.MaxSearchAttempts = 2;
                var task = new AionCombatRunner(profile, (rule, _) => new(rule.Id, DateTime.UtcNow,
                    new(reading(++count), "Simulated target"), null, ""), () => focused(count), _ => { },
                    (ms, token) => { token.ThrowIfCancellationRequested(); elapsed += ms; return Task.CompletedTask; }, () => elapsed);
                try { await task.RunAsync(default); } catch (InvalidOperationException) { }
                check(reports);
            }
            await Scenario(i => i <= 3 ? true : null, _ => true,
                list => Check(list.Any(report => report[2] == 3 && report[3] == 1) && list.Last()[3] == 0,
                    "An unavailable capture during attack must release left mouse and stop."));
            await Scenario(_ => true, i => i < 4,
                list => Check(list.Any(report => report[2] == 3 && report[3] == 1) && list.Last()[3] == 0,
                    "Game focus loss during attack must release left mouse."));
            await Scenario(_ => false, _ => true,
                list => Check(list.Count(report => report[2] == 1 && report[5] == 0x2B) == 2
                    && !list.Any(report => report[2] == 3 && report[3] == 1), "Search limits must stop without attacking an unconfirmed target."));
            await Scenario(_ => true, _ => true,
                list => Check(list.Count(report => report[2] == 1 && report[5] == 0x1E) == 1 && list.Last()[3] == 0,
                    "Attack time limit must release left mouse without spamming 1."), 1000);
            Console.WriteLine("PASS: production combat search/Tab, confirmed 1 then held attack, death/reacquisition, signed camera driver reports, cancellation, focus/capture loss and bounded search/attack; no real input sent.");
        }
        finally { FakerInputKeyboard.Shared.ReleaseAllHeldInputs(); FakerInputKeyboard.Shared = previous; }
    }

    private sealed class CombatTransport(Action<byte[]> write) : IFakerInputTransport
    {
        public void Write(byte[] report) => write(report);
        public void Dispose() { }
    }
}
