# Phase E：测试结果汇总

日期：2026-08-29  
工作区：`D:\易峥\2026\2026-09\Avalonia_上位机通用模板`  
迁移解：`D:\易峥\2026\2026-09\Avalonia_上位机通用模板\项目代码\XXX.TestBench.Uos\XXX.TestBench.Uos.sln`  
日志目录：`D:\Codex相关\phase-e\logs`

## 1. 总结果

| 检查 | 命令/范围 | 结果 |
|---|---|---|
| Phase A/P1 证据完整性 | `pwsh -NoProfile -ExecutionPolicy Bypass -File "项目代码\XXX.TestBench.Uos\tools\Test-MigrationEvidence.ps1"` | `PASS coverage_rows=393 tag_rows=140 legacy_subscription=102 source_capability=124 opf_only_reserve=16` |
| Release 解决方案构建 | `dotnet build "...\XXX.TestBench.Uos.sln" --no-restore --configuration Release --verbosity minimal` | `PASS`；0 警告、0 错误 |
| Core 行为 | `dotnet run --project tests\XXX.TestBench.Core.Tests ... --no-build --configuration Release` | `PASS`；初始化、登录、产品、DI00、Test00、停止、断线、收尾故障 |
| Gateway 合同 | `dotnet run --project tests\XXX.TestBench.Gateway.ContractChecks ... --no-build --configuration Release -- config` | `PASS`；配置、只读、适配器构造、5 点仿真、B11 |
| Gateway Runtime | `dotnet run --project tests\XXX.TestBench.Gateway.Runtime.Tests ... --no-build --configuration Release` | `PASS`；成功、部分失败、读异常断开/恢复、取消、清理、设置回滚 |
| Avalonia VM | `dotnet run --project tests\XXX.TestBench.Avalonia.Tests ... --no-build --configuration Release` | `PASS`；启动、只读刷新、仿真登录、产品选择、Test00 门禁、写禁用 |
| Avalonia Headless | 独立进程运行 `XXX.TestBench.Avalonia.Headless.Tests` | `PASS`；XAML、绑定、AutomationProperties、布局、命令驱动 Core |
| Gateway Host 离线采样 | `--config ...\config\gatewaysettings.json --once` | `PASS`；退出码 0、`simulated=true`、`healthy=true`、`writesEnabled=False`、5 点 JSON |
| Windows/UOS 自包含发布 | Avalonia 和 Host 各执行 `win-x64`、`linux-x64` publish | `PASS`；4 个发布目录均生成 apphost；配置已包含 |
| 发布版 Windows Host 启动 | 包内 `XXX.TestBench.Gateway.Host.exe --config .\config\gatewaysettings.json --once` | `PASS`；退出码 0 |
| 发布版 Windows Avalonia 启动 | 包内 `XXX.TestBench.Avalonia.exe` | `PASS-WINDOWS`；存活约 3 秒后由测试进程主动结束 |
| UOS 真机启动/设备通信 | 当前没有目标机和设备 | `PENDING-FIELD` |

## 2. 可复查日志

本轮命令产生的输出保存在：

- `D:\Codex相关\phase-e\logs\solution-build-release-after-fix.log`
- `D:\Codex相关\phase-e\logs\core-tests-after-fix.log`
- `D:\Codex相关\phase-e\logs\gateway-contract-checks-after-fix.log`
- `D:\Codex相关\phase-e\logs\gateway-runtime-tests-after-fix.log`
- `D:\Codex相关\phase-e\logs\avalonia-vm-tests-after-fix.log`
- `D:\Codex相关\phase-e\logs\avalonia-headless-tests-after-fix.log`
- `D:\Codex相关\phase-e\logs\gateway-host-once.log`
- `D:\Codex相关\phase-e\logs\solution-build-release-after-packaging-fix.log`
- `D:\Codex相关\phase-e\logs\republish-gateway-win-x64.log`
- `D:\Codex相关\phase-e\logs\republish-gateway-linux-x64.log`
- `D:\Codex相关\phase-e\logs\publish-avalonia-win-x64-final.log`
- `D:\Codex相关\phase-e\logs\publish-avalonia-linux-x64-final.log`
- `D:\Codex相关\phase-e\logs\publish-gateway-win-x64-final.log`
- `D:\Codex相关\phase-e\logs\publish-gateway-linux-x64-final.log`
- `D:\Codex相关\phase-e\logs\gateway-host-published-win-once.log`
- `D:\Codex相关\phase-e\logs\gateway-host-published-win-once-final.log`
- `D:\Codex相关\phase-e\logs\package-hashes-final.txt`

## 3. 发布目录

- Avalonia Windows：`D:\Codex相关\phase-e\publish\avalonia-win-x64`
- Avalonia UOS 候选：`D:\Codex相关\phase-e\publish\avalonia-linux-x64`
- Gateway Host Windows：`D:\Codex相关\phase-e\publish\gateway-host-win-x64`
- Gateway Host UOS 候选：`D:\Codex相关\phase-e\publish\gateway-host-linux-x64`

四个目录均未发现精确旧依赖文件名 `Interop.OPCAutomation.dll`、`SunnyUI.dll`、`AntdUI.dll`、`rw3.dll`、`rwdsl2.dll`、`office.dll`、`Report.dll`。系统运行库名称中出现的 `System.Runtime.InteropServices*.dll` 不属于旧 OPC/Office 依赖。

## 4. 失败尝试与处理

曾用统一的外置 `BaseOutputPath`/`BaseIntermediateOutputPath` 对整个解决方案执行 `dotnet restore/build`，因所有项目共用同一个资产目录造成 `MSB4006 ResolveProjectReferences` 循环依赖；该命令不是产品测试失败。改用项目已有恢复资产执行标准 Release 构建后，9 个项目全部生成成功。后续日志和发布物仍放在 `D:\Codex相关\phase-e`，没有把临时日志写入项目源目录。

## 5. 证据边界

测试是可执行的纯 Core、Gateway、VM 和独立 Headless 检查，不是读取源码文本的假测试。它们证明了当前离线实现的行为合同，不证明真实 PLC/Modbus 地址、字节序、串口、OPC UA、UOS 桌面、触控、systemd、连续运行或现场安全回读。

## 6. 本轮完整命令

以下命令按顺序执行；`XXX.TestBench.Avalonia.Headless.Tests` 是独立可执行项目，单独进程运行：

```powershell
$repo = 'D:\易峥\2026\2026-09\Avalonia_上位机通用模板\项目代码\XXX.TestBench.Uos'
pwsh -NoProfile -ExecutionPolicy Bypass -File "$repo\tools\Test-MigrationEvidence.ps1"
dotnet build "$repo\XXX.TestBench.Uos.sln" --no-restore --configuration Release --verbosity minimal
dotnet run --project "$repo\tests\XXX.TestBench.Core.Tests\XXX.TestBench.Core.Tests.csproj" --no-build --configuration Release
dotnet run --project "$repo\tests\XXX.TestBench.Gateway.ContractChecks\XXX.TestBench.Gateway.ContractChecks.csproj" --no-build --configuration Release -- "$repo\config\gatewaysettings.json"
dotnet run --project "$repo\tests\XXX.TestBench.Gateway.Runtime.Tests\XXX.TestBench.Gateway.Runtime.Tests.csproj" --no-build --configuration Release
dotnet run --project "$repo\tests\XXX.TestBench.Avalonia.Tests\XXX.TestBench.Avalonia.Tests.csproj" --no-build --configuration Release
dotnet run --project "$repo\tests\XXX.TestBench.Avalonia.Headless.Tests\XXX.TestBench.Avalonia.Headless.Tests.csproj" --no-build --configuration Release
dotnet run --project "$repo\src\XXX.TestBench.Gateway.Host\XXX.TestBench.Gateway.Host.csproj" --no-build --configuration Release -- --config "$repo\config\gatewaysettings.json" --once
```
