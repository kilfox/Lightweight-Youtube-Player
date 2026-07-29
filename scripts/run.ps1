[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$project = Join-Path $repositoryRoot 'src\YtMusicTerminal\YtMusicTerminal.csproj'
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
    & $dotnet run --project $project -c Release
}
finally {
    Pop-Location
}

