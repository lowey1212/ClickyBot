namespace ClickyBot;

internal sealed class PurpleRingTiming
{
    private long? _firstSeen;
    private long? _absentSince;
    internal void Unknown() => _absentSince = null;

    internal bool Observe(bool visible, int delayMs, long nowMs)
    {
        if (!visible)
        {
            _absentSince ??= nowMs;
            if (nowMs - _absentSince >= 120) _firstSeen = null;
            return false;
        }
        _absentSince = null;
        _firstSeen ??= nowMs;
        return nowMs - _firstSeen >= Math.Clamp(delayMs, 0, 5000);
    }
}
