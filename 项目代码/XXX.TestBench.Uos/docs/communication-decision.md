# 通信库与配置决策

日期：2026-08-29
状态：已进入基础设施实现；信捷 Modbus TCP 仅有仅读协议探针，现场 P0 仍未完成。

当前已有信捷终点 `192.168.0.51:502` 的仅读协议响应：UnitId=1 候选下，M400、HD1074 候选、D0 和输入寄存器 0 返回合法 Modbus TCP 帧。但 PLC 系列、UnitId、HD1074 浮点字序、M400 业务语义、`Modbus.WSD.CH00` 和当前 P2 模型尚未闭合；仍无目标 UOS、S7 与 USB-RS485 现场证据。执行路线继续以默认离线仿真（`OfflineSimulation`）为安全基线，仿真和独立探针都不得启用写入或替代 P0/G0。

## 1. 决策

| 链路 | 实现 | 默认值 | 当前能力 |
|---|---|---|---|
| Siemens S7 | S7NetPlus | S7-200，`192.168.0.111:102`，Rack 0，Slot 2 | 只读 TCP 适配器；CPU 档案可切换为 S7-1200 |
| Modbus RTU | NModbus + NModbus.Serial | `COM1 / 9600 / 8N1 / SlaveId 1` | 只读寄存器适配器 |
| Modbus TCP | NModbus | `127.0.0.1:502 / UnitId 1` | 只读寄存器适配器 |

所有配置位于 `config/gatewaysettings.json`，由 `GatewaySettingsFile` 读取并校验。默认运行模式为 `ReadOnly`，当前适配器没有写入 API。

运行方式由 `gateway.transportMode` 明确选择：当前默认为 `OfflineSimulation`，只创建仿真传输；只有现场明确切换为 `ConfiguredDevices` 后，Host 才会尝试访问已启用的 S7 和活动 Modbus 链路。`gateway.activeModbusTransport` 可配置为 `Rtu` 或 `Tcp`，决定 `Modbus.WSD.CH00` 使用哪条链路；另一条链路仍保留配置，但不会被本轮切片同时打开。

## 2. 库能力核查

- S7NetPlus 官方仓库列出兼容 S7-200、S7-300、S7-400、S7-1200、S7-1500，并列出 .NET Standard 1.3/2.0 支持；本项目引用 `S7netplus 0.20.0`。
- NModbus 官方仓库列出串口 ASCII、串口 RTU、TCP 和 UDP；本项目引用 `NModbus 3.0.83` 与 `NModbus.Serial 3.0.83`。
- NuGet 目标框架信息显示 NModbus 与 NModbus.Serial 可用于 .NET Standard 2.0 和 net6.0 及更高兼容目标；当前 Gateway 目标为 `net8.0`，最终 UOS 运行仍需在目标机验证。

## 3. 地址与设备风险

用户提供 S7-200 与 S7-1200 均为 `192.168.0.111`。两个独立物理设备不能在同一网络中同时使用相同 IP，因此配置中保留两个命名档案，但默认仅启用 S7-200；启用重复终点会被校验拒绝。若现场只有一台 PLC，需以实际 CPU 型号切换档案；若现场有两台 PLC，需补充第二台 IP/端口。

S7-200 的 V 区地址解析目前覆盖 `V0.0`、`VB200`、`VW1030`、`VD200` 形式，并按原 OPF 点表读取原始字节。实际浮点格式、字节序、Rack/Slot 与 S7-1200 优化块访问限制必须通过 P0 真机确认，不能由 Windows 编译结果替代。

首条业务切片配置标识为 `PressureAdjustmentValveB11`（压力调整阀 B11）。Modbus TCP 的 Host、Port、UnitId、寄存器区、起始地址、数量和超时均保留在配置边界；当前默认值只是可启动的占位配置，不代表现场仪表地址。现场确认后只修改配置，不修改业务代码。

信捷探针未修改 `config/gatewaysettings.json`，未运行 `ConfiguredDevices` 应用快照，也未发送写功能码。其原始报文位于 `D:\Codex相关\phase-e-field\2026-08-29\modbus-readonly-probe.txt`；该证据只能说明网络和候选读取协议可响应。

## 4. 安全边界

- P2 只读；不建立离线写队列，不重放断线前命令。
- 启动、刷新和打开硬件页面不得写 Zero/Gain。
- P3 写入前必须重新建立写入矩阵，并校验操作权限、质量、连接代次、业务联锁、超时和回读。
- Modbus 的写寄存器/线圈 API 暂不接入 Gateway，直到设备档案和安全矩阵签字。
- `SimulatedS7ReadOnlyTransport` 与 `SimulatedModbusReadOnlyTransport` 仅用于离线 P2 合同测试，`Gateway.Health.Simulated` 必须在界面上明确显示。
