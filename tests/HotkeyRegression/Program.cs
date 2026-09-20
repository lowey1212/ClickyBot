using ClickyBot;

// Model Win32's exact modifier matching without injecting keys into the user's
// game. Exercise the same registration ownership and message routing as the app.
var registered = new Dictionary<int, (uint Modifiers, uint Key)>();
bool Register(IntPtr handle, int id, uint modifiers, uint key)
{
    var binding = (modifiers & ~NativeMethods.ModNoRepeat, key);
    if (registered.ContainsValue(binding)) return false;
    registered.Add(id, binding);
    return true;
}
bool Unregister(IntPtr handle, int id) => registered.Remove(id);
int Find(uint key, uint modifiers) => registered
    .Where(pair => pair.Value == (modifiers, key))
    .Select(pair => pair.Key).DefaultIfEmpty(-1).Single();
void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}

var handle = new IntPtr(42);
var shortcuts = new RunningStopHotkeys(Register, Unregister);
foreach (uint key in new uint[] { 0x73, 0x75, 0x7B }) // F4, F6, F12
{
    registered.Clear();
    Register(handle, 1, NativeMethods.ModNoRepeat, key);
    Register(handle, 2, NativeMethods.ModNoRepeat, NativeMethods.VkF7);
    Check(Find(key, 4) == -1 && Find(NativeMethods.VkF7, 4) == -1,
        "Reproduce: plain registrations cannot stop while Shift is held.");
    Check(shortcuts.Register(handle, key).Count == 0, "Unexpected registration conflict.");
    for (uint modifiers = 1; modifiers <= 15; modifiers++)
    {
        Check(shortcuts.IsStopMessage(Find(key, modifiers)), "Configured key cannot stop with modifiers.");
        Check(shortcuts.IsStopMessage(Find(NativeMethods.VkF7, modifiers)), "Panic stop cannot stop with modifiers.");
    }
    Check(!shortcuts.IsStopMessage(1) && !shortcuts.IsStopMessage(3), "Unrelated messages became stop events.");
    int queuedStop = Find(key, 4);
    shortcuts.Clear();
    Check(registered.Count == 2, "Stop must preserve existing plain hotkeys.");
    Check(!shortcuts.IsStopMessage(queuedStop), "A stale modified stop must not restart automation.");
    shortcuts.Clear();
}

registered.Clear();
shortcuts.Register(handle, 0x73);
shortcuts.Register(handle, 0x75);
Check(Find(0x73, 4) == -1 && shortcuts.IsStopMessage(Find(0x75, 4)),
    "Changing settings while running must replace the old modified hotkeys.");
shortcuts.Clear();

Register(handle, 999, 4, 0x75); // Another owner already has Shift+F6.
var failures = shortcuts.Register(handle, 0x75);
Check(failures.SequenceEqual(new[] { "Shift+F6" }), "Conflicts must identify the unavailable shortcut.");
Check(!shortcuts.IsStopMessage(999), "Do not claim another owner's hotkey.");
shortcuts.Clear();
Check(registered.Count == 1 && registered.ContainsKey(999), "Cleanup removed another owner's shortcut.");
Console.WriteLine("PASS: F4/F6/F12 and F7 with all 15 modifier combinations; cleanup, rebind, stale events, conflicts.");
