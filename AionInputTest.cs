using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ClickyBot;

// Deliberately separate from normal macro input until in-game compatibility is verified.
internal sealed class AionInputTest(Action<bool> sendF)
{
    private bool _held;

    internal async Task RunAsync(Func<nint> readyTarget, Func<int, CancellationToken, Task> delay,
        Action<string> log, CancellationToken token)
    {
        log("F input test: legacy Windows keybd_event. Focus Aion 2 with an F prompt; F7 cancels.");
        for (var seconds = 5; seconds > 0; seconds--)
        {
            token.ThrowIfCancellationRequested();
            log($"F input test in {seconds}…");
            await delay(1000, token);
        }
        token.ThrowIfCancellationRequested();
        var target = readyTarget();
        if (target == 0)
        {
            log("F input test cancelled: focus AION2.exe and release F, Shift, Ctrl, Alt and Windows keys. No input requested.");
            return;
        }

        try
        {
            token.ThrowIfCancellationRequested();
            if (readyTarget() != target)
            {
                log("F input test cancelled: focus changed. No input requested.");
                return;
            }
            _held = true;
            sendF(true);
            // Short intervals allow focus loss and emergency stop to release promptly.
            for (var elapsed = 0; elapsed < 200; elapsed += 20)
            {
                await delay(20, token);
                if (readyTarget() != target)
                {
                    log("F input test stopped early: game focus or modifier state changed.");
                    return;
                }
            }
        }
        finally { ReleaseHeldKey(); }
        log("F input test: requested one 200 ms F tap via keybd_event. Windows provides no delivery result; check whether the game responded.");
    }

    internal void ReleaseHeldKey()
    {
        if (!_held) return;
        _held = false;
        sendF(false);
    }

    internal static void SendLegacyF(bool down) => KeybdEvent(0x46,
        (byte)MapVirtualKey(0x46, 0), down ? 0u : 2u, UIntPtr.Zero);

    internal static bool PhysicalFHeld() => (GetAsyncKeyState(0x46) & 0x8000) != 0;

    internal static nint ReadyAionTarget()
    {
        foreach (var key in new[] { 0x10, 0x11, 0x12, 0x5B, 0x5C })
            if ((GetAsyncKeyState(key) & 0x8000) != 0) return 0;
        var window = GetForegroundWindow();
        if (window == 0) return 0;
        GetWindowThreadProcessId(window, out var pid);
        try
        {
            using var process = Process.GetProcessById((int)pid);
            return string.Equals(process.ProcessName, "AION2", StringComparison.OrdinalIgnoreCase)
                && GetForegroundWindow() == window ? window : 0;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        { return 0; }
    }

    [DllImport("user32.dll", EntryPoint = "keybd_event")]
    private static extern void KeybdEvent(byte key, byte scan, uint flags, UIntPtr extraInfo);
    [DllImport("user32.dll")]
    private static extern uint MapVirtualKey(uint code, uint mapType);
    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint window, out uint processId);
}
