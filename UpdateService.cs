using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ClickyBot;

internal sealed record UpdateInfo(string CurrentVersion, string LatestVersion, string AssetName, string AssetUrl);
internal sealed record DownloadProgress(long BytesDownloaded, long? TotalBytes);

internal static class UpdateService
{
    private const string Repository = "lowey1212/ClickyBot";
    private const string ReleasesApiUrl = "https://api.github.com/repos/lowey1212/ClickyBot/releases/latest";
    private const string InstallerPrefix = "ClickyBot-Setup";
    private const long MaxUpdateBytes = 100L * 1024L * 1024L;
    private static readonly Regex VersionPattern = new(@"^(\d+)\.(\d+)\.(\d+)", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly HttpClient Http = CreateHttpClient();

    public static string CurrentVersion => typeof(UpdateService).Assembly.GetName().Version?.ToString(3) ?? "0.1.0";

    public static async Task<UpdateInfo?> CheckAsync(CancellationToken cancellationToken)
    {
        using var response = await Http.GetAsync(ReleasesApiUrl, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var content = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(content, cancellationToken: cancellationToken);

        var root = document.RootElement;
        var tag = root.TryGetProperty("tag_name", out var tagElement) ? tagElement.GetString() : null;
        if (!TryParseVersion(tag, out var latestVersion) || !TryParseVersion(CurrentVersion, out var currentVersion))
        {
            throw new InvalidDataException("GitHub returned an invalid ClickyBot release version.");
        }

        if (latestVersion.CompareTo(currentVersion) <= 0)
        {
            return null;
        }

        if (!root.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException("The latest GitHub release does not contain installer assets.");
        }

        foreach (var asset in assets.EnumerateArray())
        {
            var name = asset.TryGetProperty("name", out var nameElement) ? nameElement.GetString() : null;
            var url = asset.TryGetProperty("browser_download_url", out var urlElement) ? urlElement.GetString() : null;
            if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(url)
                && IsInstallerAsset(name) && IsAllowedUpdateUrl(url))
            {
                return new UpdateInfo(CurrentVersion, latestVersion.ToString(3), name, url);
            }
        }

        throw new InvalidDataException("The latest GitHub release does not contain a trusted ClickyBot installer.");
    }

    public static async Task<string> DownloadInstallerAsync(
        UpdateInfo update,
        IProgress<DownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (!IsAllowedUpdateUrl(update.AssetUrl) || !IsInstallerAsset(update.AssetName))
        {
            throw new InvalidDataException("The update download URL or asset name was not trusted.");
        }

        var updateDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ClickyBot",
            "updates");
        Directory.CreateDirectory(updateDirectory);

        var destination = Path.Combine(updateDirectory, $"ClickyBot-Setup-{update.LatestVersion}.exe");
        var temporary = destination + ".part";
        using var downloadTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        downloadTimeout.CancelAfter(TimeSpan.FromMinutes(5));
        var downloadToken = downloadTimeout.Token;
        try
        {
            File.Delete(temporary);
            using var response = await Http.GetAsync(update.AssetUrl, HttpCompletionOption.ResponseHeadersRead, downloadToken);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is > MaxUpdateBytes)
            {
                throw new InvalidDataException("The update installer is larger than the allowed download limit.");
            }

            var totalBytes = response.Content.Headers.ContentLength;
            await using var source = await response.Content.ReadAsStreamAsync(downloadToken);
            await using var target = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            var buffer = new byte[256 * 1024];
            long total = 0;
            int read;
            progress?.Report(new DownloadProgress(0, totalBytes));
            while ((read = await source.ReadAsync(buffer.AsMemory(), downloadToken)) > 0)
            {
                total += read;
                if (total > MaxUpdateBytes)
                {
                    throw new InvalidDataException("The update installer is larger than the allowed download limit.");
                }

                await target.WriteAsync(buffer.AsMemory(0, read), downloadToken);
                progress?.Report(new DownloadProgress(total, totalBytes));
            }

            File.Move(temporary, destination, overwrite: true);
            return destination;
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested && downloadTimeout.IsCancellationRequested)
        {
            throw new TimeoutException("The installer download timed out. Check your connection and try again.", ex);
        }
        catch
        {
            try
            {
                File.Delete(temporary);
            }
            catch (IOException)
            {
                // Keep the original network or disk error for the user.
            }

            throw;
        }
    }

    public static bool StartInstallerAfterExit(string installerPath)
    {
        if (!File.Exists(installerPath))
        {
            return false;
        }

        var applicationPath = Process.GetCurrentProcess().MainModule?.FileName ?? Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(applicationPath))
        {
            return false;
        }

        var processId = Environment.ProcessId;
        var scriptPath = Path.Combine(Path.GetTempPath(), $"ClickyBot-update-{processId}.cmd");
        var script = string.Join(Environment.NewLine, new[]
        {
            "@echo off",
            "setlocal",
            ":wait_for_clickybot",
            $"tasklist /FI \"PID eq {processId}\" /NH | findstr /C:\" {processId} \" >nul",
            "if not errorlevel 1 (",
            "  timeout /t 1 /nobreak >nul",
            "  goto wait_for_clickybot",
            ")",
            $"start \"\" /wait \"{installerPath}\" /VERYSILENT /SUPPRESSMSGBOXES /NORESTART",
            $"start \"\" \"{applicationPath}\"",
            "del \"%~f0\"",
            ""
        });
        File.WriteAllText(scriptPath, script);

        Process.Start(new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/d /c \"{scriptPath}\"",
            WorkingDirectory = Path.GetDirectoryName(installerPath) ?? AppContext.BaseDirectory,
            UseShellExecute = false,
            CreateNoWindow = true
        });
        return true;
    }

    private static bool IsInstallerAsset(string name) =>
        name.Equals("ClickyBot-Setup.exe", StringComparison.OrdinalIgnoreCase)
        || (name.StartsWith(InstallerPrefix + "-", StringComparison.OrdinalIgnoreCase)
            && name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));

    private static bool IsAllowedUpdateUrl(string url)
    {
        return Uri.TryCreate(url, UriKind.Absolute, out var uri)
            && uri.Scheme == Uri.UriSchemeHttps
            && uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)
            && uri.AbsolutePath.StartsWith($"/{Repository}/releases/download/", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryParseVersion(string? value, out Version version)
    {
        var match = VersionPattern.Match(value?.Trim().TrimStart('v', 'V') ?? "");
        if (match.Success && int.TryParse(match.Groups[1].Value, out var major)
            && int.TryParse(match.Groups[2].Value, out var minor)
            && int.TryParse(match.Groups[3].Value, out var patch))
        {
            version = new Version(major, minor, patch);
            return true;
        }

        version = new Version(0, 0, 0);
        return false;
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"ClickyBot/{CurrentVersion}");
        return client;
    }
}
