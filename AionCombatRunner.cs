namespace ClickyBot;

internal sealed class AionCombatRunner
{
    private readonly MacroProfile _profile;
    private readonly Func<MacroRule, CancellationToken, RuleObservation> _observe;
    private readonly Func<bool> _focused;
    private readonly Action<string> _log;
    private readonly Func<int, CancellationToken, Task> _delay;
    private readonly Func<long> _now;

    internal AionCombatRunner(MacroProfile profile, Func<MacroRule, CancellationToken, RuleObservation> observe,
        Func<bool> focused, Action<string> log, Func<int, CancellationToken, Task>? delay = null, Func<long>? now = null)
    {
        _profile = profile; _observe = observe; _focused = focused; _log = log;
        _delay = delay ?? ((ms, token) => Task.Delay(ms, token));
        _now = now ?? (() => Environment.TickCount64);
    }

    internal static MacroRule Validate(MacroProfile profile)
    {
        var settings = profile.AionCombat;
        if (settings is null) throw new InvalidOperationException("Use SET UP AION COMBAT to create its target rule.");
        var target = profile.Rules.FirstOrDefault(rule => rule.Id == settings.TargetRuleId && rule.Enabled);
        if (target is null) throw new InvalidOperationException("The combat target rule is missing or disabled. Enable it or use SET UP AION COMBAT.");
        if (target.Condition != ConditionType.AionTargetBarMatches || !target.SearchReference)
            throw new InvalidOperationException("The combat target rule must use AionTargetBarMatches and an image watch area.");
        if (AionTargetBarMatcher.ReferenceProblem(target.ReferenceRgb, target.WatchWidth, target.WatchHeight) is { } problem)
            throw new InvalidOperationException(problem);
        if (target.SearchWidth < target.WatchWidth || target.SearchHeight < target.WatchHeight)
            throw new InvalidOperationException($"Watch area {target.SearchWidth}×{target.SearchHeight} is smaller than reference {target.WatchWidth}×{target.WatchHeight}. Select a watch area containing the target.");
        if (target.SearchWidth > 3840 || target.SearchHeight > 2160)
            throw new InvalidOperationException("The combat watch area must be no larger than 3840×2160.");
        if (settings.TurnPixels is < -500 or > 500 || settings.TurnSteps is < 1 or > 20
            || settings.MaxSearchAttempts is < 1 or > 100 || settings.TargetLostMs is < 100 or > 2000
            || (settings.MaxAttackMs != 0 && settings.MaxAttackMs is < 1000 or > 300000))
            throw new InvalidOperationException("Aion combat camera or timing settings are out of range.");
        return target;
    }

    internal async Task RunAsync(CancellationToken token)
    {
        var target = Validate(_profile);
        var settings = _profile.AionCombat;
        var held = false;
        var searching = 0;
        var confirmations = 0;
        long attackStarted = 0;
        long? absentSince = null;
        bool Active()
        {
            token.ThrowIfCancellationRequested();
            if (_focused()) return true;
            _log("Aion combat stopped because the game lost focus.");
            return false;
        }
        async Task Tap(string key)
        {
            await InputSimulator.ExecuteAsync(new MacroRule { Action = ActionType.KeyPress, Key = key,
                KeyboardInputMode = KeyboardInputMode.FakerInput }, token);
        }
        try
        {
            _log("Aion combat: searching for a target HP bar. Tab selects the next monster; F7 stops.");
            while (Active())
            {
                var observation = _observe(target, token);
                if (!observation.Valid) throw new InvalidOperationException("Aion combat stopped: target HP bar capture or reference is unavailable.");
                if (!Active()) return;
                if (held)
                {
                    if (settings.MaxAttackMs > 0 && _now() - attackStarted >= settings.MaxAttackMs)
                    {
                        _log("Aion combat attack time limit reached. Stopping and releasing left mouse.");
                        return;
                    }
                    if (observation.Passed) absentSince = null;
                    else absentSince ??= _now();
                    if (absentSince is { } missing && _now() - missing >= settings.TargetLostMs)
                    {
                        FakerInputKeyboard.Shared.SendMouseButton(MouseButtonType.Left, false);
                        held = false; confirmations = 0; searching = 0; absentSince = null;
                        _log("Target HP bar disappeared. Released left mouse; looking for the next monster.");
                        await _delay(500, token);
                    }
                }
                else if (observation.Passed)
                {
                    if (++confirmations >= 2)
                    {
                        if (!Active()) return;
                        await Tap("1");
                        if (!Active()) return;
                        // Recheck after the tap so a vanished target never
                        // starts a held attack based on a stale observation.
                        var recheck = _observe(target, token);
                        if (!recheck.Valid) throw new InvalidOperationException("Target HP bar capture became unavailable.");
                        if (recheck.Passed && Active())
                        {
                            FakerInputKeyboard.Shared.SendMouseButton(MouseButtonType.Left, true);
                            held = true; attackStarted = _now(); absentSince = null;
                            _log("Target confirmed: pressed 1, holding left mouse until the target HP bar disappears.");
                        }
                        else confirmations = 0;
                    }
                }
                else
                {
                    confirmations = 0;
                    if (searching >= settings.MaxSearchAttempts)
                    {
                        _log("Aion combat search limit reached without a target. Stopping.");
                        return;
                    }
                    if (settings.HoldRightMouseToTurn && settings.TurnPixels != 0)
                        FakerInputKeyboard.Shared.SendMouseButton(MouseButtonType.Right, true);
                    try
                    {
                        for (var step = 0; step < settings.TurnSteps && settings.TurnPixels != 0; step++)
                        {
                            if (!Active()) return;
                            FakerInputKeyboard.Shared.MoveMouseRelative(settings.TurnPixels, 0);
                            await _delay(30, token);
                        }
                    }
                    finally { FakerInputKeyboard.Shared.SendMouseButton(MouseButtonType.Right, false); }
                    if (!Active()) return;
                    await Tap("Tab");
                    searching++;
                    await _delay(400, token);
                }
                await _delay(Math.Clamp(_profile.PollIntervalMs, 50, 500), token);
            }
        }
        finally
        {
            if (!InputSimulator.ReleaseAllHeldInputs()) _log("Aion combat could not release generated input. Check the driver connection and press Stop again.");
        }
    }
}
