# Phase E：Phase A 契约验收记录

日期：2026-08-29  
验收范围：当前已实现的 Core、Gateway 只读 Runtime、Avalonia P2 五点 UI 和离线发布物  
验收环境：Windows 10.0.22621 x64；.NET SDK 9.0.300；代码实际目标 `net8.0`；Avalonia 11.3.9  
现场限制：已具备一台信捷 PLC，现场终点为 `192.168.0.51:502`；本轮仅完成 UnitId=1 候选下的 Modbus TCP 仅读协议探针。PLC 具体系列、UnitId、点表语义/字节字序以及当前 Avalonia P2 的 `CH00` 映射尚未签字确认。仍无真实 UOS、USB-RS485 和现场 OPC UA。所有 `PASS-OFFLINE` 与 `PASS-FIELD-PROBE` 均不能替代完整现场验收。

状态定义：

- `PASS-OFFLINE`：本地代码、仿真、Headless 或发布物行为通过。
- `PASS-FIELD-PROBE`：真实终点返回合法仅读协议响应；不代表业务点映射、长期稳定性或现场兼容性验收。
- `PARTIAL`：只验证了合同子集，或与 Legacy 尚不等价。
- `PENDING-FIELD`：必须在真实 UOS/设备/触控环境执行。
- `BLOCKED`：切换前必须完成，当前不允许以仿真关闭。

## 1. 自动化验收

| ID | Phase A 场景 | 验收动作与预期 | Avalonia/Gateway 证据 | 结果 |
|---|---|---|---|---|
| P2-S01 | 五点首次采样 | 首样本后固定五点均有类型、值、质量、时间戳、连接代次；仿真健康 | Runtime 测试、Host `--once` JSON、VM 测试、Headless | `PASS-OFFLINE` |
| P2-S02 | 首样本默认状态 | 首样本前五点为 `Unknown`，值为 null，不出现有效 0/false | `XXX.TestBench.Gateway.Runtime.Tests` | `PASS-OFFLINE` |
| P2-S03 | 断线/恢复 | 断链点变 Bad/null；读异常主动断开；下一轮重连并递增代次 | Runtime 测试覆盖 S7/Modbus 读异常和恢复；仿真传输合同测试 | `PASS-OFFLINE` |
| P2-S04 | 仿真标识 | HMI/Host 明确显示 `OfflineSimulation`/`Simulated=true`，不得伪装设备 | VM、Headless、Host JSON | `PASS-OFFLINE` |
| A02-SAFE-01 | 启动/刷新不写 Zero/Gain | 启动、刷新和当前只读页面没有写 API/写调用；UI 显示写入禁用 | `WritesEnabled=false`、只读接口、配置 guard、发布扫描 | `PASS-OFFLINE`（写未实现） |
| A02-SAFE-02 | DI00 Unknown/Bad | 质量无效时不得启动自动试验，诊断为 `SafetySignalInvalid` | Core 测试；VM/Headless 保持控制禁用 | `PASS-OFFLINE` |
| A02-SAFE-03 | DI00 false | false 与通信无效区分；自动试验拒绝，运行中转 Stopping | Core 测试、VM 安全文本 | `PASS-OFFLINE` |
| A02-SAFE-04 | Test00 门禁 | Test00 手动拒绝自动；P2 未采 Test00 时 UI 不开放自动/手动入口 | Core 测试、VM、Headless | `PASS-OFFLINE`（控制子集） |
| CORE-01 | Core 生命周期 | Starting→Unauthenticated→Ready；登录、产品选择和非法状态有诊断 | `XXX.TestBench.Core.Tests`、`XXX.TestBench.Avalonia.Tests` | `PASS-OFFLINE` |
| CORE-02 | 启动失败 | 配置/数据库错误应有一致的 Faulted/退出策略且不吞错 | Core 有错误状态；App 组合层当前显示诊断 VM，退出策略未定 | `PARTIAL` |
| UI-01 | 产品输入/双向绑定 | 输入框双向绑定，选择动作才提交 Core，长度超限拒绝 | Headless 双向绑定、VM/Core 测试 | `PASS-OFFLINE`（非数据库型号选择） |
| B11-01 | B11 人工调整 | 高低压范围外要求人工调整，未到最大次数可重试 | Gateway ContractChecks 的 `NeedsOperatorAdjustment` | `PASS-OFFLINE`（纯判定） |
| B11-02 | B11 通过 | 两侧在范围内返回 Passed，生成 `val8`/`val16` 上传值 | Gateway ContractChecks | `PASS-OFFLINE`（纯判定） |
| B11-03 | B11 最大次数 | 第三次仍超限返回 Failed 并终止 | Gateway ContractChecks | `PASS-OFFLINE`（纯判定） |
| B11-04 | B11 完整流程 | 阀动作、5/10 秒时序、人工对话、取消、记录/报表与 Legacy 对照 | 尚未接入设备/流程/报表 | `BLOCKED` |
| RT-01 | Gateway 异常/部分成功 | 一条链路连接失败时相关点 Bad，另一条链路的 Good 结果保留，记录结构化错误 | Runtime 测试与 `RecordingLogSink` | `PASS-OFFLINE` |
| RT-02 | 取消/释放 | 取消连接/轮询后两条传输均释放；清理一条失败仍清理另一条并记警告 | Runtime 测试 | `PASS-OFFLINE` |
| RT-03 | 重叠轮询/旧快照 | PollOnce 不重叠；同一 Runtime 不允许旧轮询覆盖后续快照 | `_pollGate` 已实现；尚无迟到样本注入测试 | `PARTIAL` |
| PKG-01 | Windows 发布启动 | `win-x64` 自包含包启动，Host 用包内 config `--once` 返回 0 | `D:\Codex相关\phase-e\publish\...` | `PASS-OFFLINE` |
| PKG-02 | UOS 发布启动 | `linux-x64` 包在目标 UOS 启动，触控/字体/桌面依赖正常 | 当前无 UOS | `PENDING-FIELD` |

## 2. 手工/现场验收记录

| ID | 场景 | 现有证据 | 结果 | 必须补的证据 |
|---|---|---|---|---|
| M-01 | VS 中 Avalonia 主界面启动 | 用户在当前任务提供的 Windows VS 启动截图；画面显示 OfflineSimulation、Gateway 仿真、B11/P2 五点、DI00 true、只读/Zero/Gain 禁止 | `PASS-USER-EVIDENCE` | 将原图归档到正式验收目录并记录时间/版本 |
| M-02 | 发布版 Windows 启动 | `XXX.TestBench.Avalonia.exe` 启动后存活约 3 秒；测试进程随后主动结束 | `PASS-WINDOWS` | 正常关闭和长时间运行记录 |
| M-03 | UOS 冷启动/退出 | 未执行 | `PENDING-FIELD` | UOS 版本、架构、桌面会话、启动/退出码、包哈希 |
| M-04 | S7-200/S7-1200 读取 | 未执行 | `BLOCKED` | CPU 型号、IP、Rack/Slot、地址、类型、字节序、2 小时日志 |
| M-05 | Modbus RTU/TCP 读取 | 信捷 `192.168.0.51:502` TCP 握手成功；UnitId=1 候选下，功能码 01/M400、03/HD1074 候选、03/D0、04/0 均返回合法响应；原始帧见 `D:\Codex相关\phase-e-field\2026-08-29\modbus-readonly-probe.txt` | `PASS-FIELD-PROBE`（仅协议读通） | PLC 系列/UnitId 确认；CH00 的功能区/地址；HD1074 浮点字序；应用包实际 P2 快照、断线恢复和连续运行 |
| M-06 | 拔插恢复 | 未执行 | `BLOCKED` | 5 次以上拔插、质量降级、恢复耗时、连接代次 |
| M-07 | 1920×1080 触控/字体 | Windows Headless 只验证最小尺寸、滚动容器和 AutomationProperties；未做真实触控 | `PENDING-FIELD` | 目标屏截图、触控命中、字体/缩放和本地化记录 |
| M-08 | Legacy 同输入对照 | 只有旧源代码/OPF 证据；未在同一真实输入下并行运行 | `BLOCKED` | 分时对照值、质量、时间戳、地址和报表差异 |

## 3. 明确未验收项

本轮已证明信捷终点可以在 UnitId=1 候选下响应四个仅读请求，但不能由此推导：PLC 系列与 UnitId 已确认、`AI.MAI00`/`DI.MDI00`/`Modbus.WSD.CH00` 业务映射正确、HD1074 浮点字序正确、Avalonia 应用 P2 快照健康、UOS 兼容、systemd 非 root 生命周期、本机 OPC UA 证书/地址空间、连续 2 小时稳定性、现场拔插、真实登录/权限、电气联锁、写入/回读、数据库恢复和报表渲染。

现场探针严格使用功能码 01、03、04 的读取请求；未发送功能码 05、06、15、16、22、23，也未修改项目默认 `OfflineSimulation` 配置。
