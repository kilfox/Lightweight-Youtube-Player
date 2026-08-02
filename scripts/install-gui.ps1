[CmdletBinding()]
param(
    [string]$InstallDirectory = (Join-Path $env:LOCALAPPDATA 'Programs\LightYTP-GUI')
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$publishedDirectory = Join-Path $repositoryRoot 'artifacts\gui-win-x64'
$sourceDirectory = if (Test-Path -LiteralPath (Join-Path $PSScriptRoot 'lightytp-gui.exe')) {
    $PSScriptRoot
}
else {
    $publishedDirectory
}

$requiredFiles = @(
    'lightytp-gui.exe',
    'tools\yt-dlp.exe',
    'tools\deno.exe',
    'tools\mpv.exe'
)
$missingFiles = @($requiredFiles | Where-Object {
    -not (Test-Path -LiteralPath (Join-Path $sourceDirectory $_))
})
if ($missingFiles.Count -gt 0) {
    throw "The GUI package is incomplete. Download the Windows GUI release ZIP and extract every file. Missing: $($missingFiles -join ', ')"
}

$resolvedInstallDirectory = [System.IO.Path]::GetFullPath($InstallDirectory)
New-Item -ItemType Directory -Path $resolvedInstallDirectory -Force | Out-Null
Copy-Item -Path (Join-Path $sourceDirectory '*') -Destination $resolvedInstallDirectory -Recurse -Force

$userPath = [Environment]::GetEnvironmentVariable('Path', 'User')
$pathEntries = @($userPath -split ';' | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
$alreadyOnPath = $pathEntries | Where-Object {
    [string]::Equals($_.TrimEnd('\'), $resolvedInstallDirectory.TrimEnd('\'), [StringComparison]::OrdinalIgnoreCase)
}
if (-not $alreadyOnPath) {
    [Environment]::SetEnvironmentVariable('Path', (@($pathEntries) + $resolvedInstallDirectory) -join ';', 'User')
}

$startMenuDirectory = Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs'
$shortcutPath = Join-Path $startMenuDirectory 'LightYTP GUI.lnk'
$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut($shortcutPath)
$shortcut.TargetPath = Join-Path $resolvedInstallDirectory 'lightytp-gui.exe'
$shortcut.WorkingDirectory = $resolvedInstallDirectory
$shortcut.Description = 'LightYTP lightweight music player'
$shortcut.IconLocation = "$(Join-Path $resolvedInstallDirectory 'lightytp-gui.exe'),0"
$shortcut.Save()

Write-Host "Installed LightYTP GUI to $resolvedInstallDirectory"
Write-Host 'Launch it from the Start menu or run: lightytp-gui'
