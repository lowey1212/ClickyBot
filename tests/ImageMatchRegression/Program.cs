using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using ClickyBot;

if (args.Length == 4 && (args[0] == "--add-aio2-f" || args[0] == "--add-aio2-skip"))
{
    var prepareOptions = new JsonSerializerOptions { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };
    var profile = JsonSerializer.Deserialize<MacroProfile>(File.ReadAllText(args[1]), prepareOptions)!;
    // Hydrate a previously captured Auto Move gate before sharing it with F.
    foreach (var prepareRule in profile.Rules)
        if (ReferenceImageService.TryLoadFromRule(prepareRule, args[3], true, out var rgb, out var path))
        { prepareRule.GateReferenceRgb = rgb; prepareRule.GateReferenceImagePath = path; }
    if (args[0] == "--add-aio2-skip") Aio2ProfileSetup.ConfigureSkip(profile, Path.GetFullPath(args[3]));
    else Aio2ProfileSetup.ConfigureInteraction(profile, Path.GetFullPath(args[3]));
    File.WriteAllText(args[2], JsonSerializer.Serialize(profile, prepareOptions));
    Console.WriteLine("Added prompt; select its watch area. Existing rules and watch areas preserved.");
    return;
}
if (args.Length == 3 && args[0] == "--prepare-aio2-gather")
{
    var profile = new MacroProfile { Game = "Aion 2", Name = "aion-2-f-gather-random", PollIntervalMs = 100 };
    Aio2ProfileSetup.ConfigureGather(profile, Path.GetFullPath(args[2]));
    File.WriteAllText(args[1], JsonSerializer.Serialize(profile, new JsonSerializerOptions
    {
        WriteIndented = true, Converters = { new JsonStringEnumConverter() }
    }));
    Console.WriteLine($"Prepared {args[1]}; select the F Gather watch area before running.");
    return;
}
if (args.Length == 3 && args[0] == "--prepare-aio2-combat")
{
    var profile = new MacroProfile { Game = "Aion 2", Name = "aion-2-target-combat", PollIntervalMs = 100 };
    Aio2ProfileSetup.ConfigureCombat(profile, Path.GetFullPath(args[2]));
    File.WriteAllText(args[1], JsonSerializer.Serialize(profile, new JsonSerializerOptions
    {
        WriteIndented = true, Converters = { new JsonStringEnumConverter() }
    }));
    Console.WriteLine($"Prepared {args[1]}; select the top-centre target HP watch area before running.");
    return;
}
if (args.Length == 3 && args[0] == "--prepare-aio2")
{
    var profile = new MacroProfile { Game = "aio2", Name = "aio2-quest-prompt", PollIntervalMs = 100 };
    Aio2ProfileSetup.Configure(profile, Path.GetFullPath(args[2]));
    File.WriteAllText(args[1], JsonSerializer.Serialize(profile, new JsonSerializerOptions
    {
        WriteIndented = true, Converters = { new JsonStringEnumConverter() }
    }));
    Console.WriteLine($"Prepared {args[1]}; select the search area under your minimap before running.");
    return;
}
if (args.Length == 2)
{
    ImageFileDiagnostic.Run(args[0], args[1]);
    return;
}

static void Check(bool value, string message)
{
    if (!value) throw new Exception(message);
}

const int width = 23, height = 17, tw = 5, th = 3;
foreach (var (text, expected) in new (string, ushort)[]
{
    ("-", 0xBD), ("=", 0xBB), (";", 0xBA), (",", 0xBC), (".", 0xBE),
    ("/", 0xBF), ("`", 0xC0), ("[", 0xDB), ("\\", 0xDC), ("]", 0xDD), ("'", 0xDE)
})
{
    Check(InputSimulator.TryGetVirtualKey(text, out var key) && key == expected,
        $"Literal {text} must resolve to its keyboard key.");
    NativeMethods.KeyboardInputs.Clear();
    await InputSimulator.ExecuteAsync(new MacroRule { Action = ActionType.KeyPress, Key = text }, default);
    Check(NativeMethods.KeyboardInputs.Count == 2
        && NativeMethods.KeyboardInputs[0].ScanCode == expected
        && NativeMethods.KeyboardInputs[0].Flags == NativeMethods.KeyboardScanCode
        && NativeMethods.KeyboardInputs[1].Flags == (NativeMethods.KeyboardScanCode | 2),
        $"Literal {text} must send one matching key-down and key-up.");
}
Check(!InputSimulator.TryGetVirtualKey("not-a-valid-key", out _),
    "Malformed key names must return false without throwing.");
Console.WriteLine("PASS: literal punctuation keys and invalid key names, with no real input sent.");
NativeMethods.Events.Clear();
NativeMethods.KeyboardInputs.Clear();

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
var navigationProfile = new MacroProfile { ResourceNavigation = new ResourceNavigationSettings { Enabled = true, PromptReferenceImagePath = "prompt.png" } };
var navigationRestored = JsonSerializer.Deserialize<MacroProfile>(JsonSerializer.Serialize(navigationProfile, options), options)!;
Check(navigationRestored.ResourceNavigation.Enabled && navigationRestored.ResourceNavigation.PromptReferenceImagePath == "prompt.png",
    "Resource navigation settings must survive a profile save/load cycle.");
navigationProfile.ResourceNavigation.RestartWhenBarMissing = true;
navigationProfile.ResourceNavigation.BarReferenceImagePath = "bar.png";
navigationRestored = JsonSerializer.Deserialize<MacroProfile>(JsonSerializer.Serialize(navigationProfile, options), options)!;
Check(navigationRestored.ResourceNavigation.RestartWhenBarMissing
    && navigationRestored.ResourceNavigation.BarReferenceImagePath == "bar.png",
    "Missing-bar recovery settings must survive a profile save/load cycle.");
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

NativeMethods.KeyboardInputs.Clear();
var tapRule = new MacroRule { Action = ActionType.KeyPress, Key = "Q" };
await InputSimulator.ExecuteAsync(tapRule, default);
Check(NativeMethods.KeyboardInputs.Count == 2
    && NativeMethods.KeyboardInputs[0].Flags == NativeMethods.KeyboardScanCode
    && NativeMethods.KeyboardInputs[1].Flags == (NativeMethods.KeyboardScanCode | 2)
    && NativeMethods.KeyboardInputs[0].ScanCode == NativeMethods.KeyboardInputs[1].ScanCode,
    "A key tap must send one down and one matching up event.");
var heldForMs = (NativeMethods.KeyboardInputs[1].Ticks - NativeMethods.KeyboardInputs[0].Ticks)
    * 1000d / Stopwatch.Frequency;
Check(heldForMs >= 40, "A key tap must remain down long enough for a game frame to observe it.");
NativeMethods.KeyboardInputs.Clear();
var comboRule = new MacroRule
{
    Action = ActionType.RecordedCombo,
    RecordedSteps =
    [
        new RecordedStep { Type = RecordedStepType.KeyDown, Key = "LeftAlt" },
        new RecordedStep { Type = RecordedStepType.KeyPress, Key = "1" },
        new RecordedStep { Type = RecordedStepType.KeyUp, Key = "LeftAlt" }
    ]
};
await InputSimulator.ExecuteAsync(comboRule, default);
Check(NativeMethods.KeyboardInputs.Count == 4
    && NativeMethods.KeyboardInputs[0].Flags == NativeMethods.KeyboardScanCode
    && NativeMethods.KeyboardInputs[1].Flags == NativeMethods.KeyboardScanCode
    && NativeMethods.KeyboardInputs[2].Flags == (NativeMethods.KeyboardScanCode | 2)
    && NativeMethods.KeyboardInputs[3].Flags == (NativeMethods.KeyboardScanCode | 2)
    && NativeMethods.KeyboardInputs[0].ScanCode == NativeMethods.KeyboardInputs[3].ScanCode
    && NativeMethods.KeyboardInputs[1].ScanCode == NativeMethods.KeyboardInputs[2].ScanCode,
    "Recorded Alt+1 must hold Alt around a distinct 1 key tap.");
Console.WriteLine("PASS: keyboard taps have a visible duration and recorded combos preserve modifier order.");

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
InputSimulator.MoveMouseRelative(75, -5);
Check(NativeMethods.MouseInputs.Last().DeltaX == 75 && NativeMethods.MouseInputs.Last().DeltaY == -5
    && NativeMethods.MouseInputs.Last().Flags == 1,
    "Resource scan must use relative mouse movement for camera control.");
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

var random = new Random(1234);
var pattern = new byte[tw * th * 3];
for (int i = 0; i < pattern.Length; i += 3)
{
    byte level = (byte)random.Next(30, 160);
    pattern[i] = pattern[i + 1] = pattern[i + 2] = level;
}
var litFrame = new byte[width * height * 3];
for (int row = 0; row < th; row++)
for (int column = 0; column < tw; column++)
for (int channel = 0; channel < 3; channel++)
    litFrame[((row + 6) * width + column + 7) * 3 + channel] = (byte)(pattern[(row * tw + column) * 3 + channel] + 40);
var visualMatch = ImageMatcher.FindSimilar(litFrame, width, height, pattern, tw, th, default);
Check(visualMatch.Location == new MatchLocation(9, 7) && visualMatch.Score > 99.99,
    "Visual similarity must locate the same pattern under changed brightness.");
Check(ImageMatcher.FindSimilar(new byte[litFrame.Length], width, height, pattern, tw, th, default).Location is null,
    "A flat frame must not produce a visual match.");
Check(ImageMatcher.FindSimilar(litFrame, width, height, new byte[pattern.Length], tw, th, default).Location is null,
    "A flat reference must not produce arbitrary high similarity.");
Check(ImageMatcher.FindSimilar([], width, height, pattern, tw, th, default).Location is null,
    "Invalid image input must fail closed.");
try
{
    ImageMatcher.FindSimilar(litFrame, width, height, pattern, tw, th, cancelled.Token);
    throw new Exception("Cancelled similarity scan continued.");
}
catch (OperationCanceledException) { }
Console.WriteLine("PASS: image similarity survives brightness changes, rejects flat/invalid data, and honours cancellation.");

// Search the supplied Space key badge across an entire 1080p screen. The
// animated picture above the badge is deliberately outside the reference.
var spacePath = System.IO.Path.Combine(AppContext.BaseDirectory, "fixtures", "Space-prompt-reference.png");
using var spaceStream = System.IO.File.OpenRead(spacePath);
var spaceBitmap = new System.Windows.Media.Imaging.FormatConvertedBitmap(
    System.Windows.Media.Imaging.BitmapDecoder.Create(spaceStream,
        System.Windows.Media.Imaging.BitmapCreateOptions.PreservePixelFormat,
        System.Windows.Media.Imaging.BitmapCacheOption.OnLoad).Frames[0],
    System.Windows.Media.PixelFormats.Rgb24, null, 0);
var spaceRgb = new byte[spaceBitmap.PixelWidth * spaceBitmap.PixelHeight * 3];
spaceBitmap.CopyPixels(spaceRgb, spaceBitmap.PixelWidth * 3, 0);
var spaceClock = Stopwatch.StartNew();
foreach (var (sx, sy) in new[] { (0, 0), (920, 520), (1920 - spaceBitmap.PixelWidth, 1080 - spaceBitmap.PixelHeight) })
{
    var screen = new byte[1920 * 1080 * 3];
    for (var row = 0; row < spaceBitmap.PixelHeight; row++)
        Array.Copy(spaceRgb, row * spaceBitmap.PixelWidth * 3,
            screen, ((sy + row) * 1920 + sx) * 3, spaceBitmap.PixelWidth * 3);
    Check(ImageMatcher.Find(screen, 1920, 1080, spaceRgb, spaceBitmap.PixelWidth, spaceBitmap.PixelHeight, 25, 90, default)
        == new MatchLocation(sx + spaceBitmap.PixelWidth / 2, sy + spaceBitmap.PixelHeight / 2),
        "The actual Space prompt must be found at the centre and both screen corners.");
}
Check(ImageMatcher.Find(new byte[1920 * 1080 * 3], 1920, 1080, spaceRgb,
    spaceBitmap.PixelWidth, spaceBitmap.PixelHeight, 25, 90, default) is null,
    "An absent Space prompt must not trigger.");
NativeMethods.KeyboardInputs.Clear();
await InputSimulator.ExecuteAsync(new MacroRule { Key = "Space", Action = ActionType.KeyPress }, default);
Check(NativeMethods.KeyboardInputs.Count == 2, "Space must produce exactly one down/up pair.");
Console.WriteLine($"PASS: actual Space key badge at full-screen centre and edges, absence, and one simulated Space tap ({spaceClock.ElapsedMilliseconds} ms).");
await Aio2Regression.Run();
await KeyboardModeRegression.Run();
await FakerInputRegression.Run();
await AionCombatRegression.Run();
