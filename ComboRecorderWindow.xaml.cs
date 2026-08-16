using System.Collections.ObjectModel;
using System.Windows;

namespace ClickyBot;

public partial class ComboRecorderWindow : Window
{
    private readonly InputRecorder _recorder = new();
    private bool _isRecording;

    public ObservableCollection<RecordedStep> Steps { get; }
    public IReadOnlyList<RecordedStep> ResultSteps => Steps.ToList();

    public ComboRecorderWindow(IEnumerable<RecordedStep> initialSteps)
    {
        InitializeComponent();
        Steps = new ObservableCollection<RecordedStep>(initialSteps.Select(CloneStep));
        StepsListBox.ItemsSource = Steps;
        _recorder.StepRecorded += step => Dispatcher.BeginInvoke(() => AddStep(step));
        _recorder.StopRequested += () => Dispatcher.BeginInvoke(() => StopRecording("Stopped with F7."));
        RenumberSteps();
        UpdateSummary();
    }

    private void StartRecordingButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isRecording)
        {
            StopRecording("Recording stopped.");
            return;
        }

        Steps.Clear();
        try
        {
            _recorder.Start();
            _isRecording = true;
            StartRecordingButton.Content = "RECORDING…";
            StopRecordingButton.IsEnabled = true;
            RecordingStatusText.Text = "Recording — input passes through";
            RecordingStatusText.Foreground = (System.Windows.Media.Brush)FindResource("WarningBrush");
            UpdateSummary();
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            RecordingStatusText.Text = ex.Message;
        }
    }

    private void StopRecordingButton_Click(object sender, RoutedEventArgs e) => StopRecording("Recording stopped.");

    private void StopRecording(string message)
    {
        _recorder.Stop();
        _isRecording = false;
        StartRecordingButton.Content = "RECORD / REPLACE";
        StopRecordingButton.IsEnabled = false;
        RecordingStatusText.Text = message;
        RecordingStatusText.Foreground = (System.Windows.Media.Brush)FindResource("MutedTextBrush");
        UpdateSummary();
    }

    private void AddStep(RecordedStep step)
    {
        if (!_isRecording)
        {
            return;
        }

        if (Steps.Count == 0)
        {
            step.DelayBeforeMs = 0;
        }
        else if (UseDefaultDelayCheckBox.IsChecked == true)
        {
            step.DelayBeforeMs = ReadDefaultDelay();
        }

        Steps.Add(step);
        RenumberSteps();
        StepsListBox.ScrollIntoView(step);
        UpdateSummary();
    }

    private void ClearAllButton_Click(object sender, RoutedEventArgs e)
    {
        Steps.Clear();
        RenumberSteps();
        UpdateSummary();
    }

    private void ApplyDelayToAllButton_Click(object sender, RoutedEventArgs e)
    {
        var delay = ReadDefaultDelay();
        for (var index = 0; index < Steps.Count; index++)
        {
            Steps[index].DelayBeforeMs = index == 0 ? 0 : delay;
        }
        RenumberSteps();
        StepsListBox.Items.Refresh();
    }

    private void ApplyButton_Click(object sender, RoutedEventArgs e)
    {
        StopRecording("Combo applied.");
        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        StopRecording("Combo changes cancelled.");
        DialogResult = false;
    }

    private int ReadDefaultDelay()
    {
        return int.TryParse(DefaultDelayBox.Text, out var value) ? Math.Clamp(value, 0, 60000) : 100;
    }

    private void UpdateSummary()
    {
        StepCountText.Text = $"{Steps.Count} step{(Steps.Count == 1 ? "" : "s")}";
    }

    private void RenumberSteps()
    {
        for (var index = 0; index < Steps.Count; index++)
        {
            Steps[index].SequenceNumber = index + 1;
        }
    }

    private static RecordedStep CloneStep(RecordedStep step) => new()
    {
        Type = step.Type,
        Key = step.Key,
        ClickX = step.ClickX,
        ClickY = step.ClickY,
        MouseButton = step.MouseButton,
        DelayBeforeMs = step.DelayBeforeMs
    };

    protected override void OnClosed(EventArgs e)
    {
        _recorder.Dispose();
        base.OnClosed(e);
    }
}
