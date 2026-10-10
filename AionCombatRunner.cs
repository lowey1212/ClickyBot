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
        if (!settings.RequireTargetX)
        {
            if (AionTargetBarMatcher.ReferenceProblem(target.ReferenceRgb, target.WatchWidth, target.WatchHeight) is { } problem)
                throw new InvalidOperationException(problem);
            if (target.SearchWidth < target.WatchWidth || target.SearchHeight < target.WatchHeight)
                throw new InvalidOperationException($"Watch area {target.SearchWidth}×{target.SearchHeight} is smaller than reference {target.WatchWidth}×{target.WatchHeight}. Select a watch area containing the target.");
            if (target.SearchWidth > 3840 || target.SearchHeight > 2160)
                throw new InvalidOperationException("The combat watch area must be no larger than 3840×2160.");
        }
        if (settings.TurnPixels is < -500 or > 500 || settings.TurnSteps is < 1 or > 20
            || settings.MaxSearchAttempts is < 1 or > 100 || settings.TargetLostMs is < 100 or > 2000
            || (settings.MaxAttackMs != 0 && settings.MaxAttackMs is < 1000 or > 300000))
            throw new InvalidOperationException("Aion combat camera or timing settings are out of range.");
        ValidateCooldown(profile);
        ValidateTargetX(profile);
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

    internal static MacroRule? ValidateTargetX(MacroProfile profile)
    {
        if (!profile.AionCombat.RequireTargetX) return null;
        if (!profile.AionCombat.StopOnSkillCooldown)
            throw new InvalidOperationException("Target X checking requires skill 1 cooldown checking. Enable both, or use SET UP TARGET X + COOLDOWN.");
        var rule = profile.Rules.FirstOrDefault(rule => rule.Id == profile.AionCombat.TargetXRuleId && rule.Enabled);
        if (rule is null || rule.Condition != ConditionType.AionTargetXMatches || !rule.SearchReference || rule.GateEnabled)
            throw new InvalidOperationException("Use SET UP TARGET X + COOLDOWN to create the target X rule.");
        if (!AionTargetBarMatcher.IsMarkerReference(rule.WatchWidth, rule.WatchHeight)
            || AionTargetBarMatcher.ReferenceProblem(rule.ReferenceRgb, rule.WatchWidth, rule.WatchHeight) is not null)
            throw new InvalidOperationException("The target X reference is unavailable or has no clear outline. Capture the X with a little surrounding background.");
        if (rule.SearchWidth < rule.WatchWidth || rule.SearchHeight < rule.WatchHeight
            || rule.SearchWidth > 3840 || rule.SearchHeight > 2160)
            throw new InvalidOperationException("Select a tight watch area around the target HUD X that contains its reference, then apply and save.");
        return rule;
    }

    internal async Task RunAsync(CancellationToken token)
    {
        var target = Validate(_profile);
        var settings = _profile.AionCombat;
        var cooldown = ValidateCooldown(_profile);
        var targetX = ValidateTargetX(_profile);
        var indicator = targetX ?? target;
        var held = false;
        var cooldownSeen = false;
        var seekNext = false;
        var searching = 0;
        long attackStarted = 0;
        long? absentSince = null;
        string HoldingStatus() => targetX is not null
            ? "Holding LEFT mouse; waiting for X missing AND skill 1 off cooldown."
            : cooldown is null ? "Pressed 1; holding LEFT mouse until the target disappears."
            : "Pressed 1; holding LEFT mouse until skill 1 cooldown clears.";
        RuleObservation Read(MacroRule rule)
        {
            var reading = _observe(rule, token);
            if (!reading.Valid) throw new InvalidOperationException(rule == cooldown
                ? "Aion combat stopped: skill 1 cooldown could not be read. " + reading.Primary.Detail
                : rule == targetX ? "Aion combat stopped: target X could not be read. " + reading.Primary.Detail
                : "Aion combat stopped: target HP bar capture or reference is unavailable.");
            return reading;
        }
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
            _reportAction(indicator, targetX is null ? "Target matched; pressing 1." : "Target X matched and skill 1 ready; pressing 1.");
            await Tap("1");
            _log("Aion combat: sent 1 (FakerInput driver).");
            if (!Active()) return;
            FakerInputKeyboard.Shared.SendMouseButton(MouseButtonType.Left, true);
            held = true; cooldownSeen = false; seekNext = false; attackStarted = _now(); absentSince = null;
            _reportAction(indicator, HoldingStatus());
            _log(targetX is not null ? "Aion combat: holding LEFT mouse until target X is missing AND skill 1 is off cooldown."
                : cooldown is null ? "Aion combat: holding LEFT mouse until the target indicator disappears."
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
                var watched = held && cooldown is not null && targetX is null ? cooldown : indicator;
                var observation = Read(watched);
                var timer = targetX is not null ? Read(cooldown!) : null;
                if (!Active()) return;
                if (held)
                {
                    _reportAction(indicator, HoldingStatus());
                    if (settings.MaxAttackMs > 0 && _now() - attackStarted >= settings.MaxAttackMs)
                    {
                        _log("Aion combat attack time limit reached. Stopping and releasing left mouse.");
                        return;
                    }
                    if (targetX is not null)
                    {
                        if (!observation.Passed && !timer!.Passed) absentSince ??= _now();
                        else absentSince = null;
                    }
                    else if (observation.Passed)
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
                        _reportAction(indicator, targetX is not null ? "Target X missing and skill 1 ready; released LEFT mouse."
                            : cooldown is null ? "Target disappeared; released LEFT mouse."
                            : "Skill 1 cooldown cleared; released LEFT mouse.");
                        _log(targetX is not null ? "Target X missing and skill 1 off cooldown. Released LEFT mouse; Tab will select the next monster."
                            : cooldown is null ? "Target HP bar disappeared. Released left mouse; looking for the next monster."
                            : "Skill 1 cooldown cleared. Released LEFT mouse; Tab will select the next monster.");
                        await _delay(500, token);
                    }
                }
                else if (targetX is not null && timer!.Passed)
                {
                    _reportAction(indicator, "Skill 1 still on cooldown; waiting before targeting or attacking.");
                }
                else if (observation.Passed && (!seekNext || targetX is not null))
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
                    _reportAction(indicator, targetX is null ? "Sent Tab; waiting for target arrow shape." : "X missing and skill 1 ready; sent Tab, waiting for target X.");
                    searching++;
                    _log($"Aion combat: sent Tab (FakerInput driver), search {searching}/{settings.MaxSearchAttempts}.");
                    RuleObservation? selected = null;
                    for (var waited = 0; waited < 400; waited += 50)
                    {
                        await _delay(50, token);
                        if (!Active()) return;
                        selected = Read(indicator);
                        if (!selected.Passed) continue;
                        if (targetX is not null && Read(cooldown!).Passed) continue;
                        if (!Active()) return;
                        await BeginAttack();
                        break;
                    }
                    if (!Active()) return;
                    if (!held)
                    {
                        _log(selected!.Passed ? "Aion combat: target X present; waiting for skill 1 cooldown."
                            : "Aion combat: no target indicator matched after Tab. " + selected.Primary.Detail);
                        if (!selected.Passed && settings.CameraTurnEnabled && settings.TurnPixels != 0)
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
            _reportAction(indicator, released ? "Combat stopped; generated input released." : "Combat stopped; input cleanup failed. Check Activity.");
        }
    }
}
