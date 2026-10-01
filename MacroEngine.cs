namespace ClickyBot;

internal sealed class MacroEngine
{
    public event Action<string>? Log;

    internal bool EvaluateNow(MacroRule rule) => EvaluateCore(rule, CancellationToken.None, applyRingTiming: false);

    public async Task RunAsync(MacroProfile profile, CancellationToken token, BarPresenceProbe? barProbe = null)
    {
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

        var observedRules = new HashSet<Guid>();
        foreach (var rule in profile.Rules)
        {
            rule.LastCondition = false;
            rule.LastTriggeredUtc = DateTime.MinValue;
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
            foreach (var rule in profile.Rules)
            {
                if (!rule.Enabled || rule.ThroneHealingOnly)
                {
                    ReleaseHeldKey(rule);
                    continue;
                }

                token.ThrowIfCancellationRequested();
                var condition = rule.SearchReference && rule.Condition == ConditionType.RegionSnapshotMatches
                    ? await Task.Run(() => Evaluate(rule, token), token)
                    : Evaluate(rule, token);
                token.ThrowIfCancellationRequested();
                var risingEdge = condition && !rule.LastCondition;
                if (observedRules.Add(rule.Id) || condition != rule.LastCondition)
                {
                    var detail = rule.CurrentMatch is { } location ? $" at {location.X},{location.Y}" : "";
                    Log?.Invoke(condition ? $"{rule.Name}: condition passed{detail}. {rule.ImageSearchDiagnostic}"
                        : $"{rule.Name}: waiting for a match (including any AND gate). {rule.ImageSearchDiagnostic}");
                }

                if (rule.Action == ActionType.KeyHold)
                {
                    if (condition && !rule.KeyHoldActive)
                    {
                        try
                        {
                            InputSimulator.SendKeyDown(rule.Key);
                            rule.KeyHoldActive = true;
                            rule.LastTriggeredUtc = DateTime.UtcNow;
                            Log?.Invoke($"{rule.Name}: holding {rule.Key}");
                        }
                        catch (Exception ex) when (ex is InvalidOperationException or DllNotFoundException)
                        {
                            Log?.Invoke($"{rule.Name}: action failed — {ex.Message}");
                        }
                    }
                    else if (!condition)
                    {
                        ReleaseHeldKey(rule);
                    }

                    rule.LastCondition = condition;
                    continue;
                }

                ReleaseHeldKey(rule);
                var shouldTrigger = condition && (rule.Repeat == RepeatMode.WhileTrue || risingEdge);

                if (shouldTrigger
                    && DateTime.UtcNow - rule.LastTriggeredUtc >= TimeSpan.FromMilliseconds(Math.Max(0, rule.CooldownMs)))
                {
                    try
                    {
                        await InputSimulator.ExecuteAsync(rule, token);
                        rule.LastTriggeredUtc = DateTime.UtcNow;
                        var targetDetail = rule.Action is ActionType.MouseClick or ActionType.MouseMove
                            ? $" at {rule.ResolveMouseTarget().X},{rule.ResolveMouseTarget().Y}" : "";
                        Log?.Invoke($"{rule.Name}: sent {rule.ActionSummary}{targetDetail}");
                        if (rule.DelayAfterActionMs > 0)
                        {
                            await Task.Delay(rule.DelayAfterActionMs, token);
                        }
                    }
                    catch (Exception ex) when (ex is InvalidOperationException or DllNotFoundException)
                    {
                        Log?.Invoke($"{rule.Name}: action failed — {ex.Message}");
                    }
                }

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
                            return (Rule: rule, Ready: (bool?)false);
                        var ready = Evaluate(rule, token);
                        return (Rule: rule, Ready: rule.ObservationValid ? (bool?)ready : null);
                    }).ToList();
                    return (Observations: observations, LowHealth: healing?.Enabled == true && hp is not null && hp <= healing.LowHpPercent);
                }, token);
                token.ThrowIfCancellationRequested();
                var chosen = runner.Choose(snapshot.Observations, clock.ElapsedMilliseconds, snapshot.LowHealth);
                if (chosen is not null && NativeMethods.GetForegroundWindow() == gameWindow)
                {
                    await InputSimulator.ExecuteAsync(chosen, token);
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
            InputSimulator.SendKeyUp(rule.Key);
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
        => EvaluateCore(rule, token, applyRingTiming: true);

    private bool EvaluateCore(MacroRule rule, CancellationToken token, bool applyRingTiming)
    {
        rule.CurrentMatch = null;
        rule.LastImageScore = null;
        rule.ImageSearchDiagnostic = "";
        rule.ObservationValid = false;
        MatchLocation? match = null;
        bool primary;
        if (rule.Condition == ConditionType.PurpleRingMatches)
        {
            match = ScreenProbe.FindPurpleRing(rule, token, applyRingTiming);
            primary = match.HasValue;
        }
        else if (rule.SearchReference && rule.Condition == ConditionType.RegionSnapshotMatches)
        {
            match = ScreenProbe.FindReference(rule, token);
            primary = match.HasValue;
        }
        else
        {
            rule.ObservationValid = true;
            primary = EvaluateCondition(
                rule.Condition,
                rule.WatchX,
                rule.WatchY,
                rule.WatchWidth,
                rule.WatchHeight,
                new RgbColor(rule.TargetRed, rule.TargetGreen, rule.TargetBlue),
                rule.ReferenceRgb,
                rule.Tolerance,
                rule.CoverageThreshold,
                token);

            if (primary)
            {
                if (rule.Condition == ConditionType.PixelMatches)
                    match = new MatchLocation(rule.WatchX, rule.WatchY);
                else if (rule.Condition == ConditionType.RegionSnapshotMatches)
                    match = new MatchLocation(rule.WatchX + rule.WatchWidth / 2, rule.WatchY + rule.WatchHeight / 2);
            }
        }

        if (!primary) return false;

        if (rule.GateEnabled && !EvaluateCondition(
            rule.GateCondition,
            rule.GateX,
            rule.GateY,
            rule.GateWidth,
            rule.GateHeight,
            new RgbColor(rule.GateTargetRed, rule.GateTargetGreen, rule.GateTargetBlue),
            rule.GateReferenceRgb,
            rule.GateTolerance,
            rule.GateCoverageThreshold,
            token)) return false;

        rule.CurrentMatch = match;
        return true;
    }

    private static bool EvaluateCondition(
        ConditionType condition,
        int x,
        int y,
        int width,
        int height,
        RgbColor target,
        byte[] referenceRgb,
        int tolerance,
        int coverageThreshold,
        CancellationToken token)
    {
        return condition switch
        {
            ConditionType.Always => true,
            ConditionType.PixelMatches => ScreenProbe.TryReadPixel(x, y, out var pixel)
                && pixel.IsCloseTo(target, Math.Clamp(tolerance, 0, 255)),
            // A failed capture must fail closed. Treating an unreadable pixel as
            // "different" could fire an action while the desktop is unavailable.
            ConditionType.PixelDiffers => ScreenProbe.TryReadPixel(x, y, out var differentPixel)
                && !differentPixel.IsCloseTo(target, Math.Clamp(tolerance, 0, 255)),
            ConditionType.RegionCoverageAtLeast => ScreenProbe.Coverage(
                    x, y, width, height, target, Math.Clamp(tolerance, 0, 255), token)
                >= Math.Clamp(coverageThreshold, 0, 100),
            ConditionType.RegionCoverageAtMost => CoverageAtMost(
                x, y, width, height, target, tolerance, coverageThreshold, token),
            ConditionType.RegionSnapshotMatches => ScreenProbe.ReferenceMatchPercent(
                    x, y, width, height, referenceRgb, tolerance, token)
                >= Math.Clamp(coverageThreshold, 0, 100),
            _ => false
        };
    }

    private static bool CoverageAtMost(
        int x,
        int y,
        int width,
        int height,
        RgbColor target,
        int tolerance,
        int threshold,
        CancellationToken token)
    {
        var coverage = ScreenProbe.Coverage(x, y, width, height, target, Math.Clamp(tolerance, 0, 255), token);
        return coverage >= 0 && coverage <= Math.Clamp(threshold, 0, 100);
    }
}
