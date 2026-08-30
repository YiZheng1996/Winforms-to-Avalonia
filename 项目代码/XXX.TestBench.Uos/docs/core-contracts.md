# 应用核心合同

版本：P2 Core V1（2026-08-29）  
状态：已实现纯 C# 状态机和 B11 纯业务判定；不包含 Avalonia、WinForms、数据库、文件、PLC 或串口调用。

## 1. 实现位置

- `src/XXX.TestBench.Core/Application/ApplicationContracts.cs`：状态、模式、质量、诊断、Gateway 观测和自动试验上下文。
- `src/XXX.TestBench.Core/Application/TestBenchStateMachine.cs`：初始化、登录、Gateway 状态更新、产品选择、手动模式、自动试验启动/停止、完成、故障恢复。
- `src/XXX.TestBench.Core/B11/PressureAdjustmentWorkflow.cs`：压力调整阀 B11 的高低压范围判定、最多三次调整和报表值构造。
- `tests/XXX.TestBench.Core.Tests/Program.cs`：纯 Core 合同测试，不引用 UI 或真实设备。

## 2. 关键安全规则

1. `DI00` 质量不是 `Good` 时，自动试验启动被拒绝，诊断码为 `SafetySignalInvalid`。
2. `DI00` 为 `false` 时，自动试验启动被拒绝，诊断码为 `SafetyInterlockOpen`。
3. `Test00` 质量不可用或为手动模式时，自动试验启动被拒绝。
4. 运行中 Gateway 断线、关键点质量降级或 `DI00` 变为 false，状态转为 `Stopping`，不会转为成功。
5. 安全收尾失败进入 `Faulted`；安全收尾成功才返回 `Ready`。
6. Core 不提供设备写入方法。实际收尾动作、权限、联锁、超时、回读由 Gateway/P3 安全边界负责。

## 3. B11 首条切片

当前切片为“压力调整阀 B11”，来源为旧代码 `Procedure/Test/压力调整阀/B11_ProfilePressureTest.cs`。核心只负责：

- 接收高压侧、低压侧实测值和当前尝试次数；
- 判定是否在设定范围内；
- 未达范围且未超过次数时返回 `NeedsOperatorAdjustment`；
- 达到最大次数返回 `Failed`；
- 两侧合格时返回 `Passed`，构造 `val8`、`val16` 和高低压上传值。

排气阀、电压输出、充气、稳压、人工确认、设备写入、数据库记录和报表文件仍属于后续 Gateway/Platform/UI 集成工作，不能在 Core 中直接实现。

## 4. 当前未关闭项

- Gateway 观测尚未接入本机 OPC UA Host；当前 Avalonia 直接消费只读 Runtime，属于离线 P2 实现；
- B11 的真实点位、量程、字节序、设备动作、完整项点编排和报表字段尚未在现场确认；
- SQLite、报表、真实认证和 UOS systemd 发布尚未实现；
- 信捷 `192.168.0.51:502` 仅完成候选 UnitId 下的 Modbus TCP 仅读协议探针；尚无目标 UOS、S7、USB-RS485、P2 应用点映射或业务连续运行证据，因此 G0/P0、拔插恢复、OPC UA 证书链和 P3 写入验收保持未通过。

## 5. Phase C 边界

`ReadOnlyGatewayRuntime` 位于 Gateway Infrastructure，不进入 Core。它把通信适配器的连接、读取、质量、时间戳和连接代次转换为 5 点只读快照；Host 负责配置加载、生命周期、取消信号和结构化日志。Core 只消费平台层整理后的 Gateway 观测，不直接触碰文件、串口、TCP、PLC 或控制台。
