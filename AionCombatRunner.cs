namespace ClickyBot;

internal sealed class AionCombatRunner
{
    private readonly MacroProfile _profile;
    private readonly Func<MacroRule, CancellationToken, RuleObservation> _observe;
    private readonly Func<bool> _focused;
    private readonly Action<string> _log;
    private readonly Func<int, CancellationToken, Task> _delay;
    private readonly Func<long> _now;
    private readonly Action<MacroRule, string> _reportAction;

    internal AionCombatRunner(MacroProfile profile, Func<MacroRule, CancellationToken, RuleObservation> observe,
        Func<bool> focused, Action<string> log, Func<int, CancellationToken, Task>? delay = null, Func<long>? now = null,
        Action<MacroRule, string>? reportAction = null)
    {
        _profile = profile; _observe = observe; _focused = focused; _log = log;
        _delay = delay ?? ((ms, token) => Task.Delay(ms, token));
        _now = now ?? (() => Environment.TickCount64);
        _reportAction = reportAction ?? ((_, _) => { });
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
        ValidateCooldown(profile);
        return target;
    }

    internal static MacroRule? ValidateCooldown(MacroProfile profile)
    {
        if (!profile.AionCombat.StopOnSkillCooldown) return null;
        var rule = profile.Rules.FirstOrDefault(rule => rule.Id == profile.AionCombat.CooldownRuleId && rule.Enabled);
        if (rule is null || rule.Condition != ConditionType.CooldownTimerPresent || rule.SearchReference || rule.GateEnabled)
            throw new InvalidOperationException("Use SET UP 1 COOLDOWN STOP to create the skill 1 timer rule.");
        if (rule.WatchWidth is < 16 or > 160 || rule.WatchHeight is < 10 or > 64)
            throw new InvalidOperationException("Select only skill 1's central cooldown number (16–160 × 10–64 pixels), excluding the corner hotkey and Lv. text; then apply and save.");
        return rule;
    }

    internal async Task RunAsync(CancellationToken token)
    {
        var target = Validate(_profile);
        var settings = _profile.AionCombat;
        var cooldown = ValidateCooldown(_profile);
        var held = false;
        var cooldownSeen = false;
        var seekNext = false;
        var searching = 0;
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
        async Task BeginAttack()
        {
            if (!Active()) return;
            _log("Aion combat: target shape match detected; pressing 1 now.");
            _reportAction(target, "Target matched; pressing 1.");
            await Tap("1");
            _log("Aion combat: sent 1 (FakerInput driver).");
            if (!Active()) return;
            FakerInputKeyboard.Shared.SendMouseButton(MouseButtonType.Left, true);
            held = true; cooldownSeen = false; seekNext = false; attackStarted = _now(); absentSince = null;
            _reportAction(target, cooldown is null ? "Pressed 1; holding LEFT mouse until the target disappears."
                : "Pressed 1; holding LEFT mouse until skill 1 cooldown clears.");
            _log(cooldown is null ? "Aion combat: holding LEFT mouse until the target indicator disappears."
                : "Aion combat: holding LEFT mouse; waiting to see skill 1's cooldown, then its disappearance.");
        }
        try
        {
            if (!Active()) return;
            FakerInputKeyboard.Shared.ResetMouseButtons();
            _log("Aion combat: Tab selects a target, then 1 and held LEFT mouse. F7 stops. Camera turning "
                + (settings.CameraTurnEnabled ? "enabled only after Tab finds no target." : "disabled."));
            while (Active())
            {
                var watched = held && cooldown is not null ? cooldown : target;
                var observation = _observe(watched, token);
                if (!observation.Valid) throw new InvalidOperationException(watched == cooldown
                    ? "Aion combat stopped: skill 1 cooldown could not be read. " + observation.Primary.Detail
                    : "Aion combat stopped: target HP bar capture or reference is unavailable.");
                if (!Active()) return;
                if (held)
                {
                    _reportAction(target, cooldown is null ? "Pressed 1; holding LEFT mouse until the target disappears."
                        : "Pressed 1; holding LEFT mouse until skill 1 cooldown clears.");
                    if (settings.MaxAttackMs > 0 && _now() - attackStarted >= settings.MaxAttackMs)
                    {
                        _log("Aion combat attack time limit reached. Stopping and releasing left mouse.");
                        return;
                    }
                    if (observation.Passed)
                    {
                        if (cooldown is not null && !cooldownSeen) _log("Skill 1 cooldown detected. Keeping LEFT mouse held until it clears.");
                        cooldownSeen = true; absentSince = null;
                    }
                    else if (cooldown is null || cooldownSeen) absentSince ??= _now();
                    else if (_now() - attackStarted >= 5000)
                        throw new InvalidOperationException("Skill 1 cooldown was not detected within 5 seconds. Stopped and released LEFT mouse; check the central timer watch area.");
                    if (absentSince is { } missing && _now() - missing >= settings.TargetLostMs)
                    {
                        FakerInputKeyboard.Shared.SendMouseButton(MouseButtonType.Left, false);
                        held = false; seekNext = true; searching = 0; absentSince = null;
                        _reportAction(target, cooldown is null ? "Target disappeared; released LEFT mouse."
                            : "Skill 1 cooldown cleared; released LEFT mouse.");
                        _log(cooldown is null ? "Target HP bar disappeared. Released left mouse; looking for the next monster."
                            : "Skill 1 cooldown cleared. Released LEFT mouse; Tab will select the next monster.");
                        await _delay(500, token);
                    }
                }
                else if (observation.Passed && !seekNext)
                {
                    await BeginAttack();
                }
                else
                {
                    if (searching >= settings.MaxSearchAttempts)
                    {
                        _log("Aion combat search limit reached without a target. Stopping.");
                        return;
                    }
                    // Try the game's target key before moving the camera. Old
                    // profiles do not opt in to camera turning automatically.
                    await Tap("Tab");
                    _reportAction(target, "Sent Tab; waiting for target arrow shape.");
                    searching++;
                    _log($"Aion combat: sent Tab (FakerInput driver), search {searching}/{settings.MaxSearchAttempts}.");
                    RuleObservation? selected = null;
                    for (var waited = 0; waited < 400; waited += 50)
                    {
                        await _delay(50, token);
                        if (!Active()) return;
                        selected = _observe(target, token);
                        if (!selected.Valid) throw new InvalidOperationException("Target HP bar capture became unavailable.");
                        if (!selected.Passed) continue;
                        await BeginAttack();
                        break;
                    }
                    if (!Active()) return;
                    if (!held)
                    {
                        _log("Aion combat: no target indicator matched after Tab. " + selected!.Primary.Detail);
                        if (settings.CameraTurnEnabled && settings.TurnPixels != 0)
                        {
                            for (var step = 0; step < settings.TurnSteps; step++)
                            {
                                if (!Active()) return;
                                FakerInputKeyboard.Shared.MoveMouseRelative(settings.TurnPixels, 0);
                                await _delay(30, token);
                            }
                        }
                    }
                }
                await _delay(Math.Clamp(_profile.PollIntervalMs, 50, 500), token);
            }
        }
        finally
        {
            var released = InputSimulator.ReleaseAllHeldInputs();
            if (!released) _log("Aion combat could not release generated input. Check the driver connection and press Stop again.");
            _reportAction(target, released ? "Combat stopped; generated input released." : "Combat stopped; input cleanup failed. Check Activity.");
        }
    }
}
