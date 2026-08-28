using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;

namespace YtMusicTerminal.Services;

public sealed record PreparedAppUpgrade(string Version, string PackageDirectory);

public sealed class AppUpgradeService(HttpClient httpClient)
{
    public const string ReleasesUrl = "https://github.com/kilfox/Lightweight-Youtube-Player/releases/latest";
    private const string RepositoryUrl = "https://github.com/kilfox/Lightweight-Youtube-Player";
    private const string LatestReleaseApi = "https://api.github.com/repos/kilfox/Lightweight-Youtube-Player/releases/latest";

    public static string GetAssetName(string platform, Architecture architecture)
    {
        var suffix = (platform, architecture) switch
        {
            ("win", Architecture.X64) => "win-x64.zip",
            ("linux", Architecture.X64) => "linux-x64.tar.gz",
            ("linux", Architecture.Arm64) => "linux-arm64.tar.gz",
            ("macos", Architecture.X64) => "macos-x64.tar.gz",
            ("macos", Architecture.Arm64) => "macos-arm64.tar.gz",
            _ => throw new PlatformNotSupportedException($"No LightYTP release is available for {platform}/{architecture}.")
        };
        return $"LightYTP-{suffix}";
    }

    private async Task<(string Tag, Version Version, HashSet<string?> Assets)> GetLatestReleaseAsync(
        Version currentVersion, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseApi);
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("LightYTP", currentVersion.ToString()));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        using var release = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
        var root = release.RootElement;
        var tag = root.GetProperty("tag_name").GetString() ?? string.Empty;
        if (root.GetProperty("draft").GetBoolean() || root.GetProperty("prerelease").GetBoolean()
            || !Version.TryParse(tag.TrimStart('v'), out var latestVersion))
        {
            throw new InvalidDataException("GitHub did not return a stable release version.");
        }

        var assets = root.GetProperty("assets").EnumerateArray()
            .Select(asset => asset.GetProperty("name").GetString()).ToHashSet(StringComparer.Ordinal);
        return (tag, latestVersion, assets);
    }

    public async Task<string?> GetUpgradeNoticeAsync(Version currentVersion, string assetName, CancellationToken cancellationToken)
    {
        var release = await GetLatestReleaseAsync(currentVersion, cancellationToken).ConfigureAwait(false);
        return release.Version > currentVersion && release.Assets.Contains(assetName) && release.Assets.Contains("SHA256SUMS.txt")
            ? $"App {release.Tag} available - quit, run: lightytp upgrade"
            : null;
    }

    public async Task<PreparedAppUpgrade?> PrepareAsync(
        Version currentVersion,
        string assetName,
        string stagingDirectory,
        Action<string> report,
        CancellationToken cancellationToken)
    {
        var (tag, latestVersion, assets) = await GetLatestReleaseAsync(currentVersion, cancellationToken).ConfigureAwait(false);
        if (latestVersion <= currentVersion)
        {
            report($"LightYTP {currentVersion} is already up to date (latest: {tag}).");
            return null;
        }

        if (!assets.Contains(assetName) || !assets.Contains("SHA256SUMS.txt"))
        {
            throw new InvalidDataException($"Release {tag} is missing {assetName} or SHA256SUMS.txt. Try again after the release upload finishes.");
        }

        var downloadRoot = $"{RepositoryUrl}/releases/download/{Uri.EscapeDataString(tag)}/";
        report($"Downloading {tag}: {assetName}...");
        var checksums = await httpClient.GetStringAsync(downloadRoot + "SHA256SUMS.txt", cancellationToken).ConfigureAwait(false);
        var checksumEntries = checksums.Split('\n')
            .Select(line => line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            .Where(parts => parts.Length == 2 && parts[1] == assetName).ToArray();
        if (checksumEntries.Length != 1 || checksumEntries[0][0].Length != 64
            || !checksumEntries[0][0].All(Uri.IsHexDigit))
        {
            throw new InvalidDataException($"Release checksums do not contain a unique SHA-256 for {assetName}.");
        }

        var archivePath = Path.Combine(stagingDirectory, "release.archive");
        using (var download = await httpClient.GetAsync(downloadRoot + assetName, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false))
        {
            download.EnsureSuccessStatusCode();
            await using var destination = new FileStream(archivePath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            await download.Content.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
        }

        await using (var archive = File.OpenRead(archivePath))
        {
            var hash = await SHA256.HashDataAsync(archive, cancellationToken).ConfigureAwait(false);
            if (!Convert.ToHexString(hash).Equals(checksumEntries[0][0], StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("Release checksum verification failed. Nothing was installed.");
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        var packageDirectory = Path.Combine(stagingDirectory, "package");
        Directory.CreateDirectory(packageDirectory);
        if (assetName.EndsWith(".zip", StringComparison.Ordinal))
        {
            ZipFile.ExtractToDirectory(archivePath, packageDirectory);
            foreach (var required in new[] { "ytmusic.exe", "install.ps1", "tools/yt-dlp.exe", "tools/mpv.exe", "tools/deno.exe" })
            {
                if (!File.Exists(Path.Combine(packageDirectory, required)))
                {
                    throw new InvalidDataException($"Release package is incomplete: missing {required}.");
                }
            }
        }
        else
        {
            // Unix installations contain only the executable. Never extract archive paths or links.
            using var archive = File.OpenRead(archivePath);
            using var gzip = new GZipStream(archive, CompressionMode.Decompress);
            using var tar = new TarReader(gzip);
            var found = false;
            while (tar.GetNextEntry() is { } entry)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (entry.Name is not ("ytmusic" or "./ytmusic"))
                {
                    continue;
                }
                if (found || entry.EntryType is not (TarEntryType.RegularFile or TarEntryType.V7RegularFile) || entry.DataStream is null)
                {
                    throw new InvalidDataException("Release executable must be a unique regular file.");
                }
                await using var destination = File.Create(Path.Combine(packageDirectory, "ytmusic"));
                await entry.DataStream.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
                found = true;
            }
            if (!found)
            {
                throw new InvalidDataException("Release package is missing ytmusic.");
            }
        }

        report("Release checksum verified.");
        return new PreparedAppUpgrade(tag, packageDirectory);
    }

    public static void EnsureNoOtherInstances()
    {
        foreach (var process in Process.GetProcessesByName("lightytp"))
        {
            using (process)
            {
                if (process.Id != Environment.ProcessId)
                {
                    throw new InvalidOperationException("Close every other LightYTP terminal window before upgrading.");
                }
            }
        }
    }

    public static void InstallUnix(PreparedAppUpgrade upgrade, string executablePath)
    {
        if (OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Use the Windows upgrade helper.");
        }

        // Stage beside the installed file so rename is atomic, including on Linux with a running executable.
        var replacement = executablePath + $".upgrade-{Guid.NewGuid():N}";
        try
        {
            File.Copy(Path.Combine(upgrade.PackageDirectory, "ytmusic"), replacement);
            File.SetUnixFileMode(replacement, File.GetUnixFileMode(executablePath));
            File.Move(replacement, executablePath, overwrite: true);
        }
        finally
        {
            File.Delete(replacement);
        }
    }

    public static void ScheduleWindows(PreparedAppUpgrade upgrade, string installDirectory, string logPath, int parentProcessId)
    {
        var helperPath = Path.Combine(upgrade.PackageDirectory, "upgrade-helper.ps1");
        File.WriteAllText(helperPath, WindowsHelperScript);
        var startInfo = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetTempPath()
        };
        foreach (var argument in new[]
        {
            "-NoLogo", "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", helperPath,
            "-ParentProcessId", parentProcessId.ToString(),
            "-PackageDirectory", upgrade.PackageDirectory, "-InstallDirectory", installDirectory, "-LogPath", logPath
        })
        {
            startInfo.ArgumentList.Add(argument);
        }
        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start the upgrade helper.");
    }

    private const string WindowsHelperScript =
        """
        param([int]$ParentProcessId, [string]$PackageDirectory, [string]$InstallDirectory, [string]$LogPath)
        $ErrorActionPreference = 'Stop'
        $installed = $false
        try {
            'Waiting for LightYTP to exit...' | Set-Content -LiteralPath $LogPath -Encoding UTF8
            Wait-Process -Id $ParentProcessId -ErrorAction SilentlyContinue
            if (Get-Process -Name lightytp -ErrorAction SilentlyContinue) {
                throw 'Close every other LightYTP terminal window, then run lightytp upgrade again.'
            }
            & (Join-Path $PackageDirectory 'install.ps1') -InstallDirectory $InstallDirectory *>&1 | Out-File -LiteralPath $LogPath -Append -Encoding UTF8
            $installed = $true
            'Upgrade complete. Run lightytp --version to check, then lightytp update before launching.' | Add-Content -LiteralPath $LogPath -Encoding UTF8

            # Delete only our unique staging directory, never the installation or user data.
            $staging = [IO.Path]::GetFullPath((Split-Path -Parent $PackageDirectory))
            $temporaryRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
            if (-not $staging.StartsWith($temporaryRoot, [StringComparison]::OrdinalIgnoreCase) -or
                -not (Split-Path -Leaf $staging).StartsWith('lightytp-upgrade-')) {
                throw 'Upgrade succeeded, but staging cleanup was refused: unexpected path.'
            }
            Remove-Item -LiteralPath $staging -Recurse -Force
        }
        catch {
            $status = if ($installed) { 'Upgrade installed, but cleanup failed' } else { 'Upgrade failed' }
            "${status}: $_`nDownloaded files kept at $PackageDirectory" | Add-Content -LiteralPath $LogPath -Encoding UTF8
            exit 1
        }
        """;
}
