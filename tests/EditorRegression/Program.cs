using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Text.Json;
using ClickyBot;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        // Construct the real editor without showing a window, starting the
        // engine, registering hotkeys, saving settings, or sending input.
        var app = new App();
        app.InitializeComponent();
        var window = new MainWindow();
        T Control<T>(string name) where T : class => (T)window.FindName(name);
        void Check(bool value, string message)
        {
            if (!value) throw new Exception(message);
        }
        var imageRule = new MacroRule
        {
            Condition = ConditionType.RegionSnapshotMatches,
            SearchReference = true, SearchX = 773, SearchY = 198, SearchWidth = 281, SearchHeight = 171,
            WatchWidth = 29, WatchHeight = 43, ReferenceRgb = new byte[29 * 43 * 3],
            ReferenceImagePath = "reference.png", Action = ActionType.MouseMove, MouseTarget = MouseTargetType.MatchedLocation
        };
        typeof(MainWindow).GetMethod("LoadEditor", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [imageRule]);
        Check(Control<StackPanel>("PixelWatchPanel").Visibility == Visibility.Collapsed,
            "Image rules must not show the second watch-location selector.");
        Check(Control<StackPanel>("ImageSearchPanel").Visibility == Visibility.Visible,
            "Image rules must show the single watch area and reference controls.");
        Check(window.FindName("SearchReferenceCheckBox") is null,
            "The confusing opt-in image search checkbox must be removed.");
        Check(Control<TextBox>("SearchWidthBox").Text == "281"
            && Control<TextBlock>("ReferenceSummaryText").Text.Contains("29 × 43"),
            "Watch area and reference dimensions must stay separate.");
        Check(Control<StackPanel>("FixedMouseTargetPanel").Visibility == Visibility.Collapsed,
            "Matched-location actions must not ask for unrelated fixed click coordinates.");
        Control<ComboBox>("ConditionCombo").SelectedItem = ConditionType.PixelMatches;
        Check(Control<StackPanel>("PixelWatchPanel").Visibility == Visibility.Visible
            && Control<StackPanel>("ImageSearchPanel").Visibility == Visibility.Collapsed,
            "Pixel conditions must still have their original location controls.");
        Console.WriteLine("PASS: real WPF image editor has one area selector, separate reference details, and no fixed target fields for matched-location actions.");

        var originalSteps = new[]
        {
            new RecordedStep { Type = RecordedStepType.KeyDown, Key = "Q", DelayBeforeMs = 0 },
            new RecordedStep { Type = RecordedStepType.MouseClick, ClickX = 100, ClickY = 200, DelayBeforeMs = 125 },
            new RecordedStep { Type = RecordedStepType.KeyUp, Key = "Q", DelayBeforeMs = 350 }
        };
        var combo = new ComboRecorderWindow(originalSteps);
        var list = (ListBox)combo.FindName("StepsListBox");
        Button DeleteButton(RecordedStep step)
        {
            var row = (FrameworkElement)list.ItemTemplate.LoadContent();
            row.DataContext = step;
            return Descendants(row).OfType<Button>().Single(button => Equals(button.Content, "DELETE"));
        }
        var middleDelete = DeleteButton(combo.Steps[1]);
        middleDelete.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(combo.Steps.Count == 2 && combo.Steps[0].Type == RecordedStepType.KeyDown
            && combo.Steps[1].Type == RecordedStepType.KeyUp && combo.Steps[1].DelayBeforeMs == 350,
            "Deleting a middle row must preserve the other input events, their order, and custom delays.");
        Check(combo.Steps.Select(step => step.SequenceNumber).SequenceEqual(new[] { 1, 2 })
            && ((TextBlock)combo.FindName("StepCountText")).Text == "2 steps",
            "Deleting a row must update sequence numbers and the visible count.");
        middleDelete.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(combo.Steps.Count == 2, "A stale row must not delete another step.");
        Check(originalSteps.Length == 3 && originalSteps[2].SequenceNumber == 0,
            "Combo edits must leave the original rule untouched until applied.");
        var savedSteps = JsonSerializer.Deserialize<List<RecordedStep>>(JsonSerializer.Serialize(combo.ResultSteps))!;
        Check(savedSteps.Count == 2 && savedSteps[1].DelayBeforeMs == 350,
            "Applied and serialized results must omit the deleted row and retain custom delays.");
        DeleteButton(combo.Steps[0]).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(combo.Steps.Single().SequenceNumber == 1 && combo.Steps[0].DelayBeforeMs == 350
            && ((TextBlock)combo.FindName("StepCountText")).Text == "1 step",
            "Deleting the first row must renumber the survivor without changing its delay.");
        DeleteButton(combo.Steps[0]).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Check(combo.ResultSteps.Count == 0 && ((TextBlock)combo.FindName("StepCountText")).Text == "0 steps",
            "Deleting the last row must produce an empty combo.");
        combo.Close();
        Console.WriteLine("PASS: real combo row buttons delete middle, first, and last steps while preserving delays and original rule data.");

        var legacyE = new MacroRule { Name = "Original E", Key = "E", CooldownMs = 20 };
        var profile = new MacroProfile { Game = "Throne", Rules = [legacyE, new MacroRule { Key = "1", CooldownMs = 20 }] };
        var referenceFolder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ClickyBot-Throne-" + Guid.NewGuid());
        var assembly = typeof(MainWindow).Assembly;
        assembly.GetType("ClickyBot.ThroneProfileSetup")!.GetMethod("Configure", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, [profile, referenceFolder, new ScreenSelection(850, 800, 300, 150), new ScreenSelection(600, 250, 500, 400)]);
        Check(profile.ThroneCombatMode && profile.Rules.Single(rule => rule.Key == "1").Condition == ConditionType.Always,
            "Throne setup must tap 1 continuously without ready images.");
        var vRule = profile.Rules.Single(rule => rule.Key == "V");
        var qRule = profile.Rules.Single(rule => rule.Key == "Q");
        Check(vRule.ReferenceRgb.Length == 18 * 18 * 3 && System.IO.File.Exists(vRule.ReferenceImagePath)
            && vRule.SearchX == 850 && vRule.ImageMatchMethod == ImageMatchMethod.PixelColors,
            "Setup must extract the bundled V reference and preserve the selected prompt area.");
        Check(qRule.Condition == ConditionType.PurpleRingMatches && qRule.WatchX == 600 && qRule.WatchWidth == 500
            && qRule.ReferenceRgb.Length == 0 && profile.Rules.Contains(legacyE) && legacyE.CooldownMs == 20,
            "Q needs a ring watch area without a reference; existing unrelated rules must be preserved.");
        typeof(MainWindow).GetMethod("LoadEditor", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [qRule]);
        Check(Control<StackPanel>("PixelWatchPanel").Visibility == Visibility.Visible
            && Control<StackPanel>("ImageSearchPanel").Visibility == Visibility.Collapsed
            && Control<TextBox>("CoverageThresholdBox").Visibility == Visibility.Collapsed,
            "Q editor must show a watch area and ring help without irrelevant reference/threshold fields.");
        Control<System.Windows.Controls.ComboBox>("GameCombo").Text = "Throne";
        typeof(MainWindow).GetMethod("UpdatePaxResourceOptions", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null);
        Check(Control<StackPanel>("ThroneOptionsPanel").Visibility == Visibility.Visible,
            "Throne profiles must show combat setup controls.");
        Control<System.Windows.Controls.ComboBox>("GameCombo").Text = "soulframe";
        typeof(MainWindow).GetMethod("UpdatePaxResourceOptions", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null);
        Check(Control<StackPanel>("ThroneOptionsPanel").Visibility == Visibility.Collapsed,
            "Other games must hide Throne setup controls.");
        var healingFolder = System.IO.Path.Combine(referenceFolder, "healing");
        assembly.GetType("ClickyBot.ThroneProfileSetup")!.GetMethod("ConfigureHealing", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, [profile, healingFolder]);
        Check(profile.ThroneHealing.Enabled && profile.ThroneHealing.LowHpPercent == 82
            && profile.ThroneHealing.HealthReferenceRgb.Length == 224 * 18 * 3
            && profile.Rules.Single(rule => rule.Key == "7").ReferenceRgb.Length == 32 * 32 * 3
            && profile.Rules.Single(rule => rule.Key == "7").ThroneHealingOnly
            && profile.Rules.Single(rule => rule.Key == "8").CooldownMs == 1000,
            "Healing setup must load the HP frame and independent ready icons with visual cooldown protection.");
        Check(profile.Rules.Contains(legacyE) && profile.Rules.Contains(qRule) && profile.Rules.Contains(vRule)
            && vRule.SearchX == 850 && qRule.WatchX == 600,
            "Adding healing must preserve the existing defence/chain watch areas and unrelated rules.");
        foreach (var file in System.IO.Directory.EnumerateFiles(healingFolder)) System.IO.File.Delete(file);
        System.IO.Directory.Delete(healingFolder);
        System.IO.File.Delete(vRule.ReferenceImagePath);
        System.IO.Directory.Delete(referenceFolder);
        Console.WriteLine("PASS: real WPF Throne setup, bundled V reference, Q watch editor, game-specific controls and preservation of original rules.");
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
}
