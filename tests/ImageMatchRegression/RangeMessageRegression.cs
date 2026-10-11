using System.IO;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ClickyBot;

internal static class RangeMessageRegression
{
    private static void Check(bool passed, string message) { if (!passed) throw new Exception(message); }
    internal static async Task Run()
    {
        var bitmap = BitmapDecoder.Create(new Uri(Path.Combine(AppContext.BaseDirectory, "fixtures", "Aio2-out-of-range.png")),
            BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
        var converted = new FormatConvertedBitmap(bitmap, PixelFormats.Rgb24, null, 0);
        var rgb = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 3];
        converted.CopyPixels(rgb, bitmap.PixelWidth * 3, 0);
        Check(AionRangeMessageMatcher.Observe(rgb, 470, 65, default).Passed == true, "Supplied full range toast must match.");
        Check(AionRangeMessageMatcher.Observe(rgb.Select(value => (byte)(value * .35)).ToArray(), 470, 65, default).Passed == true,
            "Range text fading to 35% brightness must still match.");
        var blue = rgb.ToArray();
        for (var i = 0; i < blue.Length; i += 3) (blue[i], blue[i + 2]) = (blue[i + 2], blue[i]);
        Check(AionRangeMessageMatcher.Observe(blue, 470, 65, default).Passed == false, "Non-red text must not reject the target.");
        var phrase = new byte[380 * 26 * 3];
        for (var y = 0; y < 26; y++) Array.Copy(rgb, ((y + 23) * 470 + 42) * 3, phrase, y * 380 * 3, 380 * 3);
        foreach (var (x, y) in new[] { (0, 0), (90, 39) })
        {
            var moved = new byte[470 * 65 * 3];
            for (var row = 0; row < 26; row++) Array.Copy(phrase, row * 380 * 3, moved, ((y + row) * 470 + x) * 3, 380 * 3);
            Check(AionRangeMessageMatcher.Observe(moved, 470, 65, default).Passed == true, "Toast must match at either boundary of its watch area.");
        }
        for (var quarter = 0; quarter < 4; quarter++)
        {
            var incomplete = phrase.ToArray();
            for (var y = 0; y < 26; y++) Array.Clear(incomplete, (y * 380 + quarter * 95) * 3, 95 * 3);
            Check(AionRangeMessageMatcher.Observe(incomplete, 380, 26, default).Passed == false, "Every quarter of the phrase is required.");
        }
        foreach (var color in new[] { (R: 255, G: 0, B: 0), (R: 255, G: 255, B: 255), (R: 60, G: 30, B: 15) })
        {
            var flat = new byte[470 * 65 * 3];
            for (var i = 0; i < flat.Length; i += 3) { flat[i] = (byte)color.R; flat[i + 1] = (byte)color.G; flat[i + 2] = (byte)color.B; }
            Check(AionRangeMessageMatcher.Observe(flat, 470, 65, default).Passed == false, "Flat red, white and brown areas must not match.");
        }
        foreach (var name in new[] { "Aio2-Gather-active.png", "Aio2-Gather-idle.png" })
        {
            var scene = BitmapDecoder.Create(new Uri(Path.Combine(AppContext.BaseDirectory, "fixtures", name)),
                BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
            var crop = new CroppedBitmap(scene, new System.Windows.Int32Rect(730, 215, 470, 65));
            var sceneRgb = new byte[470 * 65 * 3];
            new FormatConvertedBitmap(crop, PixelFormats.Rgb24, null, 0).CopyPixels(sceneRgb, 470 * 3, 0);
            Check(AionRangeMessageMatcher.Observe(sceneRgb, 470, 65, default).Passed == false, "Actual terrain without the message must not restart combat.");
        }
        Check(AionRangeMessageMatcher.Observe([], 470, 65, default).Passed is null
            && AionRangeMessageMatcher.Observe(new byte[100 * 65 * 3], 100, 65, default).Passed is null, "Bad captures must remain unavailable.");
        using (var cancelled = new CancellationTokenSource())
        {
            cancelled.Cancel();
            try { AionRangeMessageMatcher.Observe(rgb, 470, 65, cancelled.Token); throw new Exception("Cancellation ignored."); }
            catch (OperationCanceledException) { }
        }

        var profile = new MacroProfile { Game = "Aion 2", PollIntervalMs = 100 };
        var target = Aio2ProfileSetup.ConfigureCombat(profile, Path.Combine(AppContext.BaseDirectory, "range-reference"));
        var timer = Aio2ProfileSetup.ConfigureCombatCooldown(profile);
        timer.WatchWidth = 16; timer.WatchHeight = 16;
        var targetX = Aio2ProfileSetup.ConfigureCombatTargetX(profile, Path.GetDirectoryName(target.ReferenceImagePath)!);
        targetX.SearchWidth = targetX.WatchWidth; targetX.SearchHeight = targetX.WatchHeight;
        Check(JsonSerializer.Deserialize<AionCombatSettings>("{}")!.WatchOutOfRangeMessage, "Older profiles must enable message recovery by default.");
        var previous = FakerInputKeyboard.Shared;
        try
        {
            foreach (var immediate in new[] { false, true })
            {
                var ones = 0; var tabs = 0; var held = false; long elapsed = 0; long rejectedAt = 0;
                var violations = new List<string>(); var events = new List<(string Key, long Time)>(); var messages = new List<string>();
                using var end = new CancellationTokenSource();
                bool Visible() => ones is 1 or 2 && elapsed >= rejectedAt && elapsed < rejectedAt + 500;
                FakerInputKeyboard.Shared = new(() => new Transport(report =>
                {
                    if (report[2] == 3) { held = report[3] == 1; events.Add((held ? "LEFT down" : "LEFT up", elapsed)); }
                    if (report[2] != 1) return;
                    if (report[5] == 0x1E)
                    {
                        if (Visible()) violations.Add("1 while old toast visible.");
                        ones++; rejectedAt = elapsed + (immediate ? 0 : 150); events.Add(("1", elapsed));
                    }
                    if (report[5] == 0x2B)
                    {
                        if (held) violations.Add("Tab before LEFT release.");
                        tabs++; events.Add(("Tab", elapsed));
                    }
                }));
                var task = new AionCombatRunner(profile, (rule, _) =>
                {
                    // X stays selected throughout, and OCR is uncertain after
                    // the first 1. The explicit rejection must override both.
                    bool? passed = rule.Id == targetX.Id ? true : ones == 1 && elapsed < rejectedAt + 950 ? null : false;
                    return new(rule.Id, DateTime.UtcNow, new(passed, "Simulated HUD", passed is null), null, "");
                }, () => true, messages.Add, _ => new(Visible(), "Simulated range toast"), (ms, token) =>
                { token.ThrowIfCancellationRequested(); elapsed += ms; if (elapsed > 4000) end.Cancel(); return Task.CompletedTask; }, () => elapsed);
                try { await task.RunAsync(end.Token); } catch (OperationCanceledException) when (end.IsCancellationRequested) { }
                Check(end.IsCancellationRequested && ones == 3 && tabs == 2 && !held && violations.Count == 0,
                    "Immediate/delayed rejection must release, force exactly one Tab per toast with X still visible, wait for reliable readiness and restart 1 until manual Stop. " + string.Join(" ", violations));
                Check(events.FindIndex(item => item.Key == "Tab") > events.FindIndex(item => item.Key == "1")
                    && messages.Count(message => message.Contains("target is out of reach")) == 2,
                    "Repeated failures must each restart the cycle once and remain visible in Activity.");
                Check(events.Where(item => item.Key == "1").Skip(1).First().Time >= 950,
                    "Unknown cooldown after retargeting must not allow another 1.");
                if (immediate) Check(events.Count(item => item.Key == "LEFT down") == 1,
                    "An immediate error after 1 must never start holding attack for the rejected target.");
            }

            foreach (var mode in new[] { "persistent", "startup", "unavailable", "focus", "disabled" })
            {
                using var end = new CancellationTokenSource();
                long elapsed = 0; var ones = 0; var tabs = 0; var held = false; var captures = 0; var violations = new List<string>();
                bool Focused() => mode != "focus" || elapsed < 200 || elapsed >= 900;
                FakerInputKeyboard.Shared = new(() => new Transport(report =>
                {
                    if (report[2] == 3) held = report[3] == 1;
                    if (!Focused() && (held || report[2] == 1 && report[5] != 0)) violations.Add("Input while unfocused.");
                    if (report[2] != 1) return;
                    if (report[5] == 0x1E) { ones++; if (mode == "startup" && elapsed < 500) violations.Add("Attacked before old toast cleared."); }
                    if (report[5] == 0x2B) { tabs++; if (held) violations.Add("Retargeted while held."); }
                }));
                profile.AionCombat.WatchOutOfRangeMessage = mode != "disabled";
                var runner = new AionCombatRunner(profile, (rule, _) => new(rule.Id, DateTime.UtcNow,
                    new(rule.Id == targetX.Id, "Simulated HUD"), null, ""), Focused, _ => { }, _ =>
                    {
                        captures++; if (!Focused()) violations.Add("Captured while unfocused.");
                        return new(mode switch
                        {
                            "persistent" => ones >= 1 && elapsed >= 150,
                            "startup" => elapsed < 300,
                            "unavailable" => elapsed is >= 150 and < 900 ? null : false,
                            "focus" => ones == 1 && elapsed is >= 150 and < 1400,
                            _ => true
                        }, "Simulated toast");
                    }, (ms, token) => { token.ThrowIfCancellationRequested(); elapsed += ms; if (elapsed >= 1800) end.Cancel(); return Task.CompletedTask; }, () => elapsed);
                try { await runner.RunAsync(end.Token); } catch (OperationCanceledException) when (end.IsCancellationRequested) { }
                Check(end.IsCancellationRequested && !held && violations.Count == 0, "Each recovery mode must keep running, respect focus and release on manual Stop.");
                Check(mode switch
                {
                    "persistent" => ones == 1 && tabs == 1,
                    "startup" => ones == 1 && tabs == 0,
                    "unavailable" => ones == 2 && tabs == 0,
                    "focus" => ones == 2 && tabs == 1,
                    _ => ones == 1 && tabs == 0 && captures == 0
                }, $"Persistent/stale toast, failed capture, focus pause and opt-out must behave correctly: {mode}, 1={ones}, Tab={tabs}, captures={captures}.");
            }
        }
        finally { FakerInputKeyboard.Shared.ReleaseAllHeldInputs(); FakerInputKeyboard.Shared = previous; }
        Console.WriteLine("PASS: real range message, fading/moving text, incomplete/non-red/flat/terrain rejection; immediate/delayed/repeated range failures, forced Tab with X retained and OCR uncertain, stale message debounce, focus/capture recovery and manual Stop; no real input sent.");
    }
    private sealed class Transport(Action<byte[]> write) : IFakerInputTransport
    {
        public void Write(byte[] report) => write(report);
        public void Dispose() { }
    }
}
