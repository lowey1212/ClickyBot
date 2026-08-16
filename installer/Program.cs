using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;

namespace ClickyBotSetup;

internal static class Program
{
    private const string PayloadResource = "payload/ClickyBot.exe";

    [STAThread]
    private static void Main()
    {
        try
        {
            var installFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ClickyBot");
            Directory.CreateDirectory(installFolder);

            var executablePath = Path.Combine(installFolder, "ClickyBot.exe");
            var temporaryPath = executablePath + ".new";
            ExtractPayload(temporaryPath);
            File.Move(temporaryPath, executablePath, overwrite: true);
            Directory.CreateDirectory(Path.Combine(installFolder, "macros"));

            var shortcutPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
                "Programs",
                "ClickyBot.lnk");
            Directory.CreateDirectory(Path.GetDirectoryName(shortcutPath)!);
            CreateShortcut(shortcutPath, executablePath, installFolder);

            Process.Start(new ProcessStartInfo
            {
                FileName = executablePath,
                WorkingDirectory = installFolder,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"ClickyBot could not be installed.\n\n{ex.Message}",
                "ClickyBot Setup",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private static void ExtractPayload(string destinationPath)
    {
        using var source = Assembly.GetExecutingAssembly().GetManifestResourceStream(PayloadResource)
            ?? throw new InvalidOperationException("The ClickyBot application payload is missing.");
        using var destination = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None);
        source.CopyTo(destination);
    }

    private static void CreateShortcut(string shortcutPath, string targetPath, string workingDirectory)
    {
        var shellType = Type.GetTypeFromProgID("WScript.Shell")
            ?? throw new InvalidOperationException("Windows Script Host is unavailable.");
        var shell = Activator.CreateInstance(shellType)
            ?? throw new InvalidOperationException("Could not create the Windows shortcut service.");
        try
        {
            var shortcut = shellType.InvokeMember(
                "CreateShortcut",
                BindingFlags.InvokeMethod,
                binder: null,
                target: shell,
                args: [shortcutPath]);
            if (shortcut is null)
            {
                throw new InvalidOperationException("Could not create the Start Menu shortcut.");
            }

            var shortcutType = shortcut.GetType();
            shortcutType.InvokeMember("TargetPath", BindingFlags.SetProperty, null, shortcut, [targetPath]);
            shortcutType.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, shortcut, [workingDirectory]);
            shortcutType.InvokeMember("Description", BindingFlags.SetProperty, null, shortcut, ["ClickyBot keyboard and mouse macro tool"]);
            shortcutType.InvokeMember("IconLocation", BindingFlags.SetProperty, null, shortcut, [$"{targetPath},0"]);
            shortcutType.InvokeMember("Save", BindingFlags.InvokeMethod, null, shortcut, null);

            if (Marshal.IsComObject(shortcut))
            {
                Marshal.ReleaseComObject(shortcut);
            }
        }
        finally
        {
            if (Marshal.IsComObject(shell))
            {
                Marshal.ReleaseComObject(shell);
            }
        }
    }
}
