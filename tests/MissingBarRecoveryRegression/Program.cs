using ClickyBot;

var starts = 0;
var releases = 0;
var visible = 1;
using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(5));
var runner = new MissingBarRecoveryRunner(
    runMacro: async token =>
    {
        Interlocked.Increment(ref starts);
        await Task.Delay(Timeout.InfiniteTimeSpan, token);
    },
    barVisible: _ => Task.FromResult<bool?>(Volatile.Read(ref visible) == 1),
    releaseInputs: () => Interlocked.Increment(ref releases),
    log: _ => { },
    missingMs: 150,
    checkIntervalMs: 30);

var session = runner.RunAsync(stop.Token);

async Task WaitForStarts(int expected)
{
    var deadline = DateTime.UtcNow.AddSeconds(2);
    while (Volatile.Read(ref starts) < expected && DateTime.UtcNow < deadline)
        await Task.Delay(20);
    if (Volatile.Read(ref starts) != expected)
        throw new Exception($"Expected {expected} starts, got {starts}.");
}

await WaitForStarts(1);
await Task.Delay(120); // Two visible samples arm the detector.
Interlocked.Exchange(ref visible, 0);
await WaitForStarts(2);
await Task.Delay(250);
if (Volatile.Read(ref starts) != 2)
    throw new Exception("A continuously missing bar caused repeated restarts.");

Interlocked.Exchange(ref visible, 1);
await Task.Delay(120);
Interlocked.Exchange(ref visible, 0);
await WaitForStarts(3);

stop.Cancel();
try { await session; }
catch (OperationCanceledException) { }
var countAfterStop = Volatile.Read(ref starts);
await Task.Delay(250);
if (Volatile.Read(ref starts) != countAfterStop || Volatile.Read(ref releases) < 3)
    throw new Exception("Manual Stop failed to cancel recovery or release inputs.");

Console.WriteLine("PASS: a missing bar restarts once, re-arms after reappearing, and manual Stop cancels recovery.");

var stoppedStarts = 0;
var stoppedVisible = 1;
using var stoppedSession = new CancellationTokenSource(TimeSpan.FromSeconds(3));
var stoppedRunner = new MissingBarRecoveryRunner(
    runMacro: token => Interlocked.Increment(ref stoppedStarts) == 1
        ? Task.CompletedTask : Task.Delay(Timeout.InfiniteTimeSpan, token),
    barVisible: _ => Task.FromResult<bool?>(Volatile.Read(ref stoppedVisible) == 1),
    releaseInputs: () => { },
    log: _ => { },
    missingMs: 150,
    checkIntervalMs: 30);
var stoppedTask = stoppedRunner.RunAsync(stoppedSession.Token);
await Task.Delay(120);
if (Volatile.Read(ref stoppedStarts) != 1)
    throw new Exception("An ended macro restarted while the bar was still visible.");
Interlocked.Exchange(ref stoppedVisible, 0);
var stoppedDeadline = DateTime.UtcNow.AddSeconds(2);
while (Volatile.Read(ref stoppedStarts) < 2 && DateTime.UtcNow < stoppedDeadline)
    await Task.Delay(20);
if (Volatile.Read(ref stoppedStarts) != 2)
    throw new Exception("An ended macro did not restart after the bar disappeared.");
stoppedSession.Cancel();
try { await stoppedTask; }
catch (OperationCanceledException) { }
Console.WriteLine("PASS: a macro that already ended waits for bar absence, then starts again.");
