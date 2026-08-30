# Phase E：测试结果汇总

日期：2026-08-30
工作区：`D:\易峥\2026\2026-09\Avalonia_上位机通用模板`
迁移解：`D:\易峥\2026\2026-09\Avalonia_上位机通用模板\项目代码\XXX.TestBench.Uos\XXX.TestBench.Uos.sln`
本轮日志：`D:\Codex相关\phase-d-ui-2026-08-30\logs`
信捷现场探针：`D:\Codex相关\phase-e-field\2026-08-29`

当前记录状态：管理/日志专项源码已冻结；证据脚本、Release、Core/Gateway/Avalonia/Headless、Host `--once`、四包、包内烟测、哈希/禁止依赖扫描和独立 clone 均已从本轮实现提交重新执行。Linux 包仍是发布成功而非 UOS 启动验收，现场结论继续保持 `PENDING-FIELD`。

## 1. 最终顺序验证结果

| 顺序 | 检查 | 实际命令/范围 | 最终结果 |
|---:|---|---|---|
| 1 | Phase A/P1/UI 证据完整性 | `pwsh -NoProfile -ExecutionPolicy Bypass -File .\tools\Test-MigrationEvidence.ps1` | `PASS coverage_rows=393 ui_rows=51 ui_evidence_rows=32 tag_rows=140 legacy_subscription=102 source_capability=124 opf_only_reserve=16 public_excluded_coverage_paths=13` |
| 2 | Release 解决方案构建 | `dotnet build .\XXX.TestBench.Uos.sln --no-restore --configuration Release --verbosity minimal` | `PASS`；9 个项目，0 警告、0 错误 |
| 3 | Core 行为 | `dotnet run --project tests\XXX.TestBench.Core.Tests ... --no-build --configuration Release` | `PASS core-state-machine init=login product=di00-unknown-false test00-manual disconnect stop cleanup-fault` |
| 4 | Gateway 合同 | `dotnet run --project tests\XXX.TestBench.Gateway.ContractChecks ... --no-build --configuration Release -- .\config\gatewaysettings.json` | `PASS gateway-contracts readonly=locked config=validated adapters=constructed-without-device-I/O` |
| 5 | Gateway Runtime | `dotnet run --project tests\XXX.TestBench.Gateway.Runtime.Tests ... --no-build --configuration Release` | `PASS gateway-runtime simulation=failure=cancellation=cleanup settings=atomic-predecessor` |
| 6 | Avalonia VM | `dotnet run --project tests\XXX.TestBench.Avalonia.Tests ... --no-build --configuration Release` | `PASS avalonia-vm navigation=8 product=test-items management=state reports=filter+paging calibration=6-outputs-local-only logs=filter instrument=gated startup=cancel+faulted writes=disabled` |
| 7 | Avalonia Headless | 独立进程运行 `XXX.TestBench.Avalonia.Headless.Tests` | `PASS avalonia-headless navigation=8 shortcuts=ctrl1-ctrl8 binding=two-way command=core-gated pages=test+process+management+reports+calibration+diagnostics+instrument accessibility=names+touch44 layout=narrow+wide writes=disabled` |
| 8 | Gateway Host 离线采样 | `dotnet run ...Gateway.Host... -- --config .\config\gatewaysettings.json --once` | `PASS`；退出码 0，`OfflineSimulation`、`writesEnabled=False`、5 点 Good/仿真快照 |
| 9 | 4 个自包含包 | Avalonia/Gateway Host 各执行 `win-x64`、`linux-x64` publish | `PASS`；由实现提交 `92cdec56...` 生成，Linux 仅为 `PASS-PUBLISH-ONLY` |
| 10 | 发布版 Windows Host | 包内 `XXX.TestBench.Gateway.Host.exe --config .\config\gatewaysettings.json --once` | `PASS-OFFLINE`；退出码 0，`OfflineSimulation`、`writesEnabled=False`、五点 Good |
| 11 | 发布版 Windows Avalonia | 隐藏启动 apphost，3 秒后由烟测进程按精确 PID 定向结束 | `PASS-WINDOWS-PROCESS-START`；只证明进程启动，页面行为仍以 Headless 为准 |
| 12 | 发布目录安全扫描 | 4 个目录精确扫描 7 个 Legacy 依赖名并计算 apphost/config SHA-256 | `PASS`；四包命中均为 0，配置哈希一致 |
| 13 | 信捷 Modbus TCP 仅读协议探针 | `192.168.0.51:502`；UnitId=1 候选；功能码 01/03/04 | `PASS-FIELD-PROBE`；仅证明合法读取响应，不代表 P2 业务映射通过 |
| 14 | UOS 真机/业务设备映射 | 无 UOS 目标机、无已闭合 P2 应用设备映射；仅有信捷仅读协议探针 | `PENDING-FIELD` |
| 15 | 独立 clone 复现 | 从远端 `main` 克隆实现提交，重新 restore、证据、Release、Core/Gateway/Avalonia/Headless、Host `--once` | `PASS`；clone HEAD=`92cdec56a3a93132fd5258446729fafedd9bc631` |

## 2. 本轮可复查日志

- `01-migration-evidence.log`
- `02-release-build.log`
- `03-core-tests.log`
- `04-gateway-contract-checks.log`
- `05-gateway-runtime-tests.log`
- `06-avalonia-vm-tests.log`
- `07-avalonia-headless-tests.log`
- `08-gateway-host-once.log`
- `09-publish-avalonia-win-x64.log`
- `10-publish-avalonia-linux-x64.log`
- `11-publish-gateway-host-win-x64.log`
- `12-publish-gateway-host-linux-x64.log`
- `13-published-gateway-host-win-once.log`
- `14-published-avalonia-win-smoke.log`
- `15-package-scan-and-hashes.log`
- `16-independent-clone.log`
- `17-independent-clone-validation.log`

以上文件均位于 `D:\Codex相关\phase-d-ui-2026-08-30\logs` 且不属于仓库提交。`01`～`08` 是当前源码顺序回归，`09`～`15` 是实现提交的发布/烟测/哈希证据，`16`～`17` 是远端独立 clone 与完整复现记录。

## 3. 发布目录与哈希

| 发布物 | 目录 | apphost SHA-256 |
|---|---|---|
| Avalonia Windows | `D:\Codex相关\phase-d-ui-2026-08-30\publish\avalonia-win-x64` | `4A1EA7685EB545165CD35748C730A290A196A96DD28F1755CE64ED16908A54C2` |
| Avalonia Linux/UOS 候选 | `D:\Codex相关\phase-d-ui-2026-08-30\publish\avalonia-linux-x64` | `A603336CF5C561A861A48142DD1877AECBA03C887D40066302F1256BA2A1E590` |
| Gateway Host Windows | `D:\Codex相关\phase-d-ui-2026-08-30\publish\gateway-host-win-x64` | `7522424C4FB5B82BAB993B20E9A17025BB46B247C5186EB0364113287FC4E501` |
| Gateway Host Linux/UOS 候选 | `D:\Codex相关\phase-d-ui-2026-08-30\publish\gateway-host-linux-x64` | `A4DCB00CBB84ADD3B77C87C0F7F673571FA0D694F5C014FF31546C2E4F11B869` |

四包内 `config\gatewaysettings.json` 的 SHA-256 均为 `D9377DA6B8F6C85229921C951A41DD67190961CB34FBFE08421D1C74D7574C96`，与仓库默认配置一致；四包对 7 个禁止 Legacy 依赖名的精确扫描命中均为 0。

## 4. 验证中发现并关闭的问题

1. 通信文档仅保留 `OfflineSimulation` 英文枚举，导致证据脚本缺少“离线仿真”中文语义；已在不改配置的前提下补齐双语边界。
2. Headless 全量工艺输出断言向 `HashSet.Contains` 传入可空的 Automation 名称，触发 nullable 编译错误；已先匹配非空名称再校验集合。
3. 旧 `KeyPress` API 在 Avalonia 11.3.9 已过时且按错误处理；已改用 `KeyPressQwerty(PhysicalKey, ...)`。
4. 项点首轮可见性在页面级启动命令初始化前触发事件，造成 VM 空引用；已调整构造顺序并由 VM 测试复核。
5. Legacy 硬件页实际有“预留”和“备用”两个独立 AO 槽位；已从 5 项修正为 6 项并增加名称、数量、不可执行断言。
6. RID 自包含发布必须允许 `dotnet publish` 按目标 RID 完成恢复；本轮按该前置条件从实现提交成功生成四包，并分别归档 publish 日志。
7. 权限分配原先按显示名称关联，会合并 Legacy 允许的同名权限；已改为保存稳定会话 Id、名称仅作显示，删除只级联对应 Id，并新增同名权限负向测试。
8. Headless 的 Test00、双列表方向和工艺输出证据原先口径偏弱；已改为查找真实按钮、校验左已配置/右候选与箭头，并用固定 17 项 Legacy 输出清单断言。

## 5. 远端恢复点

本轮起点为远端 `main` 的 `acf604f54a998a4e35d0e64476cb6350d7978a44`。中文实现提交 `92cdec56a3a93132fd5258446729fafedd9bc631` 已推送到 `origin/main`；独立 clone HEAD 与该提交一致，并复现证据、Release、Core/Gateway/Avalonia/Headless 和 Host `--once` 全部通过。

## 6. 证据边界

本轮 UI 测试执行真实 ViewModel 行为和独立 Headless XAML/输入事件，不是截图或文件存在性测试。会话内管理和注入式报表数据只用于验证状态、筛选与分页，不证明 SQLite/权限/报表文件/打印/上传已迁移。信捷探针只发送功能码 01、03、04 的读取请求；没有发送任何写功能码，也没有修改 `gatewaysettings.json`。它不证明 PLC 系列、UnitId、HD1074 浮点字序、M400 联锁语义、CH00 映射、UOS、OPC UA、连续运行或任何写入/回读已验收。

## 7. 最终命令顺序

```powershell
$repo = 'D:\易峥\2026\2026-09\Avalonia_上位机通用模板\项目代码\XXX.TestBench.Uos'
Set-Location $repo
pwsh -NoProfile -ExecutionPolicy Bypass -File .\tools\Test-MigrationEvidence.ps1
dotnet build .\XXX.TestBench.Uos.sln --no-restore --configuration Release --verbosity minimal
dotnet run --project .\tests\XXX.TestBench.Core.Tests\XXX.TestBench.Core.Tests.csproj --no-build --configuration Release
dotnet run --project .\tests\XXX.TestBench.Gateway.ContractChecks\XXX.TestBench.Gateway.ContractChecks.csproj --no-build --configuration Release -- .\config\gatewaysettings.json
dotnet run --project .\tests\XXX.TestBench.Gateway.Runtime.Tests\XXX.TestBench.Gateway.Runtime.Tests.csproj --no-build --configuration Release
dotnet run --project .\tests\XXX.TestBench.Avalonia.Tests\XXX.TestBench.Avalonia.Tests.csproj --no-build --configuration Release
dotnet run --project .\tests\XXX.TestBench.Avalonia.Headless.Tests\XXX.TestBench.Avalonia.Headless.Tests.csproj --no-build --configuration Release
dotnet run --project .\src\XXX.TestBench.Gateway.Host\XXX.TestBench.Gateway.Host.csproj --no-build --configuration Release -- --config .\config\gatewaysettings.json --once
```

## 8. 信捷现场仅读探针结果

执行时间：2026-08-29 11:58（北京时间）。TCP 握手成功；UnitId=1 候选下读取 M400、HD1074 候选、D0 和输入寄存器 0 均返回合法帧。完整原始结果见 `D:\Codex相关\phase-e-field\2026-08-29\modbus-readonly-probe.txt`。CSV 没有 `CH00/WSD` 行，当前 Runtime 仍将 AI00/DI00 固定为 S7 读取，因此该结果不能关闭 P2 业务映射门。
