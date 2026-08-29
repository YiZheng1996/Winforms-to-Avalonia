# Avalonia UI 合同

版本：Phase D UI V1（2026-08-29）  
状态：已实现最小可运行 UI Shell；当前仍为 P2 只读离线仿真。

## 1. 组合与边界

- `src/XXX.TestBench.Avalonia/App.axaml.cs` 是组合根：加载配置、创建只读 Gateway Runtime、创建 `TestBenchStateMachine` 和 `MainWindowViewModel`，并负责退出时释放 Runtime。
- `src/XXX.TestBench.Avalonia/MainWindow.axaml` 只负责布局、绑定和可访问性元数据；`MainWindow.axaml.cs` 不承载业务逻辑。
- ViewModel 通过 `ReadOnlyGatewayRuntime` 获取快照，通过 `TestBenchStateMachine` 执行状态转移；UI 不直接访问 S7、串口、Modbus 或旧 WinForms 类型。

## 2. 主窗口区域

| 区域 | 内容 | 行为合同 |
|---|---|---|
| 顶部状态区 | 连接、试验状态、DI00 安全状态 | Unknown/Bad/仿真状态可见，不把默认值当真实输入 |
| 左侧流程区 | 产品输入、选择产品、仿真会话、刷新、核心命令 | 命令可用性由 Core 状态和 Gateway 质量决定 |
| 右侧数据区 | P2 五点、值、质量、采样时间、连接代次 | 只读展示；质量不是 Good 时显示诊断 |
| 底部反馈区 | 结构化日志摘要和命令反馈 | 错误以诊断文本展示，不在 View 中重新实现业务规则 |

## 3. 命令映射

- `刷新只读数据` → `ReadOnlyGatewayRuntime.PollOnceAsync`。
- 产品输入框使用双向绑定，选择产品时才提交到 Core；输入过程不直接改变设备或试验状态。
- `进入仿真会话` → Core 登录合同的明确离线演示入口；ConfiguredDevices 模式不会伪造登录。
- `选择产品` → `TestBenchStateMachine.SelectProduct`。
- `启动自动试验`、`进入手动模式`、`请求停止`、`故障恢复` → Core 状态机命令；当前 Test00 未进入 P2 五点，因此自动/手动入口保持禁用。
- 所有异步命令都有忙碌态和异常处理；刷新命令不可重入。

## 4. 布局和可访问性

- 默认窗口为 1280×760，最小尺寸为 960×560；内容区使用 Grid、ScrollViewer 和可伸缩列，不使用绝对坐标。
- 产品输入框和核心按钮设置稳定 `x:Name` 与 `AutomationProperties.Name`，Headless 测试验证关键控件加载和名称。
- 右侧点表可滚动；左侧流程在窄窗口下可滚动，避免 1920×1080 触控目标被固定坐标截断。
- 当前 UI 字符串直接位于 View 层，后续本地化需迁移到 Presentation 资源；Core 不包含 UI 文化和文本规则。

## 5. 当前限制

- 尚未接入真实认证、Test00、数据库、报表、OPC UA 和物理写入；Zero/Gain 继续保持禁用。
- Headless/Windows 构建和离线仿真不能证明 UOS 触控、字体、真实设备通信或现场运行兼容性。
