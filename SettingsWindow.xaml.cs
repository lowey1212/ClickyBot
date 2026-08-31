using System.IO;
using System.Windows;

namespace ClickyBot;

public partial class SettingsWindow : Window
{
    public AppSettings Settings { get; }

    public SettingsWindow(AppSettings settings)
    {
        InitializeComponent();
        Settings = new AppSettings
        {
            ReferenceImageFolder = settings.ReferenceImageFolder,
            MacroFolder = settings.MacroFolder,
            LastMacroPath = settings.LastMacroPath,
            CheckForUpdatesOnStartup = settings.CheckForUpdatesOnStartup
        };
        ReferenceFolderBox.Text = Settings.ReferenceImageFolder;
        MacroFolderBox.Text = Settings.MacroFolder;
        CheckForUpdatesCheckBox.IsChecked = Settings.CheckForUpdatesOnStartup;
    }

    private void BrowseButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            InitialDirectory = Directory.Exists(ReferenceFolderBox.Text) ? ReferenceFolderBox.Text : AppSettings.DefaultReferenceImageFolder()
        };
        if (dialog.ShowDialog() == true)
        {
            ReferenceFolderBox.Text = dialog.FolderName;
        }
    }

    private void BrowseMacroButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            InitialDirectory = Directory.Exists(MacroFolderBox.Text) ? MacroFolderBox.Text : AppSettings.DefaultMacroFolder()
        };
        if (dialog.ShowDialog() == true)
        {
            MacroFolderBox.Text = dialog.FolderName;
        }
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        var folder = ReferenceFolderBox.Text.Trim();
        var macroFolder = MacroFolderBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(folder) || string.IsNullOrWhiteSpace(macroFolder))
        {
            MessageBox.Show(this, "Choose both a reference image folder and a macro folder first.", "Settings", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            Directory.CreateDirectory(folder);
            Directory.CreateDirectory(macroFolder);
            Settings.ReferenceImageFolder = folder;
            Settings.MacroFolder = macroFolder;
            Settings.CheckForUpdatesOnStartup = CheckForUpdatesCheckBox.IsChecked == true;
            if (!AppSettingsStore.Save(Settings, out var error))
            {
                MessageBox.Show(this, error, "Could not save settings", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            DialogResult = true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, ex.Message, "Could not create reference folder", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
