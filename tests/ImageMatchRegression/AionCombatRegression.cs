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
        Check(!reloaded.AionCombat.CameraTurnEnabled,
            "Existing camera configuration must remain available without enabling turning.");
        var legacyJson = JsonSerializer.Serialize(profile, options).Replace("\"CameraTurnEnabled\":false", "\"CameraTurnEnabled\":true,\"HoldRightMouseToTurn\":true");
        var legacy = JsonSerializer.Deserialize<MacroProfile>(legacyJson, options)!;
        Check(legacy.AionCombat.CameraTurnEnabled && !JsonSerializer.Serialize(legacy, options).Contains("HoldRightMouseToTurn"),
            "Old right-mouse settings must be ignored and removed on save while retaining optional camera movement.");

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

        foreach (var file in new[] { "Aio2-captured-marker.png", "Aio2-captured-114.png", "Aio2-captured-118.png", "Aio2-captured-119.png", "Aio2-captured-122.png", "Aio2-captured-cyan-arrow.png" })
        {
            var capture = Load(file);
            foreach (var (gain, offset) in new[] { (0.35, 0), (0.6, 35), (0.8, 45) })
            {
                var changed = capture.Rgb.Select(value => (byte)Math.Clamp(value * gain + offset, 0, 255)).ToArray();
                Check(AionTargetBarMatcher.Find(changed, capture.Width, capture.Height, capture.Rgb,
                    capture.Width, capture.Height, 90, default).Location is not null,
                    $"{file} must retain its outline at brightness gain {gain} and offset {offset}.");
            }
            foreach (var tint in new[] { new[] { 0.9, 0.55, 0.2 }, new[] { 0.2, 0.65, 0.9 }, new[] { 0.8, 0.25, 0.7 } })
            {
                var changed = capture.Rgb.ToArray();
                for (var i = 0; i < changed.Length; i += 3)
                {
                    var value = (capture.Rgb[i] + capture.Rgb[i + 1] + capture.Rgb[i + 2]) / 3d;
                    for (var channel = 0; channel < 3; channel++) changed[i + channel] = (byte)(value * tint[channel]);
                }
                Check(AionTargetBarMatcher.Find(changed, capture.Width, capture.Height, capture.Rgb,
                    capture.Width, capture.Height, 90, default).Location is not null,
                    $"{file} must match yellow, blue and magenta outlines without a white/cyan live color requirement.");
            }
            foreach (var (foreground, background) in new[]
            {
                (new byte[] { 185, 155, 105 }, new byte[] { 100, 85, 65 }),
                (new byte[] { 95, 65, 45 }, new byte[] { 20, 15, 10 })
            })
            {
                var changed = capture.Rgb.ToArray();
                for (var i = 0; i < changed.Length; i += 3)
                {
                    var r = capture.Rgb[i]; var g = capture.Rgb[i + 1]; var b = capture.Rgb[i + 2];
                    var core = (Math.Min(r, Math.Min(g, b)) >= 150 && Math.Max(r, Math.Max(g, b)) - Math.Min(r, Math.Min(g, b)) <= 60)
                        || (r >= 70 && g >= 150 && b >= 170 && b >= r + 20 && g >= r + 10 && Math.Abs(b - g) <= 80);
                    Array.Copy(core ? foreground : background, 0, changed, i, 3);
                }
                Check(AionTargetBarMatcher.Find(changed, capture.Width, capture.Height, capture.Rgb,
                    capture.Width, capture.Height, 90, default).Location is not null,
                    $"{file} must retain its silhouette with a different background and dim orange arrow.");
            }
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
        Console.WriteLine("PASS: all six real user arrow/whole-bar captures, dim/bright/tinted outlines, broad saved watch area, target presence/absence and specific startup errors; no real input sent.");
        var arrow = Load("Aio2-captured-cyan-arrow.png");
        var colored = arrow.Rgb.ToArray();
        for (var i = 0; i < colored.Length; i += 3)
        {
            // Recolor the white arrow core cyan, retaining the photographed
            // background. Cyan halo variations must still count as a hit.
            if (Math.Min(colored[i], Math.Min(colored[i + 1], colored[i + 2])) < 150) continue;
            colored[i] = 110; colored[i + 1] = 195; colored[i + 2] = 225;
        }
        Check(AionTargetBarMatcher.Find(colored, arrow.Width, arrow.Height, arrow.Rgb, arrow.Width, arrow.Height, 75, default).Location is not null,
            "White-to-cyan arrow color changes must match without comparing the photographed background.");
        foreach (var color in new byte[][] { [180, 180, 180], [110, 195, 225], [180, 120, 35] })
        {
            var flat = new byte[arrow.Rgb.Length];
            for (var i = 0; i < flat.Length; i += 3) Array.Copy(color, 0, flat, i, 3);
            Check(AionTargetBarMatcher.Find(flat, arrow.Width, arrow.Height, arrow.Rgb, arrow.Width, arrow.Height, 75, default).Location is null,
                "Flat white/cyan areas and orange grass must fail even at the user's relaxed 75% threshold.");
        }

        foreach (var shape in new[] { "rectangle", "circle", "stripe", "noise", "mirrored" })
        {
            var impostor = new byte[arrow.Rgb.Length];
            var random = new Random(1212);
            for (var y = 0; y < arrow.Height; y++)
            for (var x = 0; x < arrow.Width; x++)
            {
                var i = (y * arrow.Width + x) * 3;
                var lit = shape switch
                {
                    "rectangle" => x >= 10 && x <= 25 && y >= 10 && y <= 42,
                    "circle" => (x - 18) * (x - 18) + (y - 26) * (y - 26) <= 14 * 14,
                    "stripe" => x >= 15 && x <= 22,
                    "noise" => random.Next(2) == 0,
                    _ => false
                };
                if (shape == "mirrored") Array.Copy(arrow.Rgb, (y * arrow.Width + arrow.Width - x - 1) * 3, impostor, i, 3);
                else for (var channel = 0; channel < 3; channel++) impostor[i + channel] = lit ? (byte)230 : (byte)30;
            }
            Check(AionTargetBarMatcher.Find(impostor, arrow.Width, arrow.Height, arrow.Rgb,
                arrow.Width, arrow.Height, 75, default).Location is null, $"Bright {shape} must not impersonate the captured arrow outline.");
        }
        Check(AionTargetBarMatcher.Find(arrow.Rgb[..^3], arrow.Width, arrow.Height, arrow.Rgb,
            arrow.Width, arrow.Height, 75, default).Location is null, "Malformed frames must fail without reading beyond the buffer.");
        Check(AionTargetBarMatcher.MatchThreshold(1) == 70 && AionTargetBarMatcher.MatchThreshold(75) == 75
            && AionTargetBarMatcher.MatchThreshold(120) == 100, "Shape thresholds must retain their documented minimum.");
        var scanClock = System.Diagnostics.Stopwatch.StartNew();
        Check(AionTargetBarMatcher.Find(new byte[1911 * 1058 * 3], 1911, 1058, arrow.Rgb,
            arrow.Width, arrow.Height, 75, default).Location is null, "Full-screen absence must remain a negative result.");
        scanClock.Stop();
        Check(scanClock.ElapsedMilliseconds < 2000, "A full-screen absent target scan must remain responsive.");
        Console.WriteLine($"PASS: wrong silhouettes, texture, flat colors and malformed frames rejected; full-screen absent outline scan {scanClock.ElapsedMilliseconds} ms.");

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
            Check(moves.Length == 0 && !reports.Any(report => report[2] == 3 && report[3] == 2)
                && reports[0][2] == 3 && reports[0][3] == 0,
                "Default/legacy combat must clear stale buttons and use Tab, 1 and left mouse without right-mouse turning.");
            Check(NativeMethods.KeyboardInputs.Count == 0 && NativeMethods.MouseInputs.Count == 0,
                "Combat must use only driver reports, with no software keyboard or mouse input fallback.");

            async Task Scenario(Func<int, bool?> reading, Func<int, bool> focused, Action<List<byte[]>> check, int? maxAttack = null,
                Action<byte[], int>? onReport = null, Action<string>? onStatus = null)
            {
                reports.Clear(); var count = 0; long elapsed = 0;
                using var end = new CancellationTokenSource();
                var callbackFailures = new List<Exception>();
                FakerInputKeyboard.Shared = new(() => new CombatTransport(report =>
                {
                    reports.Add(report.ToArray());
                    try { onReport?.Invoke(report, count); } catch (Exception ex) { callbackFailures.Add(ex); }
                }));
                if (maxAttack.HasValue) profile.AionCombat.MaxAttackMs = maxAttack.Value;
                profile.AionCombat.MaxSearchAttempts = 2;
                var task = new AionCombatRunner(profile, (rule, _) => new(rule.Id, DateTime.UtcNow,
                    new(reading(++count), "Simulated target"), null, ""), () =>
                    { if (count >= 20 || !focused(count)) end.Cancel(); return focused(count); }, _ => { },
                    (ms, token) => { token.ThrowIfCancellationRequested(); elapsed += ms; return Task.CompletedTask; }, () => elapsed,
                    (_, status) => onStatus?.Invoke(status));
                try { await task.RunAsync(end.Token); } catch (OperationCanceledException) when (end.IsCancellationRequested) { }
                Check(end.IsCancellationRequested, "Runtime failures or limits must not end combat automatically.");
                Check(callbackFailures.Count == 0, string.Join(" ", callbackFailures.Select(ex => ex.Message)));
                check(reports);
            }
            await Scenario(i => i <= 3 ? true : null, _ => true,
                list => Check(list.Any(report => report[2] == 3 && report[3] == 1) && list.Last()[3] == 0,
                    "An unavailable capture during attack must release left mouse while recovery keeps running."));
            var actionStatuses = new List<string>();
            await Scenario(i => i == 1 ? false : i == 2 ? true : null, _ => true,
                list => Check(list.Count(report => report[2] == 1 && report[5] == 0x1E) == 1
                    && list.Any(report => report[2] == 3 && report[3] == 1) && list.Last()[3] == 0,
                    "One target hit after Tab must tap 1 then hold left without requiring another match; capture loss releases it."),
                onReport: (report, reads) => { if (report[2] == 1 && report[5] == 0x1E) Check(reads == 2,
                    "1 must be sent on the first positive reading after Tab, before any second capture."); }, onStatus: actionStatuses.Add);
            Check(actionStatuses.Any(status => status.Contains("pressing 1"))
                && actionStatuses.Any(status => status.Contains("holding LEFT"))
                && actionStatuses.Last() == "Combat stopped; generated input released.",
                "Combat inspection must report the actual 1/left-mouse actions and final release.");
            await Scenario(i => i >= 6 ? null : i % 2 == 1, _ => true,
                list => Check(list.Count(report => report[2] == 1 && report[5] == 0x1E) == 1
                    && list.Count(report => report[2] == 3 && report[3] == 1) == 1,
                    "Brief color flicker must not restart the attack or spam 1 before the target-loss timeout."));
            await Scenario(_ => true, i => i < 4,
                list => Check(list.Any(report => report[2] == 3 && report[3] == 1) && list.Last()[3] == 0,
                    "Game focus loss during attack must release left mouse."));
            await Scenario(_ => false, _ => true,
                list => Check(list.Count(report => report[2] == 1 && report[5] == 0x2B) == 2
                    && !list.Any(report => report[2] == 3 && report[3] == 1), "A completed search batch must restart without attacking an unconfirmed target."));
            profile.AionCombat = legacy.AionCombat;
            await Scenario(_ => false, _ => true, list =>
            {
                var tab = list.FindIndex(report => report[2] == 1 && report[5] == 0x2B);
                var camera = list.Where(report => report[2] == 3 && report[4] != 0).ToArray();
                Check(tab >= 0 && list.FindIndex(report => report[2] == 3 && report[4] != 0) > tab
                    && camera.Length == 2 * profile.AionCombat.TurnSteps
                    && camera.All(report => report[3] == 0
                        && System.Buffers.Binary.BinaryPrimitives.ReadInt16LittleEndian(report.AsSpan(4, 2)) == -50)
                    && !list.Any(report => report[2] == 3 && (report[3] & 2) != 0),
                    "Even legacy profiles with right-mouse enabled must turn with no buttons held and never press right mouse.");
            });
            await Scenario(_ => true, i => i < 4, list => Check(!list.Any(report => report[2] == 3 && report[3] == 2),
                "Even enabled camera turning must never hold right mouse while a target is present."));
            profile.AionCombat.CameraTurnEnabled = false;
            await Scenario(_ => true, _ => true,
                list => Check(list.Count(report => report[2] == 1 && report[5] == 0x1E) == 2 && list.Last()[3] == 0,
                    "Attack time limit must release left mouse and restart the combat cycle until manually cancelled."), 1000);
            profile.AionCombat.MaxAttackMs = 0;
            var timerRule = Aio2ProfileSetup.ConfigureCombatCooldown(profile);
            try { AionCombatRunner.Validate(profile); throw new Exception("Uncalibrated cooldown accepted."); }
            catch (InvalidOperationException ex) { Check(ex.Message.Contains("central cooldown"), "Cooldown startup must identify its missing watch area."); }
            timerRule.WatchX = 70; timerRule.WatchY = 80; timerRule.WatchWidth = 16; timerRule.WatchHeight = 14;
            var countBefore = profile.Rules.Count;
            Check(ReferenceEquals(timerRule, Aio2ProfileSetup.ConfigureCombatCooldown(profile)) && profile.Rules.Count == countBefore
                && timerRule.WatchX == 70 && timerRule.WatchWidth == 16 && target.SearchX == 90,
                "Cooldown setup must reuse its rule and preserve both cooldown and target calibration.");
            Check(ReferenceEquals(AionCombatRunner.ValidateCooldown(profile), timerRule), "Configured cooldown must pass startup validation.");
            var saved = JsonSerializer.Deserialize<MacroProfile>(JsonSerializer.Serialize(profile, options), options)!;
            Check(saved.AionCombat.StopOnSkillCooldown && saved.AionCombat.CooldownRuleId == timerRule.Id,
                "Cooldown stop and rule identity must survive save/load.");
            reports.Clear();
            using (var end = new CancellationTokenSource())
            {
                var timerReads = 0; var targetReads = 0; var tabs = 0; long elapsed = 0;
                var messages = new List<string>();
                FakerInputKeyboard.Shared = new(() => new CombatTransport(report =>
                {
                    reports.Add(report.ToArray());
                    if (report[2] == 1 && report[5] == 0x2B && ++tabs == 2) end.Cancel();
                    if (report[2] == 3 && report[3] == 0 && timerReads > 0)
                        Check(timerReads >= 8, "LEFT mouse must survive initial readiness and target loss, then release only after observed cooldown clears for 300 ms.");
                }));
                var task = new AionCombatRunner(profile, (rule, _) =>
                {
                    var passed = rule.Id == timerRule.Id ? ++timerReads is 3 or 4 : ++targetReads == 2;
                    return new(rule.Id, DateTime.UtcNow, new(passed, "Simulated cooldown/target"), null, "");
                }, () => true, messages.Add, (ms, token) => { token.ThrowIfCancellationRequested(); elapsed += ms; return Task.CompletedTask; }, () => elapsed);
                try { await task.RunAsync(end.Token); } catch (OperationCanceledException) { }
                Check(tabs == 2 && reports.Count(report => report[2] == 1 && report[5] == 0x1E) == 1
                    && messages.Any(message => message.StartsWith("Skill 1 cooldown cleared"))
                    && !reports.Any(report => report[2] == 3 && (report[3] & 2) != 0),
                    "Cooldown death detection must release, select the next monster with Tab and never press right mouse.");
            }
            foreach (var unavailable in new[] { false, true })
            {
                reports.Clear(); long elapsed = 0; var reads = 0;
                using var end = new CancellationTokenSource();
                FakerInputKeyboard.Shared = new(() => new CombatTransport(report => reports.Add(report.ToArray())));
                var messages = new List<string>();
                var task = new AionCombatRunner(profile, (rule, _) => new(rule.Id, DateTime.UtcNow,
                    new(rule.Id == target.Id ? true : unavailable && ++reads >= 2 ? null : unavailable, "Simulated timer failure"), null, ""),
                    () => true, messages.Add, (ms, token) => { token.ThrowIfCancellationRequested(); elapsed += ms; if (elapsed >= 6500) end.Cancel(); return Task.CompletedTask; }, () => elapsed);
                try { await task.RunAsync(end.Token); } catch (OperationCanceledException) when (end.IsCancellationRequested) { }
                Check(end.IsCancellationRequested && reports.Any(report => report[2] == 3 && report[3] == 1)
                    && reports.Last(report => report[2] == 3)[3] == 0
                    && messages.Any(message => message.Contains("Retrying; combat is still running")),
                    "Unavailable or never-starting cooldown must recover and release inputs without automatically ending combat.");
            }
            Console.WriteLine("PASS: cooldown setup/calibration/persistence, initial cooldown delay, target loss while held, cooldown clear/reacquisition and unavailable/never-seen cleanup; no real input sent.");
            var targetX = Aio2ProfileSetup.ConfigureCombatTargetX(profile, Path.GetDirectoryName(target.ReferenceImagePath)!);
            Check(profile.AionCombat.RequireTargetX && profile.AionCombat.StopOnSkillCooldown
                && profile.AionCombat.CooldownRuleId == timerRule.Id && timerRule.WatchWidth == 16,
                "X setup must enable combined checking and preserve the existing cooldown calibration.");
            try { AionCombatRunner.Validate(profile); throw new Exception("Uncalibrated X accepted."); }
            catch (InvalidOperationException ex) { Check(ex.Message.Contains("target HUD X"), "X setup must explain its missing watch area."); }
            targetX.SearchX = 464; targetX.SearchY = 9; targetX.SearchWidth = targetX.WatchWidth; targetX.SearchHeight = targetX.WatchHeight;
            var xReference = targetX.ReferenceImagePath;
            Check(ReferenceEquals(targetX, Aio2ProfileSetup.ConfigureCombatTargetX(profile, Path.GetDirectoryName(xReference)!))
                && targetX.SearchX == 464 && targetX.ReferenceImagePath == xReference && timerRule.WatchWidth == 16,
                "Repeated X setup must preserve the saved X reference, area and timer.");
            var xSaved = JsonSerializer.Deserialize<MacroProfile>(JsonSerializer.Serialize(profile, options), options)!;
            Check(xSaved.AionCombat.RequireTargetX && xSaved.AionCombat.TargetXRuleId == targetX.Id
                && xSaved.Rules.Single(rule => rule.Id == targetX.Id).Condition == ConditionType.AionTargetXMatches,
                "Combined X settings must survive save/load without changing legacy condition values.");
            var xShot = Load("Aio2-target-hp.png");
            var xMatch = AionTargetBarMatcher.Find(xShot.Rgb, xShot.Width, xShot.Height, targetX.ReferenceRgb,
                targetX.WatchWidth, targetX.WatchHeight, 90, default);
            Check(xMatch.Location is { } xPoint && Math.Abs(xPoint.X - 482) <= 1 && Math.Abs(xPoint.Y - 27) <= 1,
                $"The real target screenshot X must match at its correct position; got {xMatch}.");
            for (var y = 9; y < 45; y++) Array.Clear(xShot.Rgb, (y * xShot.Width + 464) * 3, 36 * 3);
            Check(AionTargetBarMatcher.Find(xShot.Rgb, xShot.Width, xShot.Height, targetX.ReferenceRgb,
                targetX.WatchWidth, targetX.WatchHeight, 90, default).Location is null,
                "Removing the screenshot X while retaining its HP bar must mean no target X.");
            var dimX = targetX.ReferenceRgb.Select(value => (byte)(value * 0.35)).ToArray();
            Check(AionTargetBarMatcher.Find(dimX, targetX.WatchWidth, targetX.WatchHeight, targetX.ReferenceRgb,
                targetX.WatchWidth, targetX.WatchHeight, 90, default).Location is not null,
                "The real X must still match when dimmed to 35% brightness.");

            async Task CombinedScenario(Func<int, (bool? X, bool? Timer)> reading,
                Action<List<byte[]>, int, long> verify, int focusReads = 30, bool retryableCooldown = false,
                bool stopAfterSearch = true, Action<List<string>, List<string>>? verifyDiagnostics = null,
                Action<byte[], int>? onReport = null, Exception? injectAtSecondRead = null)
            {
                reports.Clear(); var reads = 0; var tabs = 0; long elapsed = 0;
                var messages = new List<string>(); var statuses = new List<string>();
                var violations = new List<string>();
                using var end = new CancellationTokenSource();
                profile.AionCombat.MaxSearchAttempts = 1;
                FakerInputKeyboard.Shared = new(() => new CombatTransport(report =>
                {
                    reports.Add(report.ToArray());
                    onReport?.Invoke(report, reads);
                    if (report[2] == 1 && report[5] == 0x2B)
                        { tabs++; if (reading(reads) != (false, false)) violations.Add("Tab without X absent AND valid readiness."); }
                    if (report[2] == 1 && report[5] == 0x1E)
                        if (reading(reads) != (true, false)) violations.Add("Skill 1 without X present AND valid readiness.");
                }));
                var task = new AionCombatRunner(profile, (rule, _) =>
                {
                    if (rule.Id == targetX.Id) reads++;
                    if (rule.Id == targetX.Id && reads == 2 && injectAtSecondRead is not null) throw injectAtSecondRead;
                    Check(rule.Id == targetX.Id || rule.Id == timerRule.Id, "Combined mode must use X presence instead of unreliable arrows.");
                    var state = reading(reads);
                    return new(rule.Id, DateTime.UtcNow, new(rule.Id == targetX.Id ? state.X : state.Timer, "Simulated X/cooldown",
                        Retryable: rule.Id == timerRule.Id && state.Timer is null && retryableCooldown), null, "");
                }, () => { if (reads >= focusReads) end.Cancel(); return true; }, messages.Add,
                    (ms, token) => { token.ThrowIfCancellationRequested(); elapsed += ms; if (stopAfterSearch && tabs > 0 && ms == 100) end.Cancel(); return Task.CompletedTask; },
                    () => elapsed, (_, status) => statuses.Add(status));
                try { await task.RunAsync(end.Token); } catch (OperationCanceledException) when (end.IsCancellationRequested) { }
                Check(end.IsCancellationRequested, "Combined combat must remain running until manually cancelled.");
                Check(violations.Count == 0, string.Join(" ", violations));
                Check(reports.Last(report => report[2] == 3)[3] == 0, "Combined mode must release generated input when stopped.");
                verify(reports, reads, elapsed);
                verifyDiagnostics?.Invoke(messages, statuses);
            }
            foreach (var retained in new[] { (true, true), (true, false), (false, true) })
            {
                await CombinedScenario(i => i == 1 ? (true, false) : retained,
                    (list, _, _) => Check(list.Count(report => report[2] == 3 && report[3] == 1) == 1
                        && !list.Any(report => report[2] == 1 && report[5] == 0x2B),
                        $"X={retained.Item1}, cooldown={retained.Item2} must retain the attack without Tab."), 8);
            }
            await CombinedScenario(i => i == 1 ? (true, false) : (false, false),
                (list, _, elapsed) => Check(list.Count(report => report[2] == 1 && report[5] == 0x2B) == 1
                    && list.FindIndex(report => report[2] == 3 && report[3] == 0 && list.IndexOf(report) > 0)
                        < list.FindIndex(report => report[2] == 1 && report[5] == 0x2B)
                    && elapsed >= 300, "Missing X AND ready skill 1 must debounce, release LEFT and send Tab exactly once even if no cooldown was observed."));
            await CombinedScenario(i => i == 1 ? (true, false) : (false, true),
                (list, _, _) => Check(!list.Any(report => report[2] == 1 && report[5] == 0x2B),
                    "Missing X while skill 1 is on cooldown must not send Tab."), 8);
            await CombinedScenario(i => (false, i < 4),
                (list, _, _) => Check(list.Count(report => report[2] == 1 && report[5] == 0x2B) == 1
                    && !list.Any(report => report[2] == 3 && report[3] == 1),
                    "Startup without X must wait for cooldown readiness before Tab, without attacking."));
            await CombinedScenario(i => (true, i < 4),
                (list, _, _) => Check(list.Count(report => report[2] == 1 && report[5] == 0x1E) == 1
                    && !list.Any(report => report[2] == 1 && report[5] == 0x2B),
                    "Startup with X must wait for skill 1 readiness, then attack without Tab."), 8);
            await CombinedScenario(i => i == 1 || i >= 4 ? (true, false) : (false, false),
                (list, _, _) => Check(!list.Any(report => report[2] == 1 && report[5] == 0x2B),
                    "Brief X loss must reset when the X returns before the target-loss delay."), 8);
            foreach (var failure in new[] { (X: (bool?)null, Timer: (bool?)false), (X: (bool?)false, Timer: (bool?)null) })
                await CombinedScenario(i => i == 1 ? (true, false) : failure,
                    (list, _, _) => Check(!list.Any(report => report[2] == 1 && report[5] == 0x2B)
                        && list.Any(report => report[2] == 3 && report[3] == 1),
                        "An unreadable X or timer must retry recovery and release LEFT without Tab."));

            var releaseReads = new List<int>();
            await CombinedScenario(i => i == 1 ? (true, false) : i is 4 or 5 ? (false, null) : (false, false),
                (list, _, _) => Check(list.Count(report => report[2] == 1 && report[5] == 0x1E) == 1
                    && list.Count(report => report[2] == 1 && report[5] == 0x2B) == 1
                    && releaseReads.Count > 0 && releaseReads.Min() >= 9,
                    "Ambiguous OCR must preserve LEFT and the single 1 tap, reset target-loss debounce, recover and then Tab."),
                retryableCooldown: true,
                verifyDiagnostics: (messages, statuses) => Check(messages.Count(message => message.Contains("Cooldown reading uncertain")) == 1
                    && messages.Count(message => message.Contains("cooldown reading recovered")) == 1
                    && statuses.Any(status => status.Contains("keeping LEFT held")), "Retry logging must be once per episode and report recovery and held input."),
                onReport: (report, reads) => { if (report[2] == 3 && report[3] == 0 && reads > 1) releaseReads.Add(reads); });
            await CombinedScenario(i => i == 1 ? (true, false) : (false, null),
                (list, _, _) => Check(list.Count(report => report[2] == 1 && report[5] == 0x1E) == 1
                    && list.Count(report => report[2] == 3 && report[3] == 1) == 1
                    && !list.Any(report => report[2] == 1 && report[5] == 0x2B),
                    "Persistent ambiguous OCR must keep the macro and attack active without false readiness or repeated 1."), 10,
                retryableCooldown: true,
                verifyDiagnostics: (messages, _) => Check(messages.Count(message => message.Contains("Cooldown reading uncertain")) == 1,
                    "Persistent OCR uncertainty must not flood the activity log."));
            await CombinedScenario(i => (false, i < 4 ? null : false),
                (list, _, _) => Check(list.Count(report => report[2] == 1 && report[5] == 0x2B) == 1
                    && !list.Any(report => report[2] == 1 && report[5] == 0x1E),
                    "Startup uncertainty must wait before Tab until a valid ready reading arrives."), retryableCooldown: true);
            await CombinedScenario(i => (true, i < 4 ? null : false),
                (list, _, _) => Check(list.Count(report => report[2] == 1 && report[5] == 0x1E) == 1
                    && !list.Any(report => report[2] == 1 && report[5] == 0x2B),
                    "Startup uncertainty with X present must wait before 1 until a valid ready reading arrives."), 8, retryableCooldown: true);
            await CombinedScenario(i => i == 1 ? (false, false) : (true, i < 5 ? null : false),
                (list, _, _) => Check(list.Count(report => report[2] == 1 && report[5] == 0x2B) == 1
                    && list.Count(report => report[2] == 1 && report[5] == 0x1E) == 1,
                    "Unknown cooldown after Tab must not be treated as ready; valid recovery can start the attack."), retryableCooldown: true);
            await CombinedScenario(i => i is 2 or 3 ? (null, false) : (true, false),
                (list, _, _) => Check(list.Count(report => report[2] == 1 && report[5] == 0x1E) == 2
                    && !list.Any(report => report[2] == 1 && report[5] == 0x2B),
                    "Unavailable target capture must release, retry and restart combat when it recovers."), 8, stopAfterSearch: false);
            await CombinedScenario(_ => (false, false),
                (list, _, _) => Check(list.Count(report => report[2] == 1 && report[5] == 0x2B) == 3,
                    "Search batches must restart indefinitely until manually cancelled."), stopAfterSearch: false,
                verifyDiagnostics: (messages, _) => Check(messages.Count(message => message.Contains("restarting the search")) >= 2,
                    "Search-batch restarts must remain visible in Activity."));
            foreach (var failure in new Exception[] { new InvalidOperationException("Injected capture failure"),
                new OperationCanceledException("Injected capture interruption") })
                await CombinedScenario(_ => (true, false),
                    (list, _, _) => Check(list.Count(report => report[2] == 1 && report[5] == 0x1E) == 2,
                        "Runtime exceptions, including unrelated cancellation errors, must recover until the user's Stop token is cancelled."),
                    8, stopAfterSearch: false, injectAtSecondRead: failure);

            reports.Clear();
            using (var end = new CancellationTokenSource())
            {
                long elapsed = 0; var violations = new List<string>(); var messages = new List<string>();
                bool Focused() => elapsed < 300 || elapsed >= 1300;
                FakerInputKeyboard.Shared = new(() => new CombatTransport(report =>
                {
                    reports.Add(report.ToArray());
                    if (!Focused() && ((report[2] == 1 && report[5] != 0) || (report[2] == 3 && report[3] != 0)))
                        violations.Add("Generated input while the game was unfocused.");
                }));
                var task = new AionCombatRunner(profile, (rule, _) =>
                {
                    if (!Focused()) violations.Add("Captured HUD while the game was unfocused.");
                    return new(rule.Id, DateTime.UtcNow, new(rule.Id == targetX.Id, "Focused X/cooldown"), null, "");
                }, Focused, messages.Add, (ms, token) =>
                { token.ThrowIfCancellationRequested(); elapsed += ms; if (elapsed >= 1500) end.Cancel(); return Task.CompletedTask; }, () => elapsed);
                try { await task.RunAsync(end.Token); } catch (OperationCanceledException) when (end.IsCancellationRequested) { }
                Check(end.IsCancellationRequested && violations.Count == 0
                    && reports.Count(report => report[2] == 1 && report[5] == 0x1E) == 2
                    && reports.Last(report => report[2] == 3)[3] == 0
                    && messages.Count(message => message.Contains("Game is not focused")) == 1
                    && messages.Any(message => message.Contains("combat resumed")),
                    "Focus loss must release and pause without stopping, generate no input while unfocused, then resume until manual Stop.");
            }
            Console.WriteLine("PASS: ambiguous OCR retains held input, retries and recovers without false Tab/1 or log spam; startup/after-Tab readiness, hard capture recovery, indefinite search restart, focus pause/resume and manual Stop cleanup.");
            Console.WriteLine("PASS: real X presence/absence/dimming, setup/save-load/calibration preservation, all X/cooldown truth-table states, idle cooldown gating, flicker and unavailable capture cleanup; no real input sent.");
            Console.WriteLine("PASS: production combat search/Tab, confirmed 1 then held attack, death/reacquisition, signed camera driver reports, cancellation, focus/capture recovery and restarted search/attack; no real input sent.");
        }
        finally { FakerInputKeyboard.Shared.ReleaseAllHeldInputs(); FakerInputKeyboard.Shared = previous; }
    }

    private sealed class CombatTransport(Action<byte[]> write) : IFakerInputTransport
    {
        public void Write(byte[] report) => write(report);
        public void Dispose() { }
    }
}
