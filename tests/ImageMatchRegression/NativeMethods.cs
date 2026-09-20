using System.Runtime.InteropServices;

namespace ClickyBot;

// Test-only input sink: exercise production mouse actions without moving the
// user's pointer or sending any real keyboard/mouse events.
internal static class NativeMethods
{
    internal const uint KeyboardScanCode = 8;
    internal static POINT Cursor = new() { X = 10, Y = 20 };
    internal static readonly List<string> Events = [];
    internal static bool SetCursorPos(int x, int y)
    {
        Cursor = new POINT { X = x, Y = y };
        Events.Add($"move:{x},{y}");
        return true;
    }
    internal static bool GetCursorPos(out POINT point) { point = Cursor; return true; }
    internal static uint MapVirtualKey(uint code, uint type) => code;
    internal static uint SendInput(uint count, INPUT[] inputs, int size)
    {
        for (int i = 0; i < count; i++)
            Events.Add($"input:{inputs[i].Type}:{inputs[i].Union.Mouse.Flags}");
        return count;
    }
    [StructLayout(LayoutKind.Sequential)]
    internal struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)]
    internal struct INPUT { public uint Type; public InputUnion Union; }
    [StructLayout(LayoutKind.Sequential)]
    internal struct InputUnion { public KEYBDINPUT Keyboard; public MOUSEINPUT Mouse; }
    [StructLayout(LayoutKind.Sequential)]
    internal struct KEYBDINPUT { public ushort VirtualKey, ScanCode; public uint Flags; }
    [StructLayout(LayoutKind.Sequential)]
    internal struct MOUSEINPUT { public uint Flags; }
}
