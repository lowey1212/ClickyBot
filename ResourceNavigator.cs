namespace ClickyBot;

// Opt-in, bounded exploration for the Pax gathering profile. The interaction
// prompt confirms a target; the camera/forward sweep only searches for one.
internal sealed class ResourceNavigator
{
    private enum State { Harvest, Rest, Search }

    private readonly MacroProfile _profile;
    private readonly Func<MacroRule, CancellationToken, bool> _evaluate;
    private readonly Action<string> _log;
    private readonly Func<CancellationToken, Task<StaminaBarReading>>? _readBar;

    public ResourceNavigator(MacroProfile profile, Func<MacroRule, CancellationToken, bool> evaluate, Action<string> log,
        Func<CancellationToken, Task<StaminaBarReading>>? readBar = null)
    {
        _profile = profile;
        _evaluate = evaluate;
        _log = log;
        _readBar = readBar;
    }

    public async Task RunAsync(CancellationToken token)
    {
        var settings = _profile.ResourceNavigation;
        var useFill = settings.UseBarFillForStamina;
        var low = useFill ? null : FindStaminaRule(RecordedStepType.KeyUp);
        var high = useFill ? null : FindStaminaRule(RecordedStepType.KeyDown);
        if (useFill && _readBar is null)
            throw new InvalidOperationException("Bar fill stamina control needs a readable stamina bar reference.");
        if (!useFill && (low is null || high is null || low.ReferenceRgb.Length == 0 || high.ReferenceRgb.Length == 0))
            throw new InvalidOperationException("Resource search needs enabled 8/50 E-up and 48/50 E-down image rules with loaded references.");

        Validate(settings);
        if (!ReferenceImageService.TryLoadRgb(settings.PromptReferenceImagePath,
                settings.PromptWidth, settings.PromptHeight, out var promptRgb))
            throw new InvalidOperationException("The E (Hold) prompt reference could not be loaded. Check ResourceNavigation.PromptReferenceImagePath and its dimensions.");

        var promptRule = new MacroRule
        {
            Name = "E (Hold) prompt",
            Condition = ConditionType.RegionSnapshotMatches,
            SearchReference = true,
            ImageMatchMethod = ImageMatchMethod.ImageSimilarity,
            WatchWidth = settings.PromptWidth,
            WatchHeight = settings.PromptHeight,
            ReferenceRgb = promptRgb,
            CoverageThreshold = settings.PromptThreshold,
            SearchX = settings.PromptSearchX,
            SearchY = settings.PromptSearchY,
            SearchWidth = settings.PromptSearchWidth,
            SearchHeight = settings.PromptSearchHeight
        };

        var gameWindow = NativeMethods.GetForegroundWindow();
        if (gameWindow == IntPtr.Zero)
            throw new InvalidOperationException("No foreground game window was found. Start with the ClickyBot hotkey while the game is focused.");

        var state = State.Harvest;
        var staminaReady = true;
        var lastPromptSeen = DateTime.UtcNow;
        var promptConfirmations = 0;
        MatchLocation? promptCandidate = null;
        var turns = 0;
        var forwardSteps = 0;
        var movementLimitReached = false;
        DateTime? barMissingSince = null;
        InputSimulator.SendKeyDown("E");
        _log("Resource navigation: holding E. F7 stops and releases all keys.");

        try
        {
            while (true)
            {
                token.ThrowIfCancellationRequested();
                if (NativeMethods.GetForegroundWindow() != gameWindow)
                {
                    _log("Resource navigation stopped because the game lost focus.");
                    return;
                }

                var promptVisible = _evaluate(promptRule, token);
                var bar = useFill ? await _readBar!(token) : default;
                var lowStamina = useFill
                    ? bar.Visible == true && bar.FillPercent <= settings.LowFillPercent
                    : state == State.Harvest && _evaluate(low!, token);
                var highStamina = useFill
                    ? bar.Visible == true && bar.FillPercent >= settings.HighFillPercent
                    : state != State.Harvest && _evaluate(high!, token);
                var now = DateTime.UtcNow;
                if (useFill && bar.Visible == false)
                    barMissingSince ??= now;
                else
                    barMissingSince = null;
                if (state != State.Search)
                {
                    if (promptVisible) lastPromptSeen = now;
                    if ((now - lastPromptSeen).TotalMilliseconds >= settings.PromptLostMs)
                    {
                        InputSimulator.SendKeyUp("E");
                        state = State.Search;
                        turns = 0;
                        forwardSteps = 0;
                        movementLimitReached = false;
                        promptConfirmations = 0;
                        promptCandidate = null;
                        _log("Resource prompt disappeared; searching nearby. E is released.");
                    }
                    else if (state == State.Harvest && barMissingSince is { } missingSince
                        && (now - missingSince).TotalMilliseconds >= settings.BarMissingMs)
                    {
                        InputSimulator.SendKeyUp("E");
                        state = State.Search;
                        turns = 0;
                        forwardSteps = 0;
                        movementLimitReached = false;
                        promptConfirmations = 0;
                        promptCandidate = null;
                        barMissingSince = null;
                        _log("Stamina bar disappeared while harvesting; releasing E and seeking a fresh E (Hold) prompt.");
                    }
                    else if (state == State.Harvest && lowStamina)
                    {
                        InputSimulator.SendKeyUp("E");
                        state = State.Rest;
                        staminaReady = false;
                        _log(useFill ? $"Stamina bar near {bar.FillPercent}% fill; resting." : "Stamina near 8/50; resting.");
                    }
                    else if (state == State.Rest && highStamina)
                    {
                        staminaReady = true;
                        if (promptVisible)
                        {
                            InputSimulator.SendKeyDown("E");
                            state = State.Harvest;
                            _log(useFill ? $"Stamina bar near {bar.FillPercent}% fill; holding E again." : "Stamina near 48/50; holding E again.");
                        }
                        else
                        {
                            state = State.Search;
                            _log("Stamina recovered; searching for another resource.");
                        }
                    }
                }
                else
                {
                    if (!staminaReady && highStamina)
                        staminaReady = true;

                    if (promptVisible)
                    {
                        if (promptRule.CurrentMatch is { } location)
                        {
                            promptConfirmations = promptCandidate is { } previous
                                && Math.Abs(location.X - previous.X) <= 40
                                && Math.Abs(location.Y - previous.Y) <= 40
                                ? promptConfirmations + 1 : 1;
                            promptCandidate = location;
                        }
                        else
                        {
                            promptConfirmations = 0;
                        }
                        if (promptConfirmations >= 2)
                        {
                            lastPromptSeen = now;
                            promptConfirmations = 0;
                            promptCandidate = null;
                            if (staminaReady)
                            {
                                InputSimulator.SendKeyDown("E");
                                state = State.Harvest;
                                barMissingSince = null;
                                _log("Found another E (Hold) prompt; harvesting.");
                            }
                            else
                            {
                                state = State.Rest;
                                _log("Found another resource; waiting for stamina before holding E.");
                            }
                        }
                    }
                    else if (!movementLimitReached)
                    {
                        promptConfirmations = 0;
                        promptCandidate = null;
                        InputSimulator.MoveMouseRelative(settings.TurnPixels, 0);
                        turns++;
                        if (turns >= settings.TurnsBeforeStep)
                        {
                            turns = 0;
                            InputSimulator.SendKeyDown("W");
                            try
                            {
                                await Task.Delay(settings.ForwardStepMs, token);
                            }
                            finally
                            {
                                InputSimulator.SendKeyUp("W");
                            }
                            forwardSteps++;
                            _log($"Resource search: short forward step {forwardSteps}/{settings.MaxForwardSteps}.");
                            if (forwardSteps >= settings.MaxForwardSteps)
                            {
                                movementLimitReached = true;
                                _log("Movement limit reached. Holding position and watching for another E (Hold) prompt.");
                            }
                        }
                    }
                }

                await Task.Delay(Math.Clamp(_profile.PollIntervalMs, 50, 1000), token);
            }
        }
        finally
        {
            // The outer engine also releases generated inputs on Stop or error.
            InputSimulator.ReleaseAllHeldInputs();
        }
    }

    private MacroRule? FindStaminaRule(RecordedStepType stepType) => _profile.Rules.FirstOrDefault(rule =>
        rule.Enabled && rule.Condition == ConditionType.RegionSnapshotMatches && rule.SearchReference
        && rule.Action == ActionType.RecordedCombo
        && rule.RecordedSteps.Count == 1
        && rule.RecordedSteps[0].Type == stepType
        && string.Equals(rule.RecordedSteps[0].Key, "E", StringComparison.OrdinalIgnoreCase));

    private static void Validate(ResourceNavigationSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.PromptReferenceImagePath)
            || settings.PromptWidth is < 1 or > 400 || settings.PromptHeight is < 1 or > 200
            || settings.PromptThreshold is < 1 or > 100
            || settings.PromptSearchWidth is < 1 or > ScreenProbe.MaxSearchWidth
            || settings.PromptSearchHeight is < 1 or > ScreenProbe.MaxSearchHeight
            || settings.PromptWidth > settings.PromptSearchWidth
            || settings.PromptHeight > settings.PromptSearchHeight
            || settings.PromptLostMs is < 500 or > 10000
            || settings.TurnPixels is < 1 or > 500
            || settings.TurnsBeforeStep is < 1 or > 36
            || settings.ForwardStepMs is < 50 or > 1000
            || settings.MaxForwardSteps is < 1 or > 20)
            throw new InvalidOperationException("Resource navigation settings are incomplete or out of range.");
    }
}
