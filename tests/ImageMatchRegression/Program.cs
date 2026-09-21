using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using ClickyBot;

static void Check(bool value, string message)
{
    if (!value) throw new Exception(message);
}

const int width = 23, height = 17, tw = 5, th = 3;
var reference = Enumerable.Range(0, tw * th * 3).Select(i => (byte)(50 + i * 3)).ToArray();
byte[] Frame(int x, int y)
{
    var frame = new byte[width * height * 3];
    for (int row = 0; row < th; row++)
        Array.Copy(reference, row * tw * 3, frame, ((y + row) * width + x) * 3, tw * 3);
    return frame;
}
MatchLocation? Find(byte[] frame, int tolerance = 0, int threshold = 100) =>
    ImageMatcher.Find(frame, width, height, reference, tw, th, tolerance, threshold, CancellationToken.None);

foreach (var (x, y) in new[] { (0, 0), (7, 6), (width - tw, height - th) })
    Check(Find(Frame(x, y)) == new MatchLocation(x + tw / 2, y + th / 2), "Match location or edge scan is wrong.");
Check(Find(new byte[width * height * 3]) is null, "Absent reference should not match.");
Check(ImageMatcher.Find([], width, height, reference, tw, th, 0, 100, default) is null, "Invalid capture must fail closed.");
Check(ImageMatcher.Find(new byte[3], 1, 1, reference, tw, th, 0, 100, default) is null, "Oversized reference must not match.");
var noisy = Frame(7, 6);
noisy[(6 * width + 7) * 3] += 3;
Check(Find(noisy) is null, "Exact matching accepted changed pixel.");
Check(Find(noisy, 3) == new MatchLocation(9, 7), "Color tolerance rejected acceptable variation.");
Check(Find(noisy, 0, 90) == new MatchLocation(9, 7), "Percentage threshold rejected acceptable variation.");

var rule = new MacroRule { MouseTarget = MouseTargetType.MatchedLocation, CurrentMatch = Find(Frame(7, 6)) };
Check(rule.ResolveMouseTarget() == new MatchLocation(9, 7), "Mouse target did not follow match.");
rule.CurrentMatch = Find(new byte[width * height * 3]);
try { rule.ResolveMouseTarget(); throw new Exception("Missing match reused a stale location."); }
catch (InvalidOperationException) { }
rule.CurrentMatch = new MatchLocation(-400, 80);
Check(rule.ResolveMouseTarget() == new MatchLocation(-400, 80), "Negative monitor coordinates were lost.");
var options = new JsonSerializerOptions { Converters = { new JsonStringEnumConverter() } };
rule.SearchReference = true;
rule.SearchX = -500;
rule.MouseMoveDelayMs = 75;
var restored = JsonSerializer.Deserialize<MacroRule>(JsonSerializer.Serialize(rule, options), options)!;
Check(restored.CurrentMatch is null && restored.SearchReference && restored.SearchX == -500
    && restored.MouseMoveDelayMs == 75 && restored.MouseTarget == MouseTargetType.MatchedLocation,
    "Profile roundtrip must preserve configuration but discard live match coordinates.");
var legacy = JsonSerializer.Deserialize<MacroRule>("{\"ClickX\":12,\"ClickY\":34}")!;
Check(legacy.ResolveMouseTarget() == new MatchLocation(12, 34) && !legacy.SearchReference,
    "Existing macros must retain their fixed-coordinate behavior.");

using var cancelled = new CancellationTokenSource();
cancelled.Cancel();
try
{
    ImageMatcher.Find(Frame(0, 0), width, height, reference, tw, th, 0, 100, cancelled.Token);
    throw new Exception("Cancelled search continued.");
}
catch (OperationCanceledException) { }
var watch = Stopwatch.StartNew();
Check(ImageMatcher.Find(new byte[1200 * 800 * 3], 1200, 800,
    Enumerable.Repeat((byte)200, 200 * 200 * 3).ToArray(), 200, 200, 15, 90, default) is null,
    "Large no-match frame produced a false match.");
Console.WriteLine($"PASS: moving matches, edges, tolerance, thresholds, missing matches, coordinates, persistence, cancellation. Large-area scan: {watch.ElapsedMilliseconds} ms.");

rule.CurrentMatch = new MatchLocation(250, 175);
rule.Action = ActionType.MouseClick;
rule.MouseMoveDelayMs = 0;
rule.RestorePointerAfterClick = true;
await InputSimulator.ExecuteAsync(rule, default);
Check(NativeMethods.Events.SequenceEqual(new[] { "move:250,175", "input:0:49153", "input:0:2", "input:0:4", "move:10,20" }),
    "Mouse action must move to the match, left-click, then restore the pointer.");
NativeMethods.Events.Clear();
rule.Action = ActionType.MouseMove;
await InputSimulator.ExecuteAsync(rule, default);
Check(NativeMethods.Events.SequenceEqual(new[] { "move:250,175", "input:0:49153" }), "Move-only must send a movement event without clicking.");
NativeMethods.Events.Clear();
rule.CurrentMatch = null;
try { await InputSimulator.ExecuteAsync(rule, default); throw new Exception("Mouse action accepted missing match."); }
catch (InvalidOperationException) { }
Check(NativeMethods.Events.Count == 0, "Missing match generated input.");
rule.CurrentMatch = new MatchLocation(80, 90);
rule.Action = ActionType.MouseClick;
rule.MouseMoveDelayMs = 60000;
using var stop = new CancellationTokenSource();
var pendingClick = InputSimulator.ExecuteAsync(rule, stop.Token);
stop.Cancel();
try { await pendingClick; throw new Exception("Stopped delayed click was not cancelled."); }
catch (OperationCanceledException) { }
Check(NativeMethods.Events.SequenceEqual(new[] { "move:80,90", "input:0:49153", "move:250,175" }),
    "Stop must cancel the pending click and restore the original pointer.");
Console.WriteLine("PASS: production mouse action ordering, move-only, missing-target guard, and stop during pre-click delay (simulated input sink).");

NativeMethods.DesktopLeft = -1920;
rule.Action = ActionType.MouseMove;
rule.CurrentMatch = new MatchLocation(-1920, 0);
await InputSimulator.ExecuteAsync(rule, default);
Check(NativeMethods.MouseInputs.Last().DeltaX == 0 && NativeMethods.MouseInputs.Last().DeltaY == 0,
    "Virtual-desktop movement must include negative monitor origins.");
rule.CurrentMatch = new MatchLocation(1919, 2159);
await InputSimulator.ExecuteAsync(rule, default);
Check(NativeMethods.MouseInputs.Last().DeltaX == 65535 && NativeMethods.MouseInputs.Last().DeltaY == 65535,
    "Virtual-desktop endpoints must map to absolute mouse endpoints.");
NativeMethods.Events.Clear();
rule.CurrentMatch = new MatchLocation(5000, 3000);
try { await InputSimulator.ExecuteAsync(rule, default); throw new Exception("Offscreen target was accepted."); }
catch (InvalidOperationException) { }
Check(NativeMethods.Events.Count == 0, "Offscreen target generated input.");
var oldImage = new MacroRule { Condition = ConditionType.RegionSnapshotMatches, WatchX = -120, WatchY = 80, WatchWidth = 29, WatchHeight = 43 };
oldImage.UseImageSearch();
Check(oldImage.SearchReference && oldImage.SearchX == -120 && oldImage.SearchY == 80
    && oldImage.SearchWidth == 29 && oldImage.SearchHeight == 43, "Legacy watched region changed during migration.");
oldImage.SearchWidth = 500;
oldImage.WatchWidth = 75;
oldImage.UseImageSearch();
Check(oldImage.SearchWidth == 500, "Reference changes must not overwrite the selected search area.");
Console.WriteLine("PASS: absolute movement, multiple monitors, invalid coordinates, and independent area/reference migration.");
