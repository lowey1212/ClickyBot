using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ClickyBot;

public partial class MainWindow
{
    private bool _inspectorReady;
    private bool _inspectorClosing;
    private CancellationTokenSource? _inspectionCancellation;
    private Task? _inspectionTask;
    private RuleObservation? _lastEngineInspection;
    private string? _inspectedRuleId;

    private void Inspector_VisibilityChanged(object sender, RoutedEventArgs e)
    {
        if (_inspectorReady && ReferenceEquals(e.Source, InspectorExpander)) RestartInspector();
    }

    private void Inspector_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_inspectorReady) RestartInspector();
    }

    private void StopInspector()
    {
        _inspectionCancellation?.Cancel();
        _inspectionCancellation = null;
        _engine.InspectionUpdated -= ReceiveInspection;
        Volatile.Write(ref _inspectedRuleId, null);
        Volatile.Write(ref _lastEngineInspection, null);
    }

    private void ReceiveInspection(RuleObservation observation)
    {
        // No Dispatcher work per engine poll. The UI consumes only the latest
        // immutable observation at its own slower refresh rate.
        if (observation.RuleId.ToString() == Volatile.Read(ref _inspectedRuleId))
            Volatile.Write(ref _lastEngineInspection, observation);
    }

    private void RestartInspector()
    {
        StopInspector();
        if (!_inspectorReady || _inspectorClosing || !InspectorExpander.IsExpanded || !InspectorExpander.IsVisible) return;
        InspectorLiveImage.Source = null;
        InspectorReferenceImage.Source = null;
        InspectorPrimaryText.Text = InspectorGateText.Text = InspectorContextText.Text = InspectorCaptureText.Text = "";
        InspectorActionText.Text = "";
        if (RulesListBox.SelectedItem is not MacroRule selected)
        {
            InspectorModeText.Text = "Select a rule to inspect.";
            return;
        }
        InspectorModeText.Text = "Checking selected rule…";
        Volatile.Write(ref _inspectedRuleId, selected.Id.ToString());
        _engine.InspectionUpdated += ReceiveInspection;
        var cancellation = new CancellationTokenSource();
        _inspectionCancellation = cancellation;
        // Await any previous capture before beginning another. Rapid selection
        // changes cannot create concurrent full-area image searches.
        _inspectionTask = RunInspectorAsync(_inspectionTask, cancellation, selected);
    }

    private async Task RunInspectorAsync(Task? previous, CancellationTokenSource cancellation, MacroRule selected)
    {
        var token = cancellation.Token;
        try
        {
            if (previous is not null) await previous;
            while (!token.IsCancellationRequested)
            {
                token.ThrowIfCancellationRequested();
                if (!ReferenceEquals(RulesListBox.SelectedItem, selected)) return;
                var generation = _runGeneration;
                var running = _isRunning;
                var rule = CloneRule(selected);
                rule.Id = selected.Id;
                // Unsaved edits are tested on a clone, never on engine state.
                if (!running) ReadEditorIntoRule(rule);
                var result = await Task.Run(() =>
                {
                    token.ThrowIfCancellationRequested();
                    var observation = running ? Volatile.Read(ref _lastEngineInspection) : _engine.Inspect(rule, token);
                    token.ThrowIfCancellationRequested();
                    var search = rule.Condition == ConditionType.RegionSnapshotMatches && rule.SearchReference;
                    var x = search ? rule.SearchX : rule.WatchX;
                    var y = search ? rule.SearchY : rule.WatchY;
                    var width = search ? rule.SearchWidth : rule.WatchWidth;
                    var height = search ? rule.SearchHeight : rule.WatchHeight;
                    BitmapSource? live = null;
                    var captureText = "No watch area is needed for Always.";
                    if (rule.Condition != ConditionType.Always)
                    {
                        var captured = ScreenProbe.TryCapturePreview(x, y, width, height, out var rgb, out var pw, out var ph);
                        token.ThrowIfCancellationRequested();
                        live = captured ? InspectorBitmap(rgb, pw, ph) : null;
                        captureText = captured ? $"Area {x},{y} · {width} × {height} px. Preview is scaled."
                            : "Live preview unavailable. Check the area and desktop capture.";
                    }
                    var reference = rule.Condition == ConditionType.RegionSnapshotMatches
                        ? InspectorBitmap(rule.ReferenceRgb, rule.WatchWidth, rule.WatchHeight) : null;
                    return (Observation: observation, Live: live, Reference: reference, CaptureText: captureText);
                }, token);
                token.ThrowIfCancellationRequested();
                if (!ReferenceEquals(RulesListBox.SelectedItem, selected) || generation != _runGeneration || running != _isRunning)
                    continue;
                InspectorLiveImage.Source = result.Live;
                InspectorReferenceImage.Source = result.Reference;
                InspectorCaptureText.Text = result.CaptureText;
                if (result.Observation is { } observation && observation.RuleId == selected.Id)
                {
                    var age = Math.Max(0, (DateTime.UtcNow - observation.ObservedUtc).TotalSeconds);
                    InspectorModeText.Text = running
                        ? $"Engine observation · {age:F1}s ago. Live image refreshes separately."
                        : "Preview · current editor values · no input sent.";
                    InspectorPrimaryText.Text = $"Main: {observation.Primary.Status} · {observation.Primary.Detail}";
                    InspectorPrimaryText.Foreground = (Brush)FindResource(observation.Primary.Passed == true ? "AccentBrush" : "MutedTextBrush");
                    InspectorGateText.Text = observation.Gate is { } gate ? $"AND gate: {gate.Status} · {gate.Detail}" : "AND gate: off.";
                    InspectorContextText.Text = observation.Context ?? (running && age > 2
                        ? "Observation is old. The game may be unfocused, or this rule may not be checked in the current mode." : "");
                    InspectorActionText.Text = rule.Enabled ? observation.ActionStatus : "Rule disabled. Preview still checks conditions.";
                }
                else
                {
                    InspectorModeText.Text = "Running · awaiting an engine observation for this rule.";
                    InspectorPrimaryText.Text = "The active mode may not check this rule, or the game may be unfocused.";
                    InspectorGateText.Text = InspectorContextText.Text = "";
                    InspectorActionText.Text = rule.Enabled ? "No execution status available yet." : "Rule disabled.";
                }
                await Task.Delay(500, token);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (!token.IsCancellationRequested && !_inspectorClosing && ReferenceEquals(RulesListBox.SelectedItem, selected))
                InspectorModeText.Text = $"Inspector unavailable: {ex.Message}";
        }
        finally
        {
            if (ReferenceEquals(_inspectionCancellation, cancellation)) _inspectionCancellation = null;
            cancellation.Dispose();
        }
    }

    internal static BitmapSource? InspectorBitmap(byte[] rgb, int width, int height)
    {
        if (width < 1 || height < 1 || (long)width * height > ScreenProbe.MaxReferencePixels
            || rgb.LongLength != (long)width * height * 3) return null;
        var image = BitmapSource.Create(width, height, 96, 96, PixelFormats.Rgb24, null, rgb, checked(width * 3));
        image.Freeze();
        return image;
    }
}
