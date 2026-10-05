using System.Text.Json;
using System.Text.Json.Serialization;
using ClickyBot;

internal static class KeyboardModeRegression
{
    internal static async Task Run()
    {
        void Check(bool passed, string message)
        {
            if (!passed) throw new Exception(message);
        }
        var options = new JsonSerializerOptions { Converters = { new JsonStringEnumConverter() } };
        Check(JsonSerializer.Deserialize<MacroRule>("{\"Key\":\"F\"}", options)!.KeyboardInputMode == KeyboardInputMode.ScanCode,
            "Existing macros must retain scan-code input.");
        var tap = new MacroRule { Key = "F", KeyboardInputMode = KeyboardInputMode.VirtualKey };
        Check(JsonSerializer.Deserialize<MacroRule>(JsonSerializer.Serialize(tap, options), options)!.KeyboardInputMode == KeyboardInputMode.VirtualKey,
            "The compatibility setting must survive save/load.");
        InputSimulator.ReleaseAllHeldInputs();
        NativeMethods.KeyboardInputs.Clear();
        await InputSimulator.ExecuteAsync(tap, default);
        Check(NativeMethods.KeyboardInputs.Select(input => (input.VirtualKey, input.Flags)).SequenceEqual(
            new[] { ((ushort)0x46, 0u), ((ushort)0x46, 2u) }), "Windows key-code taps must send F down/up without SCANCODE.");

        var combo = new MacroRule
        {
            Action = ActionType.RecordedCombo, KeyboardInputMode = KeyboardInputMode.VirtualKey,
            RecordedSteps = [new() { Type = RecordedStepType.KeyDown, Key = "LeftAlt" },
                new() { Type = RecordedStepType.KeyPress, Key = "1" },
                new() { Type = RecordedStepType.KeyUp, Key = "LeftAlt" }]
        };
        NativeMethods.KeyboardInputs.Clear();
        await InputSimulator.ExecuteAsync(combo, default);
        Check(NativeMethods.KeyboardInputs.Select(input => (input.VirtualKey, input.Flags)).SequenceEqual(
            new[] { ((ushort)0xA4, 0u), ((ushort)0x31, 0u), ((ushort)0x31, 2u), ((ushort)0xA4, 2u) }),
            "Every combo step must use the chosen format and preserve modifier ordering.");

        // Stop during a delayed combo: release the outstanding modifier with
        // exactly the format used for its down event. No real input is sent.
        combo.RecordedSteps[1].DelayBeforeMs = 1000;
        NativeMethods.KeyboardInputs.Clear();
        using (var stop = new CancellationTokenSource())
        {
            var pending = InputSimulator.ExecuteAsync(combo, stop.Token);
            Check(NativeMethods.KeyboardInputs.Count == 1, "The initial Alt down must be flushed before waiting.");
            stop.Cancel();
            try { await pending; throw new Exception("Cancellation was ignored."); }
            catch (OperationCanceledException) { }
        }
        Check(InputSimulator.ReleaseAllHeldInputs(), "Stop must release outstanding Windows key-code input.");
        Check(NativeMethods.KeyboardInputs.Select(input => (input.VirtualKey, input.Flags)).SequenceEqual(
            new[] { ((ushort)0xA4, 0u), ((ushort)0xA4, 2u) }), "Cancel must not leave Alt held or tap 1.");

        NativeMethods.KeyboardInputs.Clear();
        InputSimulator.SendKeyDown("F", KeyboardInputMode.ScanCode);
        InputSimulator.SendKeyDown("F", KeyboardInputMode.VirtualKey);
        Check(InputSimulator.ReleaseAllHeldInputs() && NativeMethods.KeyboardInputs.Count == 4,
            "Emergency cleanup must track both input formats independently.");
        Check(NativeMethods.KeyboardInputs.Any(input => input.VirtualKey == 0 && input.Flags == 10)
            && NativeMethods.KeyboardInputs.Any(input => input.VirtualKey == 0x46 && input.Flags == 2),
            "Each held format must receive its own matching up event.");
        Check(InputSimulator.ReleaseAllHeldInputs() && NativeMethods.KeyboardInputs.Count == 4, "Cleanup must be idempotent.");
        Console.WriteLine("PASS: key-code compatibility taps, Alt+1 ordering, cancellation cleanup, mixed held formats, and legacy/save-load settings.");
    }
}
