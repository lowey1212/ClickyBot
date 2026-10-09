namespace ClickyBot;

internal sealed class MacroEngine
{
    public event Action<string>? Log;
    internal event Action<RuleObservation>? InspectionUpdated;

    internal RuleObservation Inspect(MacroRule rule, CancellationToken token)
    {
        EvaluateCore(rule, token, applyRingTiming: false, inspectAllConditions: true);
        return rule.LastInspection! with { ActionStatus = "Preview only — no input sent. Conditions do not include runtime cooldowns or priorities." };
    }

    private void ReportAction(MacroRule rule, string status)
    {
        if (rule.LastInspection is { } observation)
        {
            rule.LastInspection = observation with { ActionStatus = status, ObservedUtc = DateTime.UtcNow };
            InspectionUpdated?.Invoke(rule.LastInspection);
        }
    }

    internal bool EvaluateNow(MacroRule rule) => EvaluateCore(rule, CancellationToken.None, applyRingTiming: false);

    public async Task RunAsync(MacroProfile profile, CancellationToken token, BarPresenceProbe? barProbe = null)
    {
        if (profile.AionCombat?.Enabled == true)
        {
            var gameWindow = NativeMethods.GetForegroundWindow();
            if (gameWindow == IntPtr.Zero) throw new InvalidOperationException("Focus the game and start Aion combat with the hotkey.");
            var runner = new AionCombatRunner(profile, (rule, checkToken) =>
            {
                Evaluate(rule, checkToken);
                return rule.LastInspection!;
            }, () => NativeMethods.GetForegroundWindow() == gameWindow, message => Log?.Invoke(message), reportAction: ReportAction);
            await runner.RunAsync(token);
            return;
        }
        if (profile.ThroneCombatMode)
        {
            await RunThroneCombatAsync(profile, token);
            return;
        }
        if (profile.ResourceNavigation?.Enabled == true)
        {
            if (profile.ResourceNavigation.UseBarFillForStamina)
                barProbe ??= BarPresenceProbe.Create(profile.ResourceNavigation);
            var navigator = new ResourceNavigator(profile, Evaluate, message => Log?.Invoke(message),
                barProbe is null ? null : barProbe.ReadAsync);
            await navigator.RunAsync(token);
            return;
        }

        var timerWindow = profile.Rules.Any(rule => rule.Enabled && rule.UsesTimer()) ? NativeMethods.GetForegroundWindow() : IntPtr.Zero;
        var observedRules = new HashSet<Guid>();
        foreach (var rule in profile.Rules)
        {
            rule.LastCondition = false;
            rule.LastTriggeredUtc = DateTime.MinValue;
            rule.PendingReactionUtc = null;
            rule.KeyHoldActive = false;
            ScreenProbe.ResetPurpleRingTiming(rule);
        }

        var enabledRuleCount = 0;
        foreach (var rule in profile.Rules)
        {
            if (rule.Enabled)
            {
                enabledRuleCount++;
            }
        }

        Log?.Invoke($"Running {enabledRuleCount} enabled rule(s) at {profile.PollIntervalMs} ms.");

        while (!token.IsCancellationRequested)
        {
            // An absent countdown must not turn a covered/background game into
            // ready skills. Bind timer profiles to the window used at START.
            if (timerWindow != IntPtr.Zero && NativeMethods.GetForegroundWindow() != timerWindow)
            {
                foreach (var rule in profile.Rules) ReleaseHeldKey(rule);
                await Task.Delay(Math.Clamp(profile.PollIntervalMs, 20, 2000), token);
                continue;
            }
            foreach (var rule in profile.Rules)
            {
                if (!rule.Enabled || rule.ThroneHealingOnly)
                {
                    rule.PendingReactionUtc = null;
                    ReleaseHeldKey(rule);
                    continue;
                }

                token.ThrowIfCancellationRequested();
                var condition = (rule.SearchReference && rule.Condition.IsReference()) || rule.UsesTimer()
                    ? await Task.Run(() => Evaluate(rule, token), token)
                    : Evaluate(rule, token);
                token.ThrowIfCancellationRequested();
                if (timerWindow != IntPtr.Zero && NativeMethods.GetForegroundWindow() != timerWindow) break;
                var risingEdge = condition && !rule.LastCondition;
                if (observedRules.Add(rule.Id) || condition != rule.LastCondition)
                {
                    var detail = rule.CurrentMatch is { } location ? $" at {location.X},{location.Y}" : "";
                    Log?.Invoke(condition ? $"{rule.Name}: condition passed{detail}. {rule.ImageSearchDiagnostic}"
                        : $"{rule.Name}: waiting for a match (including any AND gate). {rule.ImageSearchDiagnostic}");
                }

                if (rule.Action == ActionType.KeyHold)
                {
                    rule.PendingReactionUtc = null;
                    if (condition && !rule.KeyHoldActive)
                    {
                        try
                        {
                            InputSimulator.SendKeyDown(rule.Key, rule.KeyboardInputMode);
                            rule.KeyHoldActive = true;
                            ReportAction(rule, $"Holding {rule.Key}.");
                            rule.LastTriggeredUtc = DateTime.UtcNow;
                            Log?.Invoke($"{rule.Name}: holding {rule.Key}");
                        }
                        catch (Exception ex) when (ex is InvalidOperationException or DllNotFoundException)
                        {
                            ReportAction(rule, $"Action failed: {ex.Message}");
                            Log?.Invoke($"{rule.Name}: action failed — {ex.Message}");
                        }
                    }
                    else if (!condition)
                    {
                        ReleaseHeldKey(rule);
                    }

                    if (condition && rule.KeyHoldActive) ReportAction(rule, $"Holding {rule.Key}.");
                    rule.LastCondition = condition;
                    continue;
                }

                ReleaseHeldKey(rule);
                var shouldTrigger = condition && (rule.Repeat == RepeatMode.WhileTrue || risingEdge || rule.PendingReactionUtc.HasValue);
                var now = DateTime.UtcNow;
                var eligible = shouldTrigger && now - rule.LastTriggeredUtc >= TimeSpan.FromMilliseconds(Math.Max(0, rule.CooldownMs));

                if (rule.ReactionReady(eligible, now))
                {
                    try
                    {
                        ReportAction(rule, $"Running {rule.ActionSummary}.");
                        await InputSimulator.ExecuteAsync(rule, token);
                        ReportAction(rule, "Action completed.");
                        rule.LastTriggeredUtc = DateTime.UtcNow;
                        rule.PendingReactionUtc = null;
                        var targetDetail = rule.Action is ActionType.MouseClick or ActionType.MouseMove
                            ? $" at {rule.ResolveMouseTarget().X},{rule.ResolveMouseTarget().Y}" : "";
                        var modeDetail = rule.Action is ActionType.KeyPress or ActionType.RecordedCombo
                            ? rule.KeyboardInputMode switch { KeyboardInputMode.FakerInput => " (FakerInput driver)", KeyboardInputMode.VirtualKey => " (Windows key codes)", _ => " (scan codes)" } : "";
                        Log?.Invoke($"{rule.Name}: sent {rule.ActionSummary}{targetDetail}{modeDetail}");
                        if (rule.DelayAfterActionMs > 0)
                        {
                            await Task.Delay(rule.DelayAfterActionMs, token);
                        }
                    }
                    catch (Exception ex) when (ex is InvalidOperationException or DllNotFoundException)
                    {
                        rule.PendingReactionUtc = null;
                        ReportAction(rule, $"Action failed: {ex.Message}");
                        Log?.Invoke($"{rule.Name}: action failed — {ex.Message}");
                    }
                }

                if (rule.PendingReactionUtc is { } due)
                    ReportAction(rule, $"Reaction delay: {Math.Max(0, (due - DateTime.UtcNow).TotalMilliseconds):0} ms remaining.");
                else if (!shouldTrigger && condition) ReportAction(rule, "Already handled this appearance; waiting for the condition to reset.");
                else if (shouldTrigger && DateTime.UtcNow - rule.LastTriggeredUtc < TimeSpan.FromMilliseconds(Math.Max(0, rule.CooldownMs)))
                    ReportAction(rule, "Cooling down.");
                rule.LastCondition = condition;
            }

            await Task.Delay(Math.Clamp(profile.PollIntervalMs, 20, 2000), token);
        }
    }

    private async Task RunThroneCombatAsync(MacroProfile profile, CancellationToken token)
    {
        var runner = new ThroneCombatRunner();
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var gameWindow = NativeMethods.GetForegroundWindow();
        var rules = profile.Rules.Where(ThroneCombatRunner.Supports).ToList();
        foreach (var rule in profile.Rules) ScreenProbe.ResetPurpleRingTiming(rule);
        foreach (var rule in profile.Rules.Where(rule => rule.Enabled && !ThroneCombatRunner.Supports(rule)))
            Log?.Invoke($"{rule.Name}: skipped in Throne mode. Use 1, V, Q, or ready-image 7/8 healing rules.");
        Log?.Invoke("Throne combat: Q defence, ready 7/8 at low HP, V chains, then continuous 1.");
        foreach (var rule in rules.Where(rule => rule.Condition == ConditionType.RegionSnapshotMatches && rule.ReferenceRgb.Length == 0))
            Log?.Invoke($"{rule.Name}: capture its ready reference and select its watch area before it can run.");
        while (true)
        {
            token.ThrowIfCancellationRequested();
            if (NativeMethods.GetForegroundWindow() == gameWindow && gameWindow != IntPtr.Zero)
            {
                var snapshot = await Task.Run(() =>
                {
                    var healing = profile.ThroneHealing;
                    var hp = healing?.Enabled == true ? ScreenProbe.ReadThroneHealth(healing, token) : null;
                    var observations = profile.Rules.Where(ThroneCombatRunner.Supports).Select(rule =>
                    {
                        if (rule.Key is "7" or "8" && (healing?.Enabled != true || hp is null || hp > healing.LowHpPercent))
                        {
                            var detail = healing?.Enabled != true ? "Low-HP healing is disabled."
                                : hp is null ? "HP bar unavailable; healing blocked."
                                : $"HP {hp}% is above the healing threshold {healing.LowHpPercent}%.";
                            var blocked = new RuleObservation(rule.Id, DateTime.UtcNow,
                                new(null, "Not checked while the HP requirement blocks healing."), null, "Healing blocked.", detail);
                            rule.LastInspection = blocked;
                            InspectionUpdated?.Invoke(blocked);
                            return (Rule: rule, Ready: (bool?)false);
                        }
                        var ready = Evaluate(rule, token);
                        if (rule.Key is "7" or "8" && rule.LastInspection is { } observed)
                        {
                            rule.LastInspection = observed with { Context = $"HP {hp}% · heals at or below {healing!.LowHpPercent}%." };
                            InspectionUpdated?.Invoke(rule.LastInspection);
                        }
                        return (Rule: rule, Ready: rule.ObservationValid ? (bool?)ready : null);
                    }).ToList();
                    return (Observations: observations, LowHealth: healing?.Enabled == true && hp is not null && hp <= healing.LowHpPercent);
                }, token);
                token.ThrowIfCancellationRequested();
                var chosen = runner.Choose(snapshot.Observations, clock.ElapsedMilliseconds, snapshot.LowHealth);
                if (chosen is not null && NativeMethods.GetForegroundWindow() == gameWindow)
                {
                    ReportAction(chosen, $"Running {chosen.ActionSummary}.");
                    await InputSimulator.ExecuteAsync(chosen, token);
                    ReportAction(chosen, "Action completed; Throne controls priority and rearming.");
                    runner.MarkSent(chosen, clock.ElapsedMilliseconds);
                    Log?.Invoke($"{chosen.Name}: sent {chosen.Key}");
                    if (chosen.DelayAfterActionMs > 0) await Task.Delay(chosen.DelayAfterActionMs, token);
                }
            }
            await Task.Delay(Math.Clamp(profile.PollIntervalMs, 20, 2000), token);
        }
    }

    private static void ReleaseHeldKey(MacroRule rule)
    {
        if (!rule.KeyHoldActive)
        {
            return;
        }

        try
        {
            InputSimulator.SendKeyUp(rule.Key, rule.KeyboardInputMode);
        }
        catch (Exception ex) when (ex is InvalidOperationException or DllNotFoundException)
        {
            // The engine's final cleanup still releases any remaining
            // generated inputs if the individual key release fails.
        }
        finally
        {
            rule.KeyHoldActive = false;
        }
    }

    private bool Evaluate(MacroRule rule, CancellationToken token)
    {
        var result = EvaluateCore(rule, token, applyRingTiming: true);
        if (rule.LastInspection is { } observation) InspectionUpdated?.Invoke(observation);
        return result;
    }

    private bool EvaluateCore(MacroRule rule, CancellationToken token, bool applyRingTiming, bool inspectAllConditions = false)
    {
        token.ThrowIfCancellationRequested();
        rule.CurrentMatch = null;
        rule.LastImageScore = null;
        rule.ImageSearchDiagnostic = "";
        rule.ObservationValid = false;
        MatchLocation? match = null;
        ConditionObservation primary;
        if (rule.Condition == ConditionType.AionTargetBarMatches)
        {
            primary = new(null, AionTargetBarMatcher.ReferenceProblem(rule.ReferenceRgb, rule.WatchWidth, rule.WatchHeight)
                ?? $"Watch area {rule.SearchWidth}×{rule.SearchHeight} must contain the {rule.WatchWidth}×{rule.WatchHeight} reference and be no larger than 3840×2160.");
            if (AionTargetBarMatcher.ValidReference(rule.ReferenceRgb, rule.WatchWidth, rule.WatchHeight)
                && rule.SearchWidth >= rule.WatchWidth && rule.SearchHeight >= rule.WatchHeight
                && rule.SearchWidth <= ScreenProbe.MaxSearchWidth && rule.SearchHeight <= ScreenProbe.MaxSearchHeight)
            {
                if (ScreenProbe.TryCaptureRegion(rule.SearchX, rule.SearchY, rule.SearchWidth, rule.SearchHeight, out var frame,
                    ScreenProbe.MaxSearchWidth * ScreenProbe.MaxSearchHeight))
                {
                    var result = AionTargetBarMatcher.Find(frame, rule.SearchWidth, rule.SearchHeight, rule.ReferenceRgb,
                        rule.WatchWidth, rule.WatchHeight, rule.CoverageThreshold, token);
                    match = result.Location is { } point ? new(point.X + rule.SearchX, point.Y + rule.SearchY) : null;
                    primary = new(match.HasValue, match.HasValue
                        ? $"Target {(AionTargetBarMatcher.IsMarkerReference(rule.WatchWidth, rule.WatchHeight) ? "arrow" : "HP end markers")} color match {result.Score:F1}% (requires {rule.CoverageThreshold}%)."
                        : $"No target {(AionTargetBarMatcher.IsMarkerReference(rule.WatchWidth, rule.WatchHeight) ? "arrow" : "HP end markers")} color match at {rule.CoverageThreshold}%.");
                }
                else primary = new(null, "Target HP bar capture is unavailable.");
            }
            rule.ImageSearchDiagnostic = primary.Detail;
        }
        else if (rule.Condition == ConditionType.PurpleRingMatches)
        {
            match = ScreenProbe.FindPurpleRing(rule, token, applyRingTiming);
            primary = new(rule.ObservationValid ? match.HasValue : null, rule.ImageSearchDiagnostic);
        }
        else if (rule.SearchReference && rule.Condition.IsReference())
        {
            match = ScreenProbe.FindReference(rule, token);
            primary = new(rule.ObservationValid ? match.HasValue : null, rule.ImageSearchDiagnostic);
            if (rule.Condition == ConditionType.RegionSnapshotDiffers)
            {
                primary = Conditions.Invert(primary);
                match = null; // Absence has no matched mouse target.
            }
        }
        else
        {
            primary = ObserveCondition(rule.Condition, rule.WatchX, rule.WatchY, rule.WatchWidth, rule.WatchHeight,
                new RgbColor(rule.TargetRed, rule.TargetGreen, rule.TargetBlue), rule.ReferenceRgb,
                rule.Tolerance, rule.CoverageThreshold, token);
            if (rule.Condition.IsTimer()) rule.ImageSearchDiagnostic = primary.Detail;
            if (primary.Passed == true)
            {
                if (rule.Condition == ConditionType.PixelMatches) match = new(rule.WatchX, rule.WatchY);
                else if (rule.Condition == ConditionType.RegionSnapshotMatches)
                    match = new(rule.WatchX + rule.WatchWidth / 2, rule.WatchY + rule.WatchHeight / 2);
            }
        }

        ConditionObservation? gate = null;
        if (rule.GateEnabled)
            gate = !rule.GateAreaSelected
                ? new(null, "Select or capture the Auto Move gate area before running.")
                : primary.Passed == true || inspectAllConditions
                ? ObserveCondition(rule.GateCondition, rule.GateX, rule.GateY, rule.GateWidth, rule.GateHeight,
                    new RgbColor(rule.GateTargetRed, rule.GateTargetGreen, rule.GateTargetBlue), rule.GateReferenceRgb,
                    rule.GateTolerance, rule.GateCoverageThreshold, token)
                : new(null, "Not checked because the main condition has not passed.");

        var passed = primary.Passed == true && (!rule.GateEnabled || gate?.Passed == true);
        rule.ObservationValid = primary.Passed.HasValue && (primary.Passed != true || !rule.GateEnabled || gate!.Passed.HasValue);
        rule.CurrentMatch = passed ? match : null;
        rule.LastInspection = new(rule.Id, DateTime.UtcNow, primary, gate,
            !rule.Enabled ? "Rule disabled."
            : !passed ? "Waiting for conditions."
            : "Conditions passed; engine applies timing, repeat mode and priority.");
        return passed;
    }

    private static ConditionObservation ObserveCondition(ConditionType condition, int x, int y, int width, int height,
        RgbColor target, byte[] referenceRgb, int tolerance, int threshold, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        switch (condition)
        {
            case ConditionType.Always:
                return new(true, "Always enabled condition.");
            case ConditionType.PixelMatches:
            case ConditionType.PixelDiffers:
                var captured = ScreenProbe.TryReadPixel(x, y, out var pixel);
                return ConditionObservation.Pixel(captured, pixel, target, tolerance, condition == ConditionType.PixelDiffers);
            case ConditionType.RegionCoverageAtLeast:
            case ConditionType.RegionCoverageAtMost:
                return ConditionObservation.Percent(ScreenProbe.Coverage(x, y, width, height, target, tolerance, token),
                    threshold, condition == ConditionType.RegionCoverageAtMost, "Colour coverage");
            case ConditionType.RegionSnapshotMatches:
            case ConditionType.RegionSnapshotDiffers:
                var reference = ConditionObservation.Percent(ScreenProbe.ReferenceMatchPercent(x, y, width, height, referenceRgb, tolerance, token),
                    threshold, false, "Reference match");
                return condition == ConditionType.RegionSnapshotDiffers ? Conditions.Invert(reference) : reference;
            case ConditionType.CooldownTimerPresent:
            case ConditionType.CooldownTimerAbsent:
                if (!ScreenProbe.TryCaptureRegion(x, y, width, height, out var rgb, CooldownTimerReader.MaxWidth * CooldownTimerReader.MaxHeight))
                    return new(null, "Windows could not capture the timer area; readiness is unavailable.");
                return CooldownTimerReader.Read(rgb, width, height, token).Observe(condition == ConditionType.CooldownTimerAbsent);
            default:
                return new(null, "This condition is not supported here.");
        }
    }
}
