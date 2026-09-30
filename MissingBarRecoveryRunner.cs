namespace ClickyBot;

// Keeps a user-started session alive if the inner macro stops, then restarts it
// once when the stamina bar stays absent. Manual Stop cancels the whole session.
internal sealed class MissingBarRecoveryRunner
{
    private readonly Func<CancellationToken, Task> _runMacro;
    private readonly Func<CancellationToken, Task<bool?>> _barVisible;
    private readonly Action _releaseInputs;
    private readonly Action<string> _log;
    private readonly int _missingMs;
    private readonly int _checkIntervalMs;

    public MissingBarRecoveryRunner(
        Func<CancellationToken, Task> runMacro,
        Func<CancellationToken, Task<bool?>> barVisible,
        Action releaseInputs,
        Action<string> log,
        int missingMs,
        int checkIntervalMs)
    {
        _runMacro = runMacro;
        _barVisible = barVisible;
        _releaseInputs = releaseInputs;
        _log = log;
        _missingMs = missingMs;
        _checkIntervalMs = checkIntervalMs;
    }

    public async Task RunAsync(CancellationToken token)
    {
        CancellationTokenSource? currentCancellation = null;
        Task? currentRun = null;

        void StartMacro()
        {
            currentCancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
            currentRun = _runMacro(currentCancellation.Token);
        }

        async Task FinishMacroAsync(bool cancel)
        {
            if (currentCancellation is null || currentRun is null) return;
            if (cancel) currentCancellation.Cancel();
            try
            {
                await currentRun;
            }
            catch (OperationCanceledException) when (currentCancellation.IsCancellationRequested)
            {
                // Expected when restarting or stopping.
            }
            catch (Exception ex)
            {
                _log($"Macro stopped after an error: {ex.Message}");
            }
            finally
            {
                _releaseInputs();
                currentCancellation.Dispose();
                currentCancellation = null;
                currentRun = null;
            }
        }

        StartMacro();
        var armed = true;
        var consecutiveVisible = 0;
        DateTime? missingSince = null;
        try
        {
            while (true)
            {
                token.ThrowIfCancellationRequested();
                if (currentRun?.IsCompleted == true)
                {
                    await FinishMacroAsync(cancel: false);
                    _log("Macro stopped; watching for the stamina bar to disappear before restarting.");
                }

                var visible = await _barVisible(token);
                var now = DateTime.UtcNow;
                if (visible == true)
                {
                    missingSince = null;
                    if (++consecutiveVisible >= 2) armed = true;
                }
                else if (visible == false)
                {
                    consecutiveVisible = 0;
                    missingSince ??= now;
                    if (armed && (now - missingSince.Value).TotalMilliseconds >= _missingMs)
                    {
                        await FinishMacroAsync(cancel: true);
                        token.ThrowIfCancellationRequested();
                        _log("Stamina bar stayed absent; restarting the macro and holding E again.");
                        StartMacro();
                        armed = false;
                        missingSince = null;
                    }
                }
                else
                {
                    // Focus changes or failed captures cannot prove the bar disappeared.
                    missingSince = null;
                    consecutiveVisible = 0;
                }

                await Task.Delay(_checkIntervalMs, token);
            }
        }
        finally
        {
            await FinishMacroAsync(cancel: true);
            _releaseInputs();
        }
    }
}
