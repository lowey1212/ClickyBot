using System.Runtime.InteropServices;

namespace ClickyBot;

// RegisterHotKey does not reach ClickyBot in every full-screen game. Observe
// physical start/stop keys as a fallback without swallowing the game's input.
internal sealed class PhysicalStartStopHotkeys : IDisposable
{
    private readonly Func<uint> _toggleKey;
    private readonly Action<uint> _keyPressed;
    private readonly NativeMethods.HookProc _callback;
    private readonly HashSet<uint> _keysDown = [];
    private IntPtr _hook;

    internal PhysicalStartStopHotkeys(Func<uint> toggleKey, Action<uint> keyPressed)
    {
        _toggleKey = toggleKey;
        _keyPressed = keyPressed;
        _callback = KeyboardHook;
    }

    internal bool Start(out int error)
    {
        error = 0;
        if (_hook != IntPtr.Zero) return true;
        _hook = NativeMethods.SetWindowsHookEx(NativeMethods.WhKeyboardLl,
            _callback, NativeMethods.GetModuleHandle(null), 0);
        if (_hook != IntPtr.Zero) return true;
        error = Marshal.GetLastWin32Error();
        return false;
    }

    private IntPtr KeyboardHook(int code, IntPtr messagePointer, IntPtr dataPointer)
    {
        if (code >= 0)
        {
            var message = messagePointer.ToInt32();
            if (message is NativeMethods.WmKeyDown or NativeMethods.WmSysKeyDown
                or NativeMethods.WmKeyUp or NativeMethods.WmSysKeyUp)
            {
                var data = Marshal.PtrToStructure<NativeMethods.KBDLLHOOKSTRUCT>(dataPointer);
                if ((data.Flags & NativeMethods.KeyboardInjected) == 0)
                {
                    var key = data.VirtualKeyCode;
                    if (message is NativeMethods.WmKeyUp or NativeMethods.WmSysKeyUp)
                        _keysDown.Remove(key);
                    else if ((key == _toggleKey() || key == NativeMethods.VkF7)
                             && _keysDown.Add(key))
                        _keyPressed(key);
                }
            }
        }

        return NativeMethods.CallNextHookEx(_hook, code, messagePointer, dataPointer);
    }

    public void Dispose()
    {
        if (_hook != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }
        _keysDown.Clear();
    }
}
