using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using YtMusicTerminal.Services;

namespace YtMusicTerminal.Tests;

internal static class AppUpgradeTests
{
    private const string WindowsAsset = "LightYTP-win-x64.zip";
    private const string UnixAsset = "LightYTP-macos-arm64.tar.gz";

    public static Task AssetSelectionAsync()
    {
        Equal(WindowsAsset, AppUpgradeService.GetAssetName("win", Architecture.X64));
        Equal("LightYTP-linux-x64.tar.gz", AppUpgradeService.GetAssetName("linux", Architecture.X64));
        Equal("LightYTP-linux-arm64.tar.gz", AppUpgradeService.GetAssetName("linux", Architecture.Arm64));
        Equal("LightYTP-macos-x64.tar.gz", AppUpgradeService.GetAssetName("macos", Architecture.X64));
        Equal(UnixAsset, AppUpgradeService.GetAssetName("macos", Architecture.Arm64));
        try
        {
            AppUpgradeService.GetAssetName("win", Architecture.Arm);
            throw new InvalidOperationException("Unsupported architecture was accepted.");
        }
        catch (PlatformNotSupportedException) { }
        return Task.CompletedTask;
    }

    public static async Task PrepareAsync()
    {
        var staging = Directory.CreateTempSubdirectory("lightytp-upgrade-test-").FullName;
        try
        {
            using var handler = new ReleaseHandler(CreateZip());
            using var client = new HttpClient(handler);
            var messages = new List<string>();
            var prepared = await new AppUpgradeService(client).PrepareAsync(new Version(0, 5, 0), WindowsAsset, staging, messages.Add, CancellationToken.None);
            Equal("v0.6.0", prepared!.Version);
            Equal("new executable", File.ReadAllText(Path.Combine(prepared.PackageDirectory, "ytmusic.exe")));
            Equal("Release checksum verified.", messages[^1]);
            Equal("3", handler.Requests.Count.ToString());
            Equal("https://github.com/kilfox/Lightweight-Youtube-Player/releases/download/v0.6.0/" + WindowsAsset, handler.Requests[^1]);
        }
        finally { Directory.Delete(staging, recursive: true); }
    }

    public static async Task StartupNoticeAsync()
    {
        foreach (var scenario in new[] { "valid", "same", "older", "missing-asset" })
        {
            using var handler = new ReleaseHandler([], scenario);
            using var client = new HttpClient(handler);
            var notice = await new AppUpgradeService(client).GetUpgradeNoticeAsync(new Version(0, 5, 0), WindowsAsset, CancellationToken.None);
            Equal(scenario == "valid" ? "App v0.6.0 available - quit, run: lightytp upgrade" : "", notice ?? "");
            Equal("1", handler.Requests.Count.ToString());
        }
        using var failedHandler = new ReleaseHandler([], "http-error");
        using var failedClient = new HttpClient(failedHandler);
        try
        {
            await new AppUpgradeService(failedClient).GetUpgradeNoticeAsync(new Version(0, 5, 0), WindowsAsset, CancellationToken.None);
            throw new Exception("Network failure must not be reported as up-to-date.");
        }
        catch (HttpRequestException) { }
    }

    public static async Task RejectBadReleasesAsync()
    {
        foreach (var scenario in new[] { "same", "older", "prerelease", "draft", "invalid-version", "missing-asset", "bad-checksum", "missing-checksum", "duplicate-checksum", "incomplete", "zip-slip", "http-error", "cancelled" })
        {
            var staging = Directory.CreateTempSubdirectory("lightytp-upgrade-test-").FullName;
            try
            {
                using var handler = new ReleaseHandler(CreateZip(scenario), scenario);
                using var client = new HttpClient(handler);
                var token = scenario == "cancelled" ? new CancellationToken(canceled: true) : CancellationToken.None;
                var failed = false;
                try
                {
                    var result = await new AppUpgradeService(client).PrepareAsync(new Version(0, 5, 0), WindowsAsset, staging, _ => { }, token);
                    if (scenario is "same" or "older")
                    {
                        if (result is not null || handler.Requests.Count != 1 || Directory.GetFileSystemEntries(staging).Length != 0)
                        {
                            throw new Exception("An equal/older version must not download or install.");
                        }
                        continue;
                    }
                }
                catch (Exception exception) when (exception is InvalidDataException or IOException or HttpRequestException or OperationCanceledException)
                {
                    failed = true;
                }
                if (!failed) { throw new InvalidOperationException($"Unsafe release accepted: {scenario}"); }
                if (File.Exists(Path.Combine(staging, "escaped.txt"))) { throw new InvalidOperationException("Archive escaped the package directory."); }
                if (scenario is "bad-checksum" or "missing-checksum" or "duplicate-checksum" && Directory.Exists(Path.Combine(staging, "package")))
                {
                    throw new InvalidOperationException("Extraction happened before checksum verification.");
                }
            }
            finally { Directory.Delete(staging, recursive: true); }
        }
    }

    public static async Task UnixArchiveAndInstallAsync()
    {
        foreach (var scenario in new[] { "valid", "symlink", "duplicate", "missing" })
        {
            var staging = Directory.CreateTempSubdirectory("lightytp-upgrade-test-").FullName;
            try
            {
                using var handler = new ReleaseHandler(CreateTar(scenario), asset: UnixAsset);
                using var client = new HttpClient(handler);
                PreparedAppUpgrade? upgrade = null;
                try
                {
                    upgrade = await new AppUpgradeService(client).PrepareAsync(new Version(0, 5, 0), UnixAsset, staging, _ => { }, CancellationToken.None);
                    if (scenario != "valid") { throw new Exception($"Invalid tar accepted: {scenario}"); }
                }
                catch (InvalidDataException) when (scenario != "valid") { }
                if (scenario != "valid") { continue; }
                Equal("new executable", File.ReadAllText(Path.Combine(upgrade!.PackageDirectory, "ytmusic")));
                if (Directory.GetFiles(upgrade.PackageDirectory).Length != 1) { throw new Exception("Unexpected archive contents extracted."); }
                if (!OperatingSystem.IsWindows())
                {
                    var installed = Path.Combine(staging, "lightytp");
                    File.WriteAllText(installed, "old executable");
                    File.SetUnixFileMode(installed, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                    var library = Path.Combine(staging, "library.json");
                    File.WriteAllText(library, "keep this");
                    AppUpgradeService.InstallUnix(upgrade, installed);
                    Equal("new executable", File.ReadAllText(installed));
                    Equal("keep this", File.ReadAllText(library));
                    if (!File.GetUnixFileMode(installed).HasFlag(UnixFileMode.UserExecute)) { throw new Exception("Executable bit was lost."); }
                    if (Directory.GetFiles(staging, "*.upgrade-*").Length > 0) { throw new Exception("Replacement file was not cleaned up."); }
                }
            }
            finally { Directory.Delete(staging, recursive: true); }
        }
    }

    public static async Task WindowsHelperAsync()
    {
        if (!OperatingSystem.IsWindows()) { return; }
        var staging = Directory.CreateTempSubdirectory("lightytp-upgrade-test-").FullName;
        var target = Directory.CreateTempSubdirectory("lightytp-upgrade-target-").FullName;
        try
        {
            var package = Path.Combine(staging, "package");
            Directory.CreateDirectory(package);
            File.WriteAllText(Path.Combine(package, "install.ps1"),
                "param([string]$InstallDirectory)\nSet-Content -LiteralPath (Join-Path $InstallDirectory 'lightytp.exe') -Value 'new executable' -NoNewline\n");
            var executable = Path.Combine(target, "lightytp.exe");
            File.WriteAllText(executable, "old executable");
            File.WriteAllText(Path.Combine(target, "library.json"), "keep this");
            var log = Path.Combine(target, "upgrade.log");
            var parentInfo = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = true };
            foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-Command", "Start-Sleep -Seconds 2" }) { parentInfo.ArgumentList.Add(argument); }
            using var parent = Process.Start(parentInfo)!;
            AppUpgradeService.ScheduleWindows(new PreparedAppUpgrade("v0.6.0", package), target, log, parent.Id);
            Equal("old executable", File.ReadAllText(executable));
            await parent.WaitForExitAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            while (Directory.Exists(staging))
            {
                await Task.Delay(100, timeout.Token);
            }
            Equal("new executable", File.ReadAllText(executable));
            Equal("keep this", File.ReadAllText(Path.Combine(target, "library.json")));
            if (!File.ReadAllText(log).Contains("Upgrade complete", StringComparison.Ordinal)) { throw new Exception("Helper did not log success."); }
        }
        finally
        {
            if (Directory.Exists(staging)) { Directory.Delete(staging, recursive: true); }
            Directory.Delete(target, recursive: true);
        }
    }

    public static async Task WindowsHelperFailureAsync()
    {
        if (!OperatingSystem.IsWindows()) { return; }
        var staging = Directory.CreateTempSubdirectory("lightytp-upgrade-test-").FullName;
        var target = Directory.CreateTempSubdirectory("lightytp-upgrade-target-").FullName;
        try
        {
            var package = Path.Combine(staging, "package");
            Directory.CreateDirectory(package);
            File.WriteAllText(Path.Combine(package, "install.ps1"), "throw 'Simulated install failure'\n");
            var installed = Path.Combine(target, "lightytp.exe");
            File.WriteAllText(installed, "old executable");
            var log = Path.Combine(target, "upgrade.log");
            // An already exited parent exercises the race between exiting and the helper starting.
            var parentInfo = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = true };
            foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-Command", "exit 0" }) { parentInfo.ArgumentList.Add(argument); }
            using var parent = Process.Start(parentInfo)!;
            await parent.WaitForExitAsync();
            AppUpgradeService.ScheduleWindows(new PreparedAppUpgrade("v0.6.0", package), target, log, parent.Id);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            while (!File.Exists(log) || !ReadLog(log).Contains("Downloaded files kept", StringComparison.Ordinal))
            {
                if (timeout.IsCancellationRequested)
                {
                    throw new Exception($"Helper did not report its failure: {(File.Exists(log) ? ReadLog(log) : "No log created")}");
                }
                await Task.Delay(100);
            }
            Equal("old executable", File.ReadAllText(installed));
            var status = ReadLog(log);
            if (!status.Contains("Simulated install failure", StringComparison.Ordinal) || status.Contains("Upgrade complete", StringComparison.Ordinal))
            {
                throw new Exception($"Failed installation was not logged correctly: {status}");
            }
            if (!File.Exists(Path.Combine(package, "install.ps1"))) { throw new Exception("Failed installation should retain recovery files."); }
        }
        finally
        {
            Directory.Delete(staging, recursive: true);
            Directory.Delete(target, recursive: true);
        }
    }

    public static async Task CliAsync()
    {
        var runner = new ProcessRunner();
        foreach (var arguments in new[]
        {
            new[] { "upgrade", "--help" }, new[] { "upgrade" }, new[] { "upgrade", "update" },
            new[] { "upgrade", "uninstall" }, new[] { "upgrade", "some song" }, new[] { "upgrade", "--diagnose" },
            new[] { "upgrade", "--version" }, new[] { "upgrade", "--yes" }
        })
        {
            var result = await runner.RunAsync("dotnet", [typeof(AppUpgradeService).Assembly.Location, .. arguments], null, null, TimeSpan.FromSeconds(20), CancellationToken.None);
            var help = arguments.Contains("--help");
            if (result.ExitCode != (help ? 0 : 2)) { throw new Exception($"Unexpected CLI result for {string.Join(' ', arguments)}: {result.StandardError}"); }
            if (help && !result.StandardOutput.Contains("upgrade", StringComparison.Ordinal)) { throw new Exception("Upgrade is absent from help."); }
            if (arguments.Length == 1 && !result.StandardError.Contains("installed terminal edition", StringComparison.Ordinal))
            {
                throw new Exception("Upgrade must bypass playback-tool checks and refuse source builds.");
            }
        }
    }

    public static async Task LiveDownloadAsync()
    {
        var staging = Directory.CreateTempSubdirectory("lightytp-upgrade-test-").FullName;
        try
        {
            using var client = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(10));
            var platform = OperatingSystem.IsWindows() ? "win" : OperatingSystem.IsMacOS() ? "macos" : "linux";
            var asset = AppUpgradeService.GetAssetName(platform, RuntimeInformation.ProcessArchitecture);
            var upgrade = await new AppUpgradeService(client).PrepareAsync(new Version(0, 0, 0), asset, staging, Console.WriteLine, timeout.Token);
            if (upgrade is null) { throw new Exception("No live release downloaded."); }
            Console.WriteLine($"Verified {upgrade.Version} without installing it.");
        }
        finally { Directory.Delete(staging, recursive: true); }
    }

    private static byte[] CreateZip(string scenario = "valid")
    {
        using var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            var names = new[] { "ytmusic.exe", "install.ps1", "tools/yt-dlp.exe", "tools/mpv.exe", "tools/deno.exe" };
            foreach (var name in names)
            {
                if (scenario == "incomplete" && name == "tools/mpv.exe") { continue; }
                using var writer = new StreamWriter(zip.CreateEntry(name).Open());
                writer.Write("new executable");
            }
            if (scenario == "zip-slip")
            {
                using var writer = new StreamWriter(zip.CreateEntry("../escaped.txt").Open());
                writer.Write("must not escape");
            }
        }
        return output.ToArray();
    }

    private static byte[] CreateTar(string scenario)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionMode.Compress, leaveOpen: true))
        using (var tar = new TarWriter(gzip))
        {
            var entry = new PaxTarEntry(scenario == "symlink" ? TarEntryType.SymbolicLink : TarEntryType.RegularFile,
                scenario == "missing" ? "../ytmusic" : "./ytmusic");
            if (scenario == "symlink") { entry.LinkName = "/unexpected/target"; }
            else { entry.DataStream = new MemoryStream(Encoding.UTF8.GetBytes("new executable")); }
            tar.WriteEntry(entry);
            if (scenario == "duplicate")
            {
                entry.DataStream!.Position = 0;
                tar.WriteEntry(entry);
            }
            var extra = new PaxTarEntry(TarEntryType.RegularFile, "../escaped.txt") { DataStream = new MemoryStream([1, 2, 3]) };
            tar.WriteEntry(extra);
        }
        return output.ToArray();
    }

    private static void Equal(string expected, string actual)
    {
        if (expected != actual) { throw new InvalidOperationException($"Expected '{expected}', got '{actual}'."); }
    }

    private static string ReadLog(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private sealed class ReleaseHandler(byte[] archive, string scenario = "valid", string asset = WindowsAsset) : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var uri = request.RequestUri!.AbsoluteUri;
            Requests.Add(uri);
            HttpContent content;
            if (uri.EndsWith("/releases/latest", StringComparison.Ordinal))
            {
                if (request.Headers.UserAgent.Count == 0) { throw new Exception("GitHub request needs a user agent."); }
                var tag = scenario switch { "same" => "v0.5.0", "older" => "v0.4.0", "invalid-version" => "latest", _ => "v0.6.0" };
                content = new StringContent($$"""
                    {"tag_name":"{{tag}}","draft":{{(scenario == "draft" ? "true" : "false")}},"prerelease":{{(scenario == "prerelease" ? "true" : "false")}},
                    "assets":[{"name":"{{(scenario == "missing-asset" ? "wrong.zip" : asset)}}","browser_download_url":"https://untrusted.invalid/payload"},{"name":"SHA256SUMS.txt"}]}
                    """);
            }
            else if (uri.EndsWith("/SHA256SUMS.txt", StringComparison.Ordinal))
            {
                var hash = scenario == "bad-checksum" ? new string('0', 64) : Convert.ToHexString(SHA256.HashData(archive)).ToLowerInvariant();
                var checksum = $"{hash}  {asset}\n";
                content = new StringContent(scenario switch { "missing-checksum" => "", "duplicate-checksum" => checksum + checksum, _ => checksum });
            }
            else if (uri.EndsWith('/' + asset, StringComparison.Ordinal)) { content = new ByteArrayContent(archive); }
            else { throw new Exception($"Unexpected download: {uri}"); }
            return Task.FromResult(new HttpResponseMessage(scenario == "http-error" ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK) { Content = content });
        }
    }
}
