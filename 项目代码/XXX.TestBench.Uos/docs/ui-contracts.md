# Avalonia UI 合同

版本：Phase D UI V2（2026-08-30）
状态：Legacy 51 个 Designer 单元已建立专项覆盖矩阵；8 个任务页面可编译并具备 VM/Headless 行为证据。外部认证、持久化、报表、设备动作与现场兼容仍按矩阵明确阻断。

## 1. 组合与边界

- `src/XXX.TestBench.Avalonia/App.axaml.cs` 是组合根：加载配置、创建只读 Gateway Runtime、Core 状态机与主 ViewModel；退出时先取消轮询，再释放 Runtime，避免迟到结果回写已关闭 UI。
- `MainWindow.axaml` 只负责导航、布局、绑定、快捷键和可访问性元数据；各 `Views/*.axaml.cs` 只调用 `InitializeComponent()`。
- ViewModel 通过 `ReadOnlyGatewayRuntime` 获取快照，通过 `TestBenchStateMachine` 执行状态转移；任何 View/ViewModel 都不直接访问 S7、Modbus、串口、旧 WinForms、Office COM 或动态 C#。
- Presentation 文本由 `Localization/PresentationTextCatalog.cs` 建立首个本地化边界；当前交付 `zh-CN`，Core/Gateway 不依赖 UI 文化。
- 完整映射见 `legacy-ui-coverage.csv`。该矩阵逐一覆盖 51 个 Designer，记录 Legacy 入口、Avalonia 目标、实现状态、行为/Headless 证据、安全处置和剩余外部缺口；`ui-test-evidence.csv` 再把矩阵中的每个证据 ID 绑定到真实测试/脚本及文件内唯一 Marker。

## 2. 任务页面

| 导航 | View / ViewModel | Legacy 覆盖 | 已实现行为 | 明确未实现 |
|---|---|---|---|---|
| 运行总览 | `OverviewView` / `MainWindowViewModel` | 主菜单、登录、OPC 状态、P2 点表 | 仿真登录、产品标识、只读刷新、质量/时间戳/代次、安全门 | 真实认证、生产 OPC UA |
| 试验作业 | `TestOperationView` / `TestOperationViewModel` | `ucHMI`、产品选择、21 个具体试验类、两个人工调整对话框 | 车型分组、产品编号/车号/备注、21 项唯一映射、选择/清空、同一 Core 产品提交 | Test00 采样、设备动作、完整 B11/EP 时序、记录/报表 |
| 工艺与手动 | `ProcessOverviewView` / `ProcessOverviewViewModel` | `UcHMI_FLE`、AI/DI/WSD、AO/DO、开关控件、数值输出 | P2 可用点只读展示；Legacy 信号组与执行元件可追溯 | VX/DO/AO、复位、手动数值写入固定禁用 |
| 参数管理 | `ManagementView` / `ManagementViewModel` | 用户、角色、权限、权限分配、车型、型号、项点、项点配置、试验参数及编辑窗体 | 9 个专项编辑器：用户/角色字段、权限代码与可空控件名、按稳定 Id 区分同名权限的角色权限勾选、删除权限精确清理会话分配、车型/型号、型号会话发布、项点实体类与启用状态、左已配置/右候选的项点有序双列表、已发布型号过滤、报表模板/保存路径；会话增改删和唯一性拒绝 | SQLite 事务、真实授权/密码哈希、文件选择器、INI/模板持久化；动态 C# 创建/移动/删除固定阻断 |
| 数据查询与报表 | `ReportsView` / `ReportsViewModel` | 数据管理、报表查看器 | 车型/型号/产品编号/车号/日期组合筛选、选择、页码边界 | SQLite、渲染/打印、删除、人工重传固定阻断 |
| 硬件校准 | `CalibrationView` / `CalibrationViewModel` | `frmHardWare`、`UCCalibration`、`PLCCalibration` | 12 个输入校准通道、6 个独立 AO 槽位和 `工程值 × Gain - Zero` 本地草稿计算 | Zero/Gain 应用、AO 输出、真实检测写入固定禁用 |
| 日志与维护 | `DiagnosticsView` / `DiagnosticsViewModel` | 日志、修改密码、设备检查、维保计量、问题统计 | 默认当天；`全部等级/跟踪/调试/信息/警告/错误/致命` 对应 Legacy `All/Trace/Debug/Info/Warn/Error/Fatal`；查询按钮应用日期，等级切换即时过滤，按时间倒序且当前会话最多 500 条；关键字是明确标注的 Avalonia 增强 | 历史 SQLite 日志、认证；三个 Legacy 项目级隐藏功能不擅自启用 |
| 绝缘耐压 | `InstrumentView` / `InstrumentViewModel` | 绝缘耐压/电阻测试窗体 | IP/端口和参数正负向校验、三种试验类型 | TCP 连接、参数发送、请求/结束/取消固定禁用 |

`Properties/Resources.Designer.cs`、`Properties/Settings.Designer.cs` 属于生成资源/设置证据，不是产品窗体；仍在专项矩阵中标为 `GeneratedReference`，避免从 51 个 Designer 统计中消失。

## 3. 命令和安全合同

- `刷新只读数据` → `ReadOnlyGatewayRuntime.PollOnceAsync(CancellationToken)`；忙碌态不可重入，退出会取消。
- `进入仿真会话` 只在 `OfflineSimulation` 开放；ConfiguredDevices 不伪造认证成功。
- `选择产品` 和试验作业的“提交本次产品信息”都进入 `TestBenchStateMachine.SelectProduct`，输入过程不改变 Core 或设备。
- `启动自动试验`、`进入手动模式`、`请求停止`、`故障恢复` 继续由 Core 状态机拥有；Test00 未进入 P2 五点，因此自动/手动入口保持禁用。
- 工艺、校准和仪器页的写入型控件同时满足：ViewModel 命令 `CanExecute=false`，XAML `IsEnabled=false` 或绑定安全状态；没有新增 Gateway 写 API。
- 参数管理只修改当前进程集合，退出即丢弃；界面用醒目文字禁止把会话交互误认成 SQLite 已迁移。
- 参数管理不伪造登录权限：普通用户对权限管理/分配的 Legacy 可见性规则必须等真实认证接入后执行。权限控件名允许为空、非空时唯一；分配关系保存稳定权限 Id、显示名称允许重复，删除时只清理对应 Id；项点配置/试验参数只列已发布型号，双列表保持 Legacy 左已配置、右候选。项点实体类字段只保存会话草稿，不执行 Legacy 的动态源码创建、移动或删除；试验参数只覆盖车型/型号、报表模板文件名和保存目录，不把 Legacy 空白“参数界面”页签虚构成可编辑能力。
- 报表查询可针对注入记录执行真实筛选/翻页行为；默认组合根不注入伪造记录，持久化未接入时结果为空。
- 日志日期采用 `[当天 00:00, 次日 00:00)` 的等价日边界，修正 Legacy `23:59:59` 可能遗漏毫秒记录的问题；“全部等级”不加等级条件，Gateway `Information/Warning` 分别兼容为 `Info/Warn`。关键字检索不是 Legacy 搜索按钮语义，只作为有意增强保留。
- 配置/Gateway 初始化失败进入可审计 `Faulted` 并保留启动诊断，不再停留在模糊的 `Starting`。

## 4. 布局、键盘、触控和可访问性

- 默认窗口 `1440×860`，最小 `960×560`；任务页采用 Grid、WrapPanel 和 ScrollViewer，不使用 WinForms 绝对坐标。
- `Ctrl+1`～`Ctrl+8` 切换任务页；主 `ListBox` 同时提供键盘方向键、鼠标和触控共享的选择路径。快捷键不占用文本编辑常用字符键。
- 通用按钮、输入控件和导航项最小高度统一为 44；实际 1920×1080 触控命中仍需 UOS 目标屏人工验收。
- 主导航、产品输入、参数编辑、日志、报表、校准、仪器和所有危险动作均设置稳定 `AutomationProperties.Name`。
- Headless 独立进程通过真实 `KeyPressQwerty` 输入验证 Ctrl+1～Ctrl+8，并验证 ListBox 选择、跨页面双向绑定、命令驱动集合/Core、危险动作禁用、最小/宽窗口切换和可访问名称。
- 当前 Headless 只证明 Windows 渲染主机下的行为约束，不证明 UOS 字体、缩放、X11/Wayland、触控或屏幕阅读器兼容。

## 5. 自动化证据

- `tests/XXX.TestBench.Avalonia.Tests`：8 个导航键、21 个试验类唯一映射、9 个专项参数模块、会话 CRUD/重复修改拒绝/权限勾选/项点双列表/型号发布、组合筛选/翻页、12 个输入通道/6 个 AO 槽位、本地校准公式、日志日期/等级/别名/排序/500 条边界、仪器参数校验、启动取消/失败 Faulted 与全部写门禁。
- `tests/XXX.TestBench.Avalonia.Headless.Tests`：实际加载 XAML，真实触发 8 个 Ctrl 快捷键并从 ListBox 到达任务页，验证双向绑定、AutomationProperties、管理专项控件、日志日期/等级/查询入口、报表查询、危险按钮禁用、窄/宽布局。
- `tools/Test-MigrationEvidence.ps1`：核对 51 个 Designer 与 `legacy-ui-coverage.csv` 一一对应、无重复/孤儿、resx 引用存在，验证 7 组 Procedure 根 UserControl 正确归入 `ui-shell`，并通过 `ui-test-evidence.csv` 校验所有证据 ID、源文件和唯一 Marker 可追溯。

## 6. 未关闭项

- 真实登录、角色权限、SQLite schema/事务/备份恢复、型号/参数读取、历史日志、纯托管报表渲染/打印/上传仍未实现。
- 完整 B11/EP/测试类设备编排、人工确认、取消/安全收尾、Test00、手动输出、复位、Zero/Gain、绝缘仪器通信均未通过 P3/现场门；界面覆盖不等于这些外部效果已通过。
- 信捷 `192.168.0.51:502` 只有候选 UnitId=1 下的仅读协议响应；`HD1074` 字序、`M400` 语义、`CH00` 和 P2 业务映射未闭合，默认配置仍为 `OfflineSimulation`。
- UOS、PLC/Modbus 业务地址、OPC UA/systemd、2 小时稳定性与 5 次拔插恢复仍为现场阻断；旧 WinForms、OPF、数据库和回滚材料继续保留。
