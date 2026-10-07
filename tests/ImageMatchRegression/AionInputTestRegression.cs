using ClickyBot;

internal static class AionInputTestRegression
{
    internal static async Task Run()
    {
        static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
        var events = new List<bool>();
        var logs = new List<string>();
        var delays = new List<int>();
        Task Delay(int milliseconds, CancellationToken token)
        { token.ThrowIfCancellationRequested(); delays.Add(milliseconds); return Task.CompletedTask; }
        var test = new AionInputTest(events.Add);
        await test.RunAsync(() => 123, Delay, logs.Add, default);
        Check(events.SequenceEqual(new[] { true, false }), "The test must send exactly one F down and up.");
        Check(delays.Take(5).All(ms => ms == 1000) && delays.Skip(5).Sum() == 200,
            "The test must count down five seconds and hold F for 200 ms.");
        Check(logs.Last().Contains("no delivery result"), "The test must not claim game acceptance.");
        test.ReleaseHeldKey();
        Check(events.Count == 2, "Release must be idempotent.");

        events.Clear();
        await test.RunAsync(() => 0, Delay, logs.Add, default);
        Check(events.Count == 0, "Invalid foreground or held modifiers must block all input.");
        var calls = 0;
        await test.RunAsync(() => ++calls == 1 ? 123 : 456, Delay, logs.Add, default);
        Check(events.Count == 0, "Focus changing immediately before down must block input.");

        calls = 0;
        await test.RunAsync(() => ++calls <= 2 ? 123 : 456, Delay, logs.Add, default);
        Check(events.SequenceEqual(new[] { true, false }), "Focus loss during a hold must release F.");
        events.Clear();
        using (var stop = new CancellationTokenSource())
        {
            async Task CancelCountdown(int ms, CancellationToken token)
            { stop.Cancel(); await Task.Delay(ms, token); }
            try { await test.RunAsync(() => 123, CancelCountdown, logs.Add, stop.Token); }
            catch (OperationCanceledException) { }
            Check(events.Count == 0, "Cancelling countdown must send no input.");
        }
        using (var stop = new CancellationTokenSource())
        {
            Task CancelHold(int ms, CancellationToken token)
            {
                if (ms == 20) { stop.Cancel(); test.ReleaseHeldKey(); }
                token.ThrowIfCancellationRequested();
                return Task.CompletedTask;
            }
            try { await test.RunAsync(() => 123, CancelHold, logs.Add, stop.Token); }
            catch (OperationCanceledException) { }
            Check(events.SequenceEqual(new[] { true, false }), "Panic/close during hold must release exactly once.");
        }
        events.Clear();
        test = new AionInputTest(down => { events.Add(down); if (down) throw new InvalidOperationException("Test failure"); });
        try { await test.RunAsync(() => 123, Delay, logs.Add, default); }
        catch (InvalidOperationException) { }
        Check(events.SequenceEqual(new[] { true, false }), "A failed down request must still attempt release.");
        Console.WriteLine("PASS: Aion F test countdown, focus guards, cancellation, early release, and truthful logging; no real input sent.");
    }
}
