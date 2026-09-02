namespace ClickyBot;

public partial class App : System.Windows.Application
{
    private System.Threading.Mutex? _singleInstanceMutex;
    private bool _ownsSingleInstanceMutex;

    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        const string mutexName = "Local\\ClickyBot.SingleInstance";
        _singleInstanceMutex = new System.Threading.Mutex(
            initiallyOwned: true,
            name: mutexName,
            createdNew: out var createdNew);
        _ownsSingleInstanceMutex = createdNew;

        if (!createdNew)
        {
            // This also protects updates started by older ClickyBot versions
            // that relaunched the app after the installer had already done so.
            Shutdown();
            return;
        }

        base.OnStartup(e);
        var mainWindow = new MainWindow();
        MainWindow = mainWindow;
        mainWindow.Show();
    }

    protected override void OnExit(System.Windows.ExitEventArgs e)
    {
        if (_ownsSingleInstanceMutex)
        {
            _singleInstanceMutex?.ReleaseMutex();
        }

        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }
}
