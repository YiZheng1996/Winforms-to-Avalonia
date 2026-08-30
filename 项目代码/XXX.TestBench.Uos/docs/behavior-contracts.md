# XXX 试验台迁移行为合同与重写索引

版本：Phase E 基线 V2（2026-08-30）
依据：`统信UOS免费通信网关与Avalonia迁移正式执行计划_V2.0精简版.md`  
旧代码根：`XXX试验台模板/src/master/MainUI`  
当前结论：P2 五点只读离线切片和 8 个任务页/51 个 Designer 唯一处置已完成自动化验证；G0/P0、外部认证/持久化/报表效果、完整设备业务和 P3 写入仍未通过，不得据此宣称现场兼容。

## 1. 使用规则

本文件记录“用户能做什么、什么时候允许做、外部会发生什么以及失败后如何恢复”。Designer、resx、旧事件处理器和 OPF 只是证据，不是新系统的架构。完整 UI 处置见 `legacy-ui-coverage.csv`：51 个 Designer 各有唯一目标、状态、测试证据、安全处置和剩余缺口。每个合同在 P2/P3 实现时必须补充单元、集成、Headless 或真机证据。

状态值必须语义化：`Unknown` 表示还没有首个有效样本，`Bad/Uncertain` 表示通信或设备状态不可用，不能用 `0` 或 `false` 冒充有效数据。

## 2. 重写索引与模块边界

| 模块 | 目标归属 | 旧代码证据 | 重写边界 |
|---|---|---|---|
| UI Shell | `XXX.TestBench.Avalonia` | `Program.cs`、`frmMainMenu.cs`、`ucHMI.cs`、各 `frm*.cs`/`*.Designer.cs`/`*.resx` | 只负责 View、ViewModel、导航、状态呈现和命令入口；不直接访问 PLC、串口或 Windows COM |
| Application Core | `XXX.TestBench.Core` | `Model/`、`Procedure/Test/`、`BaseTest.cs`、`GeneralBaseTest.cs`、`Service/TestFactory.cs` | 负责试验状态、前置条件、取消、上传值和业务状态机；不得引用 WinForms、Avalonia、COM、数据库或具体驱动 |
| Gateway Boundary | `XXX.TestBench.Gateway` | `CurrencyHelper/OPCHelper.cs`、`Modules/*.cs`、OPF、`HMIVariable.cs` | 当前只负责只读点表、驱动、轮询、质量、时间戳和连接代次；UA 地址空间与受控写入仅是未来 P3 目标，当前无写 API，DataVariable 保持只读 |
| Platform / External Boundary | `XXX.TestBench.Avalonia` 或 Gateway Host | `Config/`、`Upload/`、`ProcessHelper.cs`、日志、串口/进程/文件访问 | 以接口隔离 UOS 文件、systemd、串口、HTTP、证书、日志和进程生命周期；失败返回诊断，不弹 UI 对话框 |
| Persistence | `XXX.TestBench.Avalonia` | `BLL/`、`Model/*Model.cs`、`DB/TestBed.db` | 负责 SQLite schema、备份、事务、恢复、查询和上传状态；迁移前保留旧库只读基线 |
| UI Assets / Distribution | Avalonia 与发布工程 | `img/`、`Resources/`、`Lib/`、`*.csproj`、`*.sln`、报表模板 | 资源逐项验权和替换；旧 DLL 仅作参考，不复制到 UOS 发布包 |

完整文件主归属、SHA-256 和处理结论见同目录 `legacy-coverage.csv`；窗体/控件级能力见 `legacy-ui-coverage.csv`；证据 ID 到真实测试文件和唯一定位字符串的闭环见 `ui-test-evidence.csv`。三份 CSV 均由证据脚本复核，每个生产文件只能出现一行主归属，每个 Legacy Designer 也只能出现一个 UI 处置，每个被引用的 UI 证据 ID 必须有且仅有一个可定位注册项。

## 3. 应用状态

下表是完整产品的目标状态合同。当前可执行范围只包含启动、仿真登录、只读观测、产品上下文和故障诊断；`Manual`、`AutomaticRunning` 以及任何设备输出仍受 Test00/质量/写边界阻断，不能把目标合同误读为当前已开放能力。

| 语义状态 | 进入条件 | 允许动作 | 离开条件 |
|---|---|---|---|
| `Starting` | 进程启动、配置和数据库初始化 | 只读初始化、诊断 | 登录成功进入 `Ready`；初始化失败进入 `Faulted` |
| `Unauthenticated` | 尚未完成登录或退出登录 | 登录、退出 | 认证成功进入 `Ready` |
| `Ready` | 已登录、Gateway Session 有效、关键点首次读取完成且质量 Good | 选型号、只读概览、按权限打开页面、启动自动试验 | 启动、断线、急停、退出 |
| `Manual`（目标状态，当前不可进入） | 手动模式且权限和点质量满足 | 受控手动输出、短按/释放动作 | 切换自动、断线、急停、退出 |
| `AutomaticRunning`（目标状态，当前不可进入） | 自动模式、DI00 为有效 Good/true、型号和选项合法 | 依序执行已勾选试验项，显示进度，可取消 | 全部完成进入 `Completed`；取消/故障/DI00 false 进入 `Stopping` |
| `Stopping` | 用户停止、取消、急停或通信故障 | 取消未完成任务，执行批准的安全收尾，不发送缓存命令 | 收尾完成进入 `Ready` 或 `Faulted` |
| `Completed` | 所选项正常完成并完成记录/上传策略 | 查看结果、保存报表、回退 Ready | 新试验或退出 |
| `Faulted` | 配置、数据库、Gateway、写入、回读或报表失败 | 查看诊断、恢复连接、按权限重试单次操作 | 恢复条件全部满足后回 Ready |

关键点质量恢复 Good 前不得进入 `Manual` 或 `AutomaticRunning`。断线时禁止创建离线写队列；重连后不重放旧命令，先全量读取并递增连接代次。

## 4. 命令合同

### AppStartup

- 入口：进程启动。
- 前置：可访问应用数据目录，配置可解析，SQLite 可连接。
- 流程：初始化日志和数据库 → 确保单实例 → 初始化上传资源 → 显示登录。
- 成功：进入 `Unauthenticated`，不连接 PLC、不写 Zero/Gain。
- 失败：记录诊断并退出，不能吞掉数据库或配置错误。
- 旧证据：`Program.cs:12-41,49-67,74-109`。

### Login

- 入口：登录按钮或回车。
- 前置：账号和密码非空；下拉用户从数据库重新读取；手工输入仅保留明确允许的账号策略。
- 流程：校验输入 → 读取用户 → 校验密码 → 建立当前操作员和角色。
- 成功：进入 `Ready`，权限由当前身份重新计算。
- 失败：保留登录页，清楚区分输入错误、用户不存在、密码错误和数据库错误；日志不记录密码。
- 旧证据：`frmLogin.cs:36-61,63-117,119-225`。旧代码当前为明文密码比较，迁移需列为安全整改，不得扩大兼容范围。

### ConnectGateway

- 入口：Gateway Host 启动或 Session 恢复。
- 前置：设备配置通过校验，证书和权限有效。
- 流程：按配置选择 S7-200/S7-1200 TCP 档案与 Modbus RTU/TCP 活动链路 → 注册 5 点只读切片 → 采样并发布质量、时间戳和连接代次。信捷 `HD1074/M400` 与 `CH00` 的 P2 业务映射未闭合前，不得据探针结果改写该流程。
- 成功：发布 `Gateway.Health.*` 和设备点；关键点首次 Good 后 HMI 才解除控制禁用。
- 失败/恢复：点质量立即变为非 Good，保留诊断；恢复后连接代次递增，全量读取，丢弃旧代次事件。
- 旧证据：`CurrencyHelper/OPCHelper.cs:29-95`、`Modules/*.cs`；旧 OPC DA/COM 只作对照和回滚。

### SelectProduct

- 入口：产品/型号选择按钮。
- 前置：已登录且有权限；数据库查询可用。
- 成功：更新型号/车型和试验参数；本次试验产品编号、车号和备注保持独立输入。
- 失败：不覆盖当前有效选择，显示诊断。
- 旧证据：`ucHMI.cs:971-984`、`frmMainMenu.cs:220-365`。

### StartAutomaticTest

> 本节是完整产品的目标合同。当前 P2 未采集 `Test00`，也没有设备动作/写通道，页面和命令自身都必须拒绝执行；不得把 Core 状态合同或 21 个项点清单解释成自动试验已经可用。

- 入口：开始试验。
- 前置：`DI00` 必须为有效 Good/true；已选择型号；`Test00` 为自动模式；关键点质量 Good；已勾选项点存在且有实现；当前无运行任务。
- 流程：锁定不可变更的试验上下文 → 创建一次上传会话 → 进入 `AutomaticRunning` → 顺序执行项点 → 每项最多追加一条结果 → 完成后生成记录/报表。
- 成功：所有选中项正常完成，记录完整性成立，进入 `Completed`。
- 失败/取消：取消令牌传递到项点；停止未完成项，不重放输出；按策略记录失败/取消，不把取消伪装成成功。
- 旧证据：`ucHMI.cs:404-565`、`ucHMI.cs:723-755`。

### StopAutomaticTest

> 本节与自动试验目标配套。当前没有可启动的自动设备流程，也不得为了演示实现复位、收尾写入或离线重放。

- 入口：停止按钮、取消、DI00 运行中变为 false、Gateway 故障。
- 流程：进入 `Stopping` → 取消所有项点 → 停止计时 → 执行已批准的安全收尾 → 解除 UI 锁定。
- 必须：不把未完成试验标记为完整，不上传旧的离线命令。
- 旧证据：`ucHMI.cs:700-760`。安全收尾写点须在 P1 写安全矩阵签字后才能实现。

### ManualOutput

> 本节至 `CalibrationReadAndApply` 描述未来 P3 目标合同。当前 Gateway 没有写 API，UI 命令自身固定 `CanExecute=false`，以下流程在本版本不可执行。

- 入口：手动面板按钮、按下/释放、数值输出对话框。
- 前置：手动模式、用户权限、Session/连接代次匹配、目标质量 Good、业务联锁满足、无自动试验。
- 流程：创建带 CommandId 的受控写请求 → Gateway 校验 → 驱动写入 → PLC/物理回读 → 返回状态和审计编号。
- 失败：无权限、非 Good、代次变化、断线、超时或回读不符立即失败；不自动重试，不缓存。
- 旧证据：`ucHMI.cs:769-815,986-1015`、`GeneralBaseTest.cs:193-376`。

### FaultResetPulse

- 入口：故障复位按钮。
- 前置：用户权限、手动模式、目标 Session 和质量 Good。
- 流程：写 true → 等待 1000 ms → 写 false；每一步都必须检查结果和回读。
- 成功：发布复位完成。
- 失败：尽最大安全能力将输出置回安全态并报告；不得默认为成功或遗留持续置位。
- 旧证据：`ucHMI.cs:993-1003`。旧实现缺少逐步结果、权限、代次和回读检查，新合同覆盖旧缺口。

### CalibrationReadAndApply

- 打开页面：只读当前 Zero/Gain 与质量，不写入。
- 应用前置：管理员明确确认、差异已展示、目标点 Good、无试验运行、连接代次匹配。
- 流程：逐项创建受控校准写请求 → 写入 → 回读 → 持久化配置/审计；任一步失败停止后续项。
- 禁止：启动应用、打开硬件页面、普通刷新或仿真状态下自动写入。
- 旧证据：`frmMainMenu.cs:149-160`、`frmHardWare.cs:18-23,64-97,126-156`、`Procedure/UCCalibration.cs:247-264,426-440`。当前旧路径启动时会 `InitData()` 后 `Submit()`，形成自动写入，登记为 `A02-SAFE-01` 阻断。

### SaveQueryReport

- 查询：按记录、型号、操作员和时间范围组合过滤；时间边界和空条件必须固定并测试。
- 保存：数据库记录、报表文件和上传任务状态分开处理；文件不存在、模板不支持或上传失败不得损坏记录。
- 报表：纯托管实现；禁止 Office Interop、旧 `Report.dll`、`office.dll` 和 Excel COM 进入 UOS 发布包。
- 旧证据：`BLL/TestRecordNewBLL.cs`、`frmDataManager.cs`、`Service/ReportService.cs`、`Upload/UploadService.cs`。

### LogoutAndExit

- 退出试验中禁止直接退出；先停止/收尾并取得确认。
- 正常退出：关闭 HMI → 释放 UA Client/数据库/HTTP 资源 → Gateway 作为独立 systemd 服务继续运行或按运维命令停止。
- 旧证据：`frmMainMenu.cs:428-448`、`Program.cs:38-41`。UOS 版本不得调用 `Application.Exit`、Win32 单例或 Windows 消息 API。

## 5. 写入与恢复总规则

所有写请求至少带 `CommandId`、NodeId、期望连接代次、写入值、当前操作员、操作原因、超时和业务前置状态。Gateway 以 UA 证书身份为准，不信任客户端自报身份。

P2 仅发布只读 DataVariable，不发布写方法；当前实现中不存在 `Gateway.Commands.WriteTag`。只有 P3 安全矩阵签字并另行实现、复核后才可讨论受控写命令，DataVariable 仍须保持只读。目标驱动写入失败、回读不符、质量非 Good、连接代次变化、超时和权限不足都必须是可区分的失败结果。

## 6. 场景/夹具清单

| 场景 ID | 类型 | 核验内容 | 证据 |
|---|---|---|---|
| P2-S01 | 正常 | 5 点首次采样、值类型、时间戳、Good 质量 | Gateway 集成日志 + HMI Headless 断言 |
| P2-S02 | 首样本 | 默认状态为 Unknown/Bad，不出现有效 0/false | Unit + Headless |
| P2-S03 | 断线恢复 | 质量降级、连接代次递增、旧事件丢弃、全量重读 | Integration + 真机日志 |
| P2-S04 | 仿真 | Simulated 明确显示，不能伪装真实设备 | Unit + UOS 运行记录 |
| P3-S01 | 安全拒绝 | 未授权、质量非 Good、代次不符、断线、回读不符 | Gateway integration |
| P3-S02 | 复位 | true→1000 ms→false，任何失败有诊断 | 驱动模拟器 + 真机回读 |
| P3-S03 | 自动试验 | DI00 false/invalid、Test00 手动、取消、项点缺失 | Core + Headless + 真机 |
| P3-S04 | 标定 | 读取差异、管理员确认、逐项写入和回读；打开页面零写调用 | Unit + Gateway audit |
| P3-S05 | 数据恢复 | SQLite 事务、异常退出、备份恢复和完整性检查 | Integration |
| P3-S06 | 报表 | 关键字段、单位、数值与旧系统一致；无 Office COM | Integration + rendered report |

## 7. 决策、风险与应用顺序

| ID | 决策/风险 | 当前状态 | 关闭条件 |
|---|---|---|---|
| A02-SAFE-01 | 旧启动路径可能自动写 Zero/Gain | 阻断 | 新合同测试证明启动/打开硬件页写调用为零；管理员确认后才允许写 |
| A02-POINT-01 | 旧订阅 102、源能力 124、OPF-only reserve 16 与当前五点切片未完全闭合 | 阻断 | 逐点补地址、类型、字节序、设备型号、业务映射和现场证据；历史计划口径不得冒充实测计数 |
| P0-UOS-01 | 当前工作站无目标 KX-7000/UOS、S7、USB-RS485 真机证据；信捷仅有候选 UnitId 的仅读协议响应 | 未验证 | 目标机执行 P0，生成启动、断线、冷启动、串口、应用点值和许可证记录 |
| A01-VENDOR-01 | OPC DA/COM、Office、x86 私有 DLL、DSL 运行时存在 | 已确认风险 | 依赖替代及发布目录扫描通过；旧依赖只保留在回滚环境 |
| A01-DYNAMIC-01 | `TestClassFileManager` 会生成/移动/删除 C# 测试类 | 待处置 | 选定安全的离线配置/受控程序集策略；禁止 UOS 运行时任意编译或动态执行 |

应用顺序：Core 样本和状态合同 → Gateway 5 点只读 → 8 页 HMI Shell 与专项覆盖矩阵（当前已完成）→ 真实认证/持久化/报表外部效果 → 一条完整设备业务切片 → 安全矩阵签字后才讨论 P3 写入。页面已覆盖不等于这些外部边界已验收。

## 8. 本轮通信边界变更（2026-08-28）

用户已要求继续实施，并将通信范围扩展为：

- S7-200 与 S7-1200 均通过 Ethernet TCP 接入，默认端口 `102`，由 S7NetPlus 适配器承载；CPU 型号、IP、Rack、Slot 和超时均在 `config/gatewaysettings.json` 中配置。
- 当前提供两个 S7 配置档案，但由于用户提供的两个型号使用同一 IP `192.168.0.111`，默认只启用 S7-200；启用两个相同终点会被配置校验拒绝。若现场确有两台 PLC，必须补充不同 IP 或端口。
- Modbus RTU 保留 `COM1 / 9600 / 8N1 / SlaveId 1` 默认值；新增 Modbus TCP，默认 `127.0.0.1:502 / UnitId 1`。两种传输均通过 NModbus 只读适配器，地址、功能区、站号和超时可配置。
- 当前网关配置固定为 `ReadOnly`，适配器不提供写入方法；Zero/Gain 启动写入保护固定开启。任何 P3 写入仍须经过权限、质量、连接代次、联锁、超时和回读门禁。
- 首条完整业务切片选定为“压力调整阀 B11”，后续从旧代码 `Procedure/Test/压力调整阀` 与 `DB/reports/B11压力调整阀.xls` 逐项提取流程、点位、判定、记录和报表字段。

## 9. Phase B Core 实现状态（2026-08-29）

`XXX.TestBench.Core` 已实现 UI 无关的状态机和 B11 纯业务判定。核心合同测试覆盖初始化、登录、产品选择、DI00 Unknown/false、Test00 手动模式、运行中安全停止、Gateway 断线、安全收尾失败和 B11 三次调整边界。实现细节见同目录 `core-contracts.md`。

## 10. Phase D UI 实现状态（2026-08-30）

`XXX.TestBench.Avalonia` 已按任务重组为总览、试验作业、工艺监视、参数管理、数据报表、硬件校准、日志诊断和绝缘耐压 8 页。VM/Headless 行为测试覆盖 21 个 Legacy 试验类、9 个管理模块的专项字段与交互、会话 CRUD/唯一性、权限勾选、项点有序双列表、型号会话发布、报表筛选/分页、12 个输入校准通道、6 个 AO 槽位、日志当天/七级/别名/倒序/500 条边界、仪器参数校验、真实 Ctrl+1～Ctrl+8、960×560/1600×900 布局和启动取消/故障。所有设备写入、动态 C# 文件操作、报表删除/重传与仪器命令仍由命令自身或不可变边界拒绝。

管理专项保持以下 Legacy 语义边界：用户只采集用户名与角色且不复制明文默认密码；角色采集名称/说明；权限采集名称/代码/控件名/备注，其中控件名允许为空、仅非空时参与唯一性校验，不新增隐藏且未使用的“权限类型”；权限分配以会话记录稳定 Id 关联、名称仅作显示，因此保留 Legacy 允许的同名权限；删除权限只移除该 Id 的会话分配且不会因重新选择旧分配记录而复活；权限分配使用角色选择和权限复选框；型号发布、项点配置和试验参数均只改变当前会话。项点配置恢复 Legacy “左侧已配置、右侧候选”方向，项点配置与试验参数的型号候选只列当前会话已发布型号。项点实体类不会触发 Legacy 动态源码创建/移动/删除，试验参数只覆盖车型/型号、报表模板文件名与保存目录，Legacy 空白“参数界面”页签不被虚构为新能力。真实认证、普通用户可见性过滤、SQLite/INI 与外部发布仍未接入。

日志专项默认当天，等级显示为“全部等级/跟踪/调试/信息/警告/错误/致命”，内部语义保持 `All/Trace/Debug/Info/Warn/Error/Fatal`；日期由查询按钮应用，等级切换即时过滤，“全部等级”不增加等级条件，Gateway `Information/Warning` 兼容到 `Info/Warn`，结果按时间倒序且当前会话最多 500 条。日期采用 `[当天 00:00, 次日 00:00)`，这是对 Legacy 截止 `23:59:59` 可能遗漏毫秒记录的有意修复；关键字筛选属于 Avalonia 增强，不冒充 Legacy 搜索按钮原有语义。
