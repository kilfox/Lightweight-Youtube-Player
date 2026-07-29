[CmdletBinding()]
param(
    [string]$ToolsDirectory = (Join-Path $PSScriptRoot '..\tools'),
    [switch]$Force
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$toolsRoot = [System.IO.Path]::GetFullPath($ToolsDirectory)
$temporaryBase = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath())
$temporaryRoot = Join-Path $temporaryBase ('ytmusic-tools-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $toolsRoot -Force | Out-Null
New-Item -ItemType Directory -Path $temporaryRoot | Out-Null

$headers = @{
    'Accept' = 'application/vnd.github+json'
    'User-Agent' = 'YtMusicTerminal-Bootstrap'
}

function Get-LatestRelease([string]$Repository) {
    Invoke-RestMethod -Headers $headers -Uri "https://api.github.com/repos/$Repository/releases/latest"
}

function Get-Asset($Release, [string]$Pattern) {
    $asset = $Release.assets | Where-Object { $_.name -match $Pattern } | Select-Object -First 1
    if ($null -eq $asset) {
        throw "Release '$($Release.tag_name)' has no asset matching '$Pattern'."
    }

    $asset
}

function Save-Asset($Asset, [string]$Destination) {
    Write-Host "Downloading $($Asset.name)..."
    Invoke-WebRequest -Headers $headers -Uri $Asset.browser_download_url -OutFile $Destination
}

function Assert-Checksum([string]$File, [string]$ChecksumFile, [string]$AssetName) {
    $escapedName = [regex]::Escape($AssetName)
    $lines = Get-Content -LiteralPath $ChecksumFile
    $line = $lines |
        Where-Object { $_ -match "^([A-Fa-f0-9]{64})\s+\*?$escapedName$" } |
        Select-Object -First 1
    if ($null -ne $line) {
        $expected = ($line -split '\s+')[0].ToLowerInvariant()
    }
    else {
        $hashLine = $lines |
            Where-Object { $_ -match '^Hash\s*:\s*([A-Fa-f0-9]{64})\s*$' } |
            Select-Object -First 1
        if ($null -eq $hashLine) {
            throw "No SHA-256 checksum was published for '$AssetName'."
        }

        $expected = ($hashLine -replace '^Hash\s*:\s*', '').Trim().ToLowerInvariant()
    }

    if ([string]::IsNullOrWhiteSpace($expected)) {
        throw "No SHA-256 checksum was published for '$AssetName'."
    }

    $actual = (Get-FileHash -Algorithm SHA256 -LiteralPath $File).Hash.ToLowerInvariant()
    if ($actual -ne $expected) {
        throw "SHA-256 mismatch for '$AssetName'."
    }
}

try {
    $ytDlpDestination = Join-Path $toolsRoot 'yt-dlp.exe'
    if ($Force -or -not (Test-Path -LiteralPath $ytDlpDestination)) {
        $release = Get-LatestRelease 'yt-dlp/yt-dlp'
        $asset = Get-Asset $release '^yt-dlp\.exe$'
        $sumsAsset = Get-Asset $release '^SHA2-256SUMS$'
        $download = Join-Path $temporaryRoot $asset.name
        $sums = Join-Path $temporaryRoot $sumsAsset.name
        Save-Asset $asset $download
        Save-Asset $sumsAsset $sums
        Assert-Checksum $download $sums $asset.name
        Copy-Item -LiteralPath $download -Destination $ytDlpDestination -Force
    }

    $denoDestination = Join-Path $toolsRoot 'deno.exe'
    if ($Force -or -not (Test-Path -LiteralPath $denoDestination)) {
        $release = Get-LatestRelease 'denoland/deno'
        $asset = Get-Asset $release '^deno-x86_64-pc-windows-msvc\.zip$'
        $sumsAsset = Get-Asset $release '^deno-x86_64-pc-windows-msvc\.zip\.sha256sum$'
        $archive = Join-Path $temporaryRoot $asset.name
        $sums = Join-Path $temporaryRoot $sumsAsset.name
        $extract = Join-Path $temporaryRoot 'deno'
        Save-Asset $asset $archive
        Save-Asset $sumsAsset $sums
        Assert-Checksum $archive $sums $asset.name
        Expand-Archive -LiteralPath $archive -DestinationPath $extract -Force
        Copy-Item -LiteralPath (Join-Path $extract 'deno.exe') -Destination $denoDestination -Force
    }

    $mpvDestination = Join-Path $toolsRoot 'mpv.exe'
    if ($Force -or -not (Test-Path -LiteralPath $mpvDestination)) {
        $release = Get-LatestRelease 'zhongfly/mpv-winbuild'
        $asset = Get-Asset $release '^mpv-x86_64-[0-9].*\.7z$'
        $sumsAsset = Get-Asset $release '^sha256\.txt$'
        $archive = Join-Path $temporaryRoot $asset.name
        $sums = Join-Path $temporaryRoot $sumsAsset.name
        $extract = Join-Path $temporaryRoot 'mpv'
        New-Item -ItemType Directory -Path $extract | Out-Null
        Save-Asset $asset $archive
        Save-Asset $sumsAsset $sums
        Assert-Checksum $archive $sums $asset.name
        & tar -xf $archive -C $extract
        if ($LASTEXITCODE -ne 0) {
            throw 'Could not extract the mpv archive with bsdtar.'
        }

        $mpv = Get-ChildItem -LiteralPath $extract -Recurse -Filter 'mpv.exe' -File | Select-Object -First 1
        if ($null -eq $mpv) {
            throw 'The mpv archive did not contain mpv.exe.'
        }

        Copy-Item -LiteralPath $mpv.FullName -Destination $mpvDestination -Force
    }

    Write-Host "Tools installed in $toolsRoot"
    & $ytDlpDestination --version
    & $denoDestination --version
    & $mpvDestination --version | Select-Object -First 1
}
finally {
    $resolvedTemporaryRoot = [System.IO.Path]::GetFullPath($temporaryRoot)
    if ($resolvedTemporaryRoot.StartsWith($temporaryBase, [StringComparison]::OrdinalIgnoreCase) -and
        (Test-Path -LiteralPath $resolvedTemporaryRoot)) {
        Remove-Item -LiteralPath $resolvedTemporaryRoot -Recurse -Force
    }
}
