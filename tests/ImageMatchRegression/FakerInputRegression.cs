using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using ClickyBot;

internal sealed class RecordingFakerTransport(List<byte[]> reports, Func<bool>? fail = null) : IFakerInputTransport
{
    public void Write(byte[] report)
    {
        if (fail?.Invoke() == true) throw new InvalidOperationException("Simulated driver write failure.");
        reports.Add(report.ToArray());
    }
    public void Dispose() { }
}

internal static class FakerInputRegression
{
    internal static async Task Run()
    {
        void Check(bool value, string message) { if (!value) throw new Exception(message); }
        var previous = FakerInputKeyboard.Shared;
        var reports = new List<byte[]>();
        var fail = false;
        FakerInputKeyboard.Shared = new(() => new RecordingFakerTransport(reports, () => fail));
        try
        {
            NativeMethods.KeyboardInputs.Clear();
            var tap = new MacroRule { Key = "F", KeyboardInputMode = KeyboardInputMode.FakerInput };
            var options = new JsonSerializerOptions { Converters = { new JsonStringEnumConverter() } };
            Check(JsonSerializer.Deserialize<MacroRule>(JsonSerializer.Serialize(tap, options), options)!.KeyboardInputMode == KeyboardInputMode.FakerInput,
                "FakerInput mode must survive save/load.");
            var clock = Stopwatch.StartNew();
            await InputSimulator.ExecuteAsync(tap, default);
            Check(clock.ElapsedMilliseconds >= 50 && reports.Count == 2 && reports.All(report => report.Length == 65 && report[0] == 0x40 && report[1] == 9 && report[2] == 1)
                && reports[0][3] == 0 && reports[0][5] == 9 && reports[0].Skip(6).All(value => value == 0)
                && reports[1].Skip(3).All(value => value == 0), "F must produce the official HID usage 0x09 down report followed by a released keyboard report after its tap duration.");
            Check(NativeMethods.KeyboardInputs.Count == 0, "FakerInput must never send Windows SendInput keyboard events.");

            reports.Clear();
            var combo = new MacroRule { Action = ActionType.RecordedCombo, KeyboardInputMode = KeyboardInputMode.FakerInput,
                RecordedSteps = [new() { Type = RecordedStepType.KeyDown, Key = "LeftAlt" }, new() { Type = RecordedStepType.KeyPress, Key = "1" }, new() { Type = RecordedStepType.KeyUp, Key = "LeftAlt" }] };
            await InputSimulator.ExecuteAsync(combo, default);
            Check(reports.Count == 4 && reports[0][3] == 4 && reports[0][5] == 0 && reports[1][3] == 4 && reports[1][5] == 0x1E
                && reports[2][3] == 4 && reports[2][5] == 0 && reports[3].Skip(3).All(value => value == 0),
                "Driver combos must retain Alt around the 1 tap, then release both.");

            reports.Clear();
            using (var stop = new CancellationTokenSource())
            {
                var pending = InputSimulator.ExecuteAsync(tap, stop.Token);
                stop.Cancel();
                try { await pending; throw new Exception("Cancelled driver tap continued."); }
                catch (OperationCanceledException) { }
            }
            Check(reports.Count == 2 && reports[1].Skip(3).All(value => value == 0), "Cancellation during a driver tap must still release F.");

            reports.Clear();
            combo.RecordedSteps[1].DelayBeforeMs = 1000;
            using (var stop = new CancellationTokenSource())
            {
                var pending = InputSimulator.ExecuteAsync(combo, stop.Token);
                stop.Cancel();
                try { await pending; throw new Exception("Cancelled driver combo continued."); }
                catch (OperationCanceledException) { }
            }
            Check(InputSimulator.ReleaseAllHeldInputs() && reports.Count == 2 && reports[1].Skip(3).All(value => value == 0),
                "Emergency stop must release the driver's outstanding Alt state.");

            reports.Clear();
            InputSimulator.SendKeyDown("F", KeyboardInputMode.FakerInput);
            fail = true;
            Check(!InputSimulator.ReleaseAllHeldInputs(), "A failed release must not report success or lose held-key state.");
            fail = false;
            Check(InputSimulator.ReleaseAllHeldInputs() && reports.Count == 2 && reports[1].Skip(3).All(value => value == 0),
                "A failed driver release must be retryable after reconnecting.");
            fail = true;
            try { await InputSimulator.ExecuteAsync(tap, default); throw new Exception("Driver failure was hidden."); }
            catch (InvalidOperationException ex) when (ex.Message.Contains("Simulated")) { }
            fail = false;
            Check(NativeMethods.KeyboardInputs.Count == 0, "Driver errors must never fall back to SendInput.");

            var sixKeys = Enumerable.Range(0x41, 6).Select(key => (ushort)key).ToArray();
            Check(FakerInputKeyboard.BuildReport(sixKeys).Skip(5).Take(6).SequenceEqual(new byte[] { 4, 5, 6, 7, 8, 9 }), "Six-key reports must retain every held key.");
            try { FakerInputKeyboard.BuildReport(sixKeys.Append((ushort)0x47)); throw new Exception("Overfull report accepted."); }
            catch (InvalidOperationException) { }
            try { FakerInputKeyboard.BuildReport([(ushort)0x7C]); throw new Exception("Unsupported key accepted."); }
            catch (InvalidOperationException) { }
            Check(FakerInputKeyboard.BuildReport([(ushort)0xA2, (ushort)0xA5, (ushort)0x5B])[3] == 73,
                "Left/right modifiers and Windows keys must retain their distinct HID bits.");
            Console.WriteLine("PASS: production FakerInput routing, exact F HID reports, tap duration, modifiers/combos, cancellation, release retry, limits and no SendInput fallback; simulated transport only.");
        }
        finally { fail = false; FakerInputKeyboard.Shared.ReleaseAllHeldInputs(); FakerInputKeyboard.Shared = previous; }
    }
}
