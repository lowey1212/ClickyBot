using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ClickyBot;

static void Check(bool value, string message) { if (!value) throw new Exception(message); }
static (byte[] Rgb, int Width, int Height) Read(string name)
{
    using var stream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "fixtures", name));
    var bitmap = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
    var converted = new FormatConvertedBitmap(bitmap, PixelFormats.Rgb24, null, 0);
    var rgb = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 3];
    converted.CopyPixels(rgb, bitmap.PixelWidth * 3, 0);
    return (rgb, bitmap.PixelWidth, bitmap.PixelHeight);
}

var keys = new[] { "Q", "V", "1" };
var rules = keys.Select(key => new MacroRule { Key = key, Condition = ConditionType.RegionSnapshotMatches,
    SearchReference = true, CooldownMs = 300, Repeat = RepeatMode.WhileTrue }).ToArray();
var runner = new ThroneCombatRunner();
List<(MacroRule Rule, bool? Ready)> Observe(params string[] ready) => rules.Select(rule => (rule, (bool?)ready.Contains(rule.Key))).ToList();
var chosen = runner.Choose(Observe("1", "V", "Q"), 0);
Check(chosen?.Key == "Q", "Defence must beat chains and attack."); runner.MarkSent(chosen!, 0);
chosen = runner.Choose(Observe("1", "V", "Q"), 100);
Check(chosen?.Key == "V", "A handled Q prompt must allow the pending V response."); runner.MarkSent(chosen!, 100);
chosen = runner.Choose(Observe("1", "V", "Q"), 200);
Check(chosen?.Key == "1", "Resume 1 even if handled prompt animations remain visible."); runner.MarkSent(chosen!, 200);
Check(runner.Choose(Observe("1", "V", "Q"), 250) is null, "Continuous attack must respect its tap interval.");
chosen = runner.Choose(Observe("1", "V", "Q"), 500);
Check(chosen?.Key == "1", "Attack must continue when no new prompt appears."); runner.MarkSent(chosen!, 500);
runner.Choose(Observe(), 600);
Check(runner.Choose(Observe("V"), 680) is null, "A brief prompt flicker must not rearm V.");
runner.Choose(rules.Select(rule => (rule, (bool?)null)).ToList(), 900);
Check(runner.Choose(Observe("V"), 1200) is null, "Failed captures must not rearm V.");
runner.Choose(Observe(), 1300); runner.Choose(Observe(), 1500);
chosen = runner.Choose(Observe("1", "V"), 1600);
Check(chosen?.Key == "V", "A new chain prompt must interrupt attack again."); runner.MarkSent(chosen!, 1600);
Check(new ThroneCombatRunner().Choose(Observe(), 0) is null, "No ready icons must produce no input.");
Check(ThroneCombatRunner.Supports(new MacroRule { Key = "1", Condition = ConditionType.Always }), "1 must run without a cooldown image.");
foreach (var key in new[] { "E", "2", "3", "4" })
    Check(!ThroneCombatRunner.Supports(new MacroRule { Key = key, Condition = ConditionType.Always }), "Combat must ignore E/2/3/4.");
Check(ThroneCombatRunner.Supports(new MacroRule { Key = "7", Condition = ConditionType.RegionSnapshotMatches, SearchReference = true, ThroneHealingOnly = true })
    && !ThroneCombatRunner.Supports(new MacroRule { Key = "7", Condition = ConditionType.RegionSnapshotMatches, SearchReference = true }),
    "Only configured healing rules may run as low-HP skills.");
Check(JsonSerializer.Deserialize<MacroProfile>(JsonSerializer.Serialize(new MacroProfile { ThroneCombatMode = true }))!.ThroneCombatMode,
    "Combat mode must round-trip through saved profiles.");
Console.WriteLine("PASS: Q/V priority, continuous 1, immediate resumption, prompt debounce, capture failure, no E/2/3/4 and profile persistence.");

var timing = new PurpleRingTiming();
Check(!timing.Observe(true, 200, 1000) && !timing.Observe(true, 200, 1199)
    && timing.Observe(true, 200, 1200), "Q must wait exactly the configured delay from first detection.");
Check(!timing.Observe(false, 200, 1250) && !timing.Observe(false, 200, 1320)
    && timing.Observe(true, 200, 1330), "A short circle flicker must preserve the original delay.");
timing.Observe(false, 200, 1400); timing.Unknown(); timing.Observe(false, 200, 1600);
Check(timing.Observe(true, 200, 1650), "Failed captures must not count as continuous absence.");
timing.Observe(false, 200, 1700); timing.Observe(false, 200, 1820);
Check(!timing.Observe(true, 200, 1900) && timing.Observe(true, 200, 2100), "A new circle must wait a fresh delay.");
var expired = new PurpleRingTiming();
expired.Observe(true, 200, 0);
Check(!expired.Observe(false, 200, 200), "A vanished circle must never produce a queued Q.");
Check(new PurpleRingTiming().Observe(true, 0, 0), "Zero delay must preserve immediate Q.");
var delayedRunner = new ThroneCombatRunner(); var delayedTiming = new PurpleRingTiming();
List<(MacroRule Rule, bool? Ready)> Delayed(long now) => rules.Select(rule =>
    (rule, (bool?)(rule.Key == "Q" ? delayedTiming.Observe(true, 200, now) : rule.Key == "1"))).ToList();
Check(delayedRunner.Choose(Delayed(0), 0)?.Key == "1", "Attack must continue while Q is waiting.");
var delayedQ = delayedRunner.Choose(Delayed(200), 200);
Check(delayedQ?.Key == "Q", "Q must regain priority once its delay expires.");
delayedRunner.MarkSent(delayedQ!, 200);
Check(delayedRunner.Choose(Delayed(600), 600)?.Key == "1", "Q must be sent only once per delayed circle.");
Check(JsonSerializer.Deserialize<MacroRule>("{}")!.PurpleRingDelayMs == 200
    && JsonSerializer.Deserialize<MacroRule>(JsonSerializer.Serialize(new MacroRule { PurpleRingDelayMs = 350 }))!.PurpleRingDelayMs == 350,
    "Existing macros must default to 200 ms, and custom Q delays must persist.");
Console.WriteLine("PASS: delayed Q boundary, circle flicker/rearm, unknown captures, vanished circle, zero/custom delay, attack while waiting and one Q per prompt.");

var chain = Read("V-chain.png");
var badge = new byte[18 * 18 * 3];
for (var y = 0; y < 18; y++) Array.Copy(chain.Rgb, ((64 + y) * chain.Width + 36) * 3, badge, y * 18 * 3, 18 * 3);
Check(ImageMatcher.Find(chain.Rgb, chain.Width, chain.Height, badge, 18, 18, 20, 85, default) is not null,
    "The supplied V chain icon must match.");
Console.WriteLine("PASS: actual supplied V prompt accepted using color-sensitive matching.");
var game = Read("V-game-screen.png");
var gameV = ImageMatcher.Find(game.Rgb, game.Width, game.Height, badge, 18, 18, 20, 85, default);
Console.WriteLine($"Actual game frame {game.Width} × {game.Height}, V badge at {gameV}");
Check(gameV is { X: >= 1295 and <= 1330, Y: >= 675 and <= 710 }, "V badge must match the actual screen location with different skill artwork.");

var prompt = Read("Q-prompt.png"); var beam = Read("Purple-beam.png");
var timer = Stopwatch.StartNew();
Check(PurpleRingMatcher.Find(prompt.Rgb, prompt.Width, prompt.Height, default) is not null, "The real Q ring must be detected.");
Check(PurpleRingMatcher.Find(beam.Rgb, beam.Width, beam.Height, default) is null, "The purple horizontal beam must not trigger Q.");
var world = new byte[1540 * 710 * 3];
for (var y = 0; y < 710; y++) Array.Copy(game.Rgb, ((140 + y) * game.Width) * 3, world, y * 1540 * 3, 1540 * 3);
var falsePrompt = PurpleRingMatcher.Find(world, 1540, 710, default);
Console.WriteLine($"Actual game play area ring result: {falsePrompt}");
Check(falsePrompt is null, "The real game play area with purple character effects but no Q prompt must not trigger defence.");
foreach (var radius in new[] { 12, 18, 30, 45, 60, 90, 118 })
{
    const int width = 320, height = 260;
    var rgb = new byte[width * height * 3];
    var random = new Random(42);
    random.NextBytes(rgb);
    // A dim noisy scene rather than an empty backdrop.
    for (var i = 0; i < rgb.Length; i++) rgb[i] /= 4;
    for (var y = 0; y < height; y++) for (var x = 0; x < width; x++)
    {
        var distance = Math.Sqrt((x - 161d) * (x - 161d) + (y - 131d) * (y - 131d));
        if (Math.Abs(distance - radius) <= 2 && !(x > 161 && y < 131 && y > 131 - radius * 0.25))
        { var i = (y * width + x) * 3; rgb[i] = 240; rgb[i + 1] = 110; rgb[i + 2] = 255; }
    }
    Check(PurpleRingMatcher.Find(rgb, width, height, default) is not null, $"Shrinking ring radius {radius} must match despite a partial arc.");
}
var solid = Enumerable.Range(0, 160 * 160 * 3).Select(i => (byte)(i % 3 == 1 ? 60 : 240)).ToArray();
Check(PurpleRingMatcher.Find(solid, 160, 160, default) is null, "Solid purple background must not trigger Q.");
Check(PurpleRingMatcher.Find([], 100, 100, default) is null, "Invalid captures must fail closed.");
using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
try { PurpleRingMatcher.Find(prompt.Rgb, prompt.Width, prompt.Height, cancelled.Token); throw new Exception("Cancellation was ignored."); }
catch (OperationCanceledException) { }
Console.WriteLine($"PASS: real Q prompt, rings at 7 shrinking sizes, beam/solid negatives, invalid captures and cancellation ({timer.ElapsedMilliseconds} ms total).");

var hpImage = Read("HP-low.png"); var hpRef = Read("HP-bar.png");
var hp = ThroneHealthMatcher.Read(hpImage.Rgb, hpImage.Width, hpImage.Height, hpRef.Rgb, hpRef.Width, hpRef.Height, default);
Console.WriteLine($"Actual supplied low HP fill: {hp:F2}%");
Check(hp is >= 79 and <= 82, "The supplied 2746/3381 HP bar must trigger the 82% threshold.");
var hud = new byte[360 * 160 * 3];
for (var y = 0; y < 160; y++) Array.Copy(game.Rgb, y * game.Width * 3, hud, y * 360 * 3, 360 * 3);
var oldHp = ThroneHealthMatcher.Read(hud, 360, 160, hpRef.Rgb, hpRef.Width, hpRef.Height, default);
Console.WriteLine($"Shifted game HUD health: {oldHp:F2}%");
Check(oldHp is >= 71 and <= 78, "HP location search must survive the crop's offset and ignore mana.");
Check(ThroneHealthMatcher.Read(new byte[hud.Length], 360, 160, hpRef.Rgb, hpRef.Width, hpRef.Height, default) is null,
    "A missing HP frame must be unknown, never low health.");
var manaOnly = hpImage.Rgb.ToArray();
for (var y = 64; y < 82; y++) Array.Clear(manaOnly, (y * hpImage.Width + 84) * 3, 224 * 3);
Check(ThroneHealthMatcher.Read(manaOnly, hpImage.Width, hpImage.Height, hpRef.Rgb, hpRef.Width, hpRef.Height, default) is null,
    "The remaining mana bar must not masquerade as a low HP bar.");
var fullHp = hpRef.Rgb.ToArray();
for (var y = 3; y < hpRef.Height - 4; y++) for (var x = 8; x < hpRef.Width - 10; x++)
{ var i = (y * hpRef.Width + x) * 3; fullHp[i] = 35; fullHp[i + 1] = 165; fullHp[i + 2] = 85; }
Check(ThroneHealthMatcher.Read(fullHp, hpRef.Width, hpRef.Height, hpRef.Rgb, hpRef.Width, hpRef.Height, default) is >= 99,
    "A recovered/full bar must stop the low-health trigger.");
var cooling = Read("Heal-cooldowns.png");
foreach (var key in new[] { "7", "8" })
{
    var ready = Read($"{key}-ready.png");
    Check(ImageMatcher.Find(game.Rgb, game.Width, game.Height, ready.Rgb, ready.Width, ready.Height, 20, 90, default) is not null,
        $"Ready skill {key} must match the actual game screenshot.");
    Check(ImageMatcher.Find(cooling.Rgb, cooling.Width, cooling.Height, ready.Rgb, ready.Width, ready.Height, 20, 90, default) is null,
        $"The supplied 9s/3s cooldowns must block skill {key}.");
}
var healRules = new[] { new MacroRule { Key = "Q" }, new MacroRule { Key = "7", CooldownMs = 1000 },
    new MacroRule { Key = "8", CooldownMs = 1000 }, new MacroRule { Key = "V" }, new MacroRule { Key = "1" } };
var healRunner = new ThroneCombatRunner();
List<(MacroRule Rule, bool? Ready)> Healing(params string[] ready) => healRules.Select(rule => (rule, (bool?)ready.Contains(rule.Key))).ToList();
Check(healRunner.Choose(Healing("7", "8", "1"), 0, lowHealth: false)?.Key == "1",
    "Healthy, unknown or disabled HP healing must never press 7/8 even when their icons are ready.");
chosen = healRunner.Choose(Healing("Q", "7", "8", "V", "1"), 0, lowHealth: hp <= 82);
Check(chosen?.Key == "Q", "Q must still take priority over healing."); healRunner.MarkSent(chosen!, 0);
chosen = healRunner.Choose(Healing("7", "8", "V", "1"), 100, lowHealth: true);
Check(chosen?.Key == "7", "Ready 7 must be sent at low HP."); healRunner.MarkSent(chosen!, 100);
chosen = healRunner.Choose(Healing("7", "8", "V", "1"), 200, lowHealth: true);
Check(chosen?.Key == "8", "8 must have an independent readiness/debounce check."); healRunner.MarkSent(chosen!, 200);
chosen = healRunner.Choose(Healing("7", "8", "V", "1"), 300, lowHealth: true);
Check(chosen?.Key == "V", "Healing debounce must allow combat to continue."); healRunner.MarkSent(chosen!, 300);
chosen = healRunner.Choose(Healing("8", "1"), 1300, lowHealth: true);
Check(chosen?.Key == "8", "A recovered 8 must be usable again while HP remains low, even if 7 is still on cooldown.");
Check(healRunner.Choose(Healing("1"), 1500, lowHealth: true)?.Key == "1", "Both skills on cooldown must leave continuous attack running.");
var savedHealing = JsonSerializer.Deserialize<MacroProfile>(JsonSerializer.Serialize(new MacroProfile
    { ThroneHealing = new ThroneHealingSettings { Enabled = true, LowHpPercent = 82, HealthReferenceImagePath = "hp.png", HealthReferenceRgb = hpRef.Rgb } }))!;
Check(savedHealing.ThroneHealing.Enabled && savedHealing.ThroneHealing.HealthReferenceImagePath == "hp.png"
    && savedHealing.ThroneHealing.HealthReferenceRgb.Length == 0, "Healing settings must persist without writing runtime pixel caches into the macro.");
Console.WriteLine("PASS: HP threshold/fill and shifted HUD, missing/full bar, actual ready/cooldown 7/8 images, Q priority and independent healing reuse.");
