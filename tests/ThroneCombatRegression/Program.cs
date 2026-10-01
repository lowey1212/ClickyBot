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
Check(JsonSerializer.Deserialize<MacroProfile>(JsonSerializer.Serialize(new MacroProfile { ThroneCombatMode = true }))!.ThroneCombatMode,
    "Combat mode must round-trip through saved profiles.");
Console.WriteLine("PASS: Q/V priority, continuous 1, immediate resumption, prompt debounce, capture failure, no E/2/3/4 and profile persistence.");

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
