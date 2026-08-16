using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Input;

namespace ClickyBot;

internal sealed class InputRecorder : IDisposable
{
    private readonly NativeMethods.HookProc _keyboardCallback;
    private readonly NativeMethods.HookProc _mouseCallback;
    private IntPtr _keyboardHook;
    private IntPtr _mouseHook;
    private long _lastTimestamp;
    private volatile bool _recording;
    private static readonly string[] FunctionKeyNames =
    [
        "F1", "F2", "F3", "F4", "F5", "F6", "F7", "F8", "F9", "F10", "F11", "F12"
    ];

    public event Action<RecordedStep>? StepRecorded;
    public event Action? StopRequested;

    public bool IsRecording => _recording;

    public InputRecorder()
    {
        _keyboardCallback = KeyboardHook;
        _mouseCallback = MouseHook;
    }

    public void Start()
    {
        Stop();
        var module = NativeMethods.GetModuleHandle(null);
        _keyboardHook = NativeMethods.SetWindowsHookEx(NativeMethods.WhKeyboardLl, _keyboardCallback, module, 0);
        _mouseHook = NativeMethods.SetWindowsHookEx(NativeMethods.WhMouseLl, _mouseCallback, module, 0);
        if (_keyboardHook == IntPtr.Zero || _mouseHook == IntPtr.Zero)
        {
            Stop();
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows could not start the input recorder.");
        }

        _lastTimestamp = Stopwatch.GetTimestamp();
        _recording = true;
    }

    public void Stop()
    {
        _recording = false;
        if (_keyboardHook != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_keyboardHook);
            _keyboardHook = IntPtr.Zero;
        }
        if (_mouseHook != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_mouseHook);
            _mouseHook = IntPtr.Zero;
        }
    }

    public void Dispose() => Stop();

    private IntPtr KeyboardHook(int code, IntPtr wParam, IntPtr lParam)
    {
        var hook = _keyboardHook;
        if (code >= 0 && _recording)
        {
            var data = Marshal.PtrToStructure<NativeMethods.KBDLLHOOKSTRUCT>(lParam);
            if ((data.Flags & NativeMethods.KeyboardInjected) == 0)
            {
                var virtualKey = (int)data.VirtualKeyCode;
                var message = wParam.ToInt32();
                var isKeyDown = message is NativeMethods.WmKeyDown or NativeMethods.WmSysKeyDown;
                var isKeyUp = message is NativeMethods.WmKeyUp or NativeMethods.WmSysKeyUp || (data.Flags & NativeMethods.KeyboardKeyUp) != 0;
                if (isKeyDown && virtualKey == 0x76) // F7 ends recording and remains the panic stop.
                {
                    Stop();
                    StopRequested?.Invoke();
                }
                else if ((isKeyDown || isKeyUp) && TryGetKeyName(virtualKey, out var key))
                {
                    StepRecorded?.Invoke(new RecordedStep
                    {
                        Type = isKeyUp ? RecordedStepType.KeyUp : RecordedStepType.KeyDown,
                        Key = key,
                        DelayBeforeMs = NextDelay()
                    });
                }
            }
        }

        return NativeMethods.CallNextHookEx(hook, code, wParam, lParam);
    }

    private IntPtr MouseHook(int code, IntPtr wParam, IntPtr lParam)
    {
        var hook = _mouseHook;
        if (code >= 0 && _recording)
        {
            var message = wParam.ToInt32();
            var button = message switch
            {
                NativeMethods.WmLButtonUp => MouseButtonType.Left,
                NativeMethods.WmRButtonUp => MouseButtonType.Right,
                NativeMethods.WmMButtonUp => MouseButtonType.Middle,
                _ => (MouseButtonType?)null
            };
            if (button.HasValue)
            {
                var data = Marshal.PtrToStructure<NativeMethods.MSLLHOOKSTRUCT>(lParam);
                if ((data.Flags & NativeMethods.MouseInjected) == 0)
                {
                    StepRecorded?.Invoke(new RecordedStep
                    {
                        Type = RecordedStepType.MouseClick,
                        ClickX = data.Point.X,
                        ClickY = data.Point.Y,
                        MouseButton = button.Value,
                        DelayBeforeMs = NextDelay()
                    });
                }
            }
        }

        return NativeMethods.CallNextHookEx(hook, code, wParam, lParam);
    }

    private int NextDelay()
    {
        var now = Stopwatch.GetTimestamp();
        var elapsed = (now - _lastTimestamp) * 1000d / Stopwatch.Frequency;
        _lastTimestamp = now;
        return Math.Clamp((int)Math.Round(elapsed), 0, 60000);
    }

    private static bool TryGetKeyName(int virtualKey, out string key)
    {
        if (virtualKey is >= 0x30 and <= 0x39)
        {
            key = ((char)virtualKey).ToString();
            return true;
        }
        if (virtualKey is >= 0x41 and <= 0x5A)
        {
            key = ((char)virtualKey).ToString();
            return true;
        }

        key = virtualKey switch
        {
            0x08 => "Backspace",
            0x09 => "Tab",
            0x0D => "Enter",
            0x10 => "Shift",
            0x11 => "Ctrl",
            0x12 => "Alt",
            0x1B => "Escape",
            0x20 => "Space",
            0x21 => "PageUp",
            0x22 => "PageDown",
            0x23 => "End",
            0x24 => "Home",
            0x25 => "Left",
            0x26 => "Up",
            0x27 => "Right",
            0x28 => "Down",
            0x2D => "Insert",
            0x2E => "Delete",
            >= 0x70 and <= 0x7B => FunctionKeyNames[virtualKey - 0x70],
            0xA0 => "LeftShift",
            0xA1 => "RightShift",
            0xA2 => "LeftCtrl",
            0xA3 => "RightCtrl",
            0xA4 => "LeftAlt",
            0xA5 => "RightAlt",
            _ => KeyInterop.KeyFromVirtualKey(virtualKey).ToString()
        };

        return !string.IsNullOrWhiteSpace(key) && key != "None";
    }
}
