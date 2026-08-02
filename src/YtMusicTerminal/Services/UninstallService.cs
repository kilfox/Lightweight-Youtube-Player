using System.Diagnostics;
using System.Text;

namespace YtMusicTerminal.Services;

public enum LightYtpEdition
{
    Terminal,
    Gui
}

public enum UninstallPlatform
{
    Windows,
    Linux,
    MacOS
}

public sealed record UninstallPlan(
    string DisplayName,
    string ExpectedExecutablePath,
    string TargetPath,
    bool TargetIsDirectory,
    string? UserPathEntry,
    string? ShortcutPath,
    string? LauncherPath,
    string? DesktopEntryPath);

public static class UninstallService
{
    public static bool TryCreateDefaultPlan(
        LightYtpEdition edition,
        out UninstallPlan? plan,
        out string error)
    {
        var executablePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            plan = null;
            error = "Could not determine the running executable path.";
            return false;
        }

        var platform = OperatingSystem.IsWindows()
            ? UninstallPlatform.Windows
            : OperatingSystem.IsMacOS()
                ? UninstallPlatform.MacOS
                : UninstallPlatform.Linux;

        try
        {
            plan = CreateExpectedPlan(
                edition,
                platform,
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                Environment.GetEnvironmentVariable("XDG_DATA_HOME"));
        }
        catch (InvalidOperationException exception)
        {
            plan = null;
            error = exception.Message;
            return false;
        }

        var comparison = platform == UninstallPlatform.Windows
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        var runningPath = Path.GetFullPath(executablePath);
        if (!string.Equals(runningPath, plan.ExpectedExecutablePath, comparison))
        {
            error = $"This copy is not installed in the standard location ({plan.ExpectedExecutablePath}). Remove the portable copy manually.";
            plan = null;
            return false;
        }

        error = string.Empty;
        return true;
    }

    public static UninstallPlan CreateExpectedPlan(
        LightYtpEdition edition,
        UninstallPlatform platform,
        string homeDirectory,
        string localAppDataDirectory,
        string roamingAppDataDirectory,
        string? xdgDataHome = null)
    {
        if (platform == UninstallPlatform.Windows)
        {
            RequireDirectory(localAppDataDirectory, "local application data");
            var installDirectory = Path.GetFullPath(Path.Combine(
                localAppDataDirectory,
                "Programs",
                edition == LightYtpEdition.Terminal ? "LightYTP" : "LightYTP-GUI"));
            var executableName = edition == LightYtpEdition.Terminal ? "lightytp.exe" : "lightytp-gui.exe";
            var shortcutPath = edition == LightYtpEdition.Gui && !string.IsNullOrWhiteSpace(roamingAppDataDirectory)
                ? Path.GetFullPath(Path.Combine(roamingAppDataDirectory, "Microsoft", "Windows", "Start Menu", "Programs", "LightYTP GUI.lnk"))
                : null;

            return new UninstallPlan(
                edition == LightYtpEdition.Terminal ? "LightYTP Terminal" : "LightYTP GUI",
                Path.Combine(installDirectory, executableName),
                installDirectory,
                TargetIsDirectory: true,
                installDirectory,
                shortcutPath,
                LauncherPath: null,
                DesktopEntryPath: null);
        }

        RequireDirectory(homeDirectory, "home");
        var home = Path.GetFullPath(homeDirectory);
        var commandDirectory = Path.Combine(home, ".local", "bin");

        if (edition == LightYtpEdition.Terminal)
        {
            var executablePath = Path.GetFullPath(Path.Combine(commandDirectory, "lightytp"));
            return new UninstallPlan(
                "LightYTP Terminal",
                executablePath,
                executablePath,
                TargetIsDirectory: false,
                UserPathEntry: null,
                ShortcutPath: null,
                LauncherPath: null,
                DesktopEntryPath: null);
        }

        var launcherPath = Path.GetFullPath(Path.Combine(commandDirectory, "lightytp-gui"));
        if (platform == UninstallPlatform.MacOS)
        {
            var appDirectory = Path.GetFullPath(Path.Combine(home, "Applications", "LightYTP GUI.app"));
            return new UninstallPlan(
                "LightYTP GUI",
                Path.Combine(appDirectory, "Contents", "MacOS", "lightytp-gui"),
                appDirectory,
                TargetIsDirectory: true,
                UserPathEntry: null,
                ShortcutPath: null,
                launcherPath,
                DesktopEntryPath: null);
        }

        var dataHome = string.IsNullOrWhiteSpace(xdgDataHome)
            ? Path.Combine(home, ".local", "share")
            : Path.GetFullPath(xdgDataHome);
        var installPath = Path.GetFullPath(Path.Combine(home, ".local", "share", "lightytp-gui"));
        return new UninstallPlan(
            "LightYTP GUI",
            Path.Combine(installPath, "lightytp-gui"),
            installPath,
            TargetIsDirectory: true,
            UserPathEntry: null,
            ShortcutPath: null,
            launcherPath,
            Path.GetFullPath(Path.Combine(dataHome, "applications", "lightytp-gui.desktop")));
    }

    public static void Schedule(UninstallPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        if (HasAnotherRunningInstance(plan.ExpectedExecutablePath))
        {
            throw new InvalidOperationException($"Close every other {plan.DisplayName} window before uninstalling.");
        }

        if (OperatingSystem.IsWindows())
        {
            ScheduleWindows(plan);
        }
        else
        {
            ScheduleUnix(plan);
        }
    }

    private static void ScheduleWindows(UninstallPlan plan)
    {
        var helperPath = Path.Combine(Path.GetTempPath(), $"lightytp-uninstall-{Guid.NewGuid():N}.ps1");
        File.WriteAllText(helperPath, WindowsHelperScript, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        var startInfo = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetTempPath()
        };
        startInfo.ArgumentList.Add("-NoLogo");
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-NonInteractive");
        startInfo.ArgumentList.Add("-ExecutionPolicy");
        startInfo.ArgumentList.Add("Bypass");
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(helperPath);
        startInfo.ArgumentList.Add("-ParentProcessId");
        startInfo.ArgumentList.Add(Environment.ProcessId.ToString());
        startInfo.ArgumentList.Add("-TargetPath");
        startInfo.ArgumentList.Add(plan.TargetPath);
        startInfo.ArgumentList.Add("-TargetIsDirectory");
        startInfo.ArgumentList.Add(plan.TargetIsDirectory ? "true" : "false");
        startInfo.ArgumentList.Add("-UserPathEntry");
        startInfo.ArgumentList.Add(plan.UserPathEntry ?? string.Empty);
        startInfo.ArgumentList.Add("-ShortcutPath");
        startInfo.ArgumentList.Add(plan.ShortcutPath ?? string.Empty);
        startInfo.ArgumentList.Add("-HelperPath");
        startInfo.ArgumentList.Add(helperPath);

        try
        {
            using var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Could not start the uninstall helper.");
        }
        catch
        {
            File.Delete(helperPath);
            throw;
        }
    }

    private static void ScheduleUnix(UninstallPlan plan)
    {
        var helperPath = Path.Combine(Path.GetTempPath(), $"lightytp-uninstall-{Guid.NewGuid():N}.sh");
        File.WriteAllText(helperPath, UnixHelperScript, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        var startInfo = new ProcessStartInfo
        {
            FileName = "/bin/sh",
            UseShellExecute = false,
            WorkingDirectory = Path.GetTempPath()
        };
        startInfo.ArgumentList.Add(helperPath);
        startInfo.ArgumentList.Add(Environment.ProcessId.ToString());
        startInfo.ArgumentList.Add(plan.TargetPath);
        startInfo.ArgumentList.Add(plan.TargetIsDirectory ? "directory" : "file");
        startInfo.ArgumentList.Add(plan.LauncherPath ?? string.Empty);
        startInfo.ArgumentList.Add(plan.DesktopEntryPath ?? string.Empty);
        startInfo.ArgumentList.Add(helperPath);

        try
        {
            using var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Could not start the uninstall helper.");
        }
        catch
        {
            File.Delete(helperPath);
            throw;
        }
    }

    private static void RequireDirectory(string path, string description)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new InvalidOperationException($"Could not determine the {description} directory.");
        }
    }

    private static bool HasAnotherRunningInstance(string executablePath)
    {
        var processName = Path.GetFileNameWithoutExtension(executablePath);
        foreach (var process in Process.GetProcessesByName(processName))
        {
            using (process)
            {
                if (process.Id != Environment.ProcessId)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private const string WindowsHelperScript =
        """
        param(
            [int]$ParentProcessId,
            [string]$TargetPath,
            [string]$TargetIsDirectory,
            [string]$UserPathEntry,
            [string]$ShortcutPath,
            [string]$HelperPath
        )

        $ErrorActionPreference = 'SilentlyContinue'
        Wait-Process -Id $ParentProcessId -ErrorAction SilentlyContinue

        for ($attempt = 0; $attempt -lt 80; $attempt++) {
            if (-not (Test-Path -LiteralPath $TargetPath)) { break }
            if ($TargetIsDirectory -eq 'true') {
                Remove-Item -LiteralPath $TargetPath -Recurse -Force -ErrorAction SilentlyContinue
            }
            else {
                Remove-Item -LiteralPath $TargetPath -Force -ErrorAction SilentlyContinue
            }
            if (Test-Path -LiteralPath $TargetPath) { Start-Sleep -Milliseconds 250 }
        }

        if (-not (Test-Path -LiteralPath $TargetPath)) {
            if (-not [string]::IsNullOrWhiteSpace($ShortcutPath)) {
                Remove-Item -LiteralPath $ShortcutPath -Force -ErrorAction SilentlyContinue
            }

            if (-not [string]::IsNullOrWhiteSpace($UserPathEntry)) {
                $userPath = [Environment]::GetEnvironmentVariable('Path', 'User')
                $remaining = @($userPath -split ';' | Where-Object {
                    -not [string]::IsNullOrWhiteSpace($_) -and
                    -not [string]::Equals($_.TrimEnd('\'), $UserPathEntry.TrimEnd('\'), [StringComparison]::OrdinalIgnoreCase)
                })
                [Environment]::SetEnvironmentVariable('Path', ($remaining -join ';'), 'User')
            }
        }

        Remove-Item -LiteralPath $HelperPath -Force -ErrorAction SilentlyContinue
        """;

    private const string UnixHelperScript =
        """
        #!/usr/bin/env sh
        parent_process_id=$1
        target_path=$2
        target_kind=$3
        launcher_path=$4
        desktop_entry_path=$5
        helper_path=$6

        while kill -0 "$parent_process_id" 2>/dev/null; do
            sleep 0.1
        done

        if [ "$target_kind" = "directory" ]; then
            rm -rf -- "$target_path"
        else
            rm -f -- "$target_path"
        fi
        if [ ! -e "$target_path" ]; then
            if [ -n "$launcher_path" ]; then rm -f -- "$launcher_path"; fi
            if [ -n "$desktop_entry_path" ]; then rm -f -- "$desktop_entry_path"; fi
        fi
        rm -f -- "$helper_path"
        """;
}
