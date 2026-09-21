using System.Reflection;
using System.Windows;
using System.Windows.Controls;
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
    }
}
