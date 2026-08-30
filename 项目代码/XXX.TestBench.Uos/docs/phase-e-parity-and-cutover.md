# Phase E：Legacy vs Avalonia 差异与 Cutover 准备

日期：2026-08-30
状态：`P2_OFFLINE_VERIFIED + UI_COVERAGE_VERIFIED + PACKAGE_REVALIDATED + CUTOVER_NOT_READY`；完整产品切换未通过
仓库身份：<https://github.com/YiZheng1996/Winforms-to-Avalonia.git>
本地工作区：`D:\易峥\2026\2026-09\Avalonia_上位机通用模板`
实际迁移解：`项目代码\XXX.TestBench.Uos\XXX.TestBench.Uos.sln`
旧基线：`XXX试验台模板\src\master\MainUI`

## 1. 证据边界

当前工作区已初始化为 Git checkout，远端为 `origin`：<https://github.com/YiZheng1996/Winforms-to-Avalonia.git>。本轮开始时 `main` 与 `origin/main` 同步在 `acf604f54a998a4e35d0e64476cb6350d7978a44`；实现提交 `92cdec56a3a93132fd5258446729fafedd9bc631` 已推送，并由独立 clone 复现证据、Release、Core/Gateway/Avalonia/Headless 与 Host `--once`。本报告的产品结论以实际源码、51 行 UI 专项矩阵、旧 WinForms 源码、迁移文档、自动化输出、现场仅读 Modbus 探针和用户提供的 Windows VS 启动截图为证据；公开仓库按安全策略排除本地运行数据和部分生成/用户工件。

当前开发机证据：Windows 10.0.22621 x64，.NET SDK 9.0.300；迁移解实际目标为 `net8.0`，Avalonia 包为 11.3.9。当前已具备信捷 PLC `192.168.0.51:502`，但只完成 UnitId=1 候选下的仅读 Modbus TCP 协议探针；没有真实 UOS 工控机、USB-RS485 或现场 OPC UA 环境。PLC 系列、UnitId、点表业务语义和字节字序尚未完成确认；以下 `PASS-OFFLINE` 只表示代码/测试/仿真证据，现场探针单独标为 `PASS-FIELD-PROBE`。

固定安全边界：Gateway `ReadOnly`，只发布五点观测；不创建 Zero/Gain 自动写入、手动输出、复位写入、离线写队列或断线重放。任何真实 PLC/Modbus/UOS 兼容性、OPC UA 证书和 systemd 结论均保持未验收。

## 2. 结论

| 项目 | 结论 |
|---|---|
| 当前 P2 只读切片 | Avalonia 可作为该切片的维护线；启动、仿真采样、质量、DI00 展示、写禁用和安全门已有自动化证据 |
| Legacy UI 覆盖 | 51 个 Designer 单元均有唯一目标/状态/测试/安全处置；8 个任务页可达，纯 UI 行为通过 |
| 完整 Avalonia 产品 | 未完成；真实登录/持久化、完整 B11/EP 设备流程、报表外部效果、OPC UA Host、UOS 服务化和现场设备仍缺失 |
| Legacy 是否冻结 | 否；旧 WinForms 保留为对照/回滚基线 |
| Legacy 是否删除 | 否；存在 Blocker/High 缺口和未完成现场验收 |
| 可关闭的阶段门 | 仅关闭 P2 离线仿真证据；不关闭 G0/P0/G3 |

## 3. 模块映射

| 模块 | Legacy 证据 | Avalonia/目标位置 | 当前处置 |
|---|---|---|---|
| 启动/UI Shell | `MainUI/Program.cs`、51 个 Designer、43 个 resx、主菜单/各窗体和 UserControl | `src/XXX.TestBench.Avalonia` | 8 个任务页和专项矩阵已完成；外部依赖按状态阻断 |
| 应用核心 | `Model/`、`Procedure/Test/`、`BaseTest.cs`、`GeneralBaseTest.cs` | `src/XXX.TestBench.Core` | 已抽取状态机和 B11 纯判定；完整业务未完成 |
| 通信边界 | `CurrencyHelper/OPCHelper.cs`、`Modules/*.cs`、OPF | `src/XXX.TestBench.Gateway` | 已有 S7/Modbus 只读适配器和仿真 Runtime；尚未接本机 OPC UA |
| Gateway Host | 旧进程/OPC DA 生命周期 | `src/XXX.TestBench.Gateway.Host` | 已有离线 CLI；未形成 UOS systemd 服务 |
| 认证/持久化/报表 | `frmLogin.cs`、`BLL/`、`DB/TestBed.db`、Report/Office | 尚无等价生产实现 | 延期；不得以文本输入替代数据库认证/查询 |
| 资源/发布 | `Resources/`、`img/`、`Lib/`、旧 csproj | Avalonia 项目与 RID 发布目录 | P2 需要的最小资源可发布；旧 DLL 不进入包 |

旧文件覆盖工件 `legacy-coverage.csv` 仍为 393 行。修复 Procedure 根 UserControl 误归类后，模块唯一归属为：application-core 37、build-distribution 13、gateway-boundary 19、persistence 14、platform-boundary 30、ui-assets 122、ui-shell 127、vendor-platform 31。新增 `legacy-ui-coverage.csv` 为 51 行，对应 51 个 Designer；点表 `tag-and-write-matrix.csv` 为 140 行：旧订阅 102、源能力 124、OPF-only reserve 16。UI 覆盖计数不是外部功能或设备写入已验收的计数。

## 4. 差异矩阵

状态含义：`PASS-OFFLINE` 为代码/测试/仿真通过；`PARTIAL` 为当前切片可用但不等价；`DEFERRED` 为明确延期；`BLOCKED` 为切换前必须关闭或取得正式豁免。

| 功能/合同 | Avalonia 状态 | Legacy vs new 差异 | 影响 | 决策 | 证据 |
|---|---|---|---|---|---|
| 启动正常路径 | `PASS-OFFLINE` | 新程序由 Avalonia App 组合 Core + 只读 Runtime；不启动旧 OPC DA/COM | P2 正常启动可验证 | 保留 | `App.axaml.cs`、Headless、用户 VS 启动截图 |
| 启动配置/初始化失败 | `PASS-OFFLINE` | `AppComposition` 捕获异常；Runtime 不可用时 Core 进入 Faulted 并保留原始启动诊断 | 已选择“显示可审计 Faulted 窗口”策略 | 保留并等 UOS 启动失败烟测 | VM 行为测试、`AppComposition.cs` |
| Legacy UI Designer 覆盖 | `PASS-UI-COVERAGE` | 51 个 Designer 均有唯一 Avalonia 目标、状态、测试证据、安全处置和剩余缺口 | UI 清点无孤儿/重复；不表示外部效果通过 | 保留专项矩阵并随迁移更新 | `legacy-ui-coverage.csv`、证据脚本 |
| 8 页主导航/可达性 | `PASS-UI-COVERAGE` | Legacy 主菜单/设置窗体重组为任务页面；Ctrl+1～Ctrl+8 与 ListBox 共用状态路径 | 功能入口可达且非逐控件机械翻译 | 保留 | VM + Headless 依次到达 8 页 |
| 登录 | `PARTIAL` | 仅 OfflineSimulation 提供明确的演示登录；ConfiguredDevices 不伪造成功；旧系统有数据库用户/密码流程 | 不能替代操作员认证和角色权限 | `BLOCKED` | `MainWindowViewModel.cs`、Core 测试、旧 `frmLogin.cs` |
| 产品/型号选择 | `PARTIAL` | 新试验作业页保留车型、型号、产品编号、车号、备注并提交同一 Core 产品合同；旧系统从数据库选择型号并刷新参数 | 纯 UI 与 Core 路径闭合；数据库型号/参数仍缺失 | `BLOCKED` | VM/Headless 产品提交、旧 `frmMainMenu.cs`/`ucHMI.cs` |
| 参数管理九模块 | `PASS-UI-COVERAGE` / 持久化 `BLOCKED` | 用户、角色、权限、权限分配、车型、型号、项点、项点配置、试验参数按 Legacy 专项字段和交互呈现；权限控件名可空且非空唯一，权限分配按稳定 Id 区分同名记录并在删除时精确级联，项点双列表为左已配置/右候选，项点配置与试验参数只列已发布型号；权限复选和型号发布均只改会话 | UI 行为闭合；退出即丢弃，不能替代认证/SQLite/INI；不执行动态 C# 文件副作用，不虚构空白参数界面 | 保留会话模式并阻断真实授权、持久化、外部发布和动态源码操作 | VM/Headless、`legacy-ui-coverage.csv`、`ui-test-evidence.csv` |
| 日志与停用维护能力 | `PASS-UI-COVERAGE` | 默认当天和七个 Legacy 等级语义；日期查询、等级即时筛选、别名、倒序、500 条缓存；关键字是有意增强；设备检查/维保计量/问题统计保持停用 | 修复 Legacy 末日毫秒边界，且未擅自扩大维护能力 | 保留增强差异；历史 SQLite、真实身份/来源持久化仍阻断 | Diagnostics VM/Headless、`frmNLogs.cs`、`frmMainMenu.cs:119-121` |
| 硬件校准/工艺/绝缘仪器界面 | `PASS-OFFLINE`（安全锁定） | 通道、公式、执行元件和仪器参数可见/可校验；所有 Zero/Gain、AO/DO、复位、TCP 连接/发送固定禁用 | 覆盖界面同时维持 ReadOnly 边界 | 保留锁定；P3/现场前不得开放 | VM + Headless 危险按钮断言 |
| Gateway 健康 | `PASS-OFFLINE` | 新 Runtime 派生 `NoError`/`Simulated`，携带质量、时间戳、连接代次；没有本机 OPC UA | 离线五点健康状态可验证，生产 UA 仍未实现 | `DEFERRED` | Runtime 测试、Host JSON |
| P2 五点只读采样 | `PASS-OFFLINE` / 现场映射未闭合 | 固定五点：NoError、Simulated、AI00、DI00、CH00；新侧没有旧系统完整点表；信捷 CSV 的 AI/DI 地址尚未接入当前 P2 Runtime | 离线首条只读切片闭合；现场信捷只能证明协议读通，全量 102/124/140 点未迁移 | 保留离线切片；确认点表后再做最小只读映射 | `GatewayPointCatalog.cs`、Runtime/Headless、现场探针日志 |
| 信捷 Modbus TCP 仅读探针 | `PASS-FIELD-PROBE` | `192.168.0.51:502` 在 UnitId=1 候选下对 M400、HD1074 候选、D0 和输入寄存器 0 返回合法响应；当前应用仍要求 S7 AI/DI，且 `CH00` 未由 CSV 提供 | 协议链路可继续做集成测试，但不能把原始响应当作 P2 业务值或 DI00 安全联锁 | `BLOCKED`；先确认机型/UnitId/CH00/字序，再提交最小配置化只读变更 | `phase-e-field\2026-08-29\modbus-readonly-probe.txt` |
| 首样本 Unknown/Bad | `PASS-OFFLINE` | 新 Runtime 首样本五点为 Unknown，断链点为 Bad/null；不以 0/false 冒充 | 满足安全默认值 | 保留 | Runtime 测试 |
| DI00 安全联锁 | `PASS-OFFLINE` | Core 区分 DI00 Unknown/Bad 与 false；运行中 false 请求 Stopping；当前 UI 仍因 Test00 未接入禁用控制 | 安全门有证据，真实点位/电气语义未现场确认 | 保留并等 P0 | Core 测试、VM/Headless |
| Test00 手/自动 | `PARTIAL` | Core 有手动/自动门；P2 五点未包含 Test00，UI 不开放控制入口 | 不能宣称自动试验可用 | `DEFERRED` | Core 测试、UI 合同 |
| B11 压力调整阀 | `PARTIAL` | 新侧只抽取高低压范围、最多三次、Pass/Retry/Fail 和 val8/val16；旧侧还包含阀动作、延时、人工对话、上传 | 首条业务流程尚非完整可执行流程 | `BLOCKED` | `PressureAdjustmentWorkflow.cs`、B11 旧类、合同测试 |
| 读异常/断线/重连 | `PASS-OFFLINE` | 新 Runtime 读异常降级 Bad、主动断开，下一轮重连并产生新代次；无真实拔插证据 | 仿真恢复规则已验证，现场恢复时间未知 | 保留并等 P0 | Runtime 测试、日志 |
| 过期/重叠采样 | `PASS-OFFLINE` | Runtime 增加轮询串行门，避免重叠轮询后旧快照覆盖新快照；样本带连接代次 | P2 无独立事件总线，真实迟到 UA 事件仍未验证 | 保留并补现场证据 | Runtime、Headless 命令门 |
| 取消与资源释放 | `PASS-OFFLINE` | 新 Runtime 对取消、两条传输和异常清理有测试；旧测试任务取消语义尚未完全复刻 | 只读边界已覆盖 | 保留 | Runtime 测试 |
| 写入总边界 | `PASS-OFFLINE`（禁用） | 新侧 `WritesEnabled=false` 且无写 API；旧侧存在 AO/DO/TestCon/复位等写路径 | 当前不可执行写入是安全要求，不是完整功能通过 | 固定禁用，P3 另立安全矩阵 | Gateway 合同、发布扫描、UI 文案 |
| Zero/Gain 启动写入 | `PASS-OFFLINE`（未写） | 新侧启动/刷新无写入；旧硬件页启动路径的自动写风险保留为对照缺口 | 禁止误写 | 固定禁用；P3 审计后再议 | 配置、UI、behavior 合同 |
| 故障复位 | `DEFERRED` | Legacy 有 true→1000ms→false；新侧没有物理写实现 | 不能验收安全收尾 | 禁止实现，等待 P3 | `behavior-contracts.md:103-110` |
| 手动输出/自动试验 | `DEFERRED` | Legacy 有输出和动态测试项；新侧当前只做 Core 安全门，无设备动作 | 不能切换生产 | 禁止实现，等待 P3 | 点表、Core/UI 合同 |
| 保存/查询/报表/上传 | `PARTIAL` | 新侧实现组合筛选、选择和页码行为；默认不伪造记录，查看/打印/删除/重传外部命令阻断 | 界面可验证但数据和报表不可替代 | `BLOCKED` | `ReportsViewModel` VM/Headless；旧 BLL/Report/Upload |
| 退出/生命周期释放 | `PARTIAL` | 新 App 在桌面退出时释放 Runtime；Gateway 尚未独立为本机 OPC UA/systemd 服务 | UOS 运维生命周期未验证 | `DEFERRED` | `App.axaml.cs`、Host |
| 可访问性/布局 | `PASS-UI-COVERAGE` | 8 页关键入口有 AutomationProperties；按钮/输入最小高度、滚动布局、960×560/1600×900 和导航绑定已验证 | Windows Headless 可验证；触控/字体/焦点顺序/本地化切换未验收 | 保留并等 UOS | Headless、`ui-contracts.md` |
| Windows/UOS 发布 | Windows `PASS-OFFLINE`；Linux `PASS-PUBLISH-ONLY` / UOS `PENDING-FIELD` | 实现提交已生成 Avalonia/Host 四个自包含包；Windows Host 包内离线采样和 HMI 3 秒存活通过，7 个禁止依赖命中 0；Linux 仅在 Windows 构建机发布 | 当前提交的离线发布可复现，但 UOS 图形栈、启动/退出、systemd、触控和设备仍未验收 | Windows 离线证据保留；UOS 继续 `BLOCKED` | `phase-e-packaging-and-launch.md`、日志 09～17 |

## 5. 本轮最小修复

1. 修复证据生成器对 `Procedure` 根目录 7 个 UserControl 的误归类，新增 `legacy-ui-coverage.csv` 与 `ui-test-evidence.csv`，由脚本强制 51 个 Designer 一一对应、无孤儿/重复，resx 与主覆盖矩阵可追溯，所有证据 ID 还能定位到真实测试/脚本中的唯一 Marker。
2. 将原最小 P2 窗口重组为总览、试验作业、工艺监视、参数管理、数据报表、硬件校准、日志诊断、绝缘耐压 8 个任务页；21 个试验类和 9 个管理入口来自 Legacy 真实源码，不新增旧项目不存在的业务。
3. 用 ViewModel 状态和命令实现产品提交、九类管理专项字段/权限勾选/项点双列表/型号会话发布、会话 CRUD/唯一性、组合查询/分页、校准本地公式、Legacy 日志日期/七级/别名/倒序/500 条边界和仪器参数校验；关键字日志筛选登记为增强，外部认证/数据库/INI/动态 C#、报表、上传、仪器通信均保持明确阻断。
4. 将 12 个输入校准通道和 Legacy 6 个独立 AO 槽位逐项呈现；Zero/Gain、VX/AO/DO、复位、自动试验、仪器连接/发送、报表删除/重传均由命令自身或不可变状态固定禁用。
5. 补充启动取消和 Faulted 生命周期：取消首轮只读刷新不会继续初始化 Core，退出时先取消轮询再释放 Runtime；验证中发现并修复项点命令构造顺序空引用。
6. 通用交互最小高度提高到 44，补仪器输入的 AutomationProperties；Headless 真实发送 Ctrl+1～Ctrl+8，并验证 ListBox 选择、双向绑定、960×560/1600×900 和危险控件状态。

本轮未修改 Gateway 写边界、未新增写 API，也未把信捷探针接入默认配置；旧 WinForms、OPF 和回滚材料均未删除。

## 6. 切换判断

当前判定：`CUTOVER_NOT_READY`。信捷现场探针将 Modbus TCP 状态从“未执行”推进到“仅读协议已响应”，但没有关闭 P2 业务映射、现场安全或完整产品切换门。

允许的最小切换范围是“Windows 上的 Avalonia P2 离线仿真与 8 页纯 UI 演示/开发维护线”。会话管理和查询数据仅用于行为验证，所有外部效果及危险动作保持阻断。不允许把它切为现场生产唯一实现，原因是：

- 没有 UOS、S7、RS-485 和现场 OPC UA 证据；信捷 Modbus TCP 目前只有候选 UnitId 下的原始仅读协议证据，尚无 P2 业务映射闭环；
- 没有真实认证、数据库产品选择、完整 B11、报表/上传和服务化边界；
- P3 写入安全矩阵、质量/代次/联锁/回读/审计尚未实施，故所有写入继续关闭；
- 本轮起点为远端 `main` 的 `acf604f54a998a4e35d0e64476cb6350d7978a44`；实现提交 `92cdec56a3a93132fd5258446729fafedd9bc631` 已推送，独立 clone 在同一提交复现通过。即使 Git 恢复点成立，现场设备、UOS 和完整产品功能仍未验收，不能据此关闭 cutover。

Legacy 处理：当前工作区保留旧源码、OPF、原始数据库/报表和原生依赖作为只读对照/回滚材料；公开 Git 提交按安全策略排除旧数据库、历史报表和部分本地/生成工件，覆盖率清单保留其大小与 SHA-256 记录。不从解决方案删除，不物理删除目录，不允许新旧系统同时拥有写权限。

## 7. 延期项与关闭条件

| ID | 优先级 | 延期项 | 关闭条件 |
|---|---|---|---|
| E-BLK-01 | Blocker | UOS 版本/架构/触控/字体、自包含 Avalonia 启动、非 root systemd | 目标机原始命令输出、冷启动/退出码、服务日志和包哈希 |
| E-BLK-02 | Blocker | S7-200/S7-1200 AI00/DI00 地址、类型、字节序、Rack/Slot | 设备型号和点表签字，读取值/质量/时间戳，至少 2 小时日志 |
| E-BLK-03 | Blocker | Modbus RTU/TCP CH00 串口/终点、功能区、站号、字节序、拔插恢复 | 已有信捷终点的仅读响应；仍需 CH00 设备档案、功能码/地址、恢复时间、连接代次和连续运行记录 |
| E-BLK-04 | Blocker | 本机 OPC UA 地址空间、证书、Gateway→UA→HMI 链路 | UA 客户端只读验证、证书权限、断线/重连和禁止写扫描 |
| E-BLK-05 | Blocker | 信捷 CSV 的 `AI.MAI00=HD1074`、`DI.MDI00=M400` 与当前 P2 S7/Modbus 点模型未闭合 | 确认 PLC 系列和 UnitId；确认 HD 地址换算、浮点字序、M400 线圈语义和 `CH00` 映射；增加只读配置/测试后用应用快照、质量、时间戳和连接代次复核 |
| E-HIGH-01 | High | 真实登录、权限和数据库产品/参数选择 | 认证失败分类、角色权限、数据库事务和不覆盖旧选择测试 |
| E-HIGH-02 | High | 完整 B11 动作、人工确认、取消、安全收尾、记录和报表 | 逐项点表/时序/判定/报表字段对照和仿真/真机证据 |
| E-HIGH-03 | High | Test00、手动输出、自动试验、复位和 P3 写矩阵 | 安全审批；质量/代次/联锁/超时/回读/审计全部自动化和真机通过 |
| E-HIGH-04 | High | SQLite/报表/上传/备份恢复与旧数据兼容 | schema、事务、异常退出、恢复、字段/单位/渲染和回滚演练 |
| E-MED-01 | Medium | 启动失败的统一退出/Faulted 策略、UOS 本地化和触控 QA | 明确产品决策并补 Headless/UOS 手工记录 |

这些是延期记录，不是已批准豁免；当前没有审批人或签字，因此不得关闭 Blocker。

## 8. 信捷现场仅读协议探针

现场输入文件：`E:\Users\Administrator\xwechat_files\wxid_ffvyd8y69mry21_6c83\msg\file\2026-08\ALL_PLC_XDP.csv`。该 CSV 是 XDP/OPC 标签表，不是当前 P2 `Modbus.WSD.CH00` 的完整设备映射；其中 `AI.MAI00=HD1074, Float`、`DI.MDI00=M400, Boolean`，没有 `CH00`、`WSD`、功能区或 UnitId 字段。其 `Client Access=R/W` 只作为旧标签属性记录，本轮未据此发送任何写入。

地址探针口径：在适用的信捷以太网映射中，`M400` 作为线圈候选地址 400；按 `HD0` 起始 Modbus 地址 41088 的机型映射，`HD1074` 的候选寄存器起始地址为 `41088 + 1074 = 42162`。PLC 具体系列未提供，所以 42162 仅为验证候选，不是已签字的最终地址。参考：[信捷以太网通讯用户手册](https://cdn-en.xinje.com/TCPIP%20communication%20manual.pdf)。

执行时间：2026-08-29 11:58（北京时间，日志使用 UTC 时间戳）。执行命令为 PowerShell 内联只读探针，目标 `192.168.0.51:502`、UnitId 候选 `1`，依次发送功能码 01 地址 400 数量 1、功能码 03 地址 42162 数量 2、功能码 03 地址 0 数量 1、功能码 04 地址 0 数量 1。四次均收到合法响应：M400 原始值 0；HD1074 候选原始字 `0x0000, 0x435C`；D0 为 0；输入寄存器 0 为 0。

原始报文与命令结果：`D:\Codex相关\phase-e-field\2026-08-29\modbus-readonly-probe.txt`。本探针只验证网络/协议响应；没有修改 `config\gatewaysettings.json`，没有运行现场 `ConfiguredDevices` 应用快照，也没有发送写功能码。由于当前 Runtime 固定从 S7 读取 AI00/DI00、从 Modbus 读取 CH00，且 CSV 没有 CH00 映射，本轮不关闭 E-BLK-05。
