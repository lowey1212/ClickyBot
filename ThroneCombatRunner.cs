namespace ClickyBot;

// Observe both prompts before choosing exactly one input. After responding
// to a prompt, resume 1 while its animation finishes; don't spam Q or V.
internal sealed class ThroneCombatRunner
{
    private sealed class State
    {
        public bool Spent;
        public long? AbsentSince;
        public long LastSent = long.MinValue;
    }

    private readonly Dictionary<Guid, State> _states = [];

    internal MacroRule? Choose(IReadOnlyList<(MacroRule Rule, bool? Ready)> observations, long nowMs)
    {
        foreach (var (rule, ready) in observations)
        {
            if (!_states.TryGetValue(rule.Id, out var state))
                _states[rule.Id] = state = new State();
            if (ready == false)
            {
                state.AbsentSince ??= nowMs;
                if (nowMs - state.AbsentSince >= 120) state.Spent = false;
            }
            else
            {
                // Failed captures must not rearm an already handled prompt.
                state.AbsentSince = null;
            }
        }

        MacroRule? ReadyKey(string key) => observations.FirstOrDefault(item =>
        {
            var rule = item.Rule;
            var state = _states[rule.Id];
            return item.Ready == true && rule.Key.Equals(key, StringComparison.OrdinalIgnoreCase)
                && (!state.Spent || key == "1")
                && (state.LastSent == long.MinValue || nowMs - state.LastSent >= Math.Max(100, rule.CooldownMs));
        }).Rule;

        foreach (var key in new[] { "Q", "V" })
            if (ReadyKey(key) is { } prompt) return prompt;
        return ReadyKey("1");
    }

    internal void MarkSent(MacroRule rule, long nowMs)
    {
        var state = _states[rule.Id];
        state.Spent = true;
        state.LastSent = nowMs;
    }

    internal static bool Supports(MacroRule rule) => rule.Enabled && rule.Action == ActionType.KeyPress
        && (rule.Key.Equals("1", StringComparison.OrdinalIgnoreCase) && rule.Condition == ConditionType.Always
            || rule.Key.Equals("V", StringComparison.OrdinalIgnoreCase) && rule.Condition == ConditionType.RegionSnapshotMatches && rule.SearchReference
            || rule.Key.Equals("Q", StringComparison.OrdinalIgnoreCase) && rule.Condition == ConditionType.PurpleRingMatches);
}
