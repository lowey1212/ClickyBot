namespace ClickyBot;

internal sealed class MacroEngine
{
    public event Action<string>? Log;

    internal bool EvaluateNow(MacroRule rule) => Evaluate(rule, CancellationToken.None);

    public async Task RunAsync(MacroProfile profile, CancellationToken token)
    {
        foreach (var rule in profile.Rules)
        {
            rule.LastCondition = false;
            rule.LastTriggeredUtc = DateTime.MinValue;
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
                if (!rule.Enabled)
                {
                    continue;
                }

                token.ThrowIfCancellationRequested();
                var condition = Evaluate(rule, token);
                var risingEdge = condition && !rule.LastCondition;
                var shouldTrigger = condition && (rule.Repeat == RepeatMode.WhileTrue || risingEdge);

                if (shouldTrigger
                    && DateTime.UtcNow - rule.LastTriggeredUtc >= TimeSpan.FromMilliseconds(Math.Max(0, rule.CooldownMs)))
                {
                    try
                    {
                        await InputSimulator.ExecuteAsync(rule, token);
                        rule.LastTriggeredUtc = DateTime.UtcNow;
                        Log?.Invoke($"{rule.Name}: sent {rule.ActionSummary}");
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

    private bool Evaluate(MacroRule rule, CancellationToken token)
    {
        var primary = EvaluateCondition(
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

        if (!primary || !rule.GateEnabled)
        {
            return primary;
        }

        return EvaluateCondition(
            rule.GateCondition,
            rule.GateX,
            rule.GateY,
            rule.GateWidth,
            rule.GateHeight,
            new RgbColor(rule.GateTargetRed, rule.GateTargetGreen, rule.GateTargetBlue),
            rule.GateReferenceRgb,
            rule.GateTolerance,
            rule.GateCoverageThreshold,
            token);
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
