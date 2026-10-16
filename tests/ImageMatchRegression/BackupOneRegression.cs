using ClickyBot;

internal static class BackupOneRegression
{
    internal static async Task Run(MacroProfile profile)
    {
        static void Check(bool passed, string message) { if (!passed) throw new Exception(message); }
        var backup = new MacroRule { Name = "press 1 as a back up", Condition = ConditionType.Always,
            Action = ActionType.KeyPress, Key = "1", Repeat = RepeatMode.WhileTrue,
            CooldownMs = 5000, KeyboardInputMode = KeyboardInputMode.FakerInput };
        Check(AionCombatRunner.IsBackupRule(backup), "The user's fixed-interval 1 rule must run in combat mode.");
        backup.GateEnabled = true;
        Check(!AionCombatRunner.IsBackupRule(backup), "A gated rule must not become an unconditional backup tap.");
        backup.GateEnabled = false;
        backup.Repeat = RepeatMode.OnRisingEdge;
        Check(!AionCombatRunner.IsBackupRule(backup), "Once-per-appearance rules must not become repeating backups.");
        backup.Repeat = RepeatMode.WhileTrue;
        backup.CooldownMs = 100;
        Check(AionCombatRunner.BackupIntervalMs(backup) == 5000,
            "The user's previously ignored 100 ms rule must become a five-second backup, never rapid-fire 1.");
        var previous = FakerInputKeyboard.Shared;
        profile.Rules.Add(backup);
        try
        {
            foreach (var scenario in new[] { "held", "capture-error", "uncertain", "range-error", "focus", "disabled", "driver-error" })
            {
                long clock = 0;
                var reports = new List<(long Time, byte[] Report)>();
                var sent = new List<long>();
                var failures = 0;
                backup.CooldownMs = scenario == "held" ? 5000 : 100;
                backup.Enabled = scenario != "disabled";
                using var stop = new CancellationTokenSource();
                bool Focused() => scenario != "focus" || clock < 6000 || clock >= 16000;
                FakerInputKeyboard.Shared = new(() => new RecordingFakerTransportProxy(report =>
                {
                    if (scenario == "driver-error" && report[2] == 1 && report[5] == 0x1E)
                        throw new InvalidOperationException("Simulated backup driver failure");
                    if (report[2] == 1 && report[5] != 0)
                        Check(Focused(), "No backup or combat key may be pressed while unfocused.");
                    reports.Add((clock, report.ToArray()));
                }));
                var runner = new AionCombatRunner(profile, (rule, _) =>
                {
                    if (rule == backup) return new(rule.Id, DateTime.UtcNow, new(true, "Always"), null, "");
                    if (scenario is "capture-error" or "focus" or "disabled" or "driver-error")
                        throw new InvalidOperationException("Simulated HUD failure");
                    if (rule.Condition == ConditionType.CooldownTimerPresent)
                        return new(rule.Id, DateTime.UtcNow,
                            scenario == "uncertain" ? new(null, "Uncertain OCR", Retryable: true) : new(false, "Ready"), null, "");
                    return new(rule.Id, DateTime.UtcNow, new(true, "Target present"), null, "");
                }, Focused, message =>
                {
                    if (message.Contains(": backup sent 1")) sent.Add(clock);
                    if (message.Contains(": backup action failed")) failures++;
                }, _ => scenario == "range-error" ? new(null, "Range capture failed") : new(false, "No range message"),
                (ms, token) =>
                {
                    token.ThrowIfCancellationRequested();
                    clock += ms;
                    if (clock >= (scenario == "focus" ? 22000 : 16000)) stop.Cancel();
                    return Task.CompletedTask;
                }, () => clock);
                try { await runner.RunAsync(stop.Token); }
                catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
                Check(stop.IsCancellationRequested, "Manual Stop must end a combat session with a backup rule.");
                if (scenario == "driver-error")
                    Check(sent.Count == 0 && failures == 3, "Backup driver errors must retry at the interval without ending combat.");
                else if (scenario == "disabled")
                    Check(sent.Count == 0, "A disabled backup must send no input.");
                else
                {
                    var expected = scenario == "focus" ? new long[] { 5000, 16000, 21000 } : [5000, 10000, 15000];
                    Check(sent.SequenceEqual(expected), $"Backup timing must be five seconds, without an immediate tap or catch-up burst ({scenario}).");
                    Check(sent.All(time => reports.Any(item => item.Time == time && item.Report[2] == 1 && item.Report[5] == 0x1E)),
                        "Each backup completion must correspond to a real production 1 key-down report in the simulated driver.");
                }
                Check(reports.LastOrDefault(item => item.Report[2] == 1).Report is not { } lastKey || lastKey[5] == 0,
                    "Backup input must be released at Stop.");
            }
        }
        finally
        {
            FakerInputKeyboard.Shared.ReleaseAllHeldInputs();
            FakerInputKeyboard.Shared = previous;
            profile.Rules.Remove(backup);
        }
        Console.WriteLine("PASS: five-second backup 1 during held combat, capture/range errors and uncertain OCR; disabled rule, driver retries, focus pause/no catch-up burst and manual Stop; simulated input only.");
    }

    private sealed class RecordingFakerTransportProxy(Action<byte[]> write) : IFakerInputTransport
    {
        public void Write(byte[] report) => write(report);
        public void Dispose() { }
    }
}
