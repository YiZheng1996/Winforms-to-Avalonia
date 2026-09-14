# 标准 Modbus TCP 与 RTU 设备点位通信立即编码实施方案（v1.1 编码定稿）

编制日期：2026-09-14  
目标项目：`D:\易峥\2026\2026-09\Avalonia_上位机通用模板\项目代码\XXX.TestBench.Template`  
方案版本：`v1.1`  
方案性质：基于当前工作树的可直接编码定稿方案，本文不代表 Modbus 功能已经实现或完成实机验证。

## 1. 结论

当前项目不需要重新设计设备点位层级，继续使用：

```text
通信通道
└─ 设备
   └─ 点位分组
      └─ 点位
```

本次改造把“连接资源”和“设备协议”严格分开：

```text
TCP 网络通道
├─ 西门子 S7 设备
└─ Modbus TCP 设备

串口通道
└─ Modbus RTU 设备
```

本项目首版只支持三种硬件驱动：

1. 西门子 S7，使用 TCP 网络通道；
2. Modbus TCP，使用 TCP 网络通道；
3. Modbus RTU，使用串口通道。

明确不纳入首版：

- Modbus ASCII；
- 厂家自定义 TCP 或串口协议；
- Modbus UDP；
- “RTU over TCP”透明串口服务器模式；
- 自动扫描站号、自动猜测寄存器区、自动猜测字节序；
- Modbus Server/Slave 功能；
- 动态插件加载。

### 1.1 编码冻结决策

以下决策在本轮编码中视为已定稿，不再由实现人员临时选择：

1. Hardware Modbus 启动时必须建立传输层资源：TCP 建立 Socket，RTU 打开共享串口；启动阶段不发送没有明确点位地址的通用“Ping”或猜测性读请求。
2. 传输层建立成功只表示候选运行时可以激活，不等于设备协议在线；Hardware Modbus 必须在首次有效 Modbus 响应后才进入 `Online`。
3. Hardware Modbus 在首次有效响应前使用现有 `Connecting` 状态，客户界面显示“待点位验证”；没有点位时可以保持该状态，不伪造协议在线。
4. Modbus 读取允许在 `Connecting/Offline` 状态发起恢复探测；写入仍只允许在 `Online/Degraded` 状态执行，不能把写操作作为首次连通性探测。
5. 每次 Modbus 请求必须显式携带请求超时和 `CancellationToken`；一旦超时或取消，当前 TCP/RTU 连接立即作废并关闭，不得交给下一次请求复用。
6. 新建或重新保存的 Modbus 位点统一持久化为 `DevicePointDataType.Bool`；旧 `Boolean` 只在 Modbus 兼容边界按布尔值读取，不进行全项目枚举替换。
7. 32/64 位寄存器值的字序没有项目隐式默认值，必须由点位配置明确给出；导入模板升级并提供独立的字节序、字序列。
8. 候选连接测试不得打开活动运行时已经占用的第二份串口；占用判断由当前发布运行时提供只读查询，不暴露或转移 `SerialPort` 所有权。
9. `IsImplemented=true` 只能在 TCP/RTU 运行时、连接释放、配置应用、错误映射和相应自动化测试全部完成后统一切换，不能在中间阶段提前开放 Hardware 保存。
10. 所有软件验证、模拟器验证、虚拟串口验证和真实设备验证继续分别记录；没有真实设备证据时不得写“现场通过”。

## 2. 当前源码基线

以下内容已由当前工作树源码确认。

### 2.1 已具备的能力

| 当前能力 | 源码位置 | 结论 |
|---|---|---|
| TCP、Serial、Simulation 通道类型 | `src/XXX.TestBench.Core/Configuration/ChannelConfiguration.cs` | 通道分类已经正确，不需要重做枚举 |
| 串口号、波特率、数据位、校验位、停止位 | `SerialChannelParameters`、`ChannelEditorViewModel`、`ChannelEditorDialogWindow.axaml` | 串口配置界面已经具备 |
| 通道与设备分离 | `ChannelEntry`、`DeviceConfig.DeviceEntry.ChannelId` | 可以让一个串口通道挂接多个 RTU 站号设备 |
| Modbus RTU/TCP 稳定驱动键 | `DriverKeyCatalog.ModbusRtu`、`DriverKeyCatalog.ModbusTcp` | JSON 和点位协议枚举已经预留 |
| Modbus 站号字段 | `DeviceConfig.DeviceEntry.ModbusUnitId` | 无需再新建重复站号字段 |
| 设备独立仿真/硬件模式 | `DeviceEntry.DeviceMode`、`DeviceSession` | Modbus 设备可以先仿真配置，不能因硬件失败自动退回仿真 |
| 多设备运行时、缓存、轮询、降级、连接代次 | `MultiDeviceRuntime`、`DeviceSession`、`ChannelManager` | Modbus 应接入现有运行时，不另建第二套设备框架 |
| 原子配置应用与回滚 | `DeviceConfigurationService`、`DeviceConfigurationStore` | Modbus 配置必须继续走 Stage、候选运行时、CommitActive、Publish |
| 只读点位诊断 | `DevicePointDiagnosticsViewModel` | Modbus 实现后直接复用 `ReadFreshAsync`，不得创建旁路连接 |
| 写入安全链 | `DeviceWritePipeline`、`PointWritePolicy.ReadBackEqual` | Modbus 写入必须走现有权限、风险确认和新鲜回读 |
| S7 硬件运行时 | `S7NetPlusDeviceRuntime` | 本次不得破坏已经存在的 S7 行为 |

### 2.2 当前明确未实现的能力

`DriverRegistry.CreateDefault()` 当前把 `modbus-rtu` 和 `modbus-tcp` 注册为 `UnsupportedDriverDescriptor`，其 `IsImplemented=false`。  
`DeviceSession.CreateRuntime()` 当前硬件模式只创建 `S7NetPlusDeviceRuntime`，其他驱动明确抛出“当前不能在硬件模式使用”。

因此，界面能看见 Modbus 选项、能填写站号或地址，不等于 Modbus 已经能够通信。

### 2.3 当前工作树保护

编制本文时工作树存在大量未提交修改和新增文件，包含 S7 运行时、设备点位界面、串口下拉框、诊断和测试代码。正式编码必须遵守：

1. 开工前重新执行 `git status --short --branch`；
2. 对本文列出的目标文件逐个执行定向 `git diff`；
3. 不重置、不清理、不覆盖、不顺手格式化其他文件；
4. 每个阶段只修改对应文件；
5. 新增 Modbus 文件优先，修改大型现有文件时使用小范围补丁；
6. 未经授权不提交、不推送、不写真实设备。

## 3. 通信分类与界面约束

### 3.1 通道类型

| `ChannelTransportKind` | 客户显示 | 通道持有的资源 | 允许的首版设备驱动 |
|---|---|---|---|
| `Tcp` | TCP 网络 | 本地网卡提示、通道调度和生命周期 | `siemens-s7`、`modbus-tcp` |
| `Serial` | 串口通信 | COM/tty、波特率、数据位、校验位、停止位、串口独占资源 | `modbus-rtu` |
| `Simulation` | 兼容字段，不在新增向导显示 | 不打开硬件资源 | 不作为设备驱动 |

`DeviceMode.Simulation` 继续表示“该设备不访问硬件”。即使设备处于仿真模式，其驱动与通道仍应保持部署时的正确匹配：

- 仿真的 Modbus RTU 设备仍归属 Serial 通道；
- 仿真的 Modbus TCP 或 S7 设备仍归属 TCP 通道；
- 仿真不是一种设备协议，也不重新加入设备驱动下拉框。

### 3.2 驱动与通道联动

设备向导必须根据所属通道过滤驱动：

| 已选通道 | 驱动下拉框 |
|---|---|
| TCP 网络 | 西门子 S7、Modbus TCP |
| 串口通信 | Modbus RTU |

禁止以下组合进入候选配置：

- Serial + Siemens S7；
- Serial + Modbus TCP；
- TCP + Modbus RTU；
- Simulation Channel + 任意新建设备；
- 未注册驱动 + Hardware。

该约束必须同时存在于界面筛选和核心配置校验，不能只依赖下拉框。

## 4. 本次实施范围

### 4.1 必须完成

1. Modbus TCP 设备端点配置；
2. Modbus RTU 串口共享连接；
3. Modbus TCP 每设备独立连接；
4. 四个标准数据区的读操作；
5. Coil 与 Holding Register 的安全写操作；
6. 标准化地址解析、规范化和校验；
7. 16/32/64 位数值与字节序、字序转换；
8. 连续点位批量读取；
9. 连接状态、缓存、陈旧、重试、降级、恢复和事件日志；
10. 设备编辑器通道/驱动联动和 Modbus 参数界面；
11. 点位编辑器 Modbus 数据区与偏移配置；
12. 连接测试证据分级；
13. 配置原子应用、候选失败和旧运行时回滚；
14. 单元、集成、App、Headless 和离线模拟分层验证；
15. 驱动支持矩阵、第三方声明和操作说明更新。

### 4.2 本次不做

- 不删除或降级现有 S7 功能；
- 不把 `SerialPort`、`TcpClient` 或 NModbus 对象暴露给 App/Core；
- 不允许 ViewModel 直接读写 Modbus；
- 不绕过 `DeviceConfigurationService` 直接写 JSON；
- 不支持广播写；
- 不支持掩码写、文件记录、FIFO、诊断等扩展功能码；
- 不支持对 Input Register 或 Discrete Input 写入；
- 不支持跨点位批量写；
- 不扫描未知地址；
- 不把端口打开成功描述为 Modbus 通信成功；
- 不把本地模拟器测试描述为真实仪表、PLC、USB-RS485 或现场验收。

## 5. 目标架构

```text
DevicePointManagementView / DeviceEditorDialogWindow
                    ↓
DeviceConfigurationService
权限、引用、活动试验锁、驱动校验、Stage、候选运行时、CommitActive、Publish、回滚
                    ↓
DriverRegistry
├─ SiemensS7DriverDescriptor        → TCP
├─ ModbusTcpDriverDescriptor        → TCP
└─ ModbusRtuDriverDescriptor        → Serial
                    ↓
MultiDeviceRuntime
├─ ChannelManager
│  ├─ TCP：按设备串行、设备之间可并行
│  └─ Serial：同一串口全局串行、共享一个 RTU Master
└─ DeviceSession
   ├─ SimulationDeviceRuntime
   ├─ S7NetPlusDeviceRuntime
   ├─ ModbusTcpDeviceRuntime
   └─ ModbusRtuDeviceRuntime
                    ↓
Modbus 客户端适配层
├─ TCP：TcpClient + IModbusMaster，每台设备一份
└─ RTU：SerialPort + SerialPortAdapter + IModbusMaster，每个串口通道一份
```

### 5.1 所有权规则

| 资源 | 所有者 | 原因 |
|---|---|---|
| `TcpClient`、TCP `IModbusMaster` | `ModbusTcpDeviceRuntime` | 每台 TCP 设备有自己的 IP、端口和连接状态 |
| `SerialPort`、RTU `IModbusMaster` | `ChannelManager` 下的 `ModbusRtuChannelConnection` | 一个 RS-485 总线上多个站号必须共享同一个串口 |
| RTU 站号 | `DeviceEntry.ModbusUnitId` | 站号属于总线上的设备，不属于串口本身 |
| TCP IP、端口 | `DeviceEntry.ModbusTcp` | 远端目标属于设备，不属于共享 TCP 通道 |
| 点位数据区、偏移 | `PointsConfig.PointEntry.AddressDefinition` | 地址属于具体设备点位 |
| 重试、降级、缓存 | `DeviceSession` | 继续复用现有统一设备行为 |

严禁每个 RTU 设备各自打开同一个 COM 口。否则同一串口通道下第二台设备必然出现端口占用或报文互相干扰。

## 6. 配置模型

### 6.1 版本策略

编码前先执行一次配置版本门禁，不允许只看到源码常量为 v4 就直接追加字段：

1. 读取当前 `DeviceConfig/PointsConfig/DeviceConfigurationSnapshot` 常量；
2. 扫描 `config` 下示例、活动指针和 Revision 中是否已经存在正式保存的 v4 Modbus 配置；
3. 检查现有 v4 是否已承诺其他字段语义；
4. 用测试固定最终迁移路径后再修改模型。

版本决策固定为：

- 若扫描确认不存在已经正式保存并投入使用的 v4 Modbus 配置，继续使用当前开发中的 `DeviceConfig v4`、`PointsConfig v3`、`Snapshot v2`；
- 若已经存在正式 v4 Modbus 配置，`DeviceConfig` 升为 v5，并新增明确的 v4→v5 迁移；
- 不论是否升版，磁盘上的旧 Revision 都不得原地覆盖，只能 Stage 新 Revision；
- 旧配置中的裸数字地址、传统引用号、字节序和字序不得在迁移时猜测；无法无损确定时生成迁移问题并阻止 Hardware 激活；
- 若正式编码时当前常量已经变化，以当时源码为准向前升级，不允许把版本号改回本文数值。

当前仓库示例仍为旧版本只能说明需要迁移，不能单独证明 v4 可以安全复用。版本门禁结果必须写入对应迁移测试名称和测试说明。

### 6.2 保留现有串口通道字段

`SerialChannelParameters` 保留：

```csharp
public string PortName { get; set; }
public int BaudRate { get; set; }
public int DataBits { get; set; }
public string Parity { get; set; }
public string StopBits { get; set; }
```

不新增“RS-232/RS-485”运行参数。多数普通 COM/USB 转换器不能由 `System.IO.Ports` 软件切换电气接口，增加一个实际不生效的字段会误导客户。接口类型可暂时写在通道名称或说明中，只有以后接入能够控制收发方向或接口模式的专用硬件时再建正式字段。

### 6.3 新增 Modbus TCP 设备端点

在 `DeviceCommunicationOptions.cs` 新增：

```csharp
public sealed class ModbusTcpConnectionOptions
{
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 502;

    public IReadOnlyList<ConfigurationIssue> Validate(string path);
}
```

在 `DeviceConfig.DeviceEntry` 新增：

```csharp
public ModbusTcpConnectionOptions? ModbusTcp { get; set; }
```

现有 `ModbusUnitId` 继续共用于 RTU 和 TCP。首版产品安全范围固定为 `1-247`，不开放 `0` 广播地址。虽然 TCP 的 MBAP Unit Identifier 是一个字节，但本项目不实现广播或未经现场确认的网关特殊值。

### 6.4 RTU 配置示例

```json
{
  "schemaVersion": 4,
  "channels": [
    {
      "id": "10000000-0000-5000-8000-000000000101",
      "code": "CH_RTU_1",
      "name": "称重仪 RS-485 通道",
      "transportKind": "Serial",
      "enabled": true,
      "timeoutMs": 1000,
      "retryCount": 1,
      "serial": {
        "portName": "COM3",
        "baudRate": 19200,
        "dataBits": 8,
        "parity": "None",
        "stopBits": "One"
      }
    }
  ],
  "devices": [
    {
      "id": "20000000-0000-5000-8000-000000000101",
      "code": "DEV_SCALE_1",
      "name": "1号称重仪",
      "deviceMode": "Hardware",
      "channelId": "10000000-0000-5000-8000-000000000101",
      "driverKey": "modbus-rtu",
      "model": "GENERIC_MODBUS_RTU",
      "modbusUnitId": 1,
      "pollIntervalMs": 500,
      "staleAfterMs": 3000,
      "scanMode": "FixedInterval",
      "timing": {
        "connectTimeoutMs": 3000,
        "requestTimeoutMs": 1000,
        "retryCount": 1,
        "interRequestDelayMs": 20
      }
    }
  ]
}
```

### 6.5 Modbus TCP 配置示例

```json
{
  "id": "20000000-0000-5000-8000-000000000201",
  "code": "DEV_MODBUS_TCP_1",
  "name": "温控器",
  "deviceMode": "Hardware",
  "channelId": "10000000-0000-5000-8000-000000000201",
  "driverKey": "modbus-tcp",
  "model": "GENERIC_MODBUS_TCP",
  "modbusUnitId": 1,
  "modbusTcp": {
    "host": "192.168.1.20",
    "port": 502
  },
  "pollIntervalMs": 500,
  "staleAfterMs": 3000,
  "scanMode": "FixedInterval",
  "timing": {
    "connectTimeoutMs": 3000,
    "requestTimeoutMs": 1000,
    "retryCount": 1,
    "interRequestDelayMs": 0
  }
}
```

### 6.6 必须新增的配置校验

1. `modbus-rtu` 只能引用 `Serial` 通道；
2. `modbus-tcp` 只能引用 `Tcp` 通道；
3. `siemens-s7` 只能引用 `Tcp` 通道；
4. Hardware 的 Modbus RTU 必须具有有效串口参数和 `ModbusUnitId`；
5. Hardware 的 Modbus TCP 必须具有 `ModbusTcp.Host/Port` 和 `ModbusUnitId`；
6. 同一 Serial Channel 下 RTU 设备的站号不得重复；
7. 同一 Modbus TCP Host、Port、UnitId 组合不得重复；
8. Modbus 站号必须为 `1-247`；
9. RTU 不得持有 `ModbusTcp` 端点；
10. Modbus TCP 不得误用 `SiemensS7` 端点；
11. S7 不得持有新的 `ModbusTcp` 端点；
12. Simulation 设备仍执行驱动、通道、型号和点位静态校验，但不要求打开硬件；
13. Hardware + 未实现驱动继续失败，不得回退 Simulation；
14. 同一串口的启用通道重复占用校验继续保留；
15. 串口参数转换到 `Parity`、`StopBits` 枚举失败时在应用配置前报中文错误。

## 7. Modbus 点位地址

### 7.1 四个标准数据区

| 数据区 | 稳定值 | 数据宽度 | 访问权限 | 读取功能码 | 写入功能码 |
|---|---|---:|---|---:|---:|
| 线圈 | `Coil` / `C` | 1 bit | 读写 | 01 | 05，首版单点写 |
| 离散输入 | `DiscreteInput` / `DI` | 1 bit | 只读 | 02 | 不允许 |
| 保持寄存器 | `HoldingRegister` / `HR` | 16 bit/寄存器 | 读写 | 03 | 06 或 16 |
| 输入寄存器 | `InputRegister` / `IR` | 16 bit/寄存器 | 只读 | 04 | 不允许 |

协议报文中的地址是从零开始的无符号偏移。`40001` 等写法是设备手册常见引用号，不等于报文中直接发送 `40001`。

### 7.2 项目规范地址

新配置统一保存为：

```text
C:0
DI:0
HR:0
IR:0
```

示例：

| 手册含义 | 界面数据区 | 界面偏移 | JSON `address` |
|---|---|---:|---|
| 00001 线圈 | 线圈 | 0 | `C:0` |
| 10001 离散输入 | 离散输入 | 0 | `DI:0` |
| 30001 输入寄存器 | 输入寄存器 | 0 | `IR:0` |
| 40001 保持寄存器 | 保持寄存器 | 0 | `HR:0` |

允许导入器识别明确的传统引用号并规范化，但必须满足以下条件：

- `00001` → `C:0`；
- `10001` → `DI:0`；
- `30001` → `IR:0`；
- `40001` → `HR:0`；
- 单独的 `0`、`1` 等裸数字不包含数据区，不能自动猜测，必须拒绝并提示选择数据区；
- 六位地址、厂家从 1 开始的偏移或特殊映射只能按设备手册明确转换，不在运行时静默修正。

### 7.3 结构化地址

保存点位时同时维护：

```json
{
  "address": "HR:0",
  "addressDefinition": {
    "area": "HoldingRegister",
    "offset": 0
  }
}
```

`AddressDefinition` 是运行时和唯一性校验的主语义，`address` 是可读的规范文本。两者不一致时配置校验失败，不能任选一个继续运行。

### 7.4 数据类型支持矩阵

首版支持以下类型：

| 数据区 | 允许数据类型 | 占用数量 |
|---|---|---:|
| Coil、DiscreteInput | `Bool` | 1 bit |
| HoldingRegister、InputRegister | `Int16`、`UInt16` | 1 register |
| HoldingRegister、InputRegister | `Int32`、`UInt32`、`Float32` | 2 registers |
| HoldingRegister、InputRegister | `Double` | 4 registers |

布尔类型兼容规则：

- 新建、编辑后保存以及导入的新 Modbus 位点只写入 `Bool`；
- 旧 Revision 中的 `Boolean` 仅在驱动为 `modbus-rtu/modbus-tcp` 且地址区为 Coil/DiscreteInput 时按 `Bool` 兼容读取；
- 兼容加载不原地修改旧 Revision，下一次成功 Stage 时才在新 Revision 中规范化为 `Bool`；
- `Boolean` 或 `Bool` 出现在 Holding/Input Register 时都拒绝；
- 不修改 S7、Simulation 或其他旧配置对 `Boolean` 的既有含义；
- UI 对客户统一显示“布尔型”，不显示两个内部枚举名称。

首版 Modbus 不开放：

- `String`；
- `Char`；
- `Byte`；
- .NET `Decimal`；
- 在 16 位寄存器内任意取 BitIndex；
- BCD、日期时间、厂商自定义编码。

这些类型没有统一的 Modbus 标准寄存器编码，必须在拿到具体设备手册后单独扩展，不能靠通用驱动猜测。

### 7.5 字节序和字序

Modbus 寄存器本身按 16 位无符号值读取。多寄存器值继续使用现有 `DecodeOptions`：

- `ByteOrder.BigEndian`：单个寄存器高字节在前；
- `ByteOrder.LittleEndian`：交换单个寄存器的两个字节；
- `WordOrder.HighWordFirst`：高位寄存器在前；
- `WordOrder.LowWordFirst`：低位寄存器在前。

约束：

1. `Int16/UInt16` 的 `WordOrder` 必须为 `None`；
2. `Int32/UInt32/Float32/Double` 必须明确保存 `HighWordFirst` 或 `LowWordFirst`；
3. 不根据测量值“看起来正常”自动切换字节序；
4. 写入编码必须与读取解码严格对称；
5. 写入后比较继续使用原始值语义，不能用显示值掩盖编码错误。

首版默认和导入规则固定为：

- 新建 Modbus 点位的 `ByteOrder` 初始值为 `BigEndian`，这是项目初始值，不宣称所有设备的多字节映射都相同；
- 16 位类型固定 `WordOrder=None`；
- 32/64 位类型不得自动补齐 `WordOrder`，未选择时配置校验失败；
- 旧 v5 导入模板只允许无损导入 Bool/16 位点；遇到 32/64 位点且没有字序列时整行预览失败，提示改用新版模板或在点位编辑器明确选择；
- 新版模板中的“字节序”为空时可按 `BigEndian` 进入预览，但必须明确显示采用了项目初始值；“字序”对 32/64 位类型为必填；
- Codec 先把每个 `ushort` 按大端写入规范字节缓冲，再执行明确的字节交换和字交换，禁止直接依赖宿主机端序的 `BitConverter.GetBytes(ushort)`。

### 7.6 地址范围和重叠

- 起始偏移范围：`0-65535`；
- 一个点位占用的最后偏移不得超过 `65535`；
- 同一设备、同一数据区内点位寄存器范围不得重叠；
- Coil/DiscreteInput 只按单 bit 地址检查；
- 不跨数据区合并批量读取；
- 不跨站号合并请求；
- 不读取未配置的寄存器间隙，首版只合并连续范围。

## 8. 第三方库选择

### 8.1 选型

目标项目为 `net8.0`。按 2026-09-14 官方包信息，建议固定：

```xml
<PackageReference Include="NModbus" Version="3.0.83" />
<PackageReference Include="NModbus.Serial" Version="3.0.83" />
<PackageReference Include="System.IO.Ports" Version="8.0.0" />
```

说明：

- `NModbus` 用于 Modbus TCP 和公共协议 API；
- `NModbus.Serial` 用于 `SerialPortAdapter` 与 RTU Master；
- `System.IO.Ports 8.0.0` 与当前 App 项目保持一致；
- NModbus 使用 MIT 许可证；
- 不使用已经归档的 `NModbus4`；
- 不接入尚未稳定发布并且 API 路线不同的实验性重写包。

正式编码当天仍应执行 `dotnet list package --outdated` 做只读核对。若版本发生变化，不自动升级，先单独评估 API、许可证、Windows/UOS 行为和回归风险。

### 8.2 第三方声明

更新：

`D:\易峥\2026\2026-09\Avalonia_上位机通用模板\项目代码\XXX.TestBench.Template\THIRD-PARTY-NOTICES.md`

新增 NModbus/NModbus.Serial 的项目地址、版本、MIT 许可证正文和版权信息。不得删除现有 S7.Net Plus 声明。

## 9. 核心合同改造

### 9.1 驱动描述器公开支持的通道类型

修改：

`D:\易峥\2026\2026-09\Avalonia_上位机通用模板\项目代码\XXX.TestBench.Template\src\XXX.TestBench.Core\Ports\IDeviceDriverDescriptor.cs`

新增：

```csharp
IReadOnlySet<ChannelTransportKind> SupportedTransports { get; }
```

行为：

- UI 使用该集合过滤下拉框；
- `ValidateChannel` 仍负责最终核心校验；
- `SiemensS7DriverDescriptor` 返回 `{ Tcp }`；
- `ModbusTcpDriverDescriptor` 返回 `{ Tcp }`；
- `ModbusRtuDriverDescriptor` 返回 `{ Serial }`；
- `SimulationDriverDescriptor` 返回 `{ Simulation }` 以补齐接口，但不得重新出现在硬件设备驱动下拉框。

### 9.2 连接测试结果分层

修改：

`D:\易峥\2026\2026-09\Avalonia_上位机通用模板\项目代码\XXX.TestBench.Template\src\XXX.TestBench.Core\Domain\Devices\DeviceConnectionTestResult.cs`

固定新增：

```csharp
public enum DeviceConnectionTestLevel
{
    TransportOnly = 1,
    ProtocolRead = 2
}

public sealed record DeviceConnectionTestResult(
    bool Ok,
    bool Executed,
    string Endpoint,
    TimeSpan Elapsed,
    string DriverKey,
    DeviceConnectionTestLevel Level,
    string Summary,
    int? NegotiatedPduSize,
    string? Error);
```

`Summary` 只保存可直接显示给客户的中文短信息；`Error` 可保留诊断原因但必须由 Formatter 映射后再显示。S7 的 `NegotiatedPduSize` 保留为可选字段。所有直接构造该结果的源码和测试必须同阶段修改。

证据语义：

- Modbus TCP 设备编辑器测试：只证明 TCP 端口可建立连接；
- Modbus RTU 设备编辑器测试：只证明串口可以按候选参数打开；
- 点位诊断成功：才证明指定站号、功能码、地址和类型得到有效响应；
- 真实写入验收：必须由现场批准的可写点完成，不能由连接测试替代。

## 10. Modbus 驱动描述器

新增：

```text
src/XXX.TestBench.Devices/Drivers/ModbusRtuDriverDescriptor.cs
src/XXX.TestBench.Devices/Drivers/ModbusTcpDriverDescriptor.cs
```

两者共同复用：

```text
src/XXX.TestBench.Devices/Modbus/ModbusAddressParser.cs
src/XXX.TestBench.Devices/Modbus/ModbusTypeCapabilities.cs
```

### 10.1 `ModbusRtuDriverDescriptor`

- `DriverKey = DriverKeyCatalog.ModbusRtu`；
- `DisplayName = "Modbus RTU"`；
- `IsImplemented = true`，只能在运行时、自动化和错误处理全部接入后改为 true；
- `SupportedTransports = { Serial }`；
- 型号保留 `GENERIC_MODBUS_RTU / 通用 Modbus RTU 设备`；
- `ValidateChannel` 校验 Serial 和完整串口参数；
- `ValidateDevice` 校验站号 `1-247`；
- `ValidatePoint` 校验数据区、偏移、类型、访问权限、字节序/字序；
- `NormalizeAddress` 返回 `C:n/DI:n/HR:n/IR:n`。

### 10.2 `ModbusTcpDriverDescriptor`

- `DriverKey = DriverKeyCatalog.ModbusTcp`；
- `DisplayName = "Modbus TCP"`；
- `IsImplemented = true` 的门槛与 RTU 相同；
- `SupportedTransports = { Tcp }`；
- 型号保留 `GENERIC_MODBUS_TCP / 通用 Modbus TCP 设备`；
- `ValidateDevice` 额外校验 `ModbusTcp.Host/Port`；
- 点位规则与 RTU 共享。

### 10.3 注册表

修改：

`D:\易峥\2026\2026-09\Avalonia_上位机通用模板\项目代码\XXX.TestBench.Template\src\XXX.TestBench.Devices\Drivers\DriverRegistry.cs`

删除两个 Modbus `UnsupportedDriverDescriptor` 注册，替换为真实描述器。保留对未知驱动的明确失败，不引入反射扫描或动态插件。

## 11. Modbus 公共适配层

新增目录：

`D:\易峥\2026\2026-09\Avalonia_上位机通用模板\项目代码\XXX.TestBench.Template\src\XXX.TestBench.Devices\Modbus`

固定文件：

| 文件 | 责任 |
|---|---|
| `ModbusArea.cs` | 四个标准数据区枚举和读写能力 |
| `ModbusAddress.cs` | 已解析地址、占用长度和规范地址 |
| `ModbusAddressParser.cs` | 解析结构化地址与明确传统引用号 |
| `ModbusTypeCapabilities.cs` | 数据区与数据类型兼容矩阵、寄存器宽度 |
| `ModbusValueCodec.cs` | `ushort[]/bool[]` 与领域值互转 |
| `ModbusReadBatchPlanner.cs` | 连续点位分组与批量读取计划 |
| `IModbusClient.cs` | 对 NModbus 的最小可测试抽象 |
| `NModbusClientAdapter.cs` | 唯一调用 NModbus API 的适配器 |
| `IModbusClientFactory.cs` | 创建 TCP 或 RTU 客户端的测试边界 |
| `NModbusClientFactory.cs` | 创建 `TcpClient`、`SerialPortAdapter` 和 Master |
| `ModbusRtuChannelConnection.cs` | 一个串口通道的一份共享 RTU 连接 |

### 11.1 最小客户端接口

接口只覆盖首版需要的功能：

```csharp
internal interface IModbusClient : IAsyncDisposable
{
    Task<bool[]> ReadCoilsAsync(
        byte unitId, ushort start, ushort count, TimeSpan requestTimeout, CancellationToken ct);
    Task<bool[]> ReadDiscreteInputsAsync(
        byte unitId, ushort start, ushort count, TimeSpan requestTimeout, CancellationToken ct);
    Task<ushort[]> ReadHoldingRegistersAsync(
        byte unitId, ushort start, ushort count, TimeSpan requestTimeout, CancellationToken ct);
    Task<ushort[]> ReadInputRegistersAsync(
        byte unitId, ushort start, ushort count, TimeSpan requestTimeout, CancellationToken ct);
    Task WriteSingleCoilAsync(
        byte unitId, ushort address, bool value, TimeSpan requestTimeout, CancellationToken ct);
    Task WriteSingleRegisterAsync(
        byte unitId, ushort address, ushort value, TimeSpan requestTimeout, CancellationToken ct);
    Task WriteMultipleRegistersAsync(
        byte unitId, ushort address, ushort[] values, TimeSpan requestTimeout, CancellationToken ct);
}
```

所有方法都必须显式传入本次请求的有效超时和取消令牌，不提供无超时重载。不要把第三方 `IModbusMaster` 放进 Core、App、ViewModel 或配置模型。

### 11.2 超时和取消

NModbus 调用不接受项目的 `CancellationToken` 时，适配层必须：

1. 使用底层 Socket/SerialPort 的读写超时；
2. 使用接口传入的 `requestTimeout` 在项目侧用 `WaitAsync` 绑定取消与总请求超时；
3. 捕获项目取消和请求超时并区分：用户取消继续抛 `OperationCanceledException`，请求超时映射为项目可识别的超时异常；
4. 一旦超时或取消，在释放通道串行门之前把当前连接原子标记为不可复用并关闭；
5. 关闭操作本身必须幂等，且不能因二次异常覆盖原始取消/超时原因；
6. TCP 下次请求创建新连接；
7. RTU 清理共享串口并在下次请求重新打开；
8. 不允许一个已超时的后台 I/O 继续占用连接并与下一帧并发；
9. 适配层若无法确认底层 I/O 已终止，就不能返回一份仍可复用的客户端；
10. 不使用无限等待；
11. 不使用 `Thread.Abort`；
12. 不在 UI 线程执行同步串口 I/O。

读取请求可由 `DeviceSession` 按配置重试；写请求继续禁止自动重发。写请求发生取消、超时或连接中断时，结果必须标记为“不确定”，由现有写入安全链决定是否允许人工确认后读取，不得自动补写。

### 11.3 重试只有一层

当前 `DeviceSession.ExecuteWithRetryAsync` 已经负责设备级重试。NModbus Transport 的内部重试设置为 0，避免“外层 3 次 × 内层 3 次”形成不可控延迟。

实际请求时序以 `DeviceEntry.Timing` 为运行时来源：

- `ConnectTimeoutMs`：TCP 建连或串口打开总时间；
- `RequestTimeoutMs`：一次 Modbus 请求的最大时间；
- `RetryCount`：由 `DeviceSession` 执行；
- `InterRequestDelayMs`：重试或串口连续请求间隔。

现有 `ChannelEntry.TimeoutMs/RetryCount` 在本轮不再额外叠加到 NModbus 内部。设备编辑器中误导为“全部由通道统一维护”的文案必须同步修正，避免客户认为两套数值都会生效。

## 12. RTU 通道运行时

### 12.1 共享连接

修改：

`D:\易峥\2026\2026-09\Avalonia_上位机通用模板\项目代码\XXX.TestBench.Template\src\XXX.TestBench.Devices\Runtime\ChannelManager.cs`

新增 `ModbusRtuChannelConnection` 所有权：

- 每个 `Serial` 通道最多一份；
- 候选 Hardware RTU 运行时启动时由同一通道的第一个设备打开，后续设备复用；
- 启动只验证串口参数和打开共享串口，不发送 Modbus 请求；
- 多个 `DeviceSession` 按 `ModbusUnitId` 使用同一 Master；
- 同一串口所有请求继续使用 `ChannelSession._gate` 串行；
- `DrainAndStopAsync` 等待在途请求后关闭 SerialPort；
- Stop→Start 后允许重新打开；
- Dispose 幂等；
- 端口打开失败不影响其他 TCP 通道；
- 连接代次每次重新打开后递增；
- 串口异常后丢弃残留输入缓冲，不把旧响应交给新请求。

### 12.2 生命周期顺序

```text
MultiDeviceRuntime.StartAsync
→ ChannelManager.StartAsync
→ DeviceSession.StartAsync
→ ModbusRtuDeviceRuntime.StartAsync
→ ModbusRtuChannelConnection.EnsureOpenAsync
→ 串口打开成功：候选激活成功，设备状态为 Connecting/“待点位验证”
→ 不发送没有明确点位的协议探测

读取/写入
→ DeviceSession 重试与降级
→ ChannelSession 全局串行门
→ ModbusRtuChannelConnection.EnsureOpenAsync（异常恢复时允许重开）
→ NModbus 请求
→ 首个有效响应后 DeviceSession 转为 Online

MultiDeviceRuntime.StopAsync
→ 停止各 DeviceSession 的轮询
→ 等待在途请求结束
→ ChannelManager.DrainAndStopAsync
→ 关闭 RTU Master、Adapter、SerialPort
```

同一串口通道包含多个 RTU 设备时，`EnsureOpenAsync` 必须是并发幂等的：只产生一个打开任务和一个连接代次。若串口打开失败，该通道下所有 Hardware RTU 设备均在 `RuntimeActivationReport` 中记录各自的激活问题；不影响其他 TCP 或其他串口通道。

`RuntimeActivationReport.OperationalDevices` 在本阶段表示“设备运行时已创建且所需传输层资源已建立”，不是“已经收到该站号的 Modbus 响应”。客户可见在线状态仍以 `DeviceConnectionState` 和第一次有效响应为准。

### 12.3 串口默认值

UI 可保持当前默认 `DataBits=8`、`Parity=None`、`StopBits=One`，但不得写成 Modbus RTU 标准唯一值。最终以设备手册为准。

当 `Parity=None` 时是否要求两个停止位不能由软件擅自改写；可以在界面给出兼容性提示，但保存和运行必须使用客户实际选择值。

## 13. Modbus TCP 运行时

新增：

`D:\易峥\2026\2026-09\Avalonia_上位机通用模板\项目代码\XXX.TestBench.Template\src\XXX.TestBench.Devices\Runtime\ModbusTcpDeviceRuntime.cs`

职责：

- 每台设备持有自己的 `TcpClient` 和 `IModbusClient`；
- 从 `DeviceEntry.ModbusTcp` 读取 Host/Port，默认端口 502；
- 从 `DeviceEntry.ModbusUnitId` 读取 Unit ID；
- Start 使用连接超时，不读取任意寄存器；
- 单设备请求由当前 `ChannelManager` 的 device gate 串行；
- 不同 TCP 设备允许并行；
- Socket 失败只重置该设备连接；
- 重连递增 ConnectionGeneration；
- Stop/Dispose 释放 Master 和 TcpClient；
- 不把 TCP 建连成功当作寄存器读取成功。

`ModbusTcpDeviceRuntime.StartAsync` 必须在 `ConnectTimeoutMs` 内建立 TCP Socket，成功后候选激活通过，但 `DeviceSession` 仍保持 `Connecting/待点位验证`；只有带明确 UnitId、功能码和地址的首次有效 Modbus 响应才能转为 `Online`。TCP Connect 成功不得记录为“Modbus 通信成功”。

## 14. 两种 Modbus 设备运行时的共同逻辑

新增：

```text
src/XXX.TestBench.Devices/Runtime/ModbusRtuDeviceRuntime.cs
src/XXX.TestBench.Devices/Runtime/ModbusTcpDeviceRuntime.cs
```

共同复用地址、类型、Codec 和批量读取计划，不复制两套协议语义。

必须实现 `IDeviceRuntime`：

- `StartAsync`；
- `StopAsync`；
- `ReadAsync`；
- `ReadFreshAsync`；
- `ReadManyFreshAsync`；
- `WriteAsync`；
- `ListPointsAsync`；
- `TryGetCachedValue`；
- `ListCachedValues`；
- `Status`；
- `ActiveRevision`；
- `DisposeAsync`。

### 14.1 读取

映射：

| Area | NModbus 调用 |
|---|---|
| Coil | `ReadCoilsAsync` |
| DiscreteInput | `ReadInputsAsync` / 适配层命名为 `ReadDiscreteInputsAsync` |
| HoldingRegister | `ReadHoldingRegistersAsync` |
| InputRegister | `ReadInputRegistersAsync` |

成功后生成 `PointValue`：

- `Value`：工程换算后的值；
- `RawValue`：Codec 解码后的原始类型值；
- `Quality=Good`；
- `TimestampUtc` 使用注入的 `IClock`；
- `Revision` 与当前运行时一致；
- 更新设备缓存和成功时间。

异常、超时和取消不能更新为 Good，也不能用旧缓存冒充新鲜读取。

### 14.2 批量读取

批量计划按以下键分组：

```text
DeviceId + UnitId + Area + 连续地址范围
```

首版限制：

- Coil/DiscreteInput 每次最多 2000 点；
- Holding/Input Register 每次最多 125 个寄存器；
- 只合并连续点位，不跨未配置空洞；
- 多寄存器类型按实际占用长度计算；
- 读取结果必须按原请求 PointId 顺序返回；
- 一个批次失败时，该批次点位均返回失败，不用缓存伪装成功；
- 其他独立批次可以继续执行并保留自己的结果；
- `ReadManyFreshAsync` 不得退化成每点一帧。

### 14.3 写入

| 点位 | 写入方式 |
|---|---|
| Coil + Bool | FC05 单线圈写入 |
| HoldingRegister + 16 位 | FC06 单寄存器写入 |
| HoldingRegister + 32/64 位 | FC16 多寄存器写入 |
| DiscreteInput | 拒绝 |
| InputRegister | 拒绝 |

所有写入继续从 `DeviceWritePipeline` 进入：

```text
权限
→ 活动运行状态
→ 点位存在与 Revision
→ IsWritable
→ 风险确认
→ 工程值转原始值
→ Modbus 编码与写入
→ 同 PointId 新鲜读取
→ RawValue 等值比较
→ 审计和结果
```

禁止：

- UnitId 0 广播写；
- 对输入区写入；
- 先读寄存器再改某一 bit 的通用 RMW；
- 写失败后自动重试高风险动作而不经过现有写入策略；
- 以“请求未抛异常”代替 ReadBackEqual。

## 15. `DeviceSession` 和多设备组合

修改：

```text
D:\易峥\2026\2026-09\Avalonia_上位机通用模板\项目代码\XXX.TestBench.Template\src\XXX.TestBench.Devices\Runtime\DeviceSession.cs
D:\易峥\2026\2026-09\Avalonia_上位机通用模板\项目代码\XXX.TestBench.Template\src\XXX.TestBench.Devices\Runtime\MultiDeviceRuntime.cs
D:\易峥\2026\2026-09\Avalonia_上位机通用模板\项目代码\XXX.TestBench.Template\src\XXX.TestBench.Devices\DeviceRuntimeFactory.cs
```

`DeviceSession.CreateRuntime()` 调整为：

```csharp
if (Mode == DeviceMode.Simulation)
    return new SimulationDeviceRuntime(...);

return _configuration.DriverKey switch
{
    DriverKeyCatalog.SiemensS7 => new S7NetPlusDeviceRuntime(...),
    DriverKeyCatalog.ModbusTcp => new ModbusTcpDeviceRuntime(...),
    DriverKeyCatalog.ModbusRtu => new ModbusRtuDeviceRuntime(...),
    _ => throw new DomainException(...)
};
```

实际编码使用大小写不敏感比较，不直接照抄上面 switch。

当前 `DeviceSession` 中包含 `S7_DEVICE_DEMOTED`、`S7_RECOVERY_ATTEMPT`、`S7_RECOVERED` 等通用会话事件码。接入 Modbus 前改为：

- `DEVICE_DEMOTED`；
- `DEVICE_RECOVERY_ATTEMPT`；
- `DEVICE_RECOVERED`。

驱动自己的事件继续使用：

- `MODBUS_TCP_CONNECT_FAILED`；
- `MODBUS_RTU_PORT_OPEN_FAILED`；
- `MODBUS_REQUEST_TIMEOUT`；
- `MODBUS_EXCEPTION_RESPONSE`；
- `MODBUS_CONNECTION_RESET`。

客户界面显示中文短信息，异常类型、功能码、站号和协议细节写入事件日志，不直接弹出英文堆栈。

### 15.1 会话状态与首次协议响应

现有 `DeviceSession.StartAsync` 在任何子运行时启动返回后直接设置 `Online`，且轮询只在 `IsOperational` 时执行。编码时必须同步调整，不能只在 Modbus 子运行时内部改变状态：

1. Simulation 与已完成协议连接的 S7 保留现有启动后 `Online` 行为；
2. Hardware Modbus 传输层启动成功后，会话状态设为 `Connecting`，随后启动轮询；
3. 新增内部 `CanPoll` 判定，允许已启动的 `Connecting/Online/Degraded/Offline` 会话轮询；Stop 后轮询任务已取消，不能仅靠状态阻止轮询；
4. `ReadFreshAsync/ReadManyFreshAsync` 使用独立的 `EnsureReadable`，允许 `Connecting/Online/Degraded/Offline` 发起明确点位读取；
5. `WriteAsync` 继续使用严格的 `EnsureWritable`，只允许 `Online/Degraded`，并继续执行降级期禁止写入规则；
6. 第一次读取成功时，`RegisterSuccess` 必须把 `Connecting/Offline/Degraded` 转为 `Online`；
7. 首次协议读取失败时状态转为 `Offline` 并保留错误，轮询仍可按现有时序继续恢复；已经在线后的失败继续沿用现有降级和自动恢复策略；
8. 客户界面把 `Connecting` 显示为“待点位验证”，把 `Offline` 显示为“通信未建立”，不能显示“在线”；
9. 零点位 Hardware Modbus 可以完成传输层激活，但保持“待点位验证”，直到配置并成功读取一个点位；
10. `SessionStartResult.Success=true` 表示候选会话可启动，不等于协议在线；协议在线以 `DeviceConnectionState.Online` 为准。

上述改造不得放宽现有写入入口、Revision 校验、连接代次、缓存新鲜度或 Hardware 失败不得回退 Simulation 的规则。

## 16. 设备连接测试

### 16.1 组合测试器

新增：

```text
src/XXX.TestBench.Devices/Runtime/CompositeDeviceConnectionTester.cs
src/XXX.TestBench.Devices/Runtime/ModbusTcpConnectionTester.cs
src/XXX.TestBench.Devices/Runtime/ModbusRtuConnectionTester.cs
```

保留现有 `S7DeviceConnectionTester`，由组合测试器根据 DriverKey 分发。`AppComposition` 只注入组合测试器，不在 ViewModel 内判断具体协议。

活动串口占用查询使用以下最小实现，不改变现有 `IDeviceConnectionTester.TestAsync(device, channel, ct)` 公共签名：

1. `MultiDeviceRuntime` 新增只读 `IsSerialPortLeased(string portName)`；
2. 该方法委托 `ChannelManager` 查询已启动 Serial Channel 的规范化 `PortName` 和共享连接占用状态，不返回 `SerialPort`、Master 或可操作句柄；
3. `ModbusRtuConnectionTester` 构造函数接收 `Func<string, bool> isActiveSerialPortLeased`，便于单元测试注入；
4. `AppComposition` 使用 `DeviceModes.Runtime` 的当前发布运行时构造该委托；不是 `MultiDeviceRuntime` 或没有活动运行时时返回 false；
5. 测试前先查询占用，已占用时直接返回 `TransportOnly` 的未执行结果和第 16.3 节固定文案，不尝试打开第二个串口；
6. 查询结果与真正打开之间仍可能发生竞争，因此临时打开失败继续按“串口当前不可用或被占用”处理，但不得关闭活动运行时。

端口已被活动运行时占用时固定使用 `Ok=false, Executed=false, Level=TransportOnly`，避免 UI 把跳过测试显示为通信失败或成功。现有 S7 测试器与 Formatter 测试必须同步适配第 9.2 节结果合同。

### 16.2 Modbus TCP 测试

设备编辑器没有可靠的通用寄存器地址，因此首层测试只执行：

- Host 解析；
- TCP Connect；
- 连接超时；
- 立即安全关闭。

成功文案必须是：

> TCP 端口可连接；尚未读取 Modbus 点位，请保存配置后在点位诊断中测试读取。

不能显示“Modbus 通信成功”。

### 16.3 Modbus RTU 测试

设备编辑器没有标准 Modbus Ping。首层测试只执行：

- 串口参数转换；
- 端口占用检查；
- 打开 SerialPort；
- 立即安全关闭。

成功文案必须是：

> 串口可以打开；尚未验证站号和 Modbus 响应，请保存配置后在点位诊断中测试读取。

如果当前活动运行时已经占用该串口：

- 不强制关闭活动运行时；
- 不创建第二个串口连接；
- 提示“当前串口由生效配置占用，请在点位诊断中测试现有设备，或先安全停止运行后再测试候选参数”。

### 16.4 协议级测试

协议级测试复用 `DevicePointDiagnosticsViewModel`：

- 使用当前运行时的 `ReadFreshAsync`；
- 使用已保存的只读点位；
- 显示值、原始值、质量、时间戳、连接代次；
- Revision 或连接代次变化时丢弃旧结果；
- 不创建旁路 TCP 或串口连接。

## 17. 界面改造

### 17.1 通道向导

目标文件：

```text
D:\易峥\2026\2026-09\Avalonia_上位机通用模板\项目代码\XXX.TestBench.Template\src\XXX.TestBench.App\ViewModels\DeviceConfigurationViewModels.cs
D:\易峥\2026\2026-09\Avalonia_上位机通用模板\项目代码\XXX.TestBench.Template\src\XXX.TestBench.App\Views\ChannelEditorDialogWindow.axaml
```

保留现有两个客户选项：

- TCP 网络；
- 串口通信。

不在通道向导中加入“Modbus RTU/Modbus TCP”选项，因为协议属于设备驱动。

只需要调整说明文字：

- TCP：可挂接西门子 S7 或 Modbus TCP 设备；远端地址在设备属性中配置；
- 串口：首版用于 Modbus RTU；一个串口通道可以挂接多个不同站号设备；
- 串口参数必须与同一总线全部设备一致。

### 17.2 设备向导

目标文件：

```text
D:\易峥\2026\2026-09\Avalonia_上位机通用模板\项目代码\XXX.TestBench.Template\src\XXX.TestBench.App\ViewModels\DeviceConfigurationViewModels.cs
D:\易峥\2026\2026-09\Avalonia_上位机通用模板\项目代码\XXX.TestBench.Template\src\XXX.TestBench.App\Views\DeviceEditorDialogWindow.axaml
```

改造要求：

1. 先选择所属通道，再显示兼容驱动；
2. 从树节点进入新增设备时，继承通道并直接过滤驱动；
3. 切换通道导致当前驱动不兼容时，清空驱动并给出中文提示，不静默替换；
4. TCP + S7 显示 PLC IP、端口、Rack、Slot；
5. TCP + Modbus TCP 显示设备 IP、端口、站号；
6. Serial + Modbus RTU 显示只读的串口摘要和可编辑站号；
7. 所有 Hardware 驱动显示扫描模式、连接/请求超时、重试、请求间隔和降级保护；
8. Simulation 模式隐藏真实连接测试，但仍显示驱动与通道摘要；
9. Modbus TCP 默认端口 502；
10. 站号使用受限数字输入，范围 `1-247`；
11. 确认页显示“TCP 网络 / Modbus TCP”或“串口 / Modbus RTU”，不混成一个字段；
12. 驱动 `IsImplemented=false` 时 Hardware 继续禁止保存。

### 17.3 点位编辑器

目标文件：

```text
D:\易峥\2026\2026-09\Avalonia_上位机通用模板\项目代码\XXX.TestBench.Template\src\XXX.TestBench.App\ViewModels\DevicePointDialogViewModel.cs
D:\易峥\2026\2026-09\Avalonia_上位机通用模板\项目代码\XXX.TestBench.Template\src\XXX.TestBench.App\Views\DevicePointDialogWindow.axaml
```

当设备驱动是 Modbus 时显示：

- 数据区下拉框：线圈、离散输入、保持寄存器、输入寄存器；
- 协议偏移：`0-65535`；
- 原始数据类型：按数据区动态过滤；
- 字节序；
- 32/64 位类型的字序；
- 访问权限：输入区强制只读，Holding/Coil 才允许选择读写；
- 规范地址只读预览，例如 `HR:0`。

保存时生成 `Address` 和 `AddressDefinition`。编辑旧点位时先解析已有明确地址；遇到裸数字必须提示客户补充数据区，不猜测。

### 17.4 导入与导出

模板从当前 v5 升级为 v6，保留现有列顺序并在“数据类型”后新增“字节序”“字序”两列；“地址”列继续要求填写规范地址：

```text
C:0
DI:0
HR:0
IR:0
```

更新：

```text
docs/设备点位管理与导入说明.md
src/XXX.TestBench.Core/Configuration/DevicePointTemplateDefinition.cs
src/XXX.TestBench.Infrastructure/Configuration/DevicePointImporter.cs
src/XXX.TestBench.Infrastructure/Configuration/DevicePointTemplateExporter.cs
```

新增列合同：

| 列 | 允许值 | 必填规则 |
|---|---|---|
| 字节序 | 大端、小端 | 可空；空值按项目初始值“大端”进入预览，并明确标记“采用初始值” |
| 字序 | 高字在前、低字在前 | 32/64 位必填；Bool/16 位必须为空或“无” |

模板版本写入和导入校验必须共同使用 `DevicePointTemplateDefinition.CurrentTemplateVersion = 6`，下载模板、填写说明、示例页、CSV 表头和导入器同步更新，不能只改 Excel 导出。

导入预览必须显示：

- 原始地址；
- 规范地址；
- 数据区；
- 偏移；
- 数据类型；
- 是否可写；
- 字节序及其来源（显式填写/项目初始值）；
- 字序及其来源；
- 字节序/字序冲突或缺失；
- 地址范围重叠。

兼容旧 v5 模板时：

- Bool、`Int16`、`UInt16` 可按 `ByteOrder=BigEndian, WordOrder=None` 进入预览，并明确显示兼容来源；
- `Int32`、`UInt32`、`Float32`、`Double` 因没有字序列必须逐行报错，不能自动补 `HighWordFirst`；
- 不根据数值、地址或设备名称猜测字节序/字序；
- 不把字节序、字序偷偷塞入说明字段；
- 预览仍有错误时禁止应用，不允许只导入“看起来正常”的多寄存器点位。

## 18. 原子配置应用

Modbus 必须继续使用现有流程：

```text
编辑候选对象
→ ConfigurationValidator / DriverDescriptor
→ DeviceConfigurationService 权限与活动试验检查
→ StageAsync 新 Revision
→ 创建候选 MultiDeviceRuntime
→ StartAsync 获取 RuntimeActivationReport
→ 必要时明确确认离线设备
→ CommitActiveAsync
→ PublishRuntime
→ 释放旧运行时
```

失败规则：

- `StartAsync` 必须实际建立 Hardware Modbus 的传输层资源，不能以“稍后首次请求再打开”绕过候选激活；
- RTU 串口打不开：该串口通道下的候选 Hardware RTU 设备激活失败；
- TCP 端口不可达：对应候选 Hardware Modbus TCP 设备激活失败；
- 串口/TCP 打开成功但尚未读取点位：候选可以激活，但设备状态保持 `Connecting/待点位验证`，不能计为协议在线；
- 第一次有效 Modbus 响应后才转 `Online`；首次协议读取失败转 `Offline` 并进入正常恢复路径；
- 全部硬件设备失败：不能静默提交为在线；
- 允许离线应用必须沿用已有显式确认机制；
- 候选失败时关闭它打开的所有 Socket/SerialPort；
- active 指针未切换时旧 Revision 保持不变；
- active 已切换但发布失败时恢复旧 Revision；
- 旧运行时重启失败时进入明确 Faulted；
- 任何硬件失败都不得自动切换 Simulation。

## 19. 文件级改造清单

### 19.1 修改文件

| 绝对路径 | 修改内容 |
|---|---|
| `D:\易峥\2026\2026-09\Avalonia_上位机通用模板\项目代码\XXX.TestBench.Template\src\XXX.TestBench.Core\Configuration\DeviceCommunicationOptions.cs` | 新增 `ModbusTcpConnectionOptions` |
| `D:\易峥\2026\2026-09\Avalonia_上位机通用模板\项目代码\XXX.TestBench.Template\src\XXX.TestBench.Core\Configuration\DeviceConfig.cs` | 新端点字段、站号与唯一性校验 |
| `D:\易峥\2026\2026-09\Avalonia_上位机通用模板\项目代码\XXX.TestBench.Template\src\XXX.TestBench.Core\Configuration\DeviceConfigurationMigrator.cs` | 克隆/迁移 Modbus TCP 端点，不猜裸地址 |
| `D:\易峥\2026\2026-09\Avalonia_上位机通用模板\项目代码\XXX.TestBench.Template\src\XXX.TestBench.Core\Configuration\MultiDeviceConfigurationValidator.cs` | 通道兼容、点位协议、地址范围重叠校验 |
| `D:\易峥\2026\2026-09\Avalonia_上位机通用模板\项目代码\XXX.TestBench.Template\src\XXX.TestBench.Core\Configuration\DevicePointTemplateDefinition.cs` | 模板升 v6，增加字节序和字序列及固定选项 |
| `D:\易峥\2026\2026-09\Avalonia_上位机通用模板\项目代码\XXX.TestBench.Template\src\XXX.TestBench.Core\Ports\IDeviceDriverDescriptor.cs` | 增加 `SupportedTransports` |
| `D:\易峥\2026\2026-09\Avalonia_上位机通用模板\项目代码\XXX.TestBench.Template\src\XXX.TestBench.Core\Domain\Devices\DeviceConnectionTestResult.cs` | 增加驱动、证据层级和 `Executed` |
| `D:\易峥\2026\2026-09\Avalonia_上位机通用模板\项目代码\XXX.TestBench.Template\src\XXX.TestBench.Devices\XXX.TestBench.Devices.csproj` | 引入 NModbus/NModbus.Serial/System.IO.Ports |
| `D:\易峥\2026\2026-09\Avalonia_上位机通用模板\项目代码\XXX.TestBench.Template\src\XXX.TestBench.Devices\Drivers\DriverRegistry.cs` | 用真实 Modbus 描述器替换占位描述器 |
| `D:\易峥\2026\2026-09\Avalonia_上位机通用模板\项目代码\XXX.TestBench.Template\src\XXX.TestBench.Devices\Drivers\SiemensS7DriverDescriptor.cs` | 声明只支持 TCP，不改 S7 协议行为 |
| `D:\易峥\2026\2026-09\Avalonia_上位机通用模板\项目代码\XXX.TestBench.Template\src\XXX.TestBench.Devices\Drivers\SimulationDriverDescriptor.cs` | 补齐接口实现，声明 Simulation；不重新放回硬件驱动下拉框 |
| `D:\易峥\2026\2026-09\Avalonia_上位机通用模板\项目代码\XXX.TestBench.Template\src\XXX.TestBench.Devices\Runtime\ChannelManager.cs` | 持有共享 RTU 连接并保持可重启生命周期 |
| `D:\易峥\2026\2026-09\Avalonia_上位机通用模板\项目代码\XXX.TestBench.Template\src\XXX.TestBench.Devices\Runtime\DeviceSession.cs` | 创建两种 Modbus 运行时、区分可读/可写/可轮询状态、首次响应转在线、通用化事件码 |
| `D:\易峥\2026\2026-09\Avalonia_上位机通用模板\项目代码\XXX.TestBench.Template\src\XXX.TestBench.Devices\Runtime\MultiDeviceRuntime.cs` | 组合 Modbus 工厂和共享通道资源，提供只读串口占用查询 |
| `D:\易峥\2026\2026-09\Avalonia_上位机通用模板\项目代码\XXX.TestBench.Template\src\XXX.TestBench.Devices\Runtime\S7DeviceConnectionTester.cs` | 适配新的连接测试结果字段，不改变 S7 连接语义 |
| `D:\易峥\2026\2026-09\Avalonia_上位机通用模板\项目代码\XXX.TestBench.Template\src\XXX.TestBench.Devices\DeviceRuntimeFactory.cs` | 注入 Modbus 客户端工厂 |
| `D:\易峥\2026\2026-09\Avalonia_上位机通用模板\项目代码\XXX.TestBench.Template\src\XXX.TestBench.Infrastructure\Configuration\DevicePointImporter.cs` | 读取 v6 字节序/字序，兼容 v5 的无损类型并拒绝缺字序的多寄存器点 |
| `D:\易峥\2026\2026-09\Avalonia_上位机通用模板\项目代码\XXX.TestBench.Template\src\XXX.TestBench.Infrastructure\Configuration\DevicePointTemplateExporter.cs` | 导出 v6 表头、说明、选项和示例 |
| `D:\易峥\2026\2026-09\Avalonia_上位机通用模板\项目代码\XXX.TestBench.Template\src\XXX.TestBench.App\Composition\AppComposition.cs` | 注册描述器、工厂和组合连接测试器 |
| `D:\易峥\2026\2026-09\Avalonia_上位机通用模板\项目代码\XXX.TestBench.Template\src\XXX.TestBench.App\ViewModels\DeviceConfigurationViewModels.cs` | 通道过滤驱动、Modbus TCP 端点、公共硬件参数 |
| `D:\易峥\2026\2026-09\Avalonia_上位机通用模板\项目代码\XXX.TestBench.Template\src\XXX.TestBench.App\ViewModels\DeviceConnectionTestResultFormatter.cs` | 按证据层级输出中文，不写成 S7 专用提示 |
| `D:\易峥\2026\2026-09\Avalonia_上位机通用模板\项目代码\XXX.TestBench.Template\src\XXX.TestBench.App\ViewModels\DevicePointDialogViewModel.cs` | Modbus 数据区、偏移、类型和字序联动 |
| `D:\易峥\2026\2026-09\Avalonia_上位机通用模板\项目代码\XXX.TestBench.Template\src\XXX.TestBench.App\Views\ChannelEditorDialogWindow.axaml` | 更新通道说明文字 |
| `D:\易峥\2026\2026-09\Avalonia_上位机通用模板\项目代码\XXX.TestBench.Template\src\XXX.TestBench.App\Views\DeviceEditorDialogWindow.axaml` | Modbus TCP/RTU 参数区与公共硬件参数区 |
| `D:\易峥\2026\2026-09\Avalonia_上位机通用模板\项目代码\XXX.TestBench.Template\src\XXX.TestBench.App\Views\DevicePointDialogWindow.axaml` | 结构化 Modbus 地址编辑 |
| `D:\易峥\2026\2026-09\Avalonia_上位机通用模板\项目代码\XXX.TestBench.Template\THIRD-PARTY-NOTICES.md` | 增加 NModbus MIT 声明 |
| `D:\易峥\2026\2026-09\Avalonia_上位机通用模板\项目代码\XXX.TestBench.Template\docs\设备驱动支持矩阵.md` | 更新自动化与实机证据，不能直接写现场通过 |
| `D:\易峥\2026\2026-09\Avalonia_上位机通用模板\项目代码\XXX.TestBench.Template\docs\设备点位管理与导入说明.md` | 增加规范地址和 Modbus 操作说明 |

接口编译影响清单必须作为 P0 首轮编译的一部分同步修改：

- `SiemensS7DriverDescriptor`；
- `SimulationDriverDescriptor`；
- 编码过程中尚未删除的 `UnsupportedDriverDescriptor`；
- `tests/XXX.TestBench.App.Tests/DeviceConfigurationViewModelTests.cs` 内的 `UnsupportedDescriptor` 测试桩；
- 所有直接构造 `DeviceConnectionTestResult` 的 S7 测试与 Formatter 测试。

### 19.2 新增文件

```text
src/XXX.TestBench.Devices/Drivers/ModbusRtuDriverDescriptor.cs
src/XXX.TestBench.Devices/Drivers/ModbusTcpDriverDescriptor.cs
src/XXX.TestBench.Devices/Modbus/ModbusArea.cs
src/XXX.TestBench.Devices/Modbus/ModbusAddress.cs
src/XXX.TestBench.Devices/Modbus/ModbusAddressParser.cs
src/XXX.TestBench.Devices/Modbus/ModbusTypeCapabilities.cs
src/XXX.TestBench.Devices/Modbus/ModbusValueCodec.cs
src/XXX.TestBench.Devices/Modbus/ModbusReadBatchPlanner.cs
src/XXX.TestBench.Devices/Modbus/IModbusClient.cs
src/XXX.TestBench.Devices/Modbus/IModbusClientFactory.cs
src/XXX.TestBench.Devices/Modbus/NModbusClientAdapter.cs
src/XXX.TestBench.Devices/Modbus/NModbusClientFactory.cs
src/XXX.TestBench.Devices/Modbus/ModbusRtuChannelConnection.cs
src/XXX.TestBench.Devices/Runtime/ModbusRtuDeviceRuntime.cs
src/XXX.TestBench.Devices/Runtime/ModbusTcpDeviceRuntime.cs
src/XXX.TestBench.Devices/Runtime/CompositeDeviceConnectionTester.cs
src/XXX.TestBench.Devices/Runtime/ModbusRtuConnectionTester.cs
src/XXX.TestBench.Devices/Runtime/ModbusTcpConnectionTester.cs
```

正式编码时可在不削弱职责边界的前提下合并过小文件，但不能把协议解析、运行时、UI 和第三方调用重新堆进一个类。

## 20. 实施顺序

### P0：失败测试固定分类和配置合同

先增加失败测试，固定：

- 配置版本门禁按第 6.1 节得出唯一结论；
- TCP 只显示 S7/Modbus TCP；
- Serial 只显示 Modbus RTU；
- 错误组合在核心层拒绝；
- RTU 同通道站号重复拒绝；
- Modbus TCP 缺端点拒绝；
- Hardware + Modbus 占位驱动仍拒绝；
- 所有 `IDeviceDriverDescriptor` 实现者在增加 `SupportedTransports` 后可以编译；
- `DeviceConnectionTestResult` 新字段的所有构造点都已纳入；
- Hardware Modbus 传输层激活后保持 `Connecting`，不能直接变成 `Online`；
- v5 模板中的 32/64 位 Modbus 点因缺少字序而预览失败；
- Modbus 旧 `Boolean` 只在位区兼容并在新 Revision 中规范化为 `Bool`。

完成条件：测试明确失败在尚未实现的行为上，不修改运行时代码掩盖失败。

### P1：地址、类型和 Codec

实现：

- 四个数据区；
- `C/DI/HR/IR` 解析；
- 传统引用号规范化；
- 裸数字拒绝；
- 类型宽度；
- ByteOrder/WordOrder；
- 读写对称；
- 地址重叠检测；
- 使用固定十六进制模式验证规范大端缓冲、字节交换和字交换，不依赖运行机器端序。

完成条件：纯内存测试全部通过，不需要网络和串口。

### P2：真实驱动描述器和 UI 联动

实现两个描述器，更新注册表和设备向导过滤。此阶段 `IsImplemented` 仍保持 false，避免 UI 在运行时完成前允许 Hardware 保存。

完成条件：配置/界面测试通过，Hardware 仍安全受阻。

### P3：Modbus TCP 运行时

实现 TCP 客户端适配、传输层启动、`Connecting→Online` 首次响应转换、四区读取、Holding/Coil 写入、批量读取、超时和重连。

完成条件：Fake client 与本机 Modbus TCP 模拟器通过；没有真实设备结论。

### P4：Modbus RTU 共享通道运行时

实现启动时单串口共享打开、全局串行、多个 UnitId、`Connecting→Online` 首次响应转换、关闭重开、拔插异常恢复。

完成条件：Fake serial adapter 与虚拟串口对通过；没有 USB-RS485 或真实仪表结论。

### P5：连接测试、诊断和客户提示

实现组合测试器、活动串口占用只读查询、`Executed` 结果语义、传输层证据提示、点位诊断复用和错误映射。

完成条件：页面不会把端口打开误报为协议成功，所有错误为简洁中文。

### P6：原子配置应用与回滚

覆盖：

- 候选 Modbus TCP 失败；
- 候选 RTU 串口占用；
- 传输层成功但尚无协议响应；
- 允许离线应用；
- 旧 Revision 重启；
- 候选资源完全释放；
- S7 与 Modbus 混合设备部分上线。

完成条件：活动 Revision、运行时和页面状态一致。

### P7：全量自动化与文档

完成构建、Core、App、Integration、Headless，更新支持矩阵、导入说明和第三方声明。只有 TCP/RTU 各自对应的运行时、资源释放、配置应用、错误映射和自动化测试全部完成后，才把该驱动的 `IsImplemented` 从 false 切换为 true；两种驱动分别判定，不因其中一种完成而同时开放。

完成条件：所有有最终摘要的自动化结果记录清楚；挂起或超时仍记未验证。

### P8：真实设备验收

分别使用：

- 一台真实 Modbus TCP 设备；
- 一台 USB-RS485 适配器和至少两台不同站号 RTU 设备；
- 一个现场批准的只读点；
- 一个现场批准的低风险可写点。

完成条件见第 23 节。没有这些设备时，本阶段保持开放，不阻止软件功能继续开发，但不得写“现场通过”。

## 21. 自动化测试清单

### 21.1 Core.Tests

新增/扩展：

```text
tests/XXX.TestBench.Core.Tests/ModbusConfigurationTests.cs
tests/XXX.TestBench.Core.Tests/MultiDeviceConfigurationTests.cs
tests/XXX.TestBench.Core.Tests/DeviceConfigurationServiceTests.cs
tests/XXX.TestBench.Core.Tests/DeviceWritePipelineTests.cs
```

覆盖：

- 通道/驱动组合；
- v4 可复用门禁或 v4→v5 迁移只能产生一个明确结果；
- 旧 Revision 不被原地覆盖，无法确定的数据不被猜测迁移；
- 站号范围和重复；
- TCP 端点；
- Modbus 位区旧 `Boolean` 兼容、新保存规范为 `Bool`，寄存器区拒绝两者；
- v6 模板字节序/字序列合同及 v5 兼容失败规则；
- 活动试验禁止应用；
- 候选失败保持旧 Revision；
- Input/Discrete 写入拒绝；
- ReadBackEqual 只接受新鲜原始值；
- Hardware 失败不回退 Simulation。

### 21.2 Integration.Tests

新增：

```text
tests/XXX.TestBench.Integration.Tests/ModbusAddressParserTests.cs
tests/XXX.TestBench.Integration.Tests/ModbusDriverDescriptorTests.cs
tests/XXX.TestBench.Integration.Tests/ModbusValueCodecTests.cs
tests/XXX.TestBench.Integration.Tests/ModbusReadBatchPlannerTests.cs
tests/XXX.TestBench.Integration.Tests/ModbusTcpRuntimeTests.cs
tests/XXX.TestBench.Integration.Tests/ModbusRtuRuntimeTests.cs
tests/XXX.TestBench.Integration.Tests/ModbusChannelSharingTests.cs
tests/XXX.TestBench.Integration.Tests/ModbusConnectionTesterTests.cs
```

关键断言：

- `40001` 规范化为 `HR:0`；
- 裸 `0` 被拒绝；
- 32 位和 64 位四种常见字节/字序往返一致；
- 固定模式 `0x1122/0x3344` 在四种字节/字序组合下结果唯一且不依赖宿主机端序；
- RTU 两台设备共享一个串口实例；
- RTU 候选启动即打开一次共享串口且不发送 Modbus 请求；
- 同串口最大并发为 1；
- 两台 Modbus TCP 设备可以并行；
- 一个 TCP 设备失败不阻塞另一个设备；
- 超时后旧连接不复用；
- 取消/超时在释放通道门之前使连接失效并完成关闭；
- 写请求超时或取消后不自动重发并返回“不确定”结果；
- Stop→Start 后可重新连接；
- Modbus 传输层启动成功后状态为 `Connecting`，第一次有效读取后为 `Online`；
- 第一次读取失败后为 `Offline`，后续轮询成功可恢复为 `Online`；
- 零点位 Modbus 设备保持“待点位验证”，不误报在线；
- 批量读取按区和连续范围拆分；
- 读取数量不超过协议上限；
- 返回顺序与 PointId 请求顺序一致；
- 异常响应保留功能码和异常码到事件日志；
- 写入后执行新鲜回读；
- 取消后无遗留后台请求。

### 21.3 App.Tests

新增/扩展：

```text
tests/XXX.TestBench.App.Tests/DeviceConfigurationViewModelTests.cs
tests/XXX.TestBench.App.Tests/DevicePointDialogViewModelTests.cs
tests/XXX.TestBench.App.Tests/DeviceConnectionTestResultFormatterTests.cs
tests/XXX.TestBench.App.Tests/DevicePointDiagnosticsViewModelTests.cs
```

覆盖：

- 选择 TCP 只显示 S7/Modbus TCP；
- 选择 Serial 只显示 Modbus RTU；
- 切换通道清除不兼容驱动；
- Modbus TCP 显示 IP/502/站号；
- Modbus RTU 显示串口摘要/站号；
- 仿真模式不开放硬件连接测试；
- 输入区自动只读；
- Coil 只允许 Bool；
- 32 位类型显示字序；
- 连接测试文案区分“端口可用”和“协议读取成功”；
- 活动串口占用时 `Executed=false`，不创建第二连接且不关闭当前运行时；
- `Connecting/Offline` 允许只读诊断，写入仍被拒绝；
- 英文底层异常不直接展示给客户。

### 21.4 Headless

定向覆盖：

- 1152×720 下设备向导三种组合；
- 串口参数较长时不遮挡；
- Modbus TCP IP/端口/站号布局；
- 点位数据区和偏移联动；
- `Connecting` 显示“待点位验证”，TCP/串口打开不显示“在线”；
- 200% DPI 留作人工显示器验收，不冒充 Headless 结论。

Headless 必须单独串行执行并设置 hang timeout。没有最终测试摘要、被中断或生成 hang dump 均记为未验证。

## 22. 建议验证命令

所有缓存、构建和测试产物放在工作区外：

```powershell
$projectRoot = 'D:\易峥\2026\2026-09\Avalonia_上位机通用模板\项目代码\XXX.TestBench.Template'
$artifactRoot = 'D:\Codex相关\标准ModbusTCP与RTU通信\artifacts'
$nugetPackages = 'D:\Codex相关\标准ModbusTCP与RTU通信\nuget-packages'

$env:NUGET_PACKAGES = $nugetPackages

dotnet restore "$projectRoot\XXX.TestBench.Template.sln" --artifacts-path $artifactRoot
dotnet build "$projectRoot\XXX.TestBench.Template.sln" -c Debug --no-restore --artifacts-path $artifactRoot

dotnet test "$projectRoot\tests\XXX.TestBench.Core.Tests\XXX.TestBench.Core.Tests.csproj" -c Debug --no-build --artifacts-path $artifactRoot
dotnet test "$projectRoot\tests\XXX.TestBench.Integration.Tests\XXX.TestBench.Integration.Tests.csproj" -c Debug --no-build --artifacts-path $artifactRoot
dotnet test "$projectRoot\tests\XXX.TestBench.App.Tests\XXX.TestBench.App.Tests.csproj" -c Debug --no-build --artifacts-path $artifactRoot

dotnet test "$projectRoot\tests\XXX.TestBench.App.Headless.Tests\XXX.TestBench.App.Headless.Tests.csproj" -c Debug --no-build --artifacts-path $artifactRoot --blame-hang --blame-hang-timeout 60s
```

注意：使用新的 `--artifacts-path` 后必须先 solution-level restore，再使用 `--no-restore/--no-build`。

如需 Modbus TCP 本机模拟器或虚拟串口工具，其下载、配置、日志和临时数据放到：

`D:\Codex相关\标准ModbusTCP与RTU通信`

不得把模拟器缓存和虚拟串口临时配置放进项目目录。

## 23. 验收矩阵

| 能力 | Fake/单元 | 本机 TCP 模拟器 | 虚拟串口 | 真实设备 | UOS/Linux |
|---|---:|---:|---:|---:|---:|
| 地址和类型校验 | 必须 | 不替代 | 不替代 | 不替代 | 不替代 |
| Codec 字节序/字序 | 必须 | 可辅助 | 可辅助 | 必须 | 必须 |
| Modbus TCP 建连 | 不足 | 必须 | 不适用 | 必须 | 必须 |
| Modbus TCP 四区读取 | 不足 | 必须 | 不适用 | 必须 | 必须 |
| RTU 串口共享 | 必须 | 不适用 | 必须 | 必须 | 必须 |
| RTU 多站号轮询 | 必须 | 不适用 | 必须 | 必须 | 必须 |
| 断线/拔插恢复 | 部分 | 必须 | 必须 | 必须 | 必须 |
| Coil/Holding 写入 | 必须 | 必须 | 必须 | 现场批准点 | 现场批准点 |
| ReadBackEqual | 必须 | 必须 | 必须 | 必须 | 必须 |
| 长时间运行 | 不足 | 可做预检 | 可做预检 | 至少 2 小时 | 至少 2 小时 |

真实验收必须记录：

- 设备厂家、型号、固件；
- TCP IP/端口或串口适配器型号；
- 串口号、波特率、数据位、校验位、停止位；
- Unit ID；
- 数据区、手册地址、规范地址、类型；
- ByteOrder、WordOrder；
- 读值与设备面板/厂家工具对照；
- 写入批准人、点位、原值、目标值、回读值和恢复值；
- 断线、拔插、重连、降级和恢复时间；
- 轮询周期、请求数量、超时和异常响应；
- Windows 与 UOS/Linux 分别记录，不能互相替代。

## 24. 客户提示文案

建议统一为：

| 场景 | 客户提示 |
|---|---|
| TCP 无法连接 | `无法连接设备，请检查设备 IP、端口和网络。` |
| 串口不存在 | `未找到所选串口，请检查串口连接或重新选择。` |
| 串口被占用 | `串口正在被其他程序使用，请关闭占用程序后重试。` |
| RTU 无响应 | `设备未响应，请检查站号、串口参数和接线。` |
| Modbus 异常响应 | `设备拒绝本次读取，请核对数据区和地址。` |
| 数据类型不匹配 | `点位数据类型与所选数据区不匹配。` |
| 裸数字地址 | `请先选择数据区，再填写协议偏移。` |
| 只读区写入 | `该数据区只允许读取，不能写入。` |
| 端口测试成功 | `连接资源可用；尚未验证点位读取。` |
| 点位读取成功 | `读取成功，已获得当前设备值。` |

内部日志应附带 DriverKey、ChannelId、DeviceId、UnitId、Area、Offset、Function Code、Exception Code、ConnectionGeneration 和 Revision；客户界面不显示内部 GUID、堆栈或英文异常。

## 25. 风险与处理

| 风险 | 处理 |
|---|---|
| 一个串口被多台 DeviceRuntime 分别打开 | RTU Master 归 ChannelManager，一通道一实例 |
| 地址 `0` 无法判断区域 | 新配置拒绝裸数字，要求数据区 + 偏移 |
| 40001 直接作为报文偏移发送 | 解析为 HR:0，并通过测试锁定 |
| 字节序猜错但数值看似合理 | 必须由设备手册/已知值确认，不自动猜测 |
| 多层重试导致动作延迟 | 只保留 DeviceSession 一层重试 |
| 超时请求残留污染下一帧 | 超时即废弃连接；RTU 关闭并重开串口 |
| TCP 建连成功被误报为协议成功 | 结果增加 TransportOnly 层级 |
| RTU 没有通用 Ping | 端口测试与点位读取测试分开 |
| 输入区被错误写入 | 描述器、ViewModel、运行时、写入管线四层拒绝 |
| 高风险写入被自动重试 | 继续受 DeviceWritePipeline 风险和回读策略控制 |
| 候选配置失败破坏旧运行 | 沿用 Stage/Commit/Publish/rollback 顺序 |
| Modbus 改造破坏 S7 | 分阶段定向测试，S7 描述器和运行时保持独立 |
| 构建通过被写成现场通过 | 支持矩阵分开记录软件、模拟器、真实设备和 UOS |

## 26. 回滚方案

### 26.1 代码回滚

不得使用 `git reset --hard`、`git clean` 或覆盖整个工作树。

每阶段实施前记录：

- 目标文件 `git diff`；
- 新增文件清单；
- 已通过的定向测试；
- 当前 active Revision。

阶段失败时只撤销本阶段新增 Modbus 文件和对应小范围补丁，不触碰任务开始前已有未提交修改。

### 26.2 配置回滚

- 原 v3/v4 Revision 保留；
- 新配置生成新 Revision；
- 候选运行时失败不切 active；
- publish 失败恢复旧 active；
- UI 恢复上一版本仍必须通过 `DeviceConfigurationService`；
- 活动试验期间不允许切换配置。

### 26.3 运行时回滚

- 候选 TCP Socket、RTU Master、SerialPort、轮询任务必须全部释放；
- 旧 S7/Simulation/Modbus 运行时按原 Revision 重启；
- 重启失败进入 Faulted 并记录 RollbackError；
- 不允许用 Simulation 掩盖旧硬件恢复失败。

## 27. 最终完成标准

全部满足后才能把 Modbus TCP/RTU 标记为“软件功能已实现”：

1. 通道和协议分类符合本文矩阵；
2. TCP 下拉只出现 S7/Modbus TCP，Serial 只出现 Modbus RTU；
3. Modbus TCP 设备有独立 Host/Port；
4. RTU 多设备共享同一个串口实例；
5. 同通道站号重复在保存前被拒绝；
6. Hardware Modbus 启动实际建立传输层，但首次有效响应前保持“待点位验证”；
7. 四个标准数据区可正确读取，首次有效响应后才转为 Online；
8. Coil 和 Holding Register 按批准范围写入；
9. Input/Discrete 写入在所有入口被拒绝；
10. 地址规范为 `C/DI/HR/IR:offset`，裸数字不被猜测；
11. 新 Modbus 位点保存为 `Bool`，旧 `Boolean` 兼容不影响其他驱动；
12. 16/32/64 位 Codec 读写对称且不依赖宿主机端序；
13. v6 模板具备独立字节序/字序列，旧模板不猜测多寄存器字序；
14. 批量读取不跨区、站号、设备或协议上限；
15. 同一串口请求严格串行，不同 TCP 设备可以并行；
16. 超时、取消、断线和拔插后不复用污染连接；
17. 写超时或取消不自动重发，结果按“不确定”进入现有安全链；
18. 设备缓存、质量、时间戳、Revision 和连接代次正确；
19. Hardware 失败不回退 Simulation；
20. 连接测试不把端口可用冒充协议成功，活动串口不被第二次打开；
21. 点位诊断复用当前运行时，没有第二连接；
22. 写入继续走权限、风险确认、新鲜回读和审计；
23. 候选配置失败能恢复旧 Revision 和旧运行时；
24. 配置版本门禁及对应迁移测试有唯一明确结果；
25. Core、Integration、App 有最终通过摘要；
26. Headless 有最终通过摘要，若挂起则明确未验证；
27. NModbus 许可证声明完整；
28. 支持矩阵不再写“未选型”，但真实设备和 UOS 状态单独保留；
29. 没有真实设备证据时只写“软件实现/模拟器验证”，不写“现场通过”。

“真实 Modbus 已验证”还必须完成第 23 节真实设备矩阵，不能由以上软件完成标准替代。

### 27.1 开工前一次性检查单

开始 P0 编码前只需完成以下只读检查，不再召开新的协议分类或架构设计讨论：

- [ ] `git status --short --branch` 和目标文件定向 diff 已保存；
- [ ] 第 6.1 节版本门禁得出“保留 v4”或“升 v5”的唯一结论，并先写失败测试；
- [ ] 当前所有 `IDeviceDriverDescriptor` 实现者和 `DeviceConnectionTestResult` 构造点已由 `rg` 列全；
- [ ] 当前模板版本确认仍为 v5，升级目标固定为 v6；
- [ ] NModbus 版本和许可证与项目文件锁定一致；
- [ ] 自动化输出目录放在 `D:\Codex相关` 的任务子目录，不写入项目缓存；
- [ ] 明确本轮不连接、不写入真实设备，真实设备验收继续留在 P8。

检查单完成后，本文已达到直接编码标准。实现人员可以按 P0→P8 顺序执行；不得重新引入 Modbus ASCII、厂家自定义协议、裸地址猜测、写入自动重试或每台 RTU 设备独占串口。

## 28. 官方参考

- Modbus Application Protocol Specification V1.1b3：<https://www.modbus.org/file/secure/modbusprotocolspecification.pdf>
- Modbus Messaging on TCP/IP Implementation Guide V1.0b：<https://modbus.org/docs/Modbus_Messaging_Implementation_Guide_V1_0b.pdf>
- Modbus over Serial Line Specification and Implementation Guide V1.02：<https://modbus.org/docs/Modbus_over_serial_line_V1_02.pdf>
- NModbus 项目：<https://github.com/NModbus/NModbus>
- NModbus NuGet：<https://www.nuget.org/packages/NModbus/>
- NModbus.Serial NuGet：<https://www.nuget.org/packages/NModbus.Serial/>

---

本文按 2026-09-14 当前工作树编制。正式编码时必须先比较目标文件的最新内容；若 S7、设备点位或配置代码已经继续演进，应把本文行为合同合并到最新实现中，不得用本文覆盖用户已有改动。
