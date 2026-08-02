[CmdletBinding()]
param(
    [ValidateSet('win-x64', 'linux-x64', 'linux-arm64', 'osx-x64', 'osx-arm64')]
    [string]$Runtime = 'win-x64'
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$project = Join-Path $repositoryRoot 'src\LightYTP.Gui\LightYTP.Gui.csproj'
$output = Join-Path $repositoryRoot "artifacts\gui-$Runtime"
$artifactsRoot = [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot 'artifacts'))
$resolvedOutput = [System.IO.Path]::GetFullPath($output)

if (-not $resolvedOutput.StartsWith("$artifactsRoot$([System.IO.Path]::DirectorySeparatorChar)", [StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to replace an output directory outside $artifactsRoot"
}
if (Test-Path -LiteralPath $resolvedOutput) {
    Remove-Item -LiteralPath $resolvedOutput -Recurse -Force
}

$dotnet = $env:YTMUSIC_DOTNET
if ([string]::IsNullOrWhiteSpace($dotnet)) {
    $command = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($null -eq $command) {
        throw 'The .NET 10 SDK is required. Set YTMUSIC_DOTNET to dotnet.exe if it is not on PATH.'
    }

    $dotnet = $command.Source
}

$publishOutput = $resolvedOutput
if ($Runtime.StartsWith('osx-', [StringComparison]::Ordinal)) {
    $publishOutput = Join-Path $resolvedOutput 'LightYTP GUI.app\Contents\MacOS'
}

Push-Location $repositoryRoot
try {
    & $dotnet publish $project `
        -c Release `
        -r $Runtime `
        --self-contained true `
        -o $publishOutput `
        -p:PublishSingleFile=true `
        -p:PublishTrimmed=true `
        -p:TrimMode=partial `
        -p:EnableCompressionInSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true
    if ($LASTEXITCODE -ne 0) { throw 'GUI publish failed.' }

    Get-ChildItem -LiteralPath $publishOutput -Filter '*.pdb' -File | ForEach-Object {
        Remove-Item -LiteralPath $_.FullName -Force
    }
    $terminalRuntimeConfig = Join-Path $publishOutput 'ytmusic.runtimeconfig.json'
    if (Test-Path -LiteralPath $terminalRuntimeConfig) {
        Remove-Item -LiteralPath $terminalRuntimeConfig -Force
    }

    if ($Runtime.StartsWith('osx-', [StringComparison]::Ordinal)) {
        $contentsDirectory = Split-Path -Parent $publishOutput
        Copy-Item -LiteralPath (Join-Path $repositoryRoot 'packaging\macos\Info.plist') -Destination $contentsDirectory -Force
        $resourcesDirectory = Join-Path $contentsDirectory 'Resources'
        New-Item -ItemType Directory -Path $resourcesDirectory -Force | Out-Null
        Copy-Item -LiteralPath (Join-Path $repositoryRoot 'packaging\macos\lightytp.icns') -Destination $resourcesDirectory -Force
    }

    foreach ($documentName in @('README.md', 'GUI_MANUAL.md', 'GUI_HOTKEYS.md', 'LICENSE', 'THIRD_PARTY_NOTICES.md')) {
        Copy-Item -LiteralPath (Join-Path $repositoryRoot $documentName) -Destination $resolvedOutput -Force
    }

    if ($Runtime -eq 'win-x64') {
        Copy-Item -LiteralPath (Join-Path $repositoryRoot 'scripts\install-gui.ps1') -Destination (Join-Path $resolvedOutput 'install.ps1') -Force
        Copy-Item -LiteralPath (Join-Path $repositoryRoot 'install-gui.cmd') -Destination (Join-Path $resolvedOutput 'install.cmd') -Force
        Copy-Item -LiteralPath (Join-Path $repositoryRoot 'scripts\bootstrap-tools.ps1') -Destination (Join-Path $resolvedOutput 'update-tools.ps1') -Force

        $sourceTools = Join-Path $repositoryRoot 'tools'
        $outputTools = Join-Path $resolvedOutput 'tools'
        $toolNames = @('yt-dlp.exe', 'deno.exe', 'mpv.exe')
        $missingTools = @($toolNames | Where-Object { -not (Test-Path -LiteralPath (Join-Path $sourceTools $_)) })
        if ($missingTools.Count -gt 0) {
            throw "GUI package is missing playback tools. Run scripts\bootstrap-tools.ps1 first. Missing: $($missingTools -join ', ')"
        }

        New-Item -ItemType Directory -Path $outputTools -Force | Out-Null
        foreach ($toolName in $toolNames) {
            Copy-Item -LiteralPath (Join-Path $sourceTools $toolName) -Destination (Join-Path $outputTools $toolName) -Force
        }
    }
    else {
        Copy-Item -LiteralPath (Join-Path $repositoryRoot 'scripts\install-gui.sh') -Destination (Join-Path $resolvedOutput 'install.sh') -Force
        if ($Runtime.StartsWith('linux-', [StringComparison]::Ordinal)) {
            Copy-Item -LiteralPath (Join-Path $repositoryRoot 'src\LightYTP.Gui\Assets\lightytp.png') -Destination $resolvedOutput -Force
        }
    }

    Write-Host "Published LightYTP GUI to $resolvedOutput"
}
finally {
    Pop-Location
}
