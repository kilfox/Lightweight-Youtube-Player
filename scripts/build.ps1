[CmdletBinding()]
param(
    [switch]$SkipTests,
    [switch]$NativeAot
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$solution = Join-Path $repositoryRoot 'YtMusicTerminal.slnx'
$project = Join-Path $repositoryRoot 'src\YtMusicTerminal\YtMusicTerminal.csproj'
$testProject = Join-Path $repositoryRoot 'tests\YtMusicTerminal.Tests\YtMusicTerminal.Tests.csproj'
$output = Join-Path $repositoryRoot 'artifacts\win-x64'

$dotnet = $env:YTMUSIC_DOTNET
if ([string]::IsNullOrWhiteSpace($dotnet)) {
    $command = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($null -eq $command) {
        throw 'The .NET 10 SDK is required. Set YTMUSIC_DOTNET to dotnet.exe if it is not on PATH.'
    }

    $dotnet = $command.Source
}

Push-Location $repositoryRoot
try {
    & $dotnet build $solution -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }

    if (-not $SkipTests) {
        & $dotnet run --project $testProject -c Release --no-build
        if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
    }

    $publishArguments = @(
        'publish', $project,
        '-c', 'Release',
        '-r', 'win-x64',
        '--self-contained', 'true',
        '-o', $output
    )
    if ($NativeAot) {
        $publishArguments += '-p:PublishAot=true'
    }
    else {
        $publishArguments += @(
            '-p:PublishSingleFile=true',
            '-p:PublishTrimmed=true',
            '-p:TrimMode=full',
            '-p:EnableCompressionInSingleFile=true'
        )
    }

    & $dotnet @publishArguments
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }

    Copy-Item -LiteralPath (Join-Path $repositoryRoot 'README.md') -Destination $output -Force
    Copy-Item -LiteralPath (Join-Path $repositoryRoot 'MANUAL.md') -Destination $output -Force
    Copy-Item -LiteralPath (Join-Path $repositoryRoot 'HOTKEYS.md') -Destination $output -Force
    Copy-Item -LiteralPath (Join-Path $repositoryRoot 'LICENSE') -Destination $output -Force
    Copy-Item -LiteralPath (Join-Path $repositoryRoot 'THIRD_PARTY_NOTICES.md') -Destination $output -Force
    Copy-Item -LiteralPath (Join-Path $repositoryRoot 'scripts\install.ps1') -Destination $output -Force
    Copy-Item -LiteralPath (Join-Path $repositoryRoot 'scripts\bootstrap-tools.ps1') -Destination (Join-Path $output 'update-tools.ps1') -Force

    $sourceTools = Join-Path $repositoryRoot 'tools'
    $outputTools = Join-Path $output 'tools'
    $toolNames = @('yt-dlp.exe', 'deno.exe', 'mpv.exe')
    $missingTools = @($toolNames | Where-Object { -not (Test-Path -LiteralPath (Join-Path $sourceTools $_)) })
    if ($missingTools.Count -eq 0) {
        New-Item -ItemType Directory -Path $outputTools -Force | Out-Null
        foreach ($toolName in $toolNames) {
            Copy-Item -LiteralPath (Join-Path $sourceTools $toolName) -Destination (Join-Path $outputTools $toolName) -Force
        }

        Write-Host 'Included yt-dlp, Deno, and mpv in the published tools directory.'
    }
    else {
        Write-Warning "Playback tools were not included. Run scripts\bootstrap-tools.ps1 and build again. Missing: $($missingTools -join ', ')"
    }

    Write-Host "Published to $output"
}
finally {
    Pop-Location
}
