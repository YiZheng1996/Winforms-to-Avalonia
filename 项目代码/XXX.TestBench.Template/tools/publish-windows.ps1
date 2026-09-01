<#
.SYNOPSIS
Windows 发布脚本（默认框架依赖 win-x64；可加 -SelfContained 自包含）。
输出目录：项目 artifacts\publish（已 gitignore）。
#>
param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [switch]$SelfContained
)
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$app = Join-Path $root "src\XXX.TestBench.App\XXX.TestBench.App.csproj"
$out = Join-Path $root "artifacts\publish"
if ($SelfContained) {
    dotnet publish $app -c $Configuration -r $Runtime --self-contained true -o $out --nologo
} else {
    dotnet publish $app -c $Configuration -r $Runtime --self-contained false -o $out --nologo
}
if ($LASTEXITCODE -ne 0) { throw "Publish failed" }
Write-Host "发布完成：$out" -ForegroundColor Green
Write-Host "提示：框架依赖需要目标机安装 .NET 8 Desktop Runtime；自包含需加 -SelfContained。"
Write-Host "UOS 发布需在 UOS 环境执行（linux-x64 运行时/自包含），本脚本仅为 Windows 冒烟。"
