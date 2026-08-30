# 平台、发布与验收记录

版本：Phase E V2（2026-08-30）
状态：`P2_OFFLINE_VERIFIED + UI_COVERAGE_VERIFIED`；已有信捷 Modbus TCP 仅读协议探针，但 P0/G0、业务映射、现场 UOS、OPC UA、连续运行和 P3 仍未执行/通过。

## 1. 目标和固定边界

目标平台为统信 UOS 桌面专业版 V20 AMD64（1070），目标硬件为兆芯 KX-7000、16 GB、1 TB SSD、1920×1080 触控屏。生产进程固定为：

`S7-200/S7-1200 TCP + Modbus RTU/TCP → DeviceGateway(systemd、非 root) → 本机 OPC UA → Avalonia HMI`

HMI 不直接访问 PLC、串口、OPC DA、Windows COM 或私有驱动。S7-1200 与 Modbus TCP 已纳入本轮可配置通信范围；S7-1500、第三方局域网 UA、云端和移动端不进入首发。

## 2. 当前源码基线（本轮复核）

| 项目 | 结果 | 证据/说明 |
|---|---:|---|
| MainUI 生产文件（排除 bin/obj） | 355 | `XXX试验台模板/src/master/MainUI` |
| C# 生产文件 | 180 | 另有 4 个 `obj` 生成 C#，不计入产品源代码 |
| Designer C# | 51 | 文件名匹配 `*.Designer.cs`，大小写差异已纳入复核 |
| resx | 43 | 与旧项目资源初始化强耦合 |
| 旧目标框架 | `net8.0-windows` | `MainUI.csproj:3` |
| UI/平台 | WinForms、SunnyUI、AntdUI | `MainUI.csproj:7,132-150` |
| CPU | x86 | `MainUI.csproj:25` |
| 旧 OPC | OPC DA/COM，ProgID `KEPware.KEPServerEx.V4` | `CurrencyHelper/OPCHelper.cs:29-45` |
| 旧报表/运行时 | Office Interop、`Report.dll`、`office.dll`、Spire、DSL DLL | `MainUI.csproj:160-185` 与 `src/master/Lib` |
| 旧数据库/报表模板 | `DB/TestBed.db`、`DB/reports/report.xlsx` | 文件存在；数据库 schema 待用兼容工具只读导出 |
| 新生产代码目录 | 已有 `XXX.TestBench.Uos.sln`、Core、Gateway、Host、Avalonia 与测试项目 | 当前为 `net8.0`；Avalonia 11.3.9；实际 Phase E 结果见同目录 `phase-e-*.md` |

旧项目 Windows 构建只能作为遗留基线，不能作为 Avalonia/UOS 兼容证据。

## 3. P0 目标机与设备 POC 工单 S0

当前现场状态：没有真实 UOS 工控机、S7 或 USB-RS485 证据；已有一台信捷设备在 `192.168.0.51:502` 对 UnitId=1 候选的四个仅读请求返回合法 Modbus TCP 响应。该探针尚未确认 PLC 系列、UnitId、HD1074 字序、M400 语义、CH00 或 P2 应用映射，因此只标记 `PASS-FIELD-PROBE`，不能关闭 P0/G0。离线仿真继续用于配置、质量、连接代次、断线恢复、UI 和 B11 纯逻辑测试。

| 检查项 | 本地状态 | 现场执行命令/证据 |
|---|---|---|
| UOS 版本、架构、CPU、触控分辨率 | 等待真机 | `cat /etc/os-release; uname -m; lscpu; xrandr --current`；保存原始输出 |
| .NET 10 与 Avalonia 最小窗口 | 等待真机 | `dotnet --info`、self-contained `linux-x64` 启动日志和 SHA-256 |
| 非 root systemd | 等待真机 | `systemctl status xxx-testbench-gateway`；记录用户、权限、退出码和重启策略 |
| NTP/RTC、断电冷启动 | 未执行 | 冷启动记录；采样时间与系统时钟偏差 |
| S7-200/S7-1200 AI00、DI00 只读 | 等待真机 | 设备型号、IP/机架信息、地址、类型、字节序、至少 2 小时日志 |
| Modbus RTU/TCP WSD CH00 只读 | TCP 候选终点仅读协议已响应；CH00 未映射 | USB-RS485 芯片、串口固定名、TCP 终点、波特率/校验/站号/功能码、CH00 设备档案、报文和应用读值 |
| 拔插恢复 | 等待真机 | udev 规则、拔插时间、质量降级、恢复时间、连接代次变化 |
| 与旧 KEP 分时对照 | 未执行 | 同一输入样本的值、类型、时间戳和地址差异报告 |
| 许可证和原生依赖 | 未执行 | 发布目录清单、SBOM、许可证扫描、禁止项扫描 |

P0 现场前，所有设备地址、类型、字节序、轮询周期和写权限均按未知处理。Windows 仿真不能替代 G0。

## 4. P1 当前产物与工单

| 工单 | 本轮结果 | 证据 |
|---|---|---|
| S0 工具链/备份/目标机 | 部分启动；已有信捷仅读协议探针，目标 UOS、S7 和 USB-RS485 证据未具备 | 本文件第 3 节；旧源文件 SHA-256 见 `legacy-coverage.csv` |
| A01 文件与功能覆盖 | 文件矩阵与 51 行 UI 专项矩阵已校验 | `legacy-coverage.csv`、`legacy-ui-coverage.csv`、`ui-test-evidence.csv`；证据脚本检查 Designer 无孤儿/重复、UI 归属及每个证据 ID 的真实文件/唯一 Marker |
| A02 行为、点表、写安全 | 已生成初版，存在两个阻断 | `behavior-contracts.md`、`tag-and-write-matrix.csv`；`A02-SAFE-01`、`A02-POINT-01` |

G1 通过条件：所有生产文件唯一归属；DI00、Test00、复位、标定、断线无未关闭歧义；全部生产写点有权限、质量、代次、前置、失败动作和回读；首发设备/排除设备签字完成。

## 5. 平台替代决策

| 旧能力 | 目标处理 | 发布限制 | 验收 |
|---|---|---|---|
| OPC DA/`Interop.OPCAutomation.dll` | Gateway 内采用真实 OPC UA/驱动适配；旧 DA 仅对照和回滚 | 不进入 UOS 包 | UA 地址空间、质量、时间戳、断线/重连、证书 |
| `KEPware.KEPServerEx.V4` | 不在 UOS 生产端使用 | 禁止硬编码 ProgID | Gateway 配置和日志证明无 DA 依赖 |
| S7-200/S7-1200 | P0 只读驱动 POC；按配置选择一个活动 CPU 档案 | 同一 IP/端口不得同时启用两个物理设备 | 真机地址/类型/字节序和连续读取 |
| Modbus RTU/TCP | P0 只读串口/TCP POC；RTU 使用 udev 固定名 | TCP 默认 `127.0.0.1:502`，所有参数可配置 | 报文、设备档案、拔插/断线恢复 |
| WinForms/SunnyUI/AntdUI | Avalonia View/ViewModel 重建 | 不把 WinForms 类型带入 Core/Gateway | 构建依赖扫描、Headless 行为 |
| Office Interop/旧报表 DLL | 选择纯托管报表方案，保留模板字段映射 | Office COM 不得发布 | 关键字段/单位/数值和渲染验证 |
| `System.Management`/Win32 API | UOS 平台能力接口；只在 Host 适配 | Core 不引用 | 非 Windows 构建与运行 |
| `rw3.dll`/`rwdsl2.dll`/动态 C# | 先盘点 DSL 实际使用；采用受控迁移方案 | 禁止未经审计的 UOS 动态执行 | P1 决策记录、依赖/许可证扫描 |
| SQLite/FreeSql | 纯托管 SQLite 适配，事务、备份、恢复和完整性检查 | 不覆盖旧库；迁移可逆 | schema、异常退出、备份恢复 |

## 6. G0/G1 后才可启动 P2

### G0（P0）

- Gateway 和 Avalonia 在目标 UOS 可启动；
- S7-200/S7-1200 与 Modbus RTU/TCP 只读链路无地址、类型和字节序错误；
- 断线后质量为非 Good，恢复后连接代次递增；
- 未通过设备不进入首发；
- 发布目录通过许可证和禁止依赖扫描。

### G1（P1）

- `legacy-coverage.csv` 无孤儿文件，除生成物/供应商/旧回滚文件外均有明确处置；
- `behavior-contracts.md` 的安全和自动试验主链无未关闭歧义；
- `tag-and-write-matrix.csv` 每个生产写候选有前置、权限、质量、代次、回读、超时和失败动作；
- 102、122、124、16 的点数关系已逐点复核并由 PLC/电气签字；
- G1/现场安全门未关闭前，Avalonia 可以迁移纯界面状态、只读显示和外部依赖明确阻断的交互，但不得开放设备写入或把页面存在性当成产品验收。

## 7. P2 只读切片验收门 G2

固定 5 点：`Gateway.Health.NoError`、`Gateway.Health.Simulated`、`SMART.PLC.AI.MAI00`、`SMART.PLC.DI.MDI00`、`Modbus.WSD.CH00`。

- 5 点首样本前均为 Unknown/Bad；
- DI00 false 与 DI00 无效在 HMI 上明确区分；
- UA 地址空间没有可写 DataVariable 和写方法；
- 仿真、断线、重连、旧代次丢弃均有自动化证据；
- 1920×1080 触控布局无阻塞；
- 真实设备连续运行不少于 2 小时，完成不少于 5 次断线/拔插恢复。

当前可先完成前四项的离线仿真证据；已有信捷探针只证明候选读取协议响应，最后一项和业务映射仍必须等设备档案与现场环境完备后执行。

## 8. P3 写安全与完整业务门 G3

未通过安全矩阵签字前，所有写能力保持禁用。G3 至少证明：未授权、非 Good、代次不符、断线、超时、回读不符均立即拒绝；复位脉冲完整；标定确认前零写调用；数据库事务/恢复和报表关键字段通过；旧系统回滚 RTO 不超过 30 分钟。

## 9. 证据归档和回滚

证据必须放在项目约定的归档目录或现场介质中，不把运行日志、下载包和渲染预览写入源目录。至少归档旧源/OPF/DB/报表/配置哈希、四类主工件、P0 设备记录、UA/写回读日志、测试结果、SQLite 备份恢复、差异报告、72 小时稳定性和切换回滚演练。

旧 Windows/KEP 系统在切换后 30 天观察结束前保持可恢复且关闭写权限。任何异常输出、命令重放、关键数据损坏或联锁异常，立即撤销新系统写权限，隔离新 Gateway，恢复旧系统并保留现场日志。

## 10. Phase C 平台服务实现（2026-08-29）

本阶段已实现只读运行时和 Host 组合入口：

- `src/XXX.TestBench.Gateway/Infrastructure/ReadOnlyGatewayRuntime.cs`：负责 S7/Modbus 连接生命周期、轮询、质量降级、连接代次、5 点快照和取消/清理；所有通信依赖通过 `IReadOnlyS7Transport`、`IReadOnlyModbusTransport` 注入。
- `src/XXX.TestBench.Gateway.Host`：命令行组合根和结构化 JSON 日志输出；支持 `--config`、`--once`、`--iterations`，不包含 Avalonia XAML。
- `tests/XXX.TestBench.Gateway.Runtime.Tests`：覆盖离线成功、连接失败后的部分结果、取消、部分清理、结构化日志和设置上一版保留。
- `gateway.transportMode=OfflineSimulation` 是当前安全默认值；切换到 `ConfiguredDevices` 是现场动作，不能由离线测试代替。

本阶段验证结果：Gateway 0 警告/0 错误；Runtime 合同测试通过；Host 使用当前配置执行一次离线采样通过。信捷 Modbus TCP 仅完成候选 UnitId 仅读协议探针；仍未关闭 P0/G0 的目标 UOS、S7、RS-485、Modbus 业务映射、systemd、OPC UA 和连续运行验收。

## 11. Phase D Avalonia UI 实现（2026-08-30）

已从最小 P2 Shell 扩展为 8 个任务页面，并建立完整 Designer 覆盖证据：

- `docs/legacy-ui-coverage.csv`：51 行，逐一映射 Legacy Designer、resx、入口、真实能力、Avalonia 目标、实现状态、安全处置、行为/Headless 证据和外部缺口；`docs/ui-test-evidence.csv` 把覆盖矩阵中的每个证据 ID 绑定到真实测试/脚本及唯一 Marker；`Test-MigrationEvidence.ps1` 校验无孤儿、重复或悬空证据。
- `src/XXX.TestBench.Avalonia/Views`：运行总览、试验作业、工艺与手动、参数管理、数据查询与报表、硬件校准、日志与维护、绝缘耐压。
- `src/XXX.TestBench.Avalonia/ViewModels`：21 个试验类唯一映射；9 个管理入口按用户/角色/权限/权限分配/车型/型号/项点/项点配置/试验参数建立专项会话字段和交互；组合查询/翻页；12 个输入通道/6 个 AO 槽位；本地校准公式；日志当天、七级、别名、倒序与 500 条边界；仪器参数校验；启动取消/Faulted、导航与安全门。
- `tests/XXX.TestBench.Avalonia.Tests`：上述 ViewModel 行为和危险命令禁用。
- `tests/XXX.TestBench.Avalonia.Headless.Tests`：独立 Headless 进程真实发送 Ctrl+1～Ctrl+8 并通过 ListBox 到达页面，验证 XAML、双向绑定、命令、AutomationProperties、44 高度交互目标、窄/宽布局和写入锁定。

UI 覆盖不等于外部能力通过：参数管理只改变当前会话，真实登录/权限过滤、SQLite/INI、型号真实发布、项点动态 C# 文件副作用全部未接入；试验参数不虚构 Legacy 空白“参数界面”能力。日志关键字筛选是有意增强，历史 SQLite 日志仍未接入。报表默认不注入伪造记录，维护页不启用 Legacy 明确隐藏的三个功能，校准/手动/仪器设备命令全部固定禁用。Test00 尚未纳入 P2 五点，所以自动/手动入口继续按 Core 合同不可用。

## 12. Phase E 当前结论（2026-08-29）

Phase E 的正式产物为 `phase-e-parity-and-cutover.md`、`phase-e-acceptance.md`、`phase-e-test-results.md` 和 `phase-e-packaging-and-launch.md`。本轮已验证 Windows Release 构建、Core/Gateway/Avalonia/Headless 自动化、Gateway Host 离线采样，以及 `win-x64`/`linux-x64` 自包含发布物；读异常后的 Bad→断开→新连接代次恢复和连接代次 UI 绑定也已补测。

本轮关闭 P2 离线仿真和 Phase D UI 覆盖/交互证据，不关闭 G0/P0，也不把 `ImplementedSessionOnly`、`ImplementedSafetyLocked` 或 `ExternalBlocked` 计为完整产品 parity。当前 Avalonia 可作为只读/UI 迁移维护线，但旧 WinForms 仍保留为对照/回滚基线；真实认证、SQLite、报表、完整 B11/EP 设备流程、OPC UA Host、UOS systemd、现场业务映射和所有写入能力仍延期。
