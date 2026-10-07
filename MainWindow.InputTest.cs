using System.Windows;

namespace ClickyBot;

public partial class MainWindow
{
    private CancellationTokenSource? _inputTestCancellation;
    private AionInputTest? _inputTest;

    private async void TestAio2Input_Click(object sender, RoutedEventArgs e)
    {
        if (_inputTestCancellation is not null)
        {
            CancelAionInputTest();
            return;
        }
        if (_isRunning || _engineTask is { IsCompleted: false })
        {
            AppendLog("Stop the macro before running the F input test.");
            return;
        }
        using var cancellation = new CancellationTokenSource();
        _inputTestCancellation = cancellation;
        Aio2InputTestButton.Content = "CANCEL F INPUT TEST";
        var inputStarted = false;
        nint ReadyTarget()
        {
            if (!inputStarted && AionInputTest.PhysicalFHeld()) return 0;
            return AionInputTest.ReadyAionTarget();
        }
        // Check physical F before the generated hold; the held synthetic F must
        // not cause the subsequent focus checks to reject our own test.
        var test = new AionInputTest(down =>
        {
            inputStarted = down;
            AionInputTest.SendLegacyF(down);
        });
        _inputTest = test;
        try { await test.RunAsync(ReadyTarget, Task.Delay, AppendLog, cancellation.Token); }
        catch (OperationCanceledException) { AppendLog("F input test cancelled; any generated F was released."); }
        catch (Exception ex) { AppendLog($"F input test failed: {ex.Message}"); }
        finally
        {
            test.ReleaseHeldKey();
            _inputTest = null;
            _inputTestCancellation = null;
            Aio2InputTestButton.Content = "TEST F INPUT (FREE)";
        }
    }

    private void CancelAionInputTest()
    {
        _inputTestCancellation?.Cancel();
        // Synchronous release also handles application shutdown before an await resumes.
        _inputTest?.ReleaseHeldKey();
    }
}
