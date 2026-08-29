# Phase E：Legacy vs Avalonia 差异与 Cutover 准备

日期：2026-08-29  
状态：`P2_OFFLINE_VERIFIED`；完整产品切换未通过  
仓库身份：<https://github.com/YiZheng1996/Winforms-to-Avalonia.git>  
本地工作区：`D:\易峥\2026\2026-09\Avalonia_上位机通用模板`  
实际迁移解：`项目代码\XXX.TestBench.Uos\XXX.TestBench.Uos.sln`  
旧基线：`XXX试验台模板\src\master\MainUI`  

## 1. 证据边界

当前工作区已初始化为 Git checkout，远端为 `origin`：<https://github.com/YiZheng1996/Winforms-to-Avalonia.git>。当前 `main` 基线提交为 `28a7eb292a00bf5c957a0008ca2713644a624eab`，并已用独立 clone 完成恢复验证。本报告的产品结论仍以实际源码、旧 WinForms 源码、迁移文档、自动化输出和用户提供的 Windows VS 启动截图为证据；公开仓库按安全策略排除本地运行数据和部分生成/用户工件。

当前开发机证据：Windows 10.0.22621 x64，.NET SDK 9.0.300；迁移解实际目标为 `net8.0`，Avalonia 包为 11.3.9。当前没有真实 UOS 工控机、PLC、USB-RS485、Modbus TCP 仪表或现场 OPC UA 环境；以下 `PASS-OFFLINE` 只表示代码/仿真证据。

固定安全边界：Gateway `ReadOnly`，只发布五点观测；不创建 Zero/Gain 自动写入、手动输出、复位写入、离线写队列或断线重放。任何真实 PLC/Modbus/UOS 兼容性、OPC UA 证书和 systemd 结论均保持未验收。

## 2. 结论

| 项目 | 结论 |
|---|---|
| 当前 P2 只读切片 | Avalonia 可作为该切片的维护线；启动、仿真采样、质量、DI00 展示、写禁用和安全门已有自动化证据 |
| 完整 Avalonia 产品 | 未完成；登录、数据库产品选择、完整 B11 设备流程、报表、OPC UA Host、UOS 服务化和现场设备仍缺失 |
| Legacy 是否冻结 | 否；旧 WinForms 保留为对照/回滚基线 |
| Legacy 是否删除 | 否；存在 Blocker/High 缺口和未完成现场验收 |
| 可关闭的阶段门 | 仅关闭 P2 离线仿真证据；不关闭 G0/P0/G3 |

## 3. 模块映射

| 模块 | Legacy 证据 | Avalonia/目标位置 | 当前处置 |
|---|---|---|---|
| 启动/UI Shell | `MainUI/Program.cs`、`frmMainMenu.cs`、`ucHMI.cs`、Designer/resx | `src/XXX.TestBench.Avalonia` | 已建立最小 Shell；仅 P2 只读 |
| 应用核心 | `Model/`、`Procedure/Test/`、`BaseTest.cs`、`GeneralBaseTest.cs` | `src/XXX.TestBench.Core` | 已抽取状态机和 B11 纯判定；完整业务未完成 |
| 通信边界 | `CurrencyHelper/OPCHelper.cs`、`Modules/*.cs`、OPF | `src/XXX.TestBench.Gateway` | 已有 S7/Modbus 只读适配器和仿真 Runtime；尚未接本机 OPC UA |
| Gateway Host | 旧进程/OPC DA 生命周期 | `src/XXX.TestBench.Gateway.Host` | 已有离线 CLI；未形成 UOS systemd 服务 |
| 认证/持久化/报表 | `frmLogin.cs`、`BLL/`、`DB/TestBed.db`、Report/Office | 尚无等价生产实现 | 延期；不得以文本输入替代数据库认证/查询 |
| 资源/发布 | `Resources/`、`img/`、`Lib/`、旧 csproj | Avalonia 项目与 RID 发布目录 | P2 需要的最小资源可发布；旧 DLL 不进入包 |

旧文件覆盖工件 `legacy-coverage.csv` 仍为 393 行，模块唯一归属统计为：application-core 37、build-distribution 36、gateway-boundary 19、persistence 14、platform-boundary 30、ui-assets 122、ui-shell 104、vendor-platform 31。点表 `tag-and-write-matrix.csv` 为 140 行：旧订阅 102、源能力 124、OPF-only reserve 16；这不是“已全部迁移”的计数。

## 4. 差异矩阵

状态含义：`PASS-OFFLINE` 为代码/测试/仿真通过；`PARTIAL` 为当前切片可用但不等价；`DEFERRED` 为明确延期；`BLOCKED` 为切换前必须关闭或取得正式豁免。

| 功能/合同 | Avalonia 状态 | Legacy vs new 差异 | 影响 | 决策 | 证据 |
|---|---|---|---|---|---|
| 启动正常路径 | `PASS-OFFLINE` | 新程序由 Avalonia App 组合 Core + 只读 Runtime；不启动旧 OPC DA/COM | P2 正常启动可验证 | 保留 | `App.axaml.cs`、Headless、用户 VS 启动截图 |
| 启动配置/初始化失败 | `PARTIAL` | `AppComposition` 捕获异常并显示诊断 VM；尚未按合同统一退出/进入可审计 Faulted | 配置损坏时生命周期策略未与 Legacy 合同闭合 | `DEFERRED`，P3 前定案并测试 | `AppComposition.cs`、`behavior-contracts.md:44-51` |
| 登录 | `PARTIAL` | 仅 OfflineSimulation 提供明确的演示登录；ConfiguredDevices 不伪造成功；旧系统有数据库用户/密码流程 | 不能替代操作员认证和角色权限 | `BLOCKED` | `MainWindowViewModel.cs`、Core 测试、旧 `frmLogin.cs` |
| 产品/型号选择 | `PARTIAL` | 新 UI 是有长度边界的产品标识输入并提交 Core；旧系统从数据库选择型号并刷新参数 | 型号、参数、产品编号/车号尚未闭合 | `BLOCKED` | `TestBenchStateMachine.cs`、VM 测试、旧 `frmMainMenu.cs`/`ucHMI.cs` |
| Gateway 健康 | `PASS-OFFLINE` | 新 Runtime 派生 `NoError`/`Simulated`，携带质量、时间戳、连接代次；没有本机 OPC UA | 离线五点健康状态可验证，生产 UA 仍未实现 | `DEFERRED` | Runtime 测试、Host JSON |
| P2 五点只读采样 | `PASS-OFFLINE` | 固定五点：NoError、Simulated、AI00、DI00、CH00；新侧没有旧系统完整点表 | 首条只读切片闭合；全量 102/124/140 点未迁移 | 保留切片，延期扩展 | `GatewayPointCatalog.cs`、Runtime/Headless |
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
| 保存/查询/报表/上传 | `DEFERRED` | Legacy 有 SQLite/FreeSql、模板、Office/上传；新侧尚无等价生产能力 | 数据和报表不可替代 | `BLOCKED` | 旧 BLL/Report/Upload，现有合同 |
| 退出/生命周期释放 | `PARTIAL` | 新 App 在桌面退出时释放 Runtime；Gateway 尚未独立为本机 OPC UA/systemd 服务 | UOS 运维生命周期未验证 | `DEFERRED` | `App.axaml.cs`、Host |
| 可访问性/布局 | `PASS-OFFLINE` | 新侧验证关键 AutomationProperties、绑定、最小尺寸和滚动布局；Legacy 控件级行为未逐项复刻 | Windows Headless 可验证；触控/字体/本地化未验收 | 保留并等 UOS | Headless、`ui-contracts.md` |
| Windows/UOS 发布 | `PARTIAL` | 两个 RID 自包含包生成；仅 Windows Host/应用做启动烟测；UOS 未启动 | 可交付离线包，不是现场兼容通过 | `BLOCKED` | `phase-e-packaging-and-launch.md` |

## 5. 本轮最小修复

1. `ReadOnlyGatewayRuntime` 增加轮询串行门；读异常返回 Bad 后主动断开故障传输，下一轮才能重新连接并递增代次。新增 Runtime 行为测试覆盖 S7/Modbus 读异常、断开和恢复。
2. `MainWindow.axaml` 把 `Text="#{Binding ConnectionGeneration}"` 改为可实际解析的 `#` 文本 + 代次绑定，并由 Headless 测试检查真实渲染结果。
3. Avalonia 和 Gateway Host 项目显式设置 `CopyToPublishDirectory`；Host 自包含发布包现在包含 `config/gatewaysettings.json`。Windows 包内相对配置已用 Host `--once` 启动验证。

以上修复均未新增写入 API、写按钮或真实设备访问；旧 WinForms 代码未删除。

## 6. 切换判断

当前判定：`CUTOVER_NOT_READY`。

允许的最小切换范围是“Windows 上的 Avalonia P2 离线仿真演示/开发维护线”。不允许把它切为现场生产唯一实现，原因是：

- 没有 UOS、PLC、RS-485、Modbus TCP 和现场 OPC UA 证据；
- 没有真实认证、数据库产品选择、完整 B11、报表/上传和服务化边界；
- P3 写入安全矩阵、质量/代次/联锁/回读/审计尚未实施，故所有写入继续关闭；
- 已形成可引用的 `main` 提交基线并完成独立 clone 复现；但现场设备、UOS 和完整产品功能仍未验收，不能据此关闭 cutover。

Legacy 处理：当前工作区保留旧源码、OPF、原始数据库/报表和原生依赖作为只读对照/回滚材料；公开 Git 提交按安全策略排除旧数据库、历史报表和部分本地/生成工件，覆盖率清单保留其大小与 SHA-256 记录。不从解决方案删除，不物理删除目录，不允许新旧系统同时拥有写权限。

## 7. 延期项与关闭条件

| ID | 优先级 | 延期项 | 关闭条件 |
|---|---|---|---|
| E-BLK-01 | Blocker | UOS 版本/架构/触控/字体、自包含 Avalonia 启动、非 root systemd | 目标机原始命令输出、冷启动/退出码、服务日志和包哈希 |
| E-BLK-02 | Blocker | S7-200/S7-1200 AI00/DI00 地址、类型、字节序、Rack/Slot | 设备型号和点表签字，读取值/质量/时间戳，至少 2 小时日志 |
| E-BLK-03 | Blocker | Modbus RTU/TCP CH00 串口/终点、功能区、站号、字节序、拔插恢复 | 报文、设备档案、恢复时间、连接代次和连续运行记录 |
| E-BLK-04 | Blocker | 本机 OPC UA 地址空间、证书、Gateway→UA→HMI 链路 | UA 客户端只读验证、证书权限、断线/重连和禁止写扫描 |
| E-HIGH-01 | High | 真实登录、权限和数据库产品/参数选择 | 认证失败分类、角色权限、数据库事务和不覆盖旧选择测试 |
| E-HIGH-02 | High | 完整 B11 动作、人工确认、取消、安全收尾、记录和报表 | 逐项点表/时序/判定/报表字段对照和仿真/真机证据 |
| E-HIGH-03 | High | Test00、手动输出、自动试验、复位和 P3 写矩阵 | 安全审批；质量/代次/联锁/超时/回读/审计全部自动化和真机通过 |
| E-HIGH-04 | High | SQLite/报表/上传/备份恢复与旧数据兼容 | schema、事务、异常退出、恢复、字段/单位/渲染和回滚演练 |
| E-MED-01 | Medium | 启动失败的统一退出/Faulted 策略、UOS 本地化和触控 QA | 明确产品决策并补 Headless/UOS 手工记录 |

这些是延期记录，不是已批准豁免；当前没有审批人或签字，因此不得关闭 Blocker。
