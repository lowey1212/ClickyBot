using System.Text.Json.Serialization;

namespace ClickyBot;

internal sealed record ConditionObservation(bool? Passed, string Detail)
{
    public string Status => Detail.StartsWith("Not checked", StringComparison.Ordinal) ? "Not checked"
        : Passed switch { true => "Passed", false => "Waiting", null => "Unavailable" };

    public static ConditionObservation Pixel(bool captured, RgbColor pixel, RgbColor target, int tolerance, bool differs)
        => !captured ? new(null, "Windows could not read this pixel.")
            : new(pixel.IsCloseTo(target, Math.Clamp(tolerance, 0, 255)) != differs,
                $"Observed {pixel}; target {target}; tolerance {Math.Clamp(tolerance, 0, 255)}.");

    public static ConditionObservation Percent(int value, int threshold, bool atMost, string label)
        => value < 0 ? new(null, "Capture or reference unavailable. Check the selected area and reference.")
            : new(atMost ? value <= Math.Clamp(threshold, 0, 100) : value >= Math.Clamp(threshold, 0, 100),
                $"{label} {value}% · requires {(atMost ? "at most" : "at least")} {Math.Clamp(threshold, 0, 100)}%.");
}

internal sealed record RuleObservation(Guid RuleId, DateTime ObservedUtc, ConditionObservation Primary,
    ConditionObservation? Gate, string ActionStatus, string? Context = null)
{
    public bool Passed => Primary.Passed == true && (Gate is null || Gate.Passed == true);
    public bool Valid => Primary.Passed.HasValue && (Gate is null || Gate.Passed.HasValue);
}


public enum ConditionType
{
    Always,
    PixelMatches,
    PixelDiffers,
    RegionCoverageAtLeast,
    RegionCoverageAtMost,
    RegionSnapshotMatches,
    PurpleRingMatches,
    RegionSnapshotDiffers,
    CooldownTimerPresent,
    CooldownTimerAbsent
}

internal static class Conditions
{
    internal static bool IsReference(this ConditionType condition) => condition is ConditionType.RegionSnapshotMatches or ConditionType.RegionSnapshotDiffers;
    internal static bool IsTimer(this ConditionType condition) => condition is ConditionType.CooldownTimerPresent or ConditionType.CooldownTimerAbsent;
    internal static bool UsesTimer(this MacroRule rule) => rule.Condition.IsTimer() || (rule.GateEnabled && rule.GateCondition.IsTimer());
    internal static ConditionObservation Invert(ConditionObservation observation) => observation with
    {
        Passed = observation.Passed.HasValue ? !observation.Passed.Value : null,
        Detail = "Requires no reference match. " + observation.Detail
    };
}

public enum ActionType
{
    KeyPress,
    KeyHold,
    MouseClick,
    Wait,
    RecordedCombo,
    MouseMove
}

public enum RecordedStepType
{
    KeyPress,
    KeyDown,
    KeyUp,
    MouseClick
}

public enum RepeatMode
{
    OnRisingEdge,
    WhileTrue
}

public enum MouseButtonType
{
    Left,
    Right,
    Middle
}

public enum MouseTargetType
{
    FixedCoordinates,
    MatchedLocation
}

public enum ImageMatchMethod
{
    ImageSimilarity,
    PixelColors
}

public readonly record struct MatchLocation(int X, int Y);

public sealed class MacroProfile
{
    public const string DefaultGameName = "The First Descendant";

    public string Name { get; set; } = "Untitled profile";
    public string Game { get; set; } = DefaultGameName;
    public int PollIntervalMs { get; set; } = 80;
    public List<MacroRule> Rules { get; set; } = [];
    public ResourceNavigationSettings ResourceNavigation { get; set; } = new();
    public bool ThroneCombatMode { get; set; }
    public ThroneHealingSettings ThroneHealing { get; set; } = new();
}

public sealed class ThroneHealingSettings
{
    public bool Enabled { get; set; }
    public int LowHpPercent { get; set; } = 82;
    public string HealthReferenceImagePath { get; set; } = "";
    public int HealthReferenceWidth { get; set; } = 224;
    public int HealthReferenceHeight { get; set; } = 18;
    public int SearchX { get; set; }
    public int SearchY { get; set; }
    public int SearchWidth { get; set; } = 360;
    public int SearchHeight { get; set; } = 160;
    [JsonIgnore]
    public byte[] HealthReferenceRgb { get; set; } = [];
}

public sealed class ResourceNavigationSettings
{
    public bool Enabled { get; set; }
    public bool RestartWhenBarMissing { get; set; }
    public bool UseBarFillForStamina { get; set; }
    public string BarReferenceImagePath { get; set; } = "";
    public int BarReferenceWidth { get; set; } = 341;
    public int BarReferenceHeight { get; set; } = 44;
    public int BarCropX { get; set; } = 23;
    public int BarCropY { get; set; } = 12;
    public int BarCropWidth { get; set; } = 22;
    public int BarCropHeight { get; set; } = 16;
    public int BarMatchThreshold { get; set; } = 90;
    public int BarSearchX { get; set; } = 350;
    public int BarSearchY { get; set; } = 730;
    public int BarSearchWidth { get; set; } = 900;
    public int BarSearchHeight { get; set; } = 300;
    public int BarMissingMs { get; set; } = 1800;
    public int BarCheckIntervalMs { get; set; } = 350;
    public int BarFillStartX { get; set; } = 2;
    public int BarFillRowY { get; set; } = 13;
    public int BarFillWidth { get; set; } = 305;
    public int LowFillPercent { get; set; } = 17;
    public int HighFillPercent { get; set; } = 95;
    public string PromptReferenceImagePath { get; set; } = "";
    public int PromptWidth { get; set; } = 63;
    public int PromptHeight { get; set; } = 26;
    public int PromptThreshold { get; set; } = 82;
    public int PromptSearchX { get; set; } = 500;
    public int PromptSearchY { get; set; } = 500;
    public int PromptSearchWidth { get; set; } = 950;
    public int PromptSearchHeight { get; set; } = 400;
    public int PromptLostMs { get; set; } = 1500;
    public int TurnPixels { get; set; } = 75;
    public int TurnsBeforeStep { get; set; } = 8;
    public int ForwardStepMs { get; set; } = 250;
    public int MaxForwardSteps { get; set; } = 6;
}

public sealed class MacroRule
{
    internal RuleObservation? LastInspection { get; set; }
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "New rule";
    public bool Enabled { get; set; } = true;
    public bool ThroneHealingOnly { get; set; }
    public int PurpleRingDelayMs { get; set; } = 200;

    public ConditionType Condition { get; set; } = ConditionType.Always;
    public int WatchX { get; set; } = 0;
    public int WatchY { get; set; } = 0;
    public int WatchWidth { get; set; } = 1;
    public int WatchHeight { get; set; } = 1;
    public byte TargetRed { get; set; } = 255;
    public byte TargetGreen { get; set; } = 255;
    public byte TargetBlue { get; set; } = 255;
    public int Tolerance { get; set; } = 15;
    public int CoverageThreshold { get; set; } = 50;
    public string ReferenceImagePath { get; set; } = "";
    public bool SearchReference { get; set; }
    public ImageMatchMethod ImageMatchMethod { get; set; } = ImageMatchMethod.ImageSimilarity;

    [JsonIgnore]
    public double? LastImageScore { get; set; }

    [JsonIgnore]
    public string ImageSearchDiagnostic { get; set; } = "";

    [JsonIgnore]
    public bool ObservationValid { get; set; }
    public int SearchX { get; set; }
    public int SearchY { get; set; }
    public int SearchWidth { get; set; } = 400;
    public int SearchHeight { get; set; } = 300;

    public void UseImageSearch()
    {
        if (!Condition.IsReference() || SearchReference) return;
        // Preserve the exact region watched by profiles from before image search.
        SearchX = WatchX;
        SearchY = WatchY;
        SearchWidth = WatchWidth;
        SearchHeight = WatchHeight;
        SearchReference = true;
    }

    [JsonIgnore]
    public byte[] ReferenceRgb { get; set; } = [];

    // Optional second condition. The rule fires only when the primary condition
    // and this gate are both true.
    public bool GateEnabled { get; set; }
    // Legacy gates already have coordinates. New presets can require a manual
    // selection before an absence gate is allowed to authorize input.
    public bool GateAreaSelected { get; set; } = true;
    public ConditionType GateCondition { get; set; } = ConditionType.PixelDiffers;
    public int GateX { get; set; }
    public int GateY { get; set; }
    public int GateWidth { get; set; } = 1;
    public int GateHeight { get; set; } = 1;
    public byte GateTargetRed { get; set; } = 255;
    public byte GateTargetGreen { get; set; } = 255;
    public byte GateTargetBlue { get; set; } = 255;
    public int GateTolerance { get; set; } = 15;
    public int GateCoverageThreshold { get; set; } = 50;
    public string GateReferenceImagePath { get; set; } = "";

    [JsonIgnore]
    public byte[] GateReferenceRgb { get; set; } = [];

    public ActionType Action { get; set; } = ActionType.KeyPress;
    public string Key { get; set; } = "1";
    public int ClickX { get; set; } = 0;
    public int ClickY { get; set; } = 0;
    public MouseButtonType MouseButton { get; set; } = MouseButtonType.Left;
    public bool RestorePointerAfterClick { get; set; } = true;
    public int MouseMoveDelayMs { get; set; }
    public MouseTargetType MouseTarget { get; set; }

    [JsonIgnore]
    public MatchLocation? CurrentMatch { get; set; }

    public MatchLocation ResolveMouseTarget() => MouseTarget == MouseTargetType.MatchedLocation
        ? CurrentMatch ?? throw new InvalidOperationException("No matched location is available. Use a pixel-match or reference-image condition.")
        : new MatchLocation(ClickX, ClickY);
    public RepeatMode Repeat { get; set; } = RepeatMode.OnRisingEdge;
    public int CooldownMs { get; set; } = 500;
    public int DelayAfterActionMs { get; set; } = 20;
    public List<RecordedStep> RecordedSteps { get; set; } = [];

    [JsonIgnore]
    public bool LastCondition { get; set; }

    [JsonIgnore]
    public DateTime LastTriggeredUtc { get; set; } = DateTime.MinValue;

    [JsonIgnore]
    public bool KeyHoldActive { get; set; }

    [JsonIgnore]
    public string ConditionSummary => Condition switch
    {
        ConditionType.Always => "always",
        ConditionType.PixelMatches => $"pixel {WatchX},{WatchY} matches RGB {TargetRed},{TargetGreen},{TargetBlue}",
        ConditionType.PixelDiffers => $"pixel {WatchX},{WatchY} differs from RGB {TargetRed},{TargetGreen},{TargetBlue}",
        ConditionType.RegionCoverageAtLeast => $"region {WatchX},{WatchY} {WatchWidth}×{WatchHeight} ≥ {CoverageThreshold}%",
        ConditionType.RegionCoverageAtMost => $"region {WatchX},{WatchY} {WatchWidth}×{WatchHeight} ≤ {CoverageThreshold}%",
        ConditionType.RegionSnapshotMatches when SearchReference => $"find reference in {SearchX},{SearchY} {SearchWidth}×{SearchHeight} ≥ {CoverageThreshold}%",
        ConditionType.RegionSnapshotMatches => $"sampled region {WatchX},{WatchY} {WatchWidth}×{WatchHeight} matches ≥ {CoverageThreshold}%",
        ConditionType.RegionSnapshotDiffers when SearchReference => $"no reference match in {SearchX},{SearchY} {SearchWidth}×{SearchHeight} at {CoverageThreshold}%",
        ConditionType.RegionSnapshotDiffers => $"sampled reference match below {CoverageThreshold}% at {WatchX},{WatchY}",
        ConditionType.CooldownTimerPresent => $"cooldown timer visible at {WatchX},{WatchY} {WatchWidth}×{WatchHeight}",
        ConditionType.CooldownTimerAbsent => $"no cooldown timer at {WatchX},{WatchY} {WatchWidth}×{WatchHeight}",
        ConditionType.PurpleRingMatches => $"purple ring + {PurpleRingDelayMs} ms in {WatchX},{WatchY} {WatchWidth}×{WatchHeight}",
        _ => "condition"
    } + (GateEnabled ? $" + gate: {GateCondition} at {GateX},{GateY}" : "")
      + (Condition.IsReference() && ReferenceRgb.Length == 0 ? " (capture a reference)" : "")
      + (GateEnabled && GateCondition.IsReference() && GateReferenceRgb.Length == 0 ? " (capture gate reference)" : "");

    [JsonIgnore]
    public string ActionSummary => Action switch
    {
        ActionType.KeyPress => $"press {Key}",
        ActionType.KeyHold => $"hold {Key}",
        ActionType.MouseClick => $"{MouseButton.ToString().ToLowerInvariant()} click {MouseTargetSummary}",
        ActionType.MouseMove => $"move pointer to {MouseTargetSummary}",
        ActionType.Wait => $"wait {DelayAfterActionMs} ms",
        ActionType.RecordedCombo => $"combo ({RecordedSteps.Count} step{(RecordedSteps.Count == 1 ? "" : "s")})",
        _ => "action"
    };

    [JsonIgnore]
    public string MouseTargetSummary => MouseTarget == MouseTargetType.MatchedLocation
        ? "matched location" : $"{ClickX},{ClickY}";
}

public sealed class RecordedStep
{
    public RecordedStepType Type { get; set; }
    public string Key { get; set; } = "";
    public int ClickX { get; set; }
    public int ClickY { get; set; }
    public MouseButtonType MouseButton { get; set; } = MouseButtonType.Left;
    public int DelayBeforeMs { get; set; }

    [JsonIgnore]
    public int SequenceNumber { get; set; }

    [JsonIgnore]
    public string TypeLabel => Type switch
    {
        RecordedStepType.KeyDown => "KEY DOWN",
        RecordedStepType.KeyUp => "KEY UP",
        RecordedStepType.KeyPress => "KEY PRESS",
        _ => "MOUSE CLICK"
    };

    [JsonIgnore]
    public string Summary => Type switch
    {
        RecordedStepType.KeyDown => $"down {Key}",
        RecordedStepType.KeyUp => $"up {Key}",
        RecordedStepType.KeyPress => $"press {Key}",
        _ => $"{MouseButton.ToString().ToLowerInvariant()} click {ClickX},{ClickY}"
    };
}

public readonly record struct RgbColor(byte R, byte G, byte B)
{
    public bool IsCloseTo(RgbColor other, int tolerance)
    {
        return Math.Abs(R - other.R) <= tolerance
            && Math.Abs(G - other.G) <= tolerance
            && Math.Abs(B - other.B) <= tolerance;
    }

    public override string ToString() => $"RGB {R}, {G}, {B}";
}
