<#
.SYNOPSIS
Publishes the Avalonia app to a new Windows output directory.
.DESCRIPTION
The default output and all build, NuGet, CLI, and temporary artifacts are under the task root in D:\Codex相关.
Existing output directories are never overwritten.
#>
param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [switch]$SelfContained,
    [string]$TaskRoot = "D:\Codex相关\XXX.TestBench.Template",
    [string]$OutputPath,
    [string]$ArtifactsPath
)

$ErrorActionPreference = "Stop"
$projectRoot = Split-Path -Parent $PSScriptRoot
$appProject = Join-Path $projectRoot "src\XXX.TestBench.App\XXX.TestBench.App.csproj"

function Test-FullyQualifiedPath {
    param([string]$Path)
    return $Path -match '^(?:[A-Za-z]:[\\/]|\\\\)'
}

if ([string]::IsNullOrWhiteSpace($ArtifactsPath)) {
    $ArtifactsPath = Join-Path $TaskRoot "artifacts"
}
if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path $TaskRoot "publish\$Runtime"
}
if (-not (Test-FullyQualifiedPath $ArtifactsPath) -or -not (Test-FullyQualifiedPath $OutputPath)) {
    throw "ArtifactsPath and OutputPath must be absolute paths"
}
$ArtifactsPath = [System.IO.Path]::GetFullPath($ArtifactsPath)
$OutputPath = [System.IO.Path]::GetFullPath($OutputPath)
if (Test-Path -LiteralPath $OutputPath) {
    throw "OutputPath already exists; refusing to overwrite: $OutputPath"
}

$tempRoot = Join-Path $TaskRoot "temp"
$null = New-Item -ItemType Directory -Path $ArtifactsPath, $tempRoot -Force
$env:TEMP = $tempRoot
$env:TMP = $tempRoot
$env:DOTNET_CLI_HOME = Join-Path $TaskRoot "dotnet-cli"
$env:NUGET_PACKAGES = Join-Path $TaskRoot "nuget-packages"
$env:NUGET_HTTP_CACHE_PATH = Join-Path $TaskRoot "nuget-http-cache"
$env:NUGET_PLUGINS_CACHE_PATH = Join-Path $TaskRoot "nuget-plugins-cache"
$null = New-Item -ItemType Directory -Path $env:DOTNET_CLI_HOME, $env:NUGET_PACKAGES, $env:NUGET_HTTP_CACHE_PATH, $env:NUGET_PLUGINS_CACHE_PATH -Force

$publishArgs = @(
    "publish", $appProject, "-c", $Configuration, "-r", $Runtime,
    "--self-contained", ([string]$SelfContained.IsPresent).ToLowerInvariant(),
    "--artifacts-path", $ArtifactsPath, "-o", $OutputPath, "--nologo"
)
& dotnet @publishArgs
if ($LASTEXITCODE -ne 0) {
    throw "Publish failed with exit code $LASTEXITCODE"
}

$requiredConfigFiles = @("app.json", "device.json", "points.json", "simulation.json", "signal-bindings.json")
$missing = @($requiredConfigFiles | Where-Object { -not (Test-Path -LiteralPath (Join-Path $OutputPath "config\$_") -PathType Leaf) })
if ($missing.Count -gt 0) {
    throw "Publish output is missing config files: $($missing -join ', ')"
}
$upgradeTool = Get-ChildItem -LiteralPath $OutputPath -Recurse -File -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -like "XXX.TestBench.Upgrade*" }
if ($upgradeTool) {
    throw "The offline upgrade tool must not be included in the app publish output"
}

Write-Host "Publish succeeded: $OutputPath" -ForegroundColor Green
Write-Host "Configuration files verified: $($requiredConfigFiles -join ', ')"
Write-Host "Hardware/UOS and physical device acceptance require their own environment and evidence."
