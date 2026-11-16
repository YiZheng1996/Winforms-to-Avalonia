<#
.SYNOPSIS
Builds the solution and records Core, Integration, App, and isolated Headless test results.
.DESCRIPTION
All build, NuGet, CLI, test, and temporary artifacts default to D:\Codex相关\XXX.TestBench.Template.
Tests are supervised by their own dotnet PID tree. A timeout is always a failure.
#>
param(
    [string]$Configuration = "Release",
    [string]$TaskRoot = "D:\Codex相关\XXX.TestBench.Template",
    [int]$TestTimeoutSeconds = 600,
    [int]$HeadlessTimeoutSeconds = 180
)

$ErrorActionPreference = "Stop"
$projectRoot = Split-Path -Parent $PSScriptRoot
$solution = Join-Path $projectRoot "XXX.TestBench.Template.sln"
$artifactRoot = Join-Path $TaskRoot "artifacts"
$resultsRoot = Join-Path $TaskRoot "test-results"
$tempRoot = Join-Path $TaskRoot "temp"
$logsRoot = Join-Path $TaskRoot "logs"
$null = New-Item -ItemType Directory -Path $artifactRoot, $resultsRoot, $tempRoot, $logsRoot -Force

$env:TEMP = $tempRoot
$env:TMP = $tempRoot
$env:DOTNET_CLI_HOME = Join-Path $TaskRoot "dotnet-cli"
$env:NUGET_PACKAGES = Join-Path $TaskRoot "nuget-packages"
$env:NUGET_HTTP_CACHE_PATH = Join-Path $TaskRoot "nuget-http-cache"
$env:NUGET_PLUGINS_CACHE_PATH = Join-Path $TaskRoot "nuget-plugins-cache"
$null = New-Item -ItemType Directory -Path $env:DOTNET_CLI_HOME, $env:NUGET_PACKAGES, $env:NUGET_HTTP_CACHE_PATH, $env:NUGET_PLUGINS_CACHE_PATH -Force

$dotnetPath = (Get-Command dotnet -ErrorAction Stop).Source

function Invoke-DotNetBuild {
    param([string[]]$Arguments)
    & $dotnetPath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet $($Arguments[0]) failed with exit code $LASTEXITCODE"
    }
}

function ConvertTo-WindowsCommandLineArgument {
    param([AllowEmptyString()][string]$Value)
    if ($null -eq $Value) { return '""' }
    if ($Value.Length -gt 0 -and $Value -notmatch '[\s"]') { return $Value }

    $builder = [System.Text.StringBuilder]::new()
    [void]$builder.Append('"')
    $backslashes = 0
    foreach ($character in $Value.ToCharArray()) {
        if ($character -eq '\') {
            $backslashes++
            continue
        }
        if ($character -eq '"') {
            [void]$builder.Append(('\' * (($backslashes * 2) + 1)))
            [void]$builder.Append('"')
            $backslashes = 0
            continue
        }
        if ($backslashes -gt 0) {
            [void]$builder.Append(('\' * $backslashes))
            $backslashes = 0
        }
        [void]$builder.Append($character)
    }
    if ($backslashes -gt 0) { [void]$builder.Append(('\' * ($backslashes * 2))) }
    [void]$builder.Append('"')
    return $builder.ToString()
}

function Stop-ProcessTree {
    param([int]$RootProcessId)

    if ([System.Diagnostics.Process]::GetCurrentProcess().Id -eq $RootProcessId) {
        throw 'Refusing to stop the current PowerShell process.'
    }

    $processes = @(Get-CimInstance -ClassName Win32_Process -ErrorAction Stop)
    $pending = [System.Collections.Generic.List[int]]::new()
    $ordered = [System.Collections.Generic.List[int]]::new()
    $pending.Add($RootProcessId)
    for ($index = 0; $index -lt $pending.Count; $index++) {
        $parentId = $pending[$index]
        $ordered.Add($parentId)
        foreach ($child in $processes | Where-Object { $_.ParentProcessId -eq $parentId }) {
            $pending.Add([int]$child.ProcessId)
        }
    }

    for ($index = $ordered.Count - 1; $index -ge 0; $index--) {
        Stop-Process -Id $ordered[$index] -Force -ErrorAction SilentlyContinue
    }
}

function Invoke-DotNetSupervised {
    param(
        [string[]]$Arguments,
        [int]$TimeoutSeconds,
        [string]$LogName
    )
    $stdoutPath = Join-Path $logsRoot "$LogName.stdout.log"
    $stderrPath = Join-Path $logsRoot "$LogName.stderr.log"
    $psi = [System.Diagnostics.ProcessStartInfo]::new()
    $psi.FileName = $dotnetPath
    $psi.WorkingDirectory = $projectRoot
    $psi.UseShellExecute = $false
    $psi.CreateNoWindow = $true
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $argumentListProperty = $psi.GetType().GetProperty('ArgumentList')
    if ($null -ne $argumentListProperty) {
        $argumentList = $argumentListProperty.GetValue($psi, $null)
        foreach ($argument in $Arguments) { [void]$argumentList.Add($argument) }
    }
    else {
        $psi.Arguments = [string]::Join(' ', @($Arguments | ForEach-Object { ConvertTo-WindowsCommandLineArgument $_ }))
    }

    $process = [System.Diagnostics.Process]::new()
    $process.StartInfo = $psi
    if (-not $process.Start()) { throw "Could not start dotnet $($Arguments[0])" }
    $stdoutTask = $process.StandardOutput.ReadToEndAsync()
    $stderrTask = $process.StandardError.ReadToEndAsync()
    $finished = $process.WaitForExit($TimeoutSeconds * 1000)
    if (-not $finished) {
        $killTreeMethod = $process.GetType().GetMethod('Kill', [type[]]@([bool]))
        if ($null -ne $killTreeMethod) {
            try { $killTreeMethod.Invoke($process, @($true)) } catch { Stop-ProcessTree -RootProcessId $process.Id }
        }
        else {
            Stop-ProcessTree -RootProcessId $process.Id
        }
        $process.WaitForExit()
        $stdoutTask.GetAwaiter().GetResult() | Set-Content -LiteralPath $stdoutPath -Encoding utf8
        $stderrTask.GetAwaiter().GetResult() | Set-Content -LiteralPath $stderrPath -Encoding utf8
        "Timed out after $TimeoutSeconds seconds; PID tree rooted at $($process.Id) was terminated." |
            Set-Content -LiteralPath (Join-Path $logsRoot "$LogName.timeout.txt") -Encoding utf8
        throw "$LogName timed out after $TimeoutSeconds seconds"
    }
    $stdout = $stdoutTask.GetAwaiter().GetResult()
    $stderr = $stderrTask.GetAwaiter().GetResult()
    $stdout | Set-Content -LiteralPath $stdoutPath -Encoding utf8
    $stderr | Set-Content -LiteralPath $stderrPath -Encoding utf8
    if ($process.ExitCode -ne 0) {
        throw "$LogName failed with exit code $($process.ExitCode); see $stdoutPath and $stderrPath"
    }
}

function Assert-TrxSuccess {
    param([string]$Path, [string]$Name)
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "$Name did not produce a TRX file: $Path"
    }
    [xml]$trx = Get-Content -LiteralPath $Path -Raw
    $results = @($trx.SelectNodes("//*[local-name()='UnitTestResult']"))
    $total = $results.Count
    $passed = @($results | Where-Object { $_.GetAttribute('outcome') -eq 'Passed' }).Count
    $failed = @($results | Where-Object { $_.GetAttribute('outcome') -in @('Failed', 'Error', 'Timeout', 'Aborted', 'NotRunnable') }).Count
    $skipped = $total - $passed - $failed
    Write-Host "$Name TRX: total=$total passed=$passed failed=$failed skipped=$skipped ($Path)"
    if ($total -eq 0 -or $failed -ne 0) {
        throw "$Name TRX did not pass: total=$total, failed=$failed"
    }
}

Write-Host "== Release build (artifacts: $artifactRoot) ==" -ForegroundColor Cyan
Invoke-DotNetBuild @("build", $solution, "-c", $Configuration, "--nologo", "--artifacts-path", $artifactRoot)

$testProjects = @(
    @{ Name = "Core"; Path = "tests\XXX.TestBench.Core.Tests\XXX.TestBench.Core.Tests.csproj"; Timeout = $TestTimeoutSeconds },
    @{ Name = "Integration"; Path = "tests\XXX.TestBench.Integration.Tests\XXX.TestBench.Integration.Tests.csproj"; Timeout = $TestTimeoutSeconds },
    @{ Name = "App"; Path = "tests\XXX.TestBench.App.Tests\XXX.TestBench.App.Tests.csproj"; Timeout = $TestTimeoutSeconds },
    @{ Name = "Headless"; Path = "tests\XXX.TestBench.App.Headless.Tests\XXX.TestBench.App.Headless.Tests.csproj"; Timeout = $HeadlessTimeoutSeconds }
)

foreach ($testProject in $testProjects) {
    $projectPath = Join-Path $projectRoot $testProject.Path
    $testResults = Join-Path $resultsRoot $testProject.Name.ToLowerInvariant()
    $null = New-Item -ItemType Directory -Path $testResults -Force
    $trxPath = Join-Path $testResults "$($testProject.Name).trx"
    Write-Host "== $($testProject.Name) tests ==" -ForegroundColor Cyan
    Invoke-DotNetSupervised @(
        "test", $projectPath, "-c", $Configuration, "--no-build", "--no-restore", "--nologo",
        "--artifacts-path", $artifactRoot, "--results-directory", $testResults,
        "--logger", "trx;LogFileName=$($testProject.Name).trx", "--blame-hang-timeout", "3m"
    ) $testProject.Timeout $testProject.Name
    Assert-TrxSuccess $trxPath $testProject.Name
}

Write-Host "All builds and test projects completed. Artifacts and TRX files are under $TaskRoot" -ForegroundColor Green
