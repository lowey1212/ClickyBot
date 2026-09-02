using System.Text.Json.Serialization;

namespace ClickyBot;

public enum ConditionType
{
    Always,
    PixelMatches,
    PixelDiffers,
    RegionCoverageAtLeast,
    RegionCoverageAtMost,
    RegionSnapshotMatches
}

public enum ActionType
{
    KeyPress,
    KeyHold,
    MouseClick,
    Wait,
    RecordedCombo
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

public sealed class MacroProfile
{
    public const string DefaultGameName = "The First Descendant";

    public string Name { get; set; } = "Untitled profile";
    public string Game { get; set; } = DefaultGameName;
    public int PollIntervalMs { get; set; } = 80;
    public List<MacroRule> Rules { get; set; } = [];
}

public sealed class MacroRule
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "New rule";
    public bool Enabled { get; set; } = true;

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

    [JsonIgnore]
    public byte[] ReferenceRgb { get; set; } = [];

    // Optional second condition. The rule fires only when the primary condition
    // and this gate are both true.
    public bool GateEnabled { get; set; }
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
        ConditionType.RegionSnapshotMatches => $"sampled region {WatchX},{WatchY} {WatchWidth}×{WatchHeight} matches ≥ {CoverageThreshold}%",
        _ => "condition"
    } + (GateEnabled ? $" + gate: {GateCondition} at {GateX},{GateY}" : "")
      + (Condition == ConditionType.RegionSnapshotMatches && ReferenceRgb.Length == 0 ? " (capture a reference)" : "")
      + (GateEnabled && GateCondition == ConditionType.RegionSnapshotMatches && GateReferenceRgb.Length == 0 ? " (capture gate reference)" : "");

    [JsonIgnore]
    public string ActionSummary => Action switch
    {
        ActionType.KeyPress => $"press {Key}",
        ActionType.KeyHold => $"hold {Key}",
        ActionType.MouseClick => $"{MouseButton.ToString().ToLowerInvariant()} click {ClickX},{ClickY}",
        ActionType.Wait => $"wait {DelayAfterActionMs} ms",
        ActionType.RecordedCombo => $"combo ({RecordedSteps.Count} step{(RecordedSteps.Count == 1 ? "" : "s")})",
        _ => "action"
    };
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
