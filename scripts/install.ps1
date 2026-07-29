[CmdletBinding()]
param(
    [string]$InstallDirectory = (Join-Path $env:LOCALAPPDATA 'Programs\LightYTP')
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$publishedDirectory = Join-Path $repositoryRoot 'artifacts\win-x64'
$sourceDirectory = if (Test-Path -LiteralPath (Join-Path $PSScriptRoot 'ytmusic.exe')) {
    $PSScriptRoot
}
else {
    $publishedDirectory
}

$requiredFiles = @(
    'ytmusic.exe',
    'tools\yt-dlp.exe',
    'tools\deno.exe',
    'tools\mpv.exe'
)
$missingFiles = @($requiredFiles | Where-Object {
    -not (Test-Path -LiteralPath (Join-Path $sourceDirectory $_))
})
if ($missingFiles.Count -gt 0) {
    throw "The standalone build is incomplete. Run scripts\bootstrap-tools.ps1 and scripts\build.ps1 first. Missing: $($missingFiles -join ', ')"
}

$resolvedInstallDirectory = [System.IO.Path]::GetFullPath($InstallDirectory)
$toolDirectory = Join-Path $resolvedInstallDirectory 'tools'
New-Item -ItemType Directory -Path $toolDirectory -Force | Out-Null

Copy-Item -LiteralPath (Join-Path $sourceDirectory 'ytmusic.exe') -Destination (Join-Path $resolvedInstallDirectory 'lightytp.exe') -Force
foreach ($toolName in @('yt-dlp.exe', 'deno.exe', 'mpv.exe')) {
    Copy-Item -LiteralPath (Join-Path $sourceDirectory "tools\$toolName") -Destination (Join-Path $toolDirectory $toolName) -Force
}

foreach ($documentName in @('README.md', 'MANUAL.md', 'HOTKEYS.md', 'LICENSE', 'THIRD_PARTY_NOTICES.md')) {
    $document = Join-Path $sourceDirectory $documentName
    if (Test-Path -LiteralPath $document) {
        Copy-Item -LiteralPath $document -Destination $resolvedInstallDirectory -Force
    }
}

$updateScript = Join-Path $sourceDirectory 'update-tools.ps1'
if (Test-Path -LiteralPath $updateScript) {
    Copy-Item -LiteralPath $updateScript -Destination $resolvedInstallDirectory -Force
}

$userPath = [Environment]::GetEnvironmentVariable('Path', 'User')
$pathEntries = @($userPath -split ';' | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
$alreadyOnPath = $pathEntries | Where-Object {
    [string]::Equals($_.TrimEnd('\'), $resolvedInstallDirectory.TrimEnd('\'), [StringComparison]::OrdinalIgnoreCase)
}
if (-not $alreadyOnPath) {
    $updatedUserPath = (@($pathEntries) + $resolvedInstallDirectory) -join ';'
    [Environment]::SetEnvironmentVariable('Path', $updatedUserPath, 'User')
}

if (-not (($env:Path -split ';') | Where-Object {
    [string]::Equals($_.TrimEnd('\'), $resolvedInstallDirectory.TrimEnd('\'), [StringComparison]::OrdinalIgnoreCase)
})) {
    $env:Path = "$env:Path;$resolvedInstallDirectory"
}

Write-Host "Installed LightYTP to $resolvedInstallDirectory"
Write-Host 'Open a new terminal and run: lightytp'
