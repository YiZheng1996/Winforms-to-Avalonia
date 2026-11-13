<#
.SYNOPSIS
XXX.TestBench.Template 构建与测试脚本（Release）。
说明：Avalonia.Headless 在此环境多测试进程偶发挂起，Headless 单独执行并带一次重试。
#>
param(
    [string]$Configuration = "Release"
)
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$sln = Join-Path $root "XXX.TestBench.Template.sln"

Write-Host "== Build ($Configuration) ==" -ForegroundColor Cyan
dotnet build $sln -c $Configuration --nologo
if ($LASTEXITCODE -ne 0) { throw "Build failed" }

Write-Host "== Test Core/Integration/App ==" -ForegroundColor Cyan
dotnet test (Join-Path $root "tests\XXX.TestBench.Core.Tests\XXX.TestBench.Core.Tests.csproj") -c $Configuration --nologo -v q
if ($LASTEXITCODE -ne 0) { throw "Core tests failed" }
dotnet test (Join-Path $root "tests\XXX.TestBench.Integration.Tests\XXX.TestBench.Integration.Tests.csproj") -c $Configuration --nologo -v q
if ($LASTEXITCODE -ne 0) { throw "Integration tests failed" }
dotnet test (Join-Path $root "tests\XXX.TestBench.App.Tests\XXX.TestBench.App.Tests.csproj") -c $Configuration --nologo -v q
if ($LASTEXITCODE -ne 0) { throw "App tests failed" }

Write-Host "== Test Headless (超时120s自动重试一次) ==" -ForegroundColor Cyan
$headless = Join-Path $root "tests\XXX.TestBench.App.Headless.Tests\XXX.TestBench.App.Headless.Tests.csproj"
function Invoke-HeadlessTests {
    # Avalonia.Headless 测试完成后派发线程可能不退出进程：超时杀掉，并按日志“已通过”判定成功
    $log = Join-Path $env:TEMP ("headless-" + [guid]::NewGuid().ToString("N") + ".log")
    $proc = Start-Process -FilePath "dotnet" -ArgumentList @("test", "`"$headless`"", "-c", $Configuration, "--nologo", "-v", "q") -PassThru -NoNewWindow -RedirectStandardOutput $log -RedirectStandardError ($log + ".err")
    if (-not $proc.WaitForExit(120000)) {
        Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
        Get-Process -Name testhost -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
        Start-Sleep -Seconds 2
    }
    $content = Get-Content -LiteralPath $log -Raw -ErrorAction SilentlyContinue
    return ($content -match "已通过!") -and ($content -notmatch "失败!")
}
if (-not (Invoke-HeadlessTests)) {
    Write-Host "Headless 首次运行超时/失败，重试一次..." -ForegroundColor Yellow
    Start-Sleep -Seconds 3
    if (-not (Invoke-HeadlessTests)) { throw "Headless tests failed" }
}

Write-Host "== 全部通过 ==" -ForegroundColor Green
