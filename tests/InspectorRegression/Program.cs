using System.Reflection;
using System.Text.Json;
using System.Windows.Controls;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Collections.ObjectModel;
using System.Windows.Markup;
using System.Xml.Linq;
using ClickyBot;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        static void Check(bool passed, string message)
        {
            if (!passed) throw new Exception(message);
        }
        var missingPixel = ConditionObservation.Pixel(false, default, default, 15, differs: true);
        Check(missingPixel.Passed is null && missingPixel.Status == "Unavailable",
            "An unavailable pixel must never pass a differs condition.");
        Check(ConditionObservation.Percent(-1, 0, true, "Coverage").Passed is null,
            "Unavailable coverage must never pass an at-most condition.");
        Check(ConditionObservation.Percent(50, 50, true, "Coverage").Passed == true
            && ConditionObservation.Percent(50, 50, false, "Coverage").Passed == true,
            "Percentage thresholds must remain inclusive.");

        var engine = new MacroEngine();
        var autoMoveGate = new MacroRule
        {
            Condition = ConditionType.Always, GateEnabled = true, GateAreaSelected = false,
            GateCondition = ConditionType.RegionSnapshotDiffers, GateWidth = 72, GateHeight = 17,
            GateReferenceRgb = new byte[72 * 17 * 3]
        };
        Check(!engine.Inspect(autoMoveGate, default).Passed && !autoMoveGate.ObservationValid,
            "An unselected Auto Move gate must block input even when its reference is loaded.");
        Check(JsonSerializer.Deserialize<MacroRule>("{}")!.GateAreaSelected,
            "Legacy gates must retain their configured coordinates without a new selection.");
        Console.WriteLine("PASS: unconfigured Auto Move absence gate blocks, with legacy gate compatibility.");
        // Exercise the real engine loop without producing keyboard input.
        // An invalid key followed by a wait action must survive several polls.
        using (var stop = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
        {
            var failures = 0;
            var completed = 0;
            var continuity = new MacroEngine();
            continuity.Log += message =>
            {
                if (message.Contains("action failed")) failures++;
                if (message.Contains("sent wait") && ++completed == 3) stop.Cancel();
            };
            var profile = new MacroProfile { PollIntervalMs = 20, Rules =
            [
                new() { Name = "Invalid key", Condition = ConditionType.Always,
                    Action = ActionType.KeyPress, Key = "not-a-valid-key", Repeat = RepeatMode.WhileTrue, CooldownMs = 0 },
                new() { Name = "Following skill", Condition = ConditionType.Always,
                    Action = ActionType.Wait, Repeat = RepeatMode.WhileTrue, CooldownMs = 0 }
            ] };
            try { continuity.RunAsync(profile, stop.Token).GetAwaiter().GetResult(); }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
            Check(completed == 3 && failures == 3,
                "An invalid key must fail only its action; subsequent rules and later polls must keep running.");
        }
        Console.WriteLine("PASS: real engine continues across invalid-key actions and three polling cycles until cancelled.");
        using (var stop = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
        {
            var reactions = 0;
            var polls = 0;
            DateTime? scheduledDeadline = null;
            var randomRule = new MacroRule { Name = "Delayed action", Condition = ConditionType.Always, Action = ActionType.Wait,
                Repeat = RepeatMode.WhileTrue, RandomizeReactionDelay = true, ReactionDelayMinMs = 1000, ReactionDelayMaxMs = 1000, DelayAfterActionMs = 0 };
            var randomEngine = new MacroEngine();
            randomEngine.Log += message =>
            {
                if (message.StartsWith("Delayed action: sent")) reactions++;
                if (message.StartsWith("Other rule: sent"))
                {
                    scheduledDeadline ??= randomRule.PendingReactionUtc;
                    Check(randomRule.PendingReactionUtc == scheduledDeadline, "Polling must not resample the pending reaction delay.");
                    if (++polls == 8) stop.Cancel();
                }
            };
            var profile = new MacroProfile { PollIntervalMs = 20, Rules = [randomRule,
                new() { Name = "Other rule", Condition = ConditionType.Always, Action = ActionType.Wait,
                    Repeat = RepeatMode.WhileTrue, CooldownMs = 0, DelayAfterActionMs = 0 }] };
            var elapsed = System.Diagnostics.Stopwatch.StartNew();
            try { randomEngine.RunAsync(profile, stop.Token).GetAwaiter().GetResult(); }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
            Check(reactions == 0 && polls == 8 && scheduledDeadline.HasValue && elapsed.ElapsedMilliseconds < 1000,
                "Reaction waiting must let other rules continue and cancellation must prevent the pending action.");
        }
        using (var stop = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
        {
            var reactions = 0;
            var polls = 0;
            DateTime? reappearedDeadline = null;
            var randomRule = new MacroRule { Name = "Conditional delayed action", Condition = ConditionType.Always, Action = ActionType.Wait,
                Repeat = RepeatMode.OnRisingEdge, RandomizeReactionDelay = true, ReactionDelayMinMs = 60, ReactionDelayMaxMs = 60, DelayAfterActionMs = 0 };
            var randomEngine = new MacroEngine();
            randomEngine.Log += message =>
            {
                if (message.StartsWith("Conditional delayed action: sent"))
                {
                    reactions++;
                    Check(reappearedDeadline.HasValue && DateTime.UtcNow >= reappearedDeadline, "Reappearing conditions must finish a fresh reaction delay.");
                }
                if (message.StartsWith("Polling continues: sent"))
                {
                    polls++;
                    if (polls == 1) { randomRule.GateEnabled = true; randomRule.GateCondition = ConditionType.RegionSnapshotMatches; randomRule.GateReferenceRgb = []; }
                    if (polls == 4) Check(randomRule.PendingReactionUtc is null && reactions == 0, "An unavailable condition must cancel the pending action.");
                    if (polls == 8) randomRule.GateEnabled = false;
                    if (polls == 9) reappearedDeadline = randomRule.PendingReactionUtc;
                    if (polls == 20) stop.Cancel();
                }
            };
            var profile = new MacroProfile { PollIntervalMs = 20, Rules = [randomRule,
                new() { Name = "Polling continues", Condition = ConditionType.Always, Action = ActionType.Wait,
                    Repeat = RepeatMode.WhileTrue, CooldownMs = 0, DelayAfterActionMs = 0 }] };
            try { randomEngine.RunAsync(profile, stop.Token).GetAwaiter().GetResult(); }
            catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
            Check(reactions == 1 && polls == 20,
                "Delayed rising-edge actions must fire once when still valid, even after the initial detection poll.");
        }
        Console.WriteLine("PASS: real engine keeps polling through reaction waits, cancels on condition loss/STOP, rearms on reappearance and handles delayed rising-edge actions; no keyboard input sent.");
        var rule = new MacroRule
        {
            Condition = ConditionType.Always, GateEnabled = true,
            GateCondition = ConditionType.RegionSnapshotMatches, GateWidth = 4, GateHeight = 4,
            GateReferenceRgb = [], Action = ActionType.KeyPress, Key = "Q",
            LastCondition = true, LastTriggeredUtc = DateTime.UtcNow, KeyHoldActive = true
        };
        var triggerTime = rule.LastTriggeredUtc;
        var notifications = 0;
        engine.InspectionUpdated += _ => notifications++;
        var inspected = engine.Inspect(rule, CancellationToken.None);
        Check(inspected.Primary.Passed == true && inspected.Gate?.Passed is null && !inspected.Passed,
            "The inspector must separate a passing main condition from an unavailable gate.");
        Check(!rule.ObservationValid && rule.CurrentMatch is null,
            "An unreadable gate must block execution and leave no usable target.");
        Check(rule.LastCondition && rule.KeyHoldActive && rule.LastTriggeredUtc == triggerTime && notifications == 0,
            "Inspection must not change trigger, held-key or cooldown state, or publish runtime events.");

        rule.Condition = ConditionType.RegionSnapshotMatches;
        rule.WatchWidth = rule.WatchHeight = 4;
        rule.ReferenceRgb = [];
        rule.GateCondition = ConditionType.Always;
        inspected = engine.Inspect(rule, CancellationToken.None);
        Check(inspected.Primary.Passed is null && inspected.Gate?.Passed == true,
            "Stopped inspection must check the gate independently even when the primary is unavailable.");
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        var cancelledCorrectly = false;
        try { engine.Inspect(rule, cancelled.Token); }
        catch (OperationCanceledException) { cancelledCorrectly = true; }
        Check(cancelledCorrectly, "A cancelled inspector must not start sampling.");
        Check(!JsonSerializer.Serialize(rule).Contains("Inspection"),
            "Runtime inspector observations must never be written into profile JSON.");

        var image = MainWindow.InspectorBitmap([255, 0, 0, 0, 255, 0], 2, 1);
        Check(image is { IsFrozen: true, PixelWidth: 2, PixelHeight: 1 }
            && MainWindow.InspectorBitmap([], 4, 4) is null,
            "Preview images must be immutable and reject invalid reference dimensions.");
        Check(!ScreenProbe.TryCapturePreview(0, 0, 4000, 3000, out _, out _, out _),
            "Invalid preview dimensions must be rejected before allocation.");
        Check(ScreenProbe.TryCapturePreview(0, 0, 1920, 1080, out var preview, out var pw, out var ph)
            && pw <= 320 && ph <= 180 && preview.Length == pw * ph * 3,
            "A large preview must be downscaled before allocating the RGB buffer.");
        Console.WriteLine("PASS: unavailable conditions fail closed, gates are independent, preview has no execution side effects, cancellation and bounded frozen images.");

        // Use the real resources with a plain Application. Pumping the
        // Dispatcher must not run ClickyBot's single-instance startup.
        var app = new Application();
        var resources = XDocument.Load(System.IO.Path.Combine(AppContext.BaseDirectory, "AppResources.xaml"))
            .Root!.Elements().Single(element => element.Name.LocalName == "Application.Resources");
        var dictionary = new XElement(XNamespace.Get("http://schemas.microsoft.com/winfx/2006/xaml/presentation") + "ResourceDictionary",
            new XAttribute(XNamespace.Xmlns + "x", "http://schemas.microsoft.com/winfx/2006/xaml"), resources.Nodes());
        app.Resources = (ResourceDictionary)XamlReader.Parse(dictionary.ToString());
        var window = new MainWindow();
        var rules = (ListBox)window.FindName("RulesListBox");
        var selected = (MacroRule)rules.SelectedItem;
        var originalName = selected.Name;
        ((TextBox)window.FindName("RuleNameBox")).Text = "Unapplied preview edit";
        var copy = new MacroRule();
        typeof(MainWindow).GetMethod("ReadEditorIntoRule", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(window, [copy]);
        Check(copy.Name == "Unapplied preview edit" && selected.Name == originalName,
            "Previewing editor values must not mutate the selected profile rule.");
        Check(!((Expander)window.FindName("InspectorExpander")).IsExpanded,
            "The inspector must be opt-in and collapsed by default.");
        Check(typeof(MainWindow).GetField("_inspectionTask", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(window) is null,
            "Constructing a hidden editor must not start an inspector worker.");
        Console.WriteLine("PASS: real WPF inspector is collapsed by default, starts no hidden worker and previews edits on a separate rule.");

        // Host the real view without MainWindow's startup/closing handlers:
        // no global hotkeys, updater, engine, profile saves or generated inputs.
        var content = (FrameworkElement)window.Content;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        window.Content = null;
        var host = new Window { Content = content, Width = 1440, Height = 920, Background = window.Background, Resources = window.Resources, ShowActivated = false };
        ((System.Windows.Controls.Grid)content).Background = window.Background;
        var profileRules = (ObservableCollection<MacroRule>)typeof(MainWindow)
            .GetField("_rules", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(window)!;
        profileRules.Clear();
        var first = new MacroRule { Name = "Inspector lifecycle check", Condition = ConditionType.Always };
        var second = new MacroRule { Name = "Second inspected rule", Condition = ConditionType.Always };
        profileRules.Add(first);
        profileRules.Add(second);
        rules.SelectedItem = first;
        host.Show();
        var expander = (Expander)window.FindName("InspectorExpander");
        expander.IsExpanded = true;
        var mainText = (TextBlock)window.FindName("InspectorPrimaryText");
        try { PumpUntil(() => mainText.Text.Contains("Passed")); }
        catch
        {
            Console.WriteLine($"Inspector visible={expander.IsVisible}, expanded={expander.IsExpanded}, mode={((TextBlock)window.FindName("InspectorModeText")).Text}, main={mainText.Text}");
            throw;
        }
        Check(!first.LastCondition && first.LastTriggeredUtc == DateTime.MinValue && !first.KeyHoldActive,
            "The real live inspector must not mutate the engine's selected rule state.");
        // Change selection while an existing loop is awaiting its next tick.
        rules.SelectedItem = second;
        PumpUntil(() => mainText.Text.Contains("Passed") && (string?)typeof(MainWindow).GetField("_inspectedRuleId", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(window) == second.Id.ToString());
        Check((string?)typeof(MainWindow).GetField("_inspectedRuleId", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(window) == second.Id.ToString(), "Selection changes must select only the new rule's observations.");
        var runningField = typeof(MainWindow).GetField("_isRunning", BindingFlags.NonPublic | BindingFlags.Instance)!;
        runningField.SetValue(window, true);
        typeof(MainWindow).GetMethod("RestartInspector", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(window, null);
        typeof(MainWindow).GetMethod("ReceiveInspection", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(window,
            [new RuleObservation(second.Id, DateTime.UtcNow, new(false, "Injected engine observation"), null, "Cooling down.", "HP requirement checked by combat")]);
        PumpUntil(() => mainText.Text.Contains("Injected engine observation"));
        Check(((TextBlock)window.FindName("InspectorActionText")).Text == "Cooling down."
            && ((TextBlock)window.FindName("InspectorContextText")).Text == "HP requirement checked by combat",
            "Running inspection must consume the engine's observation and context rather than reevaluate Always.");
        runningField.SetValue(window, false);
        typeof(MainWindow).GetMethod("RestartInspector", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(window, null);
        PumpUntil(() => mainText.Text.Contains("Passed"));
        if (args.Length > 0)
        {
            var live = (Image)window.FindName("InspectorLiveImage");
            var reference = (Image)window.FindName("InspectorReferenceImage");
            var rgb = new byte[96 * 64 * 3];
            for (var y = 0; y < 64; y++)
                for (var x = 0; x < 96; x++)
                {
                    var i = (y * 96 + x) * 3;
                    var lit = x >= 20 && x < 76 && y >= 12 && y < 52;
                    rgb[i] = lit ? (byte)50 : (byte)15;
                    rgb[i + 1] = lit ? (byte)205 : (byte)22;
                    rgb[i + 2] = lit ? (byte)140 : (byte)34;
                }
            live.Source = reference.Source = MainWindow.InspectorBitmap(rgb, 96, 64);
            ((TextBlock)window.FindName("InspectorCaptureText")).Text = "Area 984,990 · 58 × 58 px. Preview is scaled.";
            mainText.Text = "Main: Passed · Best image similarity 96.4% (requires 90%).";
            ((TextBlock)window.FindName("InspectorGateText")).Text = "AND gate: Waiting · Colour coverage 87% · requires at most 82%.";
            ((TextBlock)window.FindName("InspectorActionText")).Text = "Preview only — no input sent. Conditions do not include runtime cooldowns or priorities.";
            host.UpdateLayout();
            var bitmap = new RenderTargetBitmap((int)content.ActualWidth, (int)content.ActualHeight, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(content);
            var png = new PngBitmapEncoder();
            png.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = System.IO.File.Create(args[0]);
            png.Save(stream);
        }
        expander.IsExpanded = false;
        PumpUntil(() => ((Task?)typeof(MainWindow).GetField("_inspectionTask", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(window))?.IsCompleted == true);
        Check(typeof(MainWindow).GetField("_inspectionCancellation", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(window) is null, "Collapsing the inspector must cancel and dispose its worker.");
        expander.IsExpanded = true;
        PumpUntil(() => mainText.Text.Contains("Passed"));
        ((Expander)window.FindName("AutomationMapExpander")).IsExpanded = false;
        PumpUntil(() => ((Task?)typeof(MainWindow).GetField("_inspectionTask", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(window))?.IsCompleted == true);
        Check(typeof(MainWindow).GetField("_inspectionCancellation", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(window) is null, "Hiding the inspector by collapsing its parent must also cancel work.");
        host.Close();
        Console.WriteLine("PASS: actual inspector refresh, rule selection, collapse cancellation and profile state preservation without registering hotkeys or running actions.");
    }

    private static void PumpUntil(Func<bool> condition)
    {
        var frame = new DispatcherFrame();
        var started = DateTime.UtcNow;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(10) };
        timer.Tick += (_, _) => { if (condition() || DateTime.UtcNow - started > TimeSpan.FromSeconds(8)) frame.Continue = false; };
        timer.Start();
        Dispatcher.PushFrame(frame);
        timer.Stop();
        if (!condition()) throw new Exception("Inspector lifecycle check timed out.");
    }
}
