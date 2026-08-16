using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;

namespace ClickyBot;

public partial class MainWindow : Window
{
    private const int ToggleHotKeyId = 1;
    private const int PanicHotKeyId = 2;
    private const int CapturePixelHotKeyId = 3;
    private const int CaptureClickHotKeyId = 4;
    private const int CaptureGateHotKeyId = 5;

    private readonly ObservableCollection<MacroRule> _rules = [];
    private readonly ObservableCollection<string> _macroNames = [];
    private readonly MacroEngine _engine = new();
    private AppSettings _settings;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private MacroProfile _profile = new();
    private CancellationTokenSource? _engineCancellation;
    private Task? _engineTask;
    private HwndSource? _windowSource;
    private bool _isRunning;
    private int _runGeneration;
    private readonly Queue<string> _logLines = new();
    private const int MaxLogLines = 600;
    private byte[] _watchReferenceRgb = [];
    private byte[] _gateReferenceRgb = [];
    private string _watchReferenceImagePath = "";
    private string _gateReferenceImagePath = "";
    private string? _currentMacroPath;
    private bool _updateBusy;
    private CancellationTokenSource? _updateCancellation;

    public MainWindow()
    {
        _settings = AppSettingsStore.Load();
        InitializeComponent();
        RulesListBox.ItemsSource = _rules;
        ProfileNameCombo.ItemsSource = _macroNames;
        ConditionCombo.ItemsSource = Enum.GetValues<ConditionType>();
        GateConditionCombo.ItemsSource = Enum.GetValues<ConditionType>();
        ActionCombo.ItemsSource = Enum.GetValues<ActionType>();
        RepeatCombo.ItemsSource = Enum.GetValues<RepeatMode>();
        MouseButtonCombo.ItemsSource = Enum.GetValues<MouseButtonType>();
        _engine.Log += message => Dispatcher.BeginInvoke(() => AppendLog(message));
        LoadStarterProfile();
        RefreshMacroList(_profile.Name);
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        AppendLog("Ready. Load the starter profile or add a rule.");
        UpdateStatus(false);
        if (_settings.CheckForUpdatesOnStartup)
        {
            _ = CheckForUpdatesAsync(silent: true);
        }
    }

    private void Window_SourceInitialized(object? sender, EventArgs e)
    {
        _windowSource = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
        _windowSource?.AddHook(WindowMessageHook);
        var handle = new WindowInteropHelper(this).Handle;
        var hotkeys = new[]
        {
            (Id: ToggleHotKeyId, Key: NativeMethods.VkF6, Modifiers: NativeMethods.ModNoRepeat, Name: "F6 start/stop"),
            (Id: PanicHotKeyId, Key: NativeMethods.VkF7, Modifiers: NativeMethods.ModNoRepeat, Name: "F7 panic stop"),
            (Id: CapturePixelHotKeyId, Key: NativeMethods.VkF8, Modifiers: NativeMethods.ModNoRepeat, Name: "F8 watch-area selection"),
            (Id: CaptureClickHotKeyId, Key: NativeMethods.VkF9, Modifiers: NativeMethods.ModNoRepeat, Name: "F9 click-target selection"),
            (Id: CaptureGateHotKeyId, Key: NativeMethods.VkF8, Modifiers: NativeMethods.ModControl | NativeMethods.ModNoRepeat, Name: "Ctrl+F8 gate-area selection")
        };

        foreach (var hotkey in hotkeys)
        {
            if (!NativeMethods.RegisterHotKey(handle, hotkey.Id, hotkey.Modifiers, hotkey.Key))
            {
                AppendLog($"Could not reserve {hotkey.Name}; another app may already own it.");
            }
        }
    }

    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _updateCancellation?.Cancel();
        StopEngine("Stopped before closing.");
        var handle = new WindowInteropHelper(this).Handle;
        NativeMethods.UnregisterHotKey(handle, ToggleHotKeyId);
        NativeMethods.UnregisterHotKey(handle, PanicHotKeyId);
        NativeMethods.UnregisterHotKey(handle, CapturePixelHotKeyId);
        NativeMethods.UnregisterHotKey(handle, CaptureClickHotKeyId);
        NativeMethods.UnregisterHotKey(handle, CaptureGateHotKeyId);
        _windowSource?.RemoveHook(WindowMessageHook);
    }

    private async void UpdateButton_Click(object sender, RoutedEventArgs e) => await CheckForUpdatesAsync(silent: false);

    private async Task CheckForUpdatesAsync(bool silent)
    {
        if (_updateBusy)
        {
            return;
        }

        _updateBusy = true;
        _updateCancellation?.Dispose();
        _updateCancellation = new CancellationTokenSource();
        UpdateButton.IsEnabled = false;
        UpdateButton.Content = "CHECKING…";
        try
        {
            var update = await UpdateService.CheckAsync(_updateCancellation.Token);
            if (update is null)
            {
                AppendLog($"ClickyBot is up to date ({UpdateService.CurrentVersion}).");
                if (!silent)
                {
                    MessageBox.Show(this, $"You are running the latest ClickyBot release ({UpdateService.CurrentVersion}).", "No update available", MessageBoxButton.OK, MessageBoxImage.Information);
                }

                return;
            }

            AppendLog($"ClickyBot update available: {update.LatestVersion}.");
            var answer = MessageBox.Show(
                this,
                $"ClickyBot {update.LatestVersion} is available. Download and install it now?\n\nThe app will close, install the update, and reopen.",
                "ClickyBot update available",
                MessageBoxButton.YesNo,
                MessageBoxImage.Information);
            if (answer != MessageBoxResult.Yes)
            {
                return;
            }

            UpdateButton.Content = "DOWNLOADING…";
            var installerPath = await UpdateService.DownloadInstallerAsync(update, _updateCancellation.Token);
            AppendLog($"Downloaded {update.AssetName}. ClickyBot will restart to install it.");
            if (!UpdateService.StartInstallerAfterExit(installerPath))
            {
                throw new InvalidOperationException("The downloaded installer could not be started.");
            }

            _updateBusy = false;
            Application.Current.Shutdown();
        }
        catch (OperationCanceledException)
        {
            AppendLog("Update check cancelled.");
        }
        catch (Exception ex)
        {
            AppendLog($"Update check failed: {ex.Message}");
            if (!silent)
            {
                MessageBox.Show(this, ex.Message, "Could not check for updates", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        finally
        {
            if (!_updateBusy || !IsVisible)
            {
                UpdateButton.IsEnabled = true;
                UpdateButton.Content = "CHECK FOR UPDATES";
            }
            _updateBusy = false;
        }
    }

    private IntPtr WindowMessageHook(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == NativeMethods.WmHotKey)
        {
            switch (wParam.ToInt32())
            {
                case ToggleHotKeyId:
                    ToggleEngine();
                    handled = true;
                    break;
                case PanicHotKeyId:
                    StopEngine("Panic stop pressed.");
                    handled = true;
                    break;
                case CapturePixelHotKeyId:
                    SelectWatchArea(sampleColor: true);
                    handled = true;
                    break;
                case CaptureClickHotKeyId:
                    CaptureClickTarget();
                    handled = true;
                    break;
                case CaptureGateHotKeyId:
                    SelectGateArea(sampleColor: true);
                    handled = true;
                    break;
            }
        }

        return IntPtr.Zero;
    }

    private void StartStopButton_Click(object sender, RoutedEventArgs e) => ToggleEngine();

    private void PanicStopButton_Click(object sender, RoutedEventArgs e) => StopEngine("Panic stop pressed.");

    private void ToggleEngine()
    {
        if (_isRunning)
        {
            StopEngine("Stopped.");
        }
        else
        {
            StartEngine();
        }
    }

    private void StartEngine()
    {
        if (_engineTask is { IsCompleted: false })
        {
            AppendLog("Still stopping the previous run. Try START again in a moment.");
            return;
        }

        ApplyEditorToSelectedRule();
        ApplyProfileEditorToModel();
        if (NativeMethods.GetForegroundWindow() == new WindowInteropHelper(this).Handle)
        {
            AppendLog("Warning: ClickyBot is the foreground window. Start with F6 while the game is focused so key input goes to the game.");
        }
        if (_profile.Rules.All(rule => !rule.Enabled))
        {
            AppendLog("No enabled rules. Enable at least one rule before starting.");
            return;
        }

        _engineCancellation = new CancellationTokenSource();
        var runGeneration = ++_runGeneration;
        _isRunning = true;
        UpdateStatus(true);
        AppendLog("Engine started. F7 is the emergency stop.");
        _engineTask = RunEngineAsync(_engineCancellation.Token, runGeneration);
    }

    private async Task RunEngineAsync(CancellationToken token, int runGeneration)
    {
        try
        {
            await _engine.RunAsync(_profile, token);
        }
        catch (OperationCanceledException)
        {
            // Normal stop.
        }
        catch (Exception ex)
        {
            await Dispatcher.BeginInvoke(() => AppendLog($"Engine stopped after an error: {ex.Message}"));
        }
        finally
        {
            // A cancellation can interrupt a recorded combo between a key-down
            // and its matching key-up. Release only keys generated by ClickyBot.
            InputSimulator.ReleaseAllHeldInputs();
            await Dispatcher.BeginInvoke(() =>
            {
                if (runGeneration == _runGeneration)
                {
                    _isRunning = false;
                    UpdateStatus(false);
                }
            });
        }
    }

    private void StopEngine(string message)
    {
        _runGeneration++;
        _engineCancellation?.Cancel();
        _engineCancellation = null;
        InputSimulator.ReleaseAllHeldInputs();
        _isRunning = false;
        UpdateStatus(false);
        if (!string.IsNullOrWhiteSpace(message))
        {
            AppendLog(message);
        }
    }

    private void UpdateStatus(bool running)
    {
        StatusText.Text = running ? "Running" : "Idle";
        StatusText.Foreground = running ? (Brush)FindResource("AccentBrush") : (Brush)FindResource("MutedTextBrush");
        StatusDot.Fill = running ? (Brush)FindResource("AccentBrush") : (Brush)FindResource("MutedTextBrush");
        StartStopButton.Content = running ? "STOP  F6" : "START  F6";
        StartStopButton.Style = (Style)FindResource(running ? "DangerButton" : "AccentButton");
    }

    private void NewProfileButton_Click(object sender, RoutedEventArgs e)
    {
        StopEngine("Profile reset.");
        _profile = new MacroProfile { Name = "Untitled profile" };
        _currentMacroPath = null;
        ProfileNameCombo.Text = _profile.Name;
        PollIntervalBox.Text = _profile.PollIntervalMs.ToString();
        _rules.Clear();
        AddRuleToCollection(new MacroRule { Name = "New rule" });
        AppendLog("Created a blank profile.");
    }

    private void LoadStarterButton_Click(object sender, RoutedEventArgs e)
    {
        StopEngine("Loaded starter profile.");
        LoadStarterProfile();
        _currentMacroPath = null;
        RefreshMacroList(_profile.Name);
        AppendLog("Loaded starter rules. Point F8/F9 at your game UI to replace the example coordinates.");
    }

    private void LoadStarterProfile()
    {
        _profile = new MacroProfile
        {
            Name = "Fishing + resource-aware skills",
            PollIntervalMs = 80,
            Rules =
            [
                new MacroRule
                {
                    Name = "Cast skill when ready + mana is high",
                    Condition = ConditionType.PixelMatches,
                    WatchX = 100,
                    WatchY = 100,
                    TargetRed = 255,
                    TargetGreen = 212,
                    TargetBlue = 64,
                    Tolerance = 25,
                    Action = ActionType.KeyPress,
                    Key = "1",
                    GateEnabled = true,
                    GateCondition = ConditionType.RegionCoverageAtLeast,
                    GateX = 200,
                    GateY = 200,
                    GateWidth = 120,
                    GateHeight = 10,
                    GateTargetRed = 60,
                    GateTargetGreen = 120,
                    GateTargetBlue = 240,
                    GateTolerance = 35,
                    GateCoverageThreshold = 60,
                    Repeat = RepeatMode.OnRisingEdge,
                    CooldownMs = 800
                },
                new MacroRule
                {
                    Name = "Fishing bite event",
                    Condition = ConditionType.PixelMatches,
                    WatchX = 300,
                    WatchY = 300,
                    TargetRed = 255,
                    TargetGreen = 255,
                    TargetBlue = 255,
                    Tolerance = 30,
                    Action = ActionType.MouseClick,
                    ClickX = 300,
                    ClickY = 300,
                    Repeat = RepeatMode.OnRisingEdge,
                    CooldownMs = 1000
                }
            ]
        };

        ProfileNameCombo.Text = _profile.Name;
        PollIntervalBox.Text = _profile.PollIntervalMs.ToString();
        _rules.Clear();
        foreach (var rule in _profile.Rules)
        {
            _rules.Add(rule);
        }

        RulesListBox.SelectedIndex = _rules.Count > 0 ? 0 : -1;
        UpdateRuleCount();
    }

    private void OpenMacroButton_Click(object sender, RoutedEventArgs e)
    {
        var requestedName = ProfileNameCombo.Text.Trim();
        var path = ResolveMacroPath(requestedName);
        if (path is null)
        {
            RefreshMacroList(requestedName);
            MessageBox.Show(this, "Choose a macro from the dropdown or type the name of a JSON macro in the configured macro folder.", "Macro not found", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            var json = File.ReadAllText(path);
            var loaded = JsonSerializer.Deserialize<MacroProfile>(json, _jsonOptions) ?? throw new InvalidDataException("The file did not contain a profile.");
            StopEngine("Loaded macro.");
            _profile = loaded;
            _profile.Name = MacroDisplayName(path);
            _currentMacroPath = path;
            HydrateProfileReferences(_profile);
            ProfileNameCombo.Text = _profile.Name;
            PollIntervalBox.Text = _profile.PollIntervalMs.ToString();
            _rules.Clear();
            foreach (var rule in _profile.Rules)
            {
                _rules.Add(rule);
            }
            RulesListBox.SelectedIndex = _rules.Count > 0 ? 0 : -1;
            UpdateRuleCount();
            RefreshMacroList(_profile.Name);
            AppendLog($"Opened {Path.GetFileName(path)}.");
        }
        catch (Exception ex) when (ex is IOException or JsonException or InvalidDataException)
        {
            MessageBox.Show(this, ex.Message, "Could not open macro", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void SaveMacroButton_Click(object sender, RoutedEventArgs e)
    {
        ApplyEditorToSelectedRule();
        ApplyProfileEditorToModel();
        SaveMacroToPath(CurrentSavePath(), "Saved macro");
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        var window = new SettingsWindow(_settings) { Owner = this };
        if (window.ShowDialog() == true)
        {
            _settings = window.Settings;
            _currentMacroPath = null;
            RefreshMacroList(_profile.Name);
            AppendLog($"Reference images: {_settings.ReferenceImageFolder}; macros: {_settings.MacroFolder}.");
        }
    }

    private void ApplyProfileEditorToModel()
    {
        _profile.Name = MacroDisplayName(ProfileNameCombo.Text);
        _profile.PollIntervalMs = ReadInt(PollIntervalBox, 80, 20, 2000);
        _profile.Rules = _rules.ToList();
    }

    private void PersistCurrentMacro()
    {
        if (string.IsNullOrWhiteSpace(_currentMacroPath))
        {
            AppendLog("Applied changes to the current editor. Use SAVE MACRO to create a JSON file.");
            return;
        }

        SaveMacroToPath(CurrentSavePath(), "Updated macro");
    }

    private string CurrentSavePath()
    {
        var desiredName = MacroDisplayName(_profile.Name);
        if (!string.IsNullOrWhiteSpace(_currentMacroPath)
            && string.Equals(MacroDisplayName(_currentMacroPath), desiredName, StringComparison.OrdinalIgnoreCase)
            && string.Equals(Path.GetFullPath(Path.GetDirectoryName(_currentMacroPath) ?? ""), Path.GetFullPath(_settings.MacroFolder), StringComparison.OrdinalIgnoreCase))
        {
            return _currentMacroPath;
        }

        return Path.Combine(_settings.MacroFolder, MacroFileName(desiredName));
    }

    private bool SaveMacroToPath(string path, string logVerb)
    {
        try
        {
            Directory.CreateDirectory(_settings.MacroFolder);
            File.WriteAllText(path, JsonSerializer.Serialize(_profile, _jsonOptions));
            _currentMacroPath = path;
            ProfileNameCombo.Text = MacroDisplayName(path);
            RefreshMacroList(ProfileNameCombo.Text);
            AppendLog($"{logVerb} {Path.GetFileName(path)}.");
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, ex.Message, "Could not save macro", MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
    }

    private void RefreshMacroList(string? preferredName = null)
    {
        var currentText = preferredName ?? ProfileNameCombo.Text;
        _macroNames.Clear();
        try
        {
            if (Directory.Exists(_settings.MacroFolder))
            {
                foreach (var path in Directory.EnumerateFiles(_settings.MacroFolder, "*.json", SearchOption.TopDirectoryOnly).OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
                {
                    var displayName = MacroDisplayName(path);
                    if (!_macroNames.Contains(displayName, StringComparer.OrdinalIgnoreCase))
                    {
                        _macroNames.Add(displayName);
                    }
                }
            }
        }
        catch (IOException ex)
        {
            AppendLog($"Could not list macros: {ex.Message}");
        }

        if (!string.IsNullOrWhiteSpace(currentText))
        {
            ProfileNameCombo.Text = currentText;
        }
    }

    private string? ResolveMacroPath(string requestedName)
    {
        if (string.IsNullOrWhiteSpace(requestedName) || !Directory.Exists(_settings.MacroFolder))
        {
            return null;
        }

        var fileName = Path.GetFileName(requestedName.Trim());
        string[] candidates = fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
            ? [fileName]
            : [$"{fileName}.clicky.json", $"{fileName}.json"];
        foreach (var candidate in candidates)
        {
            var path = Path.Combine(_settings.MacroFolder, candidate);
            if (File.Exists(path))
            {
                return path;
            }
        }

        return null;
    }

    private static string MacroFileName(string profileName) => $"{SanitizeFileName(MacroDisplayName(profileName))}.clicky.json";

    private static string MacroDisplayName(string pathOrName)
    {
        var name = Path.GetFileName(pathOrName.Trim());
        if (name.EndsWith(".clicky.json", StringComparison.OrdinalIgnoreCase))
        {
            name = name[..^".clicky.json".Length];
        }
        else if (name.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            name = name[..^".json".Length];
        }

        return string.IsNullOrWhiteSpace(name) ? "Untitled profile" : name;
    }

    private static string SanitizeFileName(string name)
    {
        var invalidCharacters = Path.GetInvalidFileNameChars();
        var sanitized = new string(name.Select(character => invalidCharacters.Contains(character) ? '-' : character).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(sanitized) ? "Untitled-profile" : sanitized;
    }

    private void AddRuleButton_Click(object sender, RoutedEventArgs e)
    {
        ApplyEditorToSelectedRule();
        AddRuleToCollection(new MacroRule { Name = $"Rule {_rules.Count + 1}" });
    }

    private void AddRuleToCollection(MacroRule rule)
    {
        _rules.Add(rule);
        RulesListBox.SelectedItem = rule;
        UpdateRuleCount();
    }

    private void ApplyRuleButton_Click(object sender, RoutedEventArgs e)
    {
        ApplyEditorToSelectedRule();
        ApplyProfileEditorToModel();
        RulesListBox.Items.Refresh();
        AppendLog("Applied rule changes.");
        PersistCurrentMacro();
    }

    private void DeleteRuleButton_Click(object sender, RoutedEventArgs e)
    {
        if (RulesListBox.SelectedItem is not MacroRule selected)
        {
            return;
        }

        var index = RulesListBox.SelectedIndex;
        _rules.Remove(selected);
        if (_rules.Count > 0)
        {
            RulesListBox.SelectedIndex = Math.Min(index, _rules.Count - 1);
        }
        else
        {
            LoadEditor(null);
        }
        UpdateRuleCount();
        AppendLog($"Deleted rule '{selected.Name}'.");
    }

    private void DuplicateRuleButton_Click(object sender, RoutedEventArgs e)
    {
        if (RulesListBox.SelectedItem is not MacroRule selected)
        {
            AppendLog("Select a rule before duplicating it.");
            return;
        }

        ApplyEditorToSelectedRule();
        var clone = CloneRule(selected);
        clone.Name = $"{selected.Name} copy";
        var index = _rules.IndexOf(selected);
        _rules.Insert(index + 1, clone);
        RulesListBox.SelectedItem = clone;
        UpdateRuleCount();
        AppendLog($"Duplicated rule '{selected.Name}'.");
    }

    private void MoveRuleUpButton_Click(object sender, RoutedEventArgs e) => MoveSelectedRule(-1);

    private void MoveRuleDownButton_Click(object sender, RoutedEventArgs e) => MoveSelectedRule(1);

    private void MoveSelectedRule(int offset)
    {
        if (RulesListBox.SelectedItem is not MacroRule selected)
        {
            AppendLog("Select a rule before moving it.");
            return;
        }

        var oldIndex = _rules.IndexOf(selected);
        var newIndex = oldIndex + offset;
        if (oldIndex < 0 || newIndex < 0 || newIndex >= _rules.Count)
        {
            return;
        }

        _rules.Move(oldIndex, newIndex);
        RulesListBox.SelectedItem = selected;
        AppendLog($"Moved rule '{selected.Name}' to position {newIndex + 1}.");
    }

    private void TestConditionButton_Click(object sender, RoutedEventArgs e)
    {
        if (RulesListBox.SelectedItem is not MacroRule rule)
        {
            TestResultText.Text = "Select a rule first.";
            AppendLog("Select a rule before testing its condition.");
            return;
        }

        ApplyEditorToSelectedRule();
        try
        {
            var result = _engine.EvaluateNow(rule);
            TestResultText.Text = result ? "PASS · action would run" : "FALSE · action would not run";
            TestResultText.Foreground = (Brush)FindResource(result ? "AccentBrush" : "MutedTextBrush");
            AppendLog($"Tested '{rule.Name}': condition {(result ? "passed" : "did not pass")}.");
        }
        catch (OperationCanceledException)
        {
            TestResultText.Text = "Cancelled";
        }
        catch (Exception ex)
        {
            TestResultText.Text = "ERROR · see log";
            TestResultText.Foreground = (Brush)FindResource("DangerBrush");
            AppendLog($"Could not test '{rule.Name}': {ex.Message}");
        }
    }

    private MacroRule CloneRule(MacroRule source)
    {
        var clone = JsonSerializer.Deserialize<MacroRule>(JsonSerializer.Serialize(source, _jsonOptions), _jsonOptions)
            ?? new MacroRule();
        clone.Id = Guid.NewGuid();
        clone.ReferenceRgb = source.ReferenceRgb.ToArray();
        clone.GateReferenceRgb = source.GateReferenceRgb.ToArray();
        clone.RecordedSteps = source.RecordedSteps.Select(step => new RecordedStep
        {
            Type = step.Type,
            Key = step.Key,
            ClickX = step.ClickX,
            ClickY = step.ClickY,
            MouseButton = step.MouseButton,
            DelayBeforeMs = step.DelayBeforeMs
        }).ToList();
        clone.LastCondition = false;
        clone.LastTriggeredUtc = DateTime.MinValue;
        return clone;
    }

    private void RulesListBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (e.AddedItems.Count > 0 && e.AddedItems[0] is MacroRule rule)
        {
            LoadEditor(rule);
        }
    }

    private void ConditionCombo_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        UpdateEditorState();
    }

    private void ActionCombo_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        UpdateEditorState();
    }

    private void GateEnabledCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        GatePanel.IsExpanded = GateEnabledCheckBox.IsChecked == true;
        UpdateEditorState();
    }

    private void GateConditionCombo_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        UpdateEditorState();
    }

    private void RecordComboButton_Click(object sender, RoutedEventArgs e)
    {
        if (RulesListBox.SelectedItem is not MacroRule rule)
        {
            AppendLog("Select a rule before recording a combo.");
            return;
        }
        if (_isRunning)
        {
            AppendLog("Stop the engine before recording a combo.");
            return;
        }

        ApplyEditorToSelectedRule();
        var window = new ComboRecorderWindow(rule.RecordedSteps) { Owner = this };
        if (window.ShowDialog() == true)
        {
            rule.RecordedSteps = window.ResultSteps.ToList();
            rule.Action = ActionType.RecordedCombo;
            rule.Repeat = RepeatMode.WhileTrue;
            ActionCombo.SelectedItem = ActionType.RecordedCombo;
            RepeatCombo.SelectedItem = RepeatMode.WhileTrue;
            RulesListBox.Items.Refresh();
            LoadEditor(rule);
            AppendLog($"Saved {rule.RecordedSteps.Count} combo steps to '{rule.Name}'.");
        }
    }

    private void ClearComboButton_Click(object sender, RoutedEventArgs e)
    {
        if (RulesListBox.SelectedItem is MacroRule rule)
        {
            rule.RecordedSteps = [];
            rule.Action = ActionType.RecordedCombo;
            ActionCombo.SelectedItem = ActionType.RecordedCombo;
            RulesListBox.Items.Refresh();
            LoadEditor(rule);
            AppendLog($"Cleared combo steps from '{rule.Name}'.");
        }
    }

    private void UpdateRecordedComboSummary(IEnumerable<RecordedStep>? source = null)
    {
        var steps = (source ?? (RulesListBox.SelectedItem as MacroRule)?.RecordedSteps ?? []).ToList();
        var count = steps.Count;
        RecordedComboSummary.Text = count == 0
            ? "No recorded combo. Inputs pass through while recording."
            : $"{count} recorded step{(count == 1 ? "" : "s")}: {string.Join("  →  ", steps.Take(5).Select(step => step.Summary))}{(count > 5 ? "  …" : "")}";
    }

    private void CaptureWatchButton_Click(object sender, RoutedEventArgs e) => SelectWatchArea();

    private void SamplePixelButton_Click(object sender, RoutedEventArgs e) => SelectWatchArea(sampleColor: true);

    private void CaptureClickButton_Click(object sender, RoutedEventArgs e) => CaptureClickTarget();

    private void CaptureGateButton_Click(object sender, RoutedEventArgs e) => SelectGateArea();

    private void SampleGateButton_Click(object sender, RoutedEventArgs e) => SelectGateArea(sampleColor: true);

    private void CaptureWatchReferenceButton_Click(object sender, RoutedEventArgs e) => SelectWatchArea(captureReference: true);

    private void CaptureGateReferenceButton_Click(object sender, RoutedEventArgs e) => SelectGateArea(captureReference: true);

    private ScreenSelection? SelectScreenArea()
    {
        var overlay = new SelectionOverlay { Owner = this };
        try
        {
            var accepted = overlay.ShowDialog() == true;
            if (accepted && overlay.UnderlyingWindowHandle != IntPtr.Zero)
            {
                NativeMethods.SetForegroundWindow(overlay.UnderlyingWindowHandle);
            }
            return accepted ? overlay.Selection : null;
        }
        catch (InvalidOperationException ex)
        {
            AppendLog($"Could not open screen selection: {ex.Message}");
            return null;
        }
    }

    private void SelectWatchArea(bool sampleColor = false, bool captureReference = false)
    {
        var selection = SelectScreenArea();
        if (selection is null)
        {
            AppendLog("Screen selection cancelled.");
            return;
        }

        WatchXBox.Text = selection.X.ToString();
        WatchYBox.Text = selection.Y.ToString();
        WatchWidthBox.Text = selection.Width.ToString();
        WatchHeightBox.Text = selection.Height.ToString();
        if (sampleColor)
        {
            SampleColorInto(TargetRedBox, TargetGreenBox, TargetBlueBox, selection);
        }
        if (captureReference)
        {
            ConditionCombo.SelectedItem = ConditionType.RegionSnapshotMatches;
            CaptureReferenceInto(selection, gate: false);
        }
        UpdateEditorState();
        AppendLog($"Watch area selected: {selection.X},{selection.Y} {selection.Width}×{selection.Height}.");
    }

    private void SampleColorInto(System.Windows.Controls.TextBox redBox, System.Windows.Controls.TextBox greenBox, System.Windows.Controls.TextBox blueBox, ScreenSelection selection)
    {
        var centerX = selection.X + selection.Width / 2;
        var centerY = selection.Y + selection.Height / 2;
        if (!ScreenProbe.TryReadPixel(centerX, centerY, out var color))
        {
            AppendLog("Could not sample a screen pixel from the selected area.");
            return;
        }

        redBox.Text = color.R.ToString();
        greenBox.Text = color.G.ToString();
        blueBox.Text = color.B.ToString();
        AppendLog($"Sampled center color {color} from the selected area.");
    }

    private void CaptureClickTarget()
    {
        var selection = SelectScreenArea();
        if (selection is null)
        {
            AppendLog("Click-target selection cancelled.");
            return;
        }

        ClickXBox.Text = selection.X.ToString();
        ClickYBox.Text = selection.Y.ToString();
        AppendLog($"Click target selected at {selection.X}, {selection.Y}.");
    }

    private void SelectGateArea(bool sampleColor = false, bool captureReference = false)
    {
        var selection = SelectScreenArea();
        if (selection is null)
        {
            AppendLog("Gate selection cancelled.");
            return;
        }

        GateEnabledCheckBox.IsChecked = true;
        GateXBox.Text = selection.X.ToString();
        GateYBox.Text = selection.Y.ToString();
        GateWidthBox.Text = selection.Width.ToString();
        GateHeightBox.Text = selection.Height.ToString();
        if (sampleColor)
        {
            SampleColorInto(GateTargetRedBox, GateTargetGreenBox, GateTargetBlueBox, selection);
        }
        if (captureReference)
        {
            GateConditionCombo.SelectedItem = ConditionType.RegionSnapshotMatches;
            CaptureReferenceInto(selection, gate: true);
        }
        UpdateEditorState();
        AppendLog($"Gate area selected: {selection.X},{selection.Y} {selection.Width}×{selection.Height}.");
    }

    private async void CaptureReferenceInto(ScreenSelection selection, bool gate)
    {
        var selectedRuleId = (RulesListBox.SelectedItem as MacroRule)?.Id;
        var folder = _settings.ReferenceImageFolder;
        var ruleName = string.IsNullOrWhiteSpace(RuleNameBox.Text) ? "reference" : RuleNameBox.Text.Trim();
        var result = await Task.Run(() => CaptureAndSaveReference(selection, folder, ruleName, gate));
        if (!result.Success)
        {
            AppendLog(result.Error);
            return;
        }

        if (selectedRuleId.HasValue && (RulesListBox.SelectedItem as MacroRule)?.Id != selectedRuleId.Value)
        {
            AppendLog($"Captured reference saved as {Path.GetFileName(result.Path)}, but the active rule changed before capture finished.");
            return;
        }

        if (gate)
        {
            _gateReferenceRgb = result.Reference;
            _gateReferenceImagePath = result.Path;
            GateCoverageThresholdBox.Text = "90";
            GateEnabledCheckBox.IsChecked = true;
        }
        else
        {
            _watchReferenceRgb = result.Reference;
            _watchReferenceImagePath = result.Path;
            CoverageThresholdBox.Text = "90";
        }

        ApplyEditorToSelectedRule();
        RulesListBox.Items.Refresh();

        AppendLog($"Captured a {selection.Width}×{selection.Height} reference image: {Path.GetFileName(result.Path)}.");
    }

    private static (bool Success, byte[] Reference, string Path, string Error) CaptureAndSaveReference(ScreenSelection selection, string folder, string ruleName, bool gate)
    {
        if (!ScreenProbe.TryCaptureRegion(selection.X, selection.Y, selection.Width, selection.Height, out var reference))
        {
            return (false, [], "", $"The selected area is too large or could not be captured. Keep it under {ScreenProbe.MaxReferencePixels:N0} pixels.");
        }

        try
        {
            var imagePath = ReferenceImageService.CreateNextPath(folder, ruleName, gate);
            if (!ReferenceImageService.TrySavePng(imagePath, selection.Width, selection.Height, reference, out var saveError))
            {
                return (false, [], "", $"Reference PNG could not be saved: {saveError}");
            }

            return (true, reference, imagePath, "");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return (false, [], "", $"Reference PNG could not be saved: {ex.Message}");
        }
    }

    private void LoadEditor(MacroRule? rule)
    {
        if (rule is null)
        {
            RuleNameBox.Text = "";
            return;
        }

        RuleNameBox.Text = rule.Name;
        RuleEnabledCheckBox.IsChecked = rule.Enabled;
        ConditionCombo.SelectedItem = rule.Condition;
        WatchXBox.Text = rule.WatchX.ToString();
        WatchYBox.Text = rule.WatchY.ToString();
        WatchWidthBox.Text = rule.WatchWidth.ToString();
        WatchHeightBox.Text = rule.WatchHeight.ToString();
        TargetRedBox.Text = rule.TargetRed.ToString();
        TargetGreenBox.Text = rule.TargetGreen.ToString();
        TargetBlueBox.Text = rule.TargetBlue.ToString();
        ToleranceBox.Text = rule.Tolerance.ToString();
        CoverageThresholdBox.Text = rule.CoverageThreshold.ToString();
        _watchReferenceRgb = rule.ReferenceRgb.ToArray();
        _watchReferenceImagePath = rule.ReferenceImagePath;
        GateEnabledCheckBox.IsChecked = rule.GateEnabled;
        GateConditionCombo.SelectedItem = rule.GateCondition;
        GateXBox.Text = rule.GateX.ToString();
        GateYBox.Text = rule.GateY.ToString();
        GateWidthBox.Text = rule.GateWidth.ToString();
        GateHeightBox.Text = rule.GateHeight.ToString();
        GateTargetRedBox.Text = rule.GateTargetRed.ToString();
        GateTargetGreenBox.Text = rule.GateTargetGreen.ToString();
        GateTargetBlueBox.Text = rule.GateTargetBlue.ToString();
        GateToleranceBox.Text = rule.GateTolerance.ToString();
        GateCoverageThresholdBox.Text = rule.GateCoverageThreshold.ToString();
        _gateReferenceRgb = rule.GateReferenceRgb.ToArray();
        _gateReferenceImagePath = rule.GateReferenceImagePath;
        ActionCombo.SelectedItem = rule.Action;
        KeyBox.Text = rule.Key;
        ClickXBox.Text = rule.ClickX.ToString();
        ClickYBox.Text = rule.ClickY.ToString();
        MouseButtonCombo.SelectedItem = rule.MouseButton;
        RestorePointerCheckBox.IsChecked = rule.RestorePointerAfterClick;
        RepeatCombo.SelectedItem = rule.Repeat;
        CooldownBox.Text = rule.CooldownMs.ToString();
        DelayAfterActionBox.Text = rule.DelayAfterActionMs.ToString();
        UpdateRecordedComboSummary(rule.RecordedSteps);
        UpdateEditorState();
    }

    private void ApplyEditorToSelectedRule()
    {
        if (RulesListBox.SelectedItem is not MacroRule rule)
        {
            return;
        }

        rule.Name = string.IsNullOrWhiteSpace(RuleNameBox.Text) ? "Unnamed rule" : RuleNameBox.Text.Trim();
        rule.Enabled = RuleEnabledCheckBox.IsChecked == true;
        rule.Condition = ConditionCombo.SelectedItem is ConditionType condition ? condition : ConditionType.Always;
        rule.WatchX = ReadInt(WatchXBox, rule.WatchX);
        rule.WatchY = ReadInt(WatchYBox, rule.WatchY);
        rule.WatchWidth = ReadInt(WatchWidthBox, rule.WatchWidth, 1, 1200);
        rule.WatchHeight = ReadInt(WatchHeightBox, rule.WatchHeight, 1, 800);
        rule.TargetRed = (byte)ReadInt(TargetRedBox, rule.TargetRed, 0, 255);
        rule.TargetGreen = (byte)ReadInt(TargetGreenBox, rule.TargetGreen, 0, 255);
        rule.TargetBlue = (byte)ReadInt(TargetBlueBox, rule.TargetBlue, 0, 255);
        rule.Tolerance = ReadInt(ToleranceBox, rule.Tolerance, 0, 255);
        rule.CoverageThreshold = ReadInt(CoverageThresholdBox, rule.CoverageThreshold, 0, 100);
        rule.ReferenceRgb = _watchReferenceRgb.ToArray();
        rule.ReferenceImagePath = _watchReferenceImagePath;
        rule.GateEnabled = GateEnabledCheckBox.IsChecked == true;
        rule.GateCondition = GateConditionCombo.SelectedItem is ConditionType gateCondition ? gateCondition : ConditionType.PixelDiffers;
        rule.GateX = ReadInt(GateXBox, rule.GateX);
        rule.GateY = ReadInt(GateYBox, rule.GateY);
        rule.GateWidth = ReadInt(GateWidthBox, rule.GateWidth, 1, 1200);
        rule.GateHeight = ReadInt(GateHeightBox, rule.GateHeight, 1, 800);
        rule.GateTargetRed = (byte)ReadInt(GateTargetRedBox, rule.GateTargetRed, 0, 255);
        rule.GateTargetGreen = (byte)ReadInt(GateTargetGreenBox, rule.GateTargetGreen, 0, 255);
        rule.GateTargetBlue = (byte)ReadInt(GateTargetBlueBox, rule.GateTargetBlue, 0, 255);
        rule.GateTolerance = ReadInt(GateToleranceBox, rule.GateTolerance, 0, 255);
        rule.GateCoverageThreshold = ReadInt(GateCoverageThresholdBox, rule.GateCoverageThreshold, 0, 100);
        rule.GateReferenceRgb = _gateReferenceRgb.ToArray();
        rule.GateReferenceImagePath = _gateReferenceImagePath;
        rule.Action = ActionCombo.SelectedItem is ActionType action ? action : ActionType.KeyPress;
        rule.Key = string.IsNullOrWhiteSpace(KeyBox.Text) ? "1" : KeyBox.Text.Trim();
        rule.ClickX = ReadInt(ClickXBox, rule.ClickX);
        rule.ClickY = ReadInt(ClickYBox, rule.ClickY);
        rule.MouseButton = MouseButtonCombo.SelectedItem is MouseButtonType button ? button : MouseButtonType.Left;
        rule.RestorePointerAfterClick = RestorePointerCheckBox.IsChecked == true;
        rule.Repeat = RepeatCombo.SelectedItem is RepeatMode repeat ? repeat : RepeatMode.OnRisingEdge;
        rule.CooldownMs = ReadInt(CooldownBox, rule.CooldownMs, 0, 600000);
        rule.DelayAfterActionMs = ReadInt(DelayAfterActionBox, rule.DelayAfterActionMs, 0, 60000);
    }

    private void UpdateEditorState()
    {
        var condition = ConditionCombo.SelectedItem is ConditionType selectedCondition ? selectedCondition : ConditionType.Always;
        var action = ActionCombo.SelectedItem is ActionType selectedAction ? selectedAction : ActionType.KeyPress;
        var gateCondition = GateConditionCombo.SelectedItem is ConditionType selectedGateCondition ? selectedGateCondition : ConditionType.PixelDiffers;
        ConditionTargetPanel.IsEnabled = condition != ConditionType.Always;
        var snapshotCondition = condition == ConditionType.RegionSnapshotMatches;
        ColorPanel.Visibility = condition is ConditionType.PixelMatches or ConditionType.PixelDiffers or ConditionType.RegionCoverageAtLeast or ConditionType.RegionCoverageAtMost
            ? Visibility.Visible : Visibility.Collapsed;
        CoveragePanel.Visibility = condition is ConditionType.RegionCoverageAtLeast or ConditionType.RegionCoverageAtMost || snapshotCondition
            ? Visibility.Visible : Visibility.Collapsed;
        CoverageThresholdLabel.Content = snapshotCondition ? "Reference match threshold (%)" : "Region coverage / match threshold (%)";
        KeyPanel.Visibility = action == ActionType.KeyPress ? Visibility.Visible : Visibility.Collapsed;
        ClickPanel.Visibility = action == ActionType.MouseClick ? Visibility.Visible : Visibility.Collapsed;
        RecordComboPanel.Visibility = action is ActionType.KeyPress or ActionType.MouseClick or ActionType.RecordedCombo
            ? Visibility.Visible : Visibility.Collapsed;
        RecordComboButton.Content = action == ActionType.RecordedCombo && (RulesListBox.SelectedItem as MacroRule)?.RecordedSteps.Count > 0
            ? "EDIT COMBO" : "RECORD COMBO";
        UpdateRecordedComboSummary();
        GateEditorPanel.IsEnabled = GateEnabledCheckBox.IsChecked == true;
        var gateSnapshotCondition = gateCondition == ConditionType.RegionSnapshotMatches;
        GateColorPanel.Visibility = GateEnabledCheckBox.IsChecked == true && gateCondition != ConditionType.Always && !gateSnapshotCondition
            ? Visibility.Visible : Visibility.Collapsed;
        GateCoveragePanel.Visibility = GateEnabledCheckBox.IsChecked == true && (gateCondition is ConditionType.RegionCoverageAtLeast or ConditionType.RegionCoverageAtMost || gateSnapshotCondition)
            ? Visibility.Visible : Visibility.Collapsed;
        GateCoverageThresholdLabel.Content = gateSnapshotCondition ? "Gate reference match threshold (%)" : "Gate coverage / match threshold (%)";
    }

    private void HydrateProfileReferences(MacroProfile profile)
    {
        foreach (var rule in profile.Rules)
        {
            if (ReferenceImageService.TryLoadFromRule(rule, _settings.ReferenceImageFolder, gate: false, out var reference, out var resolvedPath))
            {
                rule.ReferenceRgb = reference;
                rule.ReferenceImagePath = resolvedPath;
            }
            else
            {
                rule.ReferenceRgb = [];
            }

            if (ReferenceImageService.TryLoadFromRule(rule, _settings.ReferenceImageFolder, gate: true, out var gateReference, out var gateResolvedPath))
            {
                rule.GateReferenceRgb = gateReference;
                rule.GateReferenceImagePath = gateResolvedPath;
            }
            else
            {
                rule.GateReferenceRgb = [];
            }
        }
    }

    private void UpdateRuleCount()
    {
        RuleCountText.Text = $"{_rules.Count} rule{(_rules.Count == 1 ? "" : "s")}";
    }

    private static int ReadInt(System.Windows.Controls.TextBox box, int fallback, int min = int.MinValue, int max = int.MaxValue)
    {
        return int.TryParse(box.Text, out var value) ? Math.Clamp(value, min, max) : fallback;
    }

    private void AppendLog(string message)
    {
        _logLines.Enqueue($"[{DateTime.Now:HH:mm:ss}] {message}");
        if (_logLines.Count > MaxLogLines)
        {
            while (_logLines.Count > MaxLogLines - 100)
            {
                _logLines.Dequeue();
            }

            LogBox.Text = string.Join(Environment.NewLine, _logLines) + Environment.NewLine;
        }
        else
        {
            LogBox.AppendText(_logLines.Last() + Environment.NewLine);
        }
        LogBox.ScrollToEnd();
    }
}
