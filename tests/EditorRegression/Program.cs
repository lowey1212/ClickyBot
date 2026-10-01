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
