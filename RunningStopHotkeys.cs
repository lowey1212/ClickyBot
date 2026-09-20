namespace ClickyBot;

// RegisterHotKey matches modifiers exactly. A macro holding Shift turns F6
// into Shift+F6, so reserve the modified stop keys for the duration of a run.
internal sealed class RunningStopHotkeys
{
    private readonly Func<IntPtr, int, uint, uint, bool> _register;
    private readonly Func<IntPtr, int, bool> _unregister;
    private readonly HashSet<int> _registeredIds = [];
    private IntPtr _handle;

    internal RunningStopHotkeys()
        : this(NativeMethods.RegisterHotKey, NativeMethods.UnregisterHotKey) { }

    internal RunningStopHotkeys(
        Func<IntPtr, int, uint, uint, bool> register,
        Func<IntPtr, int, bool> unregister)
    {
        _register = register;
        _unregister = unregister;
    }

    internal bool IsStopMessage(int id) => _registeredIds.Contains(id);

    internal List<string> Register(IntPtr handle, uint toggleKey)
    {
        Clear();
        _handle = handle;
        var failures = new List<string>();
        // Win32 modifier bits: Alt=1, Ctrl=2, Shift=4, Windows=8.
        foreach (var (key, idBase) in new[] { (toggleKey, 0x100), (NativeMethods.VkF7, 0x200) })
        {
            for (uint modifiers = 1; modifiers <= 15; modifiers++)
            {
                var id = idBase + (int)modifiers;
                if (_register(handle, id, modifiers | NativeMethods.ModNoRepeat, key))
                    _registeredIds.Add(id);
                else
                    failures.Add($"{ModifierName(modifiers)}F{key - 0x70 + 1}");
            }
        }
        return failures;
    }

    internal void Clear()
    {
        foreach (var id in _registeredIds)
            _unregister(_handle, id);
        _registeredIds.Clear();
    }

    private static string ModifierName(uint modifiers) =>
        ((modifiers & 2) != 0 ? "Ctrl+" : "") +
        ((modifiers & 1) != 0 ? "Alt+" : "") +
        ((modifiers & 4) != 0 ? "Shift+" : "") +
        ((modifiers & 8) != 0 ? "Win+" : "");
}
