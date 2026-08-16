using System.Collections.Concurrent;
using System.Windows.Input;

namespace ClickyBot;

internal static class InputSimulator
{
    private const uint InputKeyboard = 1;
    private const uint InputMouse = 0;
    private const uint KeyUp = 0x0002;
    private const uint MouseLeftDown = 0x0002;
    private const uint MouseLeftUp = 0x0004;
    private const uint MouseRightDown = 0x0008;
    private const uint MouseRightUp = 0x0010;
    private const uint MouseMiddleDown = 0x0020;
    private const uint MouseMiddleUp = 0x0040;
    private static readonly int InputSize = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.INPUT>();
    private static readonly ConcurrentDictionary<string, ushort> VirtualKeyCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<ushort, ushort> ScanCodeCache = new();
    private static readonly object HeldInputLock = new();
    private static readonly HashSet<ushort> HeldScanCodes = [];

    public static async Task ExecuteAsync(MacroRule rule, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        switch (rule.Action)
        {
            case ActionType.KeyPress:
                PressKey(rule.Key);
                break;
            case ActionType.MouseClick:
                Click(rule.ClickX, rule.ClickY, rule.MouseButton, rule.RestorePointerAfterClick);
                break;
            case ActionType.Wait:
                break;
            case ActionType.RecordedCombo:
                NativeMethods.INPUT[]? pendingKeyboard = null;
                var pendingCount = 0;
                foreach (var step in rule.RecordedSteps)
                {
                    token.ThrowIfCancellationRequested();
                    if (step.DelayBeforeMs > 0)
                    {
                        FlushPendingKeyboard(pendingKeyboard, ref pendingCount);
                        await Task.Delay(step.DelayBeforeMs, token);
                        token.ThrowIfCancellationRequested();
                    }

                    try
                    {
                        switch (step.Type)
                        {
                            case RecordedStepType.KeyPress:
                                AddPendingKeyboard(ref pendingKeyboard, ref pendingCount, CreateKeyInput(step.Key, 0));
                                AddPendingKeyboard(ref pendingKeyboard, ref pendingCount, CreateKeyInput(step.Key, KeyUp));
                                break;
                            case RecordedStepType.KeyDown:
                                AddPendingKeyboard(ref pendingKeyboard, ref pendingCount, CreateKeyInput(step.Key, 0));
                                break;
                            case RecordedStepType.KeyUp:
                                AddPendingKeyboard(ref pendingKeyboard, ref pendingCount, CreateKeyInput(step.Key, KeyUp));
                                break;
                            case RecordedStepType.MouseClick:
                                FlushPendingKeyboard(pendingKeyboard, ref pendingCount);
                                Click(step.ClickX, step.ClickY, step.MouseButton, rule.RestorePointerAfterClick);
                                break;
                        }
                    }
                    catch
                    {
                        // Preserve the old behavior where earlier combo steps
                        // have already been sent if a later step is invalid.
                        FlushPendingKeyboard(pendingKeyboard, ref pendingCount);
                        throw;
                    }
                }
                FlushPendingKeyboard(pendingKeyboard, ref pendingCount);
                break;
        }
    }

    public static bool TryGetVirtualKey(string text, out ushort virtualKey)
    {
        virtualKey = 0;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        text = text.Trim();
        if (VirtualKeyCache.TryGetValue(text, out virtualKey))
        {
            return virtualKey != 0;
        }

        virtualKey = ResolveVirtualKey(text);
        VirtualKeyCache.TryAdd(text, virtualKey);
        return virtualKey != 0;
    }

    private static ushort ResolveVirtualKey(string text)
    {
        ushort virtualKey;
        if (text.Length == 1 && char.IsDigit(text[0]))
        {
            virtualKey = (ushort)('0' <= text[0] && text[0] <= '9' ? text[0] : 0);
            return virtualKey;
        }

        if (text.Length == 1 && char.IsLetter(text[0]))
        {
            virtualKey = (ushort)char.ToUpperInvariant(text[0]);
            return virtualKey;
        }

        try
        {
            var converter = new KeyConverter();
            if (converter.ConvertFromInvariantString(text) is Key key && key != Key.None)
            {
                virtualKey = (ushort)KeyInterop.VirtualKeyFromKey(key);
                return virtualKey;
            }
        }
        catch (FormatException)
        {
            // Fall through to the small set of friendly aliases below.
        }

        var alias = text.ToLowerInvariant() switch
        {
            "ctrl" or "control" => 0x11,
            "shift" => 0x10,
            "alt" => 0x12,
            "esc" or "escape" => 0x1B,
            "return" or "enter" => 0x0D,
            "spacebar" => 0x20,
            "tab" => 0x09,
            "backspace" => 0x08,
            "left" => 0x25,
            "up" => 0x26,
            "right" => 0x27,
            "down" => 0x28,
            "leftshift" => 0xA0,
            "rightshift" => 0xA1,
            "leftctrl" => 0xA2,
            "rightctrl" => 0xA3,
            "leftalt" => 0xA4,
            "rightalt" => 0xA5,
            _ => 0
        };
        virtualKey = (ushort)alias;
        return virtualKey;
    }

    private static void PressKey(string text)
    {
        EnsureSent([CreateKeyInput(text, 0), CreateKeyInput(text, KeyUp)]);
    }

    internal static void SendKeyDown(string text)
    {
        EnsureSent([CreateKeyInput(text, 0)]);
    }

    internal static void SendKeyUp(string text)
    {
        EnsureSent([CreateKeyInput(text, KeyUp)]);
    }

    internal static bool ReleaseAllHeldInputs()
    {
        ushort[] scanCodes;
        lock (HeldInputLock)
        {
            if (HeldScanCodes.Count == 0)
            {
                return true;
            }

            scanCodes = HeldScanCodes.ToArray();
            HeldScanCodes.Clear();
        }

        var inputs = new NativeMethods.INPUT[scanCodes.Length];
        for (var index = 0; index < scanCodes.Length; index++)
        {
            inputs[index] = new NativeMethods.INPUT
            {
                Type = InputKeyboard,
                Union = new NativeMethods.InputUnion
                {
                    Keyboard = new NativeMethods.KEYBDINPUT
                    {
                        VirtualKey = 0,
                        ScanCode = scanCodes[index],
                        Flags = KeyUp | NativeMethods.KeyboardScanCode
                    }
                }
            };
        }

        var sent = NativeMethods.SendInput((uint)inputs.Length, inputs, InputSize);
        return sent == inputs.Length;
    }

    private static NativeMethods.INPUT CreateKeyInput(string text, uint flags)
    {
        if (!TryGetVirtualKey(text, out var key))
        {
            throw new InvalidOperationException($"'{text}' is not a recognized key.");
        }

        var scanCode = ScanCodeCache.GetOrAdd(key, static value => (ushort)NativeMethods.MapVirtualKey(value, 0));
        if (scanCode == 0)
        {
            throw new InvalidOperationException($"Windows could not map '{text}' to a keyboard scan code.");
        }

        return new NativeMethods.INPUT
        {
            Type = InputKeyboard,
            Union = new NativeMethods.InputUnion
            {
                Keyboard = new NativeMethods.KEYBDINPUT
                {
                    VirtualKey = 0,
                    ScanCode = (ushort)scanCode,
                    Flags = flags | NativeMethods.KeyboardScanCode
                }
            }
        };
    }

    private static void Click(int x, int y, MouseButtonType button, bool restorePointer)
    {
        var original = default(NativeMethods.POINT);
        if (restorePointer)
        {
            NativeMethods.GetCursorPos(out original);
        }
        if (!NativeMethods.SetCursorPos(x, y))
        {
            throw new InvalidOperationException("Windows could not move the pointer to the click target.");
        }

        var (down, up) = button switch
        {
            MouseButtonType.Right => (MouseRightDown, MouseRightUp),
            MouseButtonType.Middle => (MouseMiddleDown, MouseMiddleUp),
            _ => (MouseLeftDown, MouseLeftUp)
        };

        try
        {
            var inputs = new[]
            {
                new NativeMethods.INPUT
                {
                    Type = InputMouse,
                    Union = new NativeMethods.InputUnion
                    {
                        Mouse = new NativeMethods.MOUSEINPUT { Flags = down }
                    }
                },
                new NativeMethods.INPUT
                {
                    Type = InputMouse,
                    Union = new NativeMethods.InputUnion
                    {
                        Mouse = new NativeMethods.MOUSEINPUT { Flags = up }
                    }
                }
            };
            EnsureSent(inputs);
        }
        finally
        {
            if (restorePointer)
            {
                NativeMethods.SetCursorPos(original.X, original.Y);
            }
        }
    }

    private static void AddPendingKeyboard(
        ref NativeMethods.INPUT[]? pending,
        ref int count,
        NativeMethods.INPUT input)
    {
        if (pending is null)
        {
            pending = new NativeMethods.INPUT[4];
        }
        else if (count == pending.Length)
        {
            Array.Resize(ref pending, pending.Length * 2);
        }

        pending[count++] = input;
    }

    private static void FlushPendingKeyboard(NativeMethods.INPUT[]? pending, ref int count)
    {
        if (pending is null || count == 0)
        {
            return;
        }

        EnsureSent(pending, count);
        count = 0;
    }

    private static void EnsureSent(NativeMethods.INPUT[] inputs) => EnsureSent(inputs, inputs.Length);

    private static void EnsureSent(NativeMethods.INPUT[] inputs, int count)
    {
        var sent = NativeMethods.SendInput((uint)count, inputs, InputSize);
        if (sent != count)
        {
            throw new InvalidOperationException("Windows rejected the generated input event.");
        }

        lock (HeldInputLock)
        {
            for (var index = 0; index < count; index++)
            {
                var input = inputs[index];
                if (input.Type != InputKeyboard || (input.Union.Keyboard.Flags & NativeMethods.KeyboardScanCode) == 0)
                {
                    continue;
                }

                if ((input.Union.Keyboard.Flags & KeyUp) != 0)
                {
                    HeldScanCodes.Remove(input.Union.Keyboard.ScanCode);
                }
                else
                {
                    HeldScanCodes.Add(input.Union.Keyboard.ScanCode);
                }
            }
        }
    }
}
