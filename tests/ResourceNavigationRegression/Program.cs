using ClickyBot;

static MacroRule StaminaRule(RecordedStepType action) => new()
{
    Condition = ConditionType.RegionSnapshotMatches,
    SearchReference = true,
    Action = ActionType.RecordedCombo,
    ReferenceRgb = [1, 2, 3],
    RecordedSteps = [new RecordedStep { Type = action, Key = "E" }]
};

var low = StaminaRule(RecordedStepType.KeyUp);
var high = StaminaRule(RecordedStepType.KeyDown);
var profile = new MacroProfile
{
    PollIntervalMs = 50,
    Rules = [low, high],
    ResourceNavigation = new ResourceNavigationSettings
    {
        Enabled = true,
        PromptReferenceImagePath = "prompt.png",
        PromptLostMs = 500,
        TurnsBeforeStep = 1,
        ForwardStepMs = 50,
        MaxForwardSteps = 1
    }
};

var navigator = new ResourceNavigator(profile, (rule, _) => false, _ => { });
await navigator.RunAsync(CancellationToken.None);
var events = InputSimulator.Events;
if (!events.SequenceEqual(new[] { "down:E", "up:E", "turn:75,0", "down:W", "up:W", "release-all" }))
    throw new Exception($"Bounded search or key release failed: {string.Join(", ", events)}");
Console.WriteLine("PASS: lost prompt releases E, bounded search turns and steps, then releases all input.");

events.Clear();
var lowSeen = false;
var highSeen = false;
using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(2));
navigator = new ResourceNavigator(profile, (rule, _) =>
{
    if (rule.Name == "E (Hold) prompt") return true;
    if (rule == low && !lowSeen) { lowSeen = true; return true; }
    if (rule == high && !highSeen) { highSeen = true; return true; }
    return false;
}, _ => { });
try { await navigator.RunAsync(stop.Token); }
catch (OperationCanceledException) { }
if (!events.Take(3).SequenceEqual(new[] { "down:E", "up:E", "down:E" }) || events.LastOrDefault() != "release-all")
    throw new Exception($"Stamina cycle failed: {string.Join(", ", events)}");
Console.WriteLine("PASS: 8/50 releases E, 48/50 holds E again, and Stop cleans up.");

events.Clear();
profile.ResourceNavigation.MaxForwardSteps = 2;
using var foundStop = new CancellationTokenSource(TimeSpan.FromSeconds(3));
navigator = new ResourceNavigator(profile,
    (rule, _) =>
    {
        if (rule.Name != "E (Hold) prompt" || !events.Contains("up:W")) return false;
        rule.CurrentMatch = new MatchLocation(800, 680);
        return true;
    },
    message => { if (message.Contains("Found another", StringComparison.Ordinal)) foundStop.Cancel(); });
try { await navigator.RunAsync(foundStop.Token); }
catch (OperationCanceledException) { }
var forwardRelease = events.IndexOf("up:W");
var resumedHarvest = events.FindIndex(forwardRelease + 1, item => item == "down:E");
if (forwardRelease < 0 || resumedHarvest <= forwardRelease || events.LastOrDefault() != "release-all")
    throw new Exception($"New resource was not acquired after moving: {string.Join(", ", events)}");
Console.WriteLine("PASS: a new prompt after movement resumes E and Stop releases held input.");
