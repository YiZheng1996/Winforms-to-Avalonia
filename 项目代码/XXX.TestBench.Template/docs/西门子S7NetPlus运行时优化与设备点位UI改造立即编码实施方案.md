# 西门子 S7NetPlus 运行时优化与设备点位 UI 改造立即编码实施方案

> 文档版本：V1.0  
> 编制日期：2026-09-13  
> 目标项目：`项目代码/XXX.TestBench.Template`  
> 目标解决方案：`XXX.TestBench.Template.sln`  
> 技术栈：.NET 8、Avalonia 11.3.9、CommunityToolkit.Mvvm 8.4.0、S7netplus 0.20.0  
> 文档性质：可直接进入编码的实施规格，不是概念性建议  
> 当前状态：只新增本文档；未修改源码、配置、数据库或现有 UI

## 1. 一页结论

当前项目已经引用 `S7netplus 0.20.0`，并存在 `S7NetPlusDeviceRuntime`、`S7NetPlusValueCodec`、`SiemensS7DriverDescriptor` 和对应集成测试，但当前实现仍有五类会影响现场正确性的缺口：

1. S7-200 SMART 的 `VB/VW/VD/Vx.y` 被界面和描述器接受，却会被 S7netplus 的 `DataItem.FromAddress` 拒绝；
2. PLC 地址优先取 TCP 通道的 Host，同一通道下多台设备会连接到同一目标；
3. `ChannelSession.StopAsync` 会永久关闭调度门，但重连和失败回滚仍重启同一运行时；
4. 采集周期、陈旧时间、重试次数和扫描模式已经显示在 UI，却没有形成真实后台采集、缓存、降级和恢复逻辑；
5. 点位类型与量程换算在描述器、编辑器、Codec、读写管线之间不一致。

本轮实施必须按以下顺序推进：

```text
行为合同和失败测试
    ↓
设备级 S7 配置与地址解析
    ↓
可重启生命周期、候选运行时应用与回滚
    ↓
设备采集缓存、重试、故障降级、批量读取、工程换算
    ↓
设备属性、实时监视、运行状态、事件日志 UI
    ↓
自动化、Headless、真实 PLC 分层验收
```

不得先改 XAML 再补运行时。UI 上出现的每个扫描、超时、重试、降级和连接状态字段，都必须由真实后端合同支撑。

## 2. 强制边界

### 2.1 保留的项目安全边界

- 层级保持为：`通信通道 → 设备 → 点位分组`，点位继续只在右侧清单管理；
- `ChannelId → DeviceId → GroupId → PointId` 稳定身份不得改成名称关联；
- 配置继续使用完整 Revision、`manifest.json` 哈希和 `active.json` 原子指针；
- 所有配置修改必须经过 `DeviceConfigurationService`，UI 不得直接写 JSON；
- 活动试验期间禁止应用配置、测试新连接和发起额外 Fresh 读取；
- 运行中的实时监视只能读取运行时缓存，不得每次 UI 刷新都访问 PLC；
- 所有设备写入仍必须经过 `DeviceWritePipeline` 的权限、风险、Revision 和 ReadBackEqual 校验；
- 不因 S7 驱动优化降低高风险写入确认要求；
- 不提交、不清理、不回退当前大量未提交修改。

### 2.2 本轮非目标

- 不接入 KEPServerEX 服务端，也不修改 KEPServerEX 项目；
- 不复制 KEPServerEX 品牌、图标或深色外观，只借鉴操作工作流；
- 不实现 Modbus TCP/RTU；
- 不增加动态 Tag、自动发现、TIA Portal 自动导入或 OPC UA Server；
- 不在没有现场协议依据时支持 S7 String、WString、DATE/TIME、数组和结构体；
- 不升级或替换 S7netplus 包；版本升级应作为独立兼容性任务；
- 不把单元测试、集成测试、Headless 或 Snap7 仿真外推为真实 PLC 验收。

### 2.3 工作区保护

当前 S7 Runtime、Codec、Descriptor、部分 UI 和测试仍包含未提交或未跟踪修改。编码前必须保存：

```powershell
git -c safe.directory='D:/易峥/2026/2026-09/Avalonia_上位机通用模板' status --short --branch
git -c safe.directory='D:/易峥/2026/2026-09/Avalonia_上位机通用模板' diff --stat
```

禁止使用：

- `git reset --hard`；
- `git checkout -- <file>`；
- `git clean`；
- 全解决方案格式化；
- 覆盖用户现有 `config/device.json`、`config/points.json` 和 `device-config/revisions`。

构建输出、TRX、Headless 截图和临时导出放在：

```text
D:\Codex相关\S7NetPlus运行时与UI优化\
```

## 3. 当前源码事实与确认问题

| 编号 | 当前事实 | 影响 | 优先级 |
|---|---|---|---|
| C01 | `SiemensS7DriverDescriptor` 接受 `VW5022/VD800/V0.1`，`S7NetPlusDeviceRuntime.ParseAddress` 直接调用 `DataItem.FromAddress` | S7-200 SMART V 区点位在启动前即解析失败 | P0 |
| C02 | `ResolveHost` 优先取 `channel.Tcp.Host`，设备编辑器只保留不可见的旧 `DeviceEntry.Address` | 同一 TCP 通道下的多台 PLC 无法拥有不同 IP | P0 |
| C03 | `ChannelSession.StopAsync` 将 `_stopping` 永久设为 1，`DeviceModeController.ReconnectAsync` 又对同一实例 Stop→Start | 重连、配置回滚后通道仍拒绝通信 | P0 |
| C04 | `DeviceSession.StartAsync` 把连接失败转换为 `SessionStartResult(false)`，`MultiDeviceRuntime.StartAsync` 丢弃结果 | 候选运行时可能在全部设备故障时继续进入提交路径 | P0 |
| C05 | `DeviceConfigurationService` 没有明确区分“在线验证通过”和“允许离线应用” | 配置成功与硬件上线状态语义不清 | P0 |
| C06 | 描述器支持集合含 `Decimal`，硬件校验又拒绝；Codec 含 `Double`，描述器不暴露 | 类型来源不唯一，UI 可能提供不可运行类型 | P0 |
| C07 | 编辑已有点位时 `_preservedRawDataType` 优先于新选择类型 | UI 显示类型可能改变，但运行时仍按旧 `RawDataType` 解析 | P0 |
| C08 | `DeviceWritePipeline` 做工程值→原始值换算，S7 读取直接返回原始值 | 同一点位读写语义不对称，回读可能误判 | P0 |
| C09 | `PollIntervalMs`、`StaleAfterMs`、`RetryCount` 未驱动 S7 后台任务 | UI 配置看似生效，实际无扫描、陈旧和重试行为 | P1 |
| C10 | 每次读取一个点位单独发送 S7 请求 | 点位多时吞吐差，难以满足设备采集周期 | P1 |
| C11 | I/O 异常主要只改 `_lastError`，不会可靠更新 `DeviceSession` 状态 | 页面可能继续显示“在线” | P1 |
| C12 | Rack 固定为 0，Slot 使用隐藏式候选回退 | 客户不知道实际连接参数，外部 CP 场景不可配置 | P1 |
| C13 | 工艺监控使用固定 1 秒 `DispatcherTimer`，Tick 为异步委托且无防重入 | 慢设备时可能堆积刷新任务 | P1 |
| C14 | `docs/设备点位管理与导入说明.md` 同时保留“S7 未实现”和“S7NetPlus 已接入”描述 | 支持矩阵自相矛盾 | P2 |
| C15 | 点位右键仍有“新建标记” | 与项目统一术语“点位”不一致 | P2 |

### 3.1 S7-200 SMART 的确定映射

本机实际引用的 `S7.Net.dll 0.20.0` 对以下调用结果为：

```text
DataItem.FromAddress("VW5022") → V is not a valid address
DataItem.FromAddress("VD800")  → V is not a valid address
DataItem.FromAddress("V0.1")   → V is not a valid address
DataItem.FromAddress("DB1.DBD0") → 成功
```

Siemens《S7-200 SMART V3 System Manual》把 V 存储区同时定义为 DB2 绝对地址，因此项目适配层采用以下确定转换：

| 客户地址 | 低层地址模型 | S7netplus 调用参数 |
|---|---|---|
| `V10.2` | DB2，第 10 字节，第 2 位 | `DataType.DataBlock, db=2, startByte=10, bit=2` |
| `VB16` | DB2，第 16 字节，1 字节 | `DataType.DataBlock, db=2, startByte=16, length=1` |
| `VW100` | DB2，第 100 字节，2 字节 | `DataType.DataBlock, db=2, startByte=100, length=2` |
| `VD2136` | DB2，第 2136 字节，4 字节 | `DataType.DataBlock, db=2, startByte=2136, length=4` |

参考：

- [Siemens S7-200 SMART V3 System Manual](https://support.industry.siemens.com/cs/attachments/109978364/S7-200_SMART_V3_system_manual_en-HS.pdf)
- [S7netplus PLCAddress 源码](https://github.com/S7NetPlus/s7netplus/blob/main/S7.Net/PLCAddress.cs)

该映射必须先由自动化固定，再在真实 S7-200 SMART 上验证；现场验证失败时不得改成猜测性的 DB1 映射。

## 4. 目标配置合同

### 4.1 层级职责

| 层级 | 保存内容 | 不再保存的内容 |
|---|---|---|
| Channel | 传输类型、共享网卡/串口资源、通道启用状态 | TCP 目标 PLC IP、设备请求超时 |
| Device | PLC IP、端口、型号、Rack、Slot、扫描、时序、故障降级 | 点位地址、业务分组 |
| Group | 名称、说明、排序 | 通信路由 |
| Point | 地址、PLC 数据类型、读写权限、量程、说明 | PLC IP、扫描周期 |

KEPServerEX 的 Device 代表一台目标设备，设备 ID 可使用 IP；扫描、时序和故障降级也属于设备属性。本项目沿用这个操作习惯，但仍使用自己的中文合同和 Revision 安全链。

### 4.2 配置版本

将 `DeviceConfig.CurrentSchemaVersion` 从 3 升为 4；`PointsConfig`、`SimulationConfig` 和 `DeviceConfigurationSnapshot` 的 SchemaVersion 本轮不变。

在 `DeviceConfig.cs` 增加：

```csharp
public const int PreviousSchemaVersion = 3;
public const int CurrentSchemaVersion = 4;
```

新增文件：

```text
src/XXX.TestBench.Core/Configuration/DeviceCommunicationOptions.cs
```

新增类型：

```csharp
public enum DeviceScanMode
{
    FixedInterval = 1,
    OnDemand = 2
}

public sealed class DeviceTimingOptions
{
    public int ConnectTimeoutMs { get; set; } = 3000;
    public int RequestTimeoutMs { get; set; } = 1000;
    public int RetryCount { get; set; } = 2;
    public int InterRequestDelayMs { get; set; }
}

public sealed class DeviceDemotionOptions
{
    public bool Enabled { get; set; } = true;
    public int FailureThreshold { get; set; } = 3;
    public int DemotionPeriodMs { get; set; } = 10000;
    public bool DiscardWritesWhileDemoted { get; set; } = true;
}

public sealed class SiemensS7ConnectionOptions
{
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 102;
    public short Rack { get; set; }
    public short Slot { get; set; }
}
```

同时调整 `ChannelConfiguration.cs` 中的 TCP 参数。为了让旧 Revision 仍可被迁移读取，`Host/Port` 本轮不能立即从 CLR 模型删除；v4 校验和序列化必须忽略它们，只使用 `LocalInterface`：

```csharp
public sealed class TcpChannelParameters
{
    // 仅供 schema 3 反序列化和迁移读取，业务代码禁止继续使用。
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public string? Host { get; set; }

    // 仅供 schema 3 反序列化和迁移读取；0 表示未提供。
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int Port { get; set; }

    // schema 4 通道级可选网卡；空字符串表示由操作系统选择。
    public string LocalInterface { get; set; } = string.Empty;
}
```

不要仅依赖属性上的 `JsonIgnore` 完成版本行为。若当前统一序列化选项不能区分 schema 3/4，应在 v4 DTO/映射层显式控制输出，保证旧版本可读、新版本不再写出目标 PLC 地址。

在 `DeviceConfig.DeviceEntry` 增加：

```csharp
public DeviceScanMode ScanMode { get; set; } = DeviceScanMode.FixedInterval;
public DeviceTimingOptions Timing { get; set; } = new();
public DeviceDemotionOptions AutoDemotion { get; set; } = new();
public SiemensS7ConnectionOptions? SiemensS7 { get; set; }
```

保留现有 `PollIntervalMs` 和 `StaleAfterMs`：

- `FixedInterval`：使用 `PollIntervalMs`；
- `OnDemand`：不启动后台采集，`PollIntervalMs` 仍保留为切回固定采集时的用户值；
- `StaleAfterMs` 必须满足 `StaleAfterMs >= max(2 × PollIntervalMs, RequestTimeoutMs + 100)`；
- `OnDemand` 不用定时器自动制造 Stale，质量以最近一次显式读取结果为准。

### 4.3 v3 → v4 迁移

修改：

```text
src/XXX.TestBench.Core/Configuration/DeviceConfigurationMigrator.cs
src/XXX.TestBench.Core/Ports/IDeviceConfigurationStore.cs
src/XXX.TestBench.Infrastructure/Configuration/DeviceConfigurationStore.cs
src/XXX.TestBench.Infrastructure/Configuration/DeviceConfigurationBootstrapper.cs
```

新增方法：

```csharp
public static ConfigurationMigrationResult MigrateDeviceToV4(
    DeviceConfigurationSnapshot source,
    string? revision = null);
```

迁移规则：

1. 仅接受 `source.Device.SchemaVersion == 3`；
2. 对 `driverKey == "siemens-s7"` 的设备：
   - `Host = device.Address` 非空时优先，否则取所属 `channel.Tcp.Host`；
   - `Port = channel.Tcp.Port`，无有效值时取 102；
   - `Rack = 0`；
   - S7-1200/S7-1500 默认 `Slot = 0`；
   - S7-200 SMART 默认 `Slot = 0`；
3. `ConnectTimeoutMs = max(1000, channel.TimeoutMs)`；
4. `RequestTimeoutMs = max(50, channel.TimeoutMs)`；
5. `RetryCount = channel.RetryCount`；
6. `ScanMode = FixedInterval`；
7. `AutoDemotion` 使用上述安全默认值；
8. 将迁移后 TCP 通道的兼容字段 `Host = null`、`Port = 0`，避免 v4 再次写出设备目标；
9. 不改变 ChannelId、DeviceId、GroupId、PointId；
10. 不修改点位地址、类型、权限、RiskLevel、WritePolicy、DecodeOptions；
11. 生成新的不可变 Revision：`migration-device-v4-<stable suffix>`，不得原地覆盖旧 Revision。

由于 `LoadActiveAsync` 当前在反序列化后立即执行当前版本校验，增加一个仅供 Bootstrapper 使用的迁移读取边界：

```csharp
public interface IDeviceConfigurationMigrationSource
{
    Task<DeviceConfigurationSnapshot> LoadActiveForMigrationAsync(
        CancellationToken ct = default);
}
```

`DeviceConfigurationStore.LoadActiveForMigrationAsync` 必须继续校验：

- `active.json`/`active.previous.json`；
- manifest 哈希；
- 文件集合；
- 文件哈希；
- JSON 可反序列化。

它只跳过“必须等于当前业务 SchemaVersion”的语义校验。只有 `DeviceConfigurationBootstrapper` 可以消费该接口，迁移后仍必须调用 `MultiDeviceConfigurationValidator.EnsureValid`、`StageAsync` 和 `CommitActiveAsync`。

### 4.4 v4 JSON 示例

```json
{
  "schemaVersion": 4,
  "channels": [
    {
      "id": "10000000-0000-5000-8000-000000000001",
      "code": "CH_PLC",
      "name": "PLC 网络通道",
      "transportKind": "Tcp",
      "enabled": true,
      "tcp": {
        "localInterface": ""
      }
    }
  ],
  "devices": [
    {
      "id": "20000000-0000-5000-8000-000000000001",
      "code": "DEV_S71500_01",
      "name": "S7-1500 主站",
      "deviceMode": "Hardware",
      "channelId": "10000000-0000-5000-8000-000000000001",
      "driverKey": "siemens-s7",
      "model": "S7-1500",
      "scanMode": "FixedInterval",
      "pollIntervalMs": 500,
      "staleAfterMs": 2000,
      "timing": {
        "connectTimeoutMs": 3000,
        "requestTimeoutMs": 1000,
        "retryCount": 2,
        "interRequestDelayMs": 0
      },
      "autoDemotion": {
        "enabled": true,
        "failureThreshold": 3,
        "demotionPeriodMs": 10000,
        "discardWritesWhileDemoted": true
      },
      "siemensS7": {
        "host": "192.168.1.10",
        "port": 102,
        "rack": 0,
        "slot": 0
      }
    }
  ]
}
```

`TcpChannelParameters.Host/Port` 作为 v3 迁移输入保留在反序列化模型中，但 v4 校验必须要求 `Host` 为空且 `Port == 0`，序列化时不再写出。新增可选字段 `LocalInterface`，首版允许为空，运行时不绑定指定网卡。

## 5. S7 地址与类型能力单一来源

### 5.1 新增地址模型

新增文件：

```text
src/XXX.TestBench.Devices/Runtime/S7AddressParser.cs
```

新增内部类型：

```csharp
internal enum S7AddressShape
{
    Bit,
    Byte,
    Word,
    DoubleWord
}

internal sealed record S7ParsedAddress(
    string CanonicalAddress,
    S7.Net.DataType Area,
    int DbNumber,
    int StartByte,
    int BitIndex,
    S7AddressShape Shape);

internal static class S7AddressParser
{
    public static bool TryParse(
        string model,
        string address,
        out S7ParsedAddress result,
        out string error);

    public static S7ParsedAddress Parse(string model, string address);
}
```

`SiemensS7DriverDescriptor` 和 `S7NetPlusDeviceRuntime` 必须同时调用 `S7AddressParser`，禁止各保留一套 Regex/解析规则。

### 5.2 首版支持地址

| 系列 | 位 | 字节 | 字 | 双字 |
|---|---|---|---|---|
| S7-1200/1500 DB | `DB1.DBX0.0` | `DB1.DBB0` | `DB1.DBW0` | `DB1.DBD0` |
| S7-1200/1500 M | `M10.1` | `MB10` | `MW10` | `MD10` |
| S7-1200/1500 I | `I0.0` | `IB0` | `IW0` | `ID0` |
| S7-1200/1500 Q | `Q0.0` | `QB0` | `QW0` | `QD0` |
| S7-200 SMART V | `V0.1` | `VB0` | `VW0` | `VD0` |
| S7-200 SMART M/I/Q | 同上对应 M/I/Q 形式 | 同上 | 同上 | 同上 |

规范化规则：

- 去除半角/全角空格；
- 转大写；
- `%` 前缀允许输入但规范化后移除；
- bit 必须为 0–7；
- byte offset 和 DB number 必须为非负整数；
- S7-200 SMART V 地址转换为 DB2 低层参数，但 `CanonicalAddress` 仍保留客户熟悉的 `V/VB/VW/VD`；
- 不允许 `M10`、`I0`、`Q0` 这种大小含义不明确的地址；必须写成 bit 或 MB/MW/MD、IB/IW/ID、QB/QW/QD。

### 5.3 首版类型矩阵

新增文件：

```text
src/XXX.TestBench.Devices/Drivers/SiemensS7TypeCapabilities.cs
```

公开方法：

```csharp
public static IReadOnlySet<DevicePointDataType> GetSupportedTypes(
    string model,
    S7AddressShape shape);

public static DriverValidationIssue? Validate(
    string model,
    S7ParsedAddress address,
    DevicePointDataType type);
```

首版合同：

| 地址形状 | 允许类型 |
|---|---|
| Bit | `Boolean`、`Bool` |
| Byte | `Byte`、`Char` |
| Word | `Int16`、`UInt16` |
| DoubleWord | `Int32`、`UInt32`、`Float32` |

首版明确拒绝：

- `Decimal`；
- `Double`；
- `String`；
- `Unknown`。

`S7NetPlusValueCodec` 中的 `Double` 分支可以暂时保留为内部未公开能力，但 Descriptor、编辑器和运行时配置校验不得让它进入生效配置。后续支持 LREAL 时必须新增明确的 8 字节地址形状和真实 PLC 测试，不得把 DBD 默认解释为 8 字节。

### 5.4 修复点位类型保存

修改：

```text
src/XXX.TestBench.App/ViewModels/DevicePointDialogViewModel.cs
src/XXX.TestBench.App/ViewModels/DevicePointDialogViewModel.Clipboard.cs
```

删除简化编辑上下文中 `_preservedRawDataType` 对保存结果的优先覆盖。当前产品没有独立工程数据类型，因此：

```csharp
result.DataType == selectedType;
result.RawDataType == DevicePointTypeCatalog.ToStorage(selectedType);
```

只有导入旧未知类型、且用户尚未进入编辑保存时，旧字符串才可以原样保留。用户确认保存后必须使用驱动能力集合中的确定类型。

新增测试：

```text
EditingExistingPoint_ChangesRawDataTypeTogetherWithSelectedType
S7PointDialog_DoesNotOfferDecimalDoubleOrString
S7BitAddress_OnlyOffersBooleanTypes
S7WordAddress_OnlyOffersInt16AndUInt16
```

## 6. 隔离 S7netplus 依赖

### 6.1 新增适配接口

新增文件：

```text
src/XXX.TestBench.Devices/Runtime/IS7PlcClient.cs
src/XXX.TestBench.Devices/Runtime/S7NetPlusPlcClient.cs
```

接口：

```csharp
internal interface IS7PlcClient : IAsyncDisposable
{
    bool IsConnected { get; }
    int MaxPduSize { get; }
    Task OpenAsync(CancellationToken ct);
    Task<byte[]> ReadBytesAsync(
        S7.Net.DataType area,
        int dbNumber,
        int startByte,
        int count,
        CancellationToken ct);
    Task WriteBytesAsync(
        S7.Net.DataType area,
        int dbNumber,
        int startByte,
        byte[] bytes,
        CancellationToken ct);
    Task WriteBitAsync(
        S7.Net.DataType area,
        int dbNumber,
        int startByte,
        byte bit,
        bool value,
        CancellationToken ct);
}

internal interface IS7PlcClientFactory
{
    IS7PlcClient Create(
        CpuType cpu,
        string host,
        int port,
        short rack,
        short slot,
        int requestTimeoutMs);
}
```

`S7NetPlusPlcClient` 是唯一直接引用 `S7.Net.Plc` 的类型。`S7NetPlusDeviceRuntime` 只依赖接口，使连接失败、超时、批量读取和恢复测试不需要真实网络。

### 6.2 连接超时

S7netplus 的 `ReadTimeout/WriteTimeout` 不是独立的 TCP 建连超时。运行时必须创建 linked token：

```csharp
using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
connectCts.CancelAfter(options.Timing.ConnectTimeoutMs);
await client.OpenAsync(connectCts.Token);
```

超时时返回确定错误：

```text
连接 PLC 超时：192.168.1.10:102，3000 ms
```

不得把所有连接异常替换成“优化块访问”提示。优化块提示只在一次 DB 绝对地址读取返回访问拒绝/对象不存在时追加，不能覆盖 socket、DNS、连接超时和认证前错误。

### 6.3 Rack/Slot

删除 `OpenWithFallbackAsync` 和 `ResolveCandidateSlots` 的隐藏尝试，改为只使用配置中的 Rack/Slot：

```csharp
private Task<IS7PlcClient> OpenConfiguredEndpointAsync(CancellationToken ct);
```

设备属性页提供明确默认值和说明：

- S7-1200/1500 集成 CPU：默认 Rack 0 / Slot 0；
- S7-200 SMART：默认 Rack 0 / Slot 0；
- 外部 CP 或特殊拓扑：按现场配置填写；
- 程序不得猜测并静默尝试第二个 Slot。

## 7. 可重启运行时与配置原子应用

### 7.1 ChannelManager 生命周期

修改：

```text
src/XXX.TestBench.Devices/Runtime/ChannelManager.cs
```

目标状态：

```csharp
internal enum ChannelSessionState
{
    Ready,
    Draining,
    Stopped,
    Disposed
}
```

新增/调整方法：

```csharp
public void Start();
public Task DrainAndStopAsync(CancellationToken ct = default);
public Task<T> ExecuteAsync<T>(
    string channelId,
    string deviceId,
    Func<CancellationToken, Task<T>> operation,
    CancellationToken ct = default);
```

规则：

1. `Start` 可将 Stopped 恢复为 Ready；
2. `DrainAndStopAsync` 拒绝新请求并等待当前请求结束，但不 Dispose Semaphore；
3. `DisposeAsync` 才永久关闭；
4. Serial 通道以 `ChannelId` 为串行门；
5. TCP/S7 以 `DeviceId` 为串行门，避免一台离线 PLC 阻塞同通道其他 PLC；
6. 每台 S7 PLC 内部仍只允许一个请求并发；
7. 停止与重新启动必须受 `MultiDeviceRuntime` 生命周期锁保护。

### 7.2 启动报告

新增：

```text
src/XXX.TestBench.Core/Domain/Devices/RuntimeActivationReport.cs
```

```csharp
public sealed record DeviceActivationIssue(
    string DeviceId,
    string DeviceName,
    DeviceConnectionState State,
    string Error);

public sealed record RuntimeActivationReport(
    int TotalDevices,
    int OnlineDevices,
    IReadOnlyList<DeviceActivationIssue> Issues)
{
    public bool AllOnline => TotalDevices > 0 && OnlineDevices == TotalDevices;
}
```

在 `IDeviceRuntime` 增加：

```csharp
RuntimeActivationReport ActivationReport { get; }
```

`MultiDeviceRuntime.StartAsync` 必须保存所有 `SessionStartResult`，不得再丢弃返回值。结构错误、取消或运行时内部异常继续抛出；单台设备连接失败进入 ActivationReport。

### 7.3 离线应用确认

修改 `DeviceConfigurationService`：

```csharp
public sealed record DeviceConfigurationApplyOptions(
    bool S7OptimizedBlockAccessConfirmed = false,
    bool AllowOfflineHardwareDevices = false);
```

扩展结果：

```csharp
public sealed record DeviceConfigurationApplyResult(
    bool Ok,
    string Revision,
    string? Error,
    DeviceConfigurationSnapshot? Snapshot = null,
    bool RequiresS7OptimizedBlockAccessConfirmation = false,
    SiemensS7OptimizedBlockAccessNotice? S7OptimizedBlockAccessNotice = null,
    bool RequiresOfflineApplyConfirmation = false,
    IReadOnlyList<DeviceActivationIssue>? ActivationIssues = null,
    string? RollbackError = null);
```

应用流程固定为：

```text
权限、引用、活动试验、驱动校验
    ↓
Stage 候选 Revision
    ↓
Drain/Stop 旧运行时
    ↓
创建并启动候选运行时，取得 ActivationReport
    ↓
若硬件设备离线且未确认允许离线应用：
    停止候选 → 重启旧运行时 → 不切 active → 返回需确认
    ↓
CommitActive 候选 Revision
    ↓
PublishStartedRuntimeAsync
    ↓
Dispose 旧运行时
```

离线配置不是静默失败，也不是禁止配置。首次应用发现硬件离线时，UI 显示具体设备和错误，并提供：

- `返回修改`；
- `仍然应用配置（设备将显示离线）`。

第二个动作重新调用 Apply，并设置 `AllowOfflineHardwareDevices=true`。成功结果必须显示“配置已生效，1 台设备离线”，不能只显示“已生效”。

### 7.4 回滚规则

- 候选未发布前任何异常：停止并释放候选，重启旧运行时；
- active 已切换但 publish 失败：恢复 oldRevision 指针，再重启旧运行时；
- 旧运行时重启失败：返回 `RollbackError`，`DeviceModeController` 进入 Faulted，页面显示“配置切换失败且旧运行时恢复失败”；
- 不允许 `catch { }` 静默吞掉回滚失败；
- 审计后端失败仍不反转已确认的 active 状态，但必须写应用日志并返回 Warning。

新增测试：

```text
StopThenStart_ReopensChannelSession
ReconnectAfterStop_AllowsReadAgain
StartAsync_PreservesEveryDeviceActivationIssue
Apply_WhenHardwareOffline_DoesNotCommitWithoutConfirmation
Apply_WhenOfflineConfirmed_CommitsWithActivationWarning
Apply_WhenCandidateThrows_RestartsOldRuntimeAndKeepsOldRevision
Apply_WhenRollbackFails_ReturnsRollbackErrorAndFaultsController
```

## 8. 扫描、缓存、质量、重试与降级

### 8.1 运行时读取合同

在 `IDeviceRuntime` 增加：

```csharp
Task<IReadOnlyList<PointValue>> ReadManyFreshAsync(
    IReadOnlyList<string> pointIds,
    CancellationToken ct = default);

bool TryGetCachedValue(string pointId, out PointValue value);

IReadOnlyList<PointValue> ListCachedValues();
```

合同定义：

- `ReadAsync(point)`：优先返回缓存；OnDemand 且无缓存时允许执行一次 Fresh；
- `ReadFreshAsync(pointId)`：一定访问目标设备，不得返回旧缓存；
- `ReadManyFreshAsync`：一次逻辑批次的新鲜读取；
- `TryGetCachedValue/ListCachedValues`：绝不访问硬件；
- 诊断窗口可以 Fresh；主页面“实时监视”只能读取缓存；
- 活动试验期间诊断 Fresh 继续被操作门拒绝，但缓存监视可用。

### 8.2 DeviceSession 后台采集

修改：

```text
src/XXX.TestBench.Devices/Runtime/DeviceSession.cs
```

新增字段：

```csharp
private readonly ConcurrentDictionary<string, PointValue> _cache;
private CancellationTokenSource? _pollCts;
private Task? _pollTask;
private int _consecutiveFailures;
private DateTime? _demotedUntilUtc;
private DateTime? _lastSuccessUtc;
private DateTime? _lastFailureUtc;
```

新增方法：

```csharp
private Task PollLoopAsync(CancellationToken ct);
private Task PollOnceAsync(CancellationToken ct);
private void RecordSuccess(IReadOnlyList<PointValue> values);
private void RecordFailure(Exception error);
private bool IsDemoted(DateTime utcNow);
private PointValue ApplyStaleQuality(PointValue value, DateTime utcNow);
```

轮询规则：

1. Simulation 保持既有行为，但也写入统一缓存；
2. Hardware + FixedInterval 启动 PollLoop；
3. Hardware + OnDemand 不启动 PollLoop；
4. 每轮调用 inner runtime 的 `ReadManyFreshAsync`；
5. 单个点地址错误产生该点 Bad，不应让同批其他点丢失；
6. 连接/超时类错误记为设备失败；
7. 达到 RetryCount 后本轮失败，连续失败达到 FailureThreshold 进入 Demoted；
8. Demoted 期间不采集，到期只做一次探测；
9. 探测成功恢复 Online、清零失败计数、递增 ConnectionGeneration；
10. `StaleAfterMs` 到期后缓存质量变为 Stale，值保留用于诊断但业务信号仍按质量拒绝；
11. Stop 必须取消并等待 PollLoop；Dispose 不得遗留后台任务。

不要使用 UI `DispatcherTimer` 作为硬件轮询器。

### 8.3 重试边界

重试只放在 DeviceSession，底层 S7 client 每次调用只发一次请求，禁止两层嵌套重试。

```csharp
private async Task<T> ExecuteWithRetryAsync<T>(
    Func<CancellationToken, Task<T>> operation,
    CancellationToken ct);
```

规则：

- 总尝试次数 = `RetryCount + 1`；
- `DomainException` 中的确定配置/地址/类型错误不重试；
- socket、I/O、请求超时可重试；
- 每次重试前等待 `InterRequestDelayMs`；
- 用户取消不记失败、不重试；
- 高风险写入不得自动重复发送；写操作一旦发送状态不确定，由 `DeviceWritePipeline` 记录不确定失败并要求人工确认；
- 自动重试默认只用于连接和读取。

### 8.4 状态字段

扩展 `DeviceRuntimeInfo` 的尾部可选字段，保持现有构造调用兼容：

```csharp
DateTime? LastSuccessUtc = null,
DateTime? LastFailureUtc = null,
int ConsecutiveFailures = 0,
DateTime? DemotedUntilUtc = null,
int? NegotiatedPduSize = null
```

状态机：

```text
Unknown → Connecting → Online
Connecting → Faulted
Online → Degraded（一次通信失败或缓存出现 Stale）
Degraded → Demoted（达到连续失败阈值）
Demoted → Connecting（降级时间到，单次探测）
Connecting → Online（恢复成功，连接代次 +1）
任意运行态 → Offline（用户停止）
```

如不扩展 `DeviceConnectionState`，可用 `Degraded + DemotedUntilUtc` 表达 Demoted；UI 显示“通信降级”。不要新建一个与 Health 冲突的并行状态枚举。

## 9. S7 批量读取

### 9.1 新增批次规划器

新增：

```text
src/XXX.TestBench.Devices/Runtime/S7ReadBatchPlanner.cs
```

类型：

```csharp
internal sealed record S7PointReadPlan(
    DevicePoint Point,
    S7ParsedAddress Address,
    int ByteCount);

internal sealed record S7ReadBlock(
    S7.Net.DataType Area,
    int DbNumber,
    int StartByte,
    int ByteCount,
    IReadOnlyList<S7PointReadPlan> Points);
```

算法：

1. 按 `Area + DbNumber` 分组；
2. 按 StartByte 排序；
3. 合并重叠或相邻区间；首版允许最大空洞 `MaxMergeGapBytes = 8`；
4. 每个块不得超过 `min(222, MaxPduSize - 18)`；
5. bit 点读取其所在完整字节；同字节多个 bit 只读一次；
6. 单个地址解析失败只生成该点 Bad 结果，不执行网络请求；
7. 块读取失败后可降级为该块逐点读取，用于定位地址错误；连接错误不逐点重试，避免风暴。

S7netplus 已提供受 PDU 限制的多变量读取能力，但本项目首版使用 `ReadBytesAsync` 连续块并复用现有手工 Codec，以保证 DecodeOptions 和 bit 解码语义不改变。后续可在性能测试证明有收益后替换为 `ReadMultipleVarsAsync`。

参考：[S7netplus ReadMultipleVarsAsync](https://github.com/S7NetPlus/s7netplus/blob/main/S7.Net/PlcAsynchronous.cs)

### 9.2 性能验收指标

在 Fake client 下固定：

- 同 DB 的 20 个连续点位最多形成 2 个请求；
- 同一字节 8 个 bit 只形成 1 个读取；
- 两个不同 PLC 可并行；
- 同一 PLC 最大并发为 1；
- 一个离线 PLC 不阻塞同 TCP 通道另一 PLC；
- PollLoop 不发生重叠执行；
- Stop 后 2 秒内无残留轮询任务。

## 10. 工程值换算与回读

新增：

```text
src/XXX.TestBench.Core/Domain/Devices/DevicePointValueConverter.cs
```

方法：

```csharp
public static object? ToEngineering(DevicePoint point, object? rawValue);
public static object? ToRaw(DevicePoint point, object? engineeringValue);
public static bool RawValuesEqual(
    DevicePoint point,
    object? expected,
    object? actual);
```

扩展 `PointValue` 尾部可选参数：

```csharp
object? RawValue = null
```

合同：

- `PointValue.Value` 始终是业务使用的工程值；
- `PointValue.RawValue` 是 PLC 解码后的原始值；
- 未配置量程时二者相同；
- S7 Runtime 读取后调用 `ToEngineering`；
- `DeviceWritePipeline` 使用 `ToRaw` 后写设备；
- ReadBackEqual 比较 `readback.RawValue ?? readback.Value` 与预期 raw；
- 日志同时记录 engineering、raw、readbackRaw；
- 诊断页面显示两列；主实时监视默认只显示工程值，悬浮提示原始值。

线性公式：

```text
engineering = EngMin
            + (raw - RawMin) × (EngMax - EngMin)
            / (RawMax - RawMin)
```

必须拒绝：

- 四项量程不完整；
- RawMin == RawMax；
- EngMin == EngMax；
- 非数值点配置量程；
- 换算结果超出目标 PLC 原始类型范围。

新增测试：

```text
Read_ConvertsRawValueToEngineeringAndPreservesRawValue
Write_ConvertsEngineeringToRawAndComparesRawReadback
Scaling_RejectsIncompleteOrZeroSpanRange
Scaling_Int16_RoundsUsingDocumentedPolicy
```

整数写入取整策略固定为 `MidpointRounding.AwayFromZero`，并在点位编辑器说明中显示。

## 11. 连接测试

新增：

```text
src/XXX.TestBench.Core/Ports/IDeviceConnectionTester.cs
src/XXX.TestBench.Core/Domain/Devices/DeviceConnectionTestResult.cs
src/XXX.TestBench.Devices/Runtime/S7DeviceConnectionTester.cs
```

接口：

```csharp
public interface IDeviceConnectionTester
{
    Task<DeviceConnectionTestResult> TestAsync(
        DeviceConfig.DeviceEntry device,
        ChannelEntry channel,
        CancellationToken ct = default);
}

public sealed record DeviceConnectionTestResult(
    bool Ok,
    string Endpoint,
    TimeSpan Elapsed,
    int? NegotiatedPduSize,
    string? Error);
```

行为：

- 只建立并关闭候选连接；
- 不保存配置；
- 不切换 active Revision；
- 不读取或写入点位；
- 使用候选设备当前 IP/Rack/Slot/超时；
- 活动试验期间拒绝；
- 使用 `DeviceOperationCoordinator.EnterDiagnosticsAsync`；
- 结果显示 endpoint、耗时、PDU 和错误类别；
- 不显示“PLC 正常”这类扩大结论，只显示“TCP/S7 会话建立成功”。

## 12. 设备通信事件

### 12.1 事件合同

新增：

```text
src/XXX.TestBench.Core/Domain/Devices/DeviceCommunicationEvent.cs
src/XXX.TestBench.Core/Ports/IDeviceEventSink.cs
src/XXX.TestBench.Infrastructure/Logging/InMemoryDeviceEventHub.cs
```

```csharp
public enum DeviceEventSeverity
{
    Information,
    Warning,
    Error
}

public sealed record DeviceCommunicationEvent(
    DateTime TimestampUtc,
    DeviceEventSeverity Severity,
    string ChannelId,
    string DeviceId,
    string Source,
    string EventCode,
    string Message);

public interface IDeviceEventSink
{
    event EventHandler<DeviceCommunicationEvent>? EventReceived;
    void Publish(DeviceCommunicationEvent value);
    IReadOnlyList<DeviceCommunicationEvent> Snapshot(int limit = 200);
}
```

`InMemoryDeviceEventHub` 使用锁保护的 500 条环形缓冲区。运行时事件同时写入 `IAppLogger`，但底部 UI 读取 EventHub，不解析文本日志文件。

事件码至少包括：

```text
S7_CONNECTING
S7_CONNECTED
S7_CONNECT_FAILED
S7_REQUEST_TIMEOUT
S7_DEVICE_DEMOTED
S7_RECOVERY_ATTEMPT
S7_RECOVERED
S7_POINT_BAD
CONFIG_APPLIED_WITH_OFFLINE_DEVICE
CONFIG_ROLLBACK_FAILED
```

敏感信息规则：

- 可记录 PLC IP/端口；
- 不记录用户密码、令牌或数据库连接串；
- 点位写入值仍由现有审计管线记录，不在普通通信事件中重复泄漏。

## 13. UI 实施规格

### 13.1 总体原则

保留当前浅色工业风、蓝白配色、左侧主导航和设备点位三栏工作区。借鉴 KEPServerEX 的项目树、设备属性、Quick Client 和 Event Log 操作习惯，不复制其外观。

目标布局：

```text
┌ 设备与点位 ─ 配置已生效 ─ 运行状态：降级 ───────────────────────┐
│ [新增通道] [新增设备] [新增点位] [测试连接] [读取当前范围] [应用更改] │
├──────────────┬───────────────────────────┬──────────────────────┤
│ 设备结构      │ 点位配置 | 实时监视        │ 设备属性              │
│ 搜索          │ 搜索                      │ 常规 通信 扫描 故障     │
│ 通道          │ 配置表或缓存实时值表        │ 当前选中对象只读摘要     │
│  └设备        │                           │ [编辑属性] [测试连接]   │
│    └分组      │                           │                       │
├──────────────┴───────────────────────────┴──────────────────────┤
│ 设备事件日志：全部 | 信息 | 警告 | 错误                         │
└───────────────────────────────────────────────────────────────┘
```

### 13.2 主页面 ViewModel

修改：

```text
src/XXX.TestBench.App/ViewModels/DevicePointManagementViewModel.cs
src/XXX.TestBench.App/ViewModels/DevicePointManagementViewModel.Operations.cs
src/XXX.TestBench.App/ViewModels/DevicePointManagementViewModel.Diagnostics.cs
```

新增：

```csharp
public enum DevicePointWorkspaceMode
{
    Configuration,
    LiveMonitor
}

[ObservableProperty]
private DevicePointWorkspaceMode _workspaceMode;

public bool IsConfigurationMode => WorkspaceMode == DevicePointWorkspaceMode.Configuration;
public bool IsLiveMonitorMode => WorkspaceMode == DevicePointWorkspaceMode.LiveMonitor;

[RelayCommand]
private void ShowConfiguration();

[RelayCommand]
private void ShowLiveMonitor();

[RelayCommand]
private void RefreshCachedValues();
```

`DevicePointRow` 增加：

```csharp
public string CurrentValueText { get; set; } = "—";
public string RawValueText { get; set; } = "—";
public string QualityText { get; set; } = "未读取";
public string TimestampText { get; set; } = "—";
public string QualityBackground { get; set; } = "#F2F4F7";
public string QualityForeground { get; set; } = "#667085";
```

`RefreshCachedValues` 只能调用：

```csharp
runtime.TryGetCachedValue(row.PointId, out var value)
```

禁止调用 `ReadAsync` 或 `ReadFreshAsync`。

### 13.3 主页面 XAML

修改：

```text
src/XXX.TestBench.App/Views/DevicePointManagementView.axaml
src/XXX.TestBench.App/Views/DevicePointManagementView.axaml.cs
```

具体修改：

1. 顶部配置状态拆成两个 Badge：
   - `配置：已生效 / 有待应用修改 / 应用失败`；
   - `运行：在线 / 降级 / 离线 / 仿真`；
2. 顶部保留新增入口，增加当前范围的“测试连接”和“读取当前范围”；
3. 中间区域增加“点位配置 / 实时监视”二段切换；
4. 配置表继续使用当前列：名称、地址、类型、权限、备注；
5. 实时监视表使用：名称、地址、类型、当前值、质量、更新时间、权限；
6. 实时表数据来自缓存，右键仍只有查看状态/编辑配置，不增加直接写值；
7. 右侧“范围详情”改成“属性与状态”，保留只读展示；编辑按钮继续打开现有安全弹窗；
8. 底部增加可折叠事件日志，高度 170，折叠后 36；
9. 修正“新建标记”为“新增点位”；
10. 右键、Shift+F10、菜单键、双击和 Enter 编辑继续保留。

不要把可编辑 TextBox 直接放在右侧属性栏并即时写配置。所有编辑仍经弹窗构造候选，再调用 `DeviceConfigurationService`。

### 13.4 设备属性页

修改：

```text
src/XXX.TestBench.App/ViewModels/DeviceConfigurationViewModels.cs
src/XXX.TestBench.App/Views/DeviceEditorDialogWindow.axaml
src/XXX.TestBench.App/Views/DeviceEditorDialogWindow.axaml.cs
```

保持现有三步向导：

#### 第一步：设备身份

- 设备名称；
- 运行模式；
- 所属通道；
- 设备驱动；
- 设备系列。

#### 第二步：通信与采集

仅 S7 Hardware 显示：

- PLC IP；
- 端口；
- Rack；
- Slot；
- 扫描模式：固定周期、按需读取；
- 固定周期；
- 陈旧判定；
- 连接超时；
- 请求超时；
- 读取重试次数；
- 请求间隔；
- 启用通信故障降级；
- 连续失败阈值；
- 降级时长；
- 降级期间拒绝写入。

删除当前三个只有视觉效果的文本选项“按设备周期采集 / 按客户端周期 / 按需采集”。“按客户端周期”在当前上位机没有客户端订阅合同，本轮不得提供。

增加按钮：

```text
[测试连接]
```

测试结果区域：

```text
连接成功 · 192.168.1.10:102 · Rack 0 / Slot 0 · PDU 960 · 38 ms
```

或：

```text
连接失败 · 请求超时 · 3000 ms
```

#### 第三步：确认保存

必须显示：

- 设备名称；
- 通道；
- 驱动/系列；
- 运行模式；
- PLC IP:Port；
- Rack/Slot；
- 扫描模式和周期；
- 请求超时/重试；
- 故障降级策略；
- 说明“保存配置不代表向 PLC 写入点位值”。

### 13.5 事件日志 ViewModel

新增：

```text
src/XXX.TestBench.App/ViewModels/DeviceEventLogViewModel.cs
```

职责：

- 订阅 `IDeviceEventSink.EventReceived`；
- 使用 `Dispatcher.UIThread.Post` 更新集合；
- 最多显示 200 条；
- 支持全部/信息/警告/错误过滤；
- 支持按当前树范围过滤 ChannelId/DeviceId；
- 页面卸载时解除订阅；
- 不查询 SQLite 审计日志，不阻塞 UI。

在 `ShellServices` 增加：

```csharp
IDeviceConnectionTester DeviceConnectionTester,
IDeviceEventSink DeviceEvents
```

在 `AppComposition` 创建单例 EventHub，并注入 DeviceRuntimeFactory、DeviceModeController、设备点位页面服务。

### 13.6 可访问性与尺寸

- 目标最低窗口：1152×720；推荐 1366×768；
- 顶部按钮必须可键盘聚焦；
- Tab 顺序：树搜索 → 树 → 模式切换 → 点位搜索 → 点位表 → 属性操作 → 日志；
- 质量不能只靠颜色，必须显示“正常/陈旧/无效”；
- Online/Degraded/Offline 图标必须带 ToolTip；
- 输入框必须有显式 Label，不只依赖 Watermark；
- 表格列不允许小于可读宽度，窄屏优先让中间表水平滚动；
- 中文名称优先，内部 Code 只在诊断 ToolTip 或高级信息中显示。

## 14. 文档和支持矩阵

修改：

```text
docs/设备点位管理与导入说明.md
docs/设备驱动支持矩阵.md（若当前工作树存在）
THIRD-PARTY-NOTICES.md
```

统一结论：

- S7netplus 已完成代码接入；
- 支持矩阵分为“已实现”“自动化验证”“真实 PLC 验证”“UOS 验证”；
- S7-200 SMART V 区必须单列真实设备验收状态；
- S7-1200/1500 绝对 DB 地址必须提示 PUT/GET 与优化块访问前置条件；
- `S7netplus 0.20.0` 是第三方 MIT 组件，不是 Siemens 官方驱动；
- 不再同时保留“S7 未实现”和“S7 已实现”两种互相冲突的表述。

## 15. 文件级改动清单

### 15.1 新增文件

| 文件 | 作用 |
|---|---|
| `Core/Configuration/DeviceCommunicationOptions.cs` | 扫描、时序、降级、S7 连接配置 |
| `Core/Domain/Devices/RuntimeActivationReport.cs` | 候选运行时启动报告 |
| `Core/Domain/Devices/DevicePointValueConverter.cs` | 原始值/工程值双向换算 |
| `Core/Domain/Devices/DeviceConnectionTestResult.cs` | 连接测试结果 |
| `Core/Domain/Devices/DeviceCommunicationEvent.cs` | 通信事件模型 |
| `Core/Ports/IDeviceConnectionTester.cs` | 只读连接测试边界 |
| `Core/Ports/IDeviceEventSink.cs` | 事件发布/订阅边界 |
| `Devices/Runtime/S7AddressParser.cs` | 单一 S7 地址解析器 |
| `Devices/Drivers/SiemensS7TypeCapabilities.cs` | 单一类型能力矩阵 |
| `Devices/Runtime/IS7PlcClient.cs` | S7netplus 隔离和可测试接口 |
| `Devices/Runtime/S7NetPlusPlcClient.cs` | S7.Net.Plc 适配实现 |
| `Devices/Runtime/S7ReadBatchPlanner.cs` | 连续块批量读取规划 |
| `Devices/Runtime/S7DeviceConnectionTester.cs` | 候选设备只读连接测试 |
| `Infrastructure/Logging/InMemoryDeviceEventHub.cs` | 设备通信事件环形缓存 |
| `App/ViewModels/DeviceEventLogViewModel.cs` | 底部事件日志状态与过滤 |

### 15.2 重点修改文件

| 文件 | 必改内容 |
|---|---|
| `DeviceConfig.cs` | schema 4、设备通信配置、校验 |
| `ChannelConfiguration.cs` | v4 TCP 通道不再拥有目标 Host/Port |
| `DeviceConfigurationMigrator.cs` | v3→v4 稳定迁移 |
| `IDeviceConfigurationStore.cs` | 迁移读取边界 |
| `DeviceConfigurationStore.cs` | 校验哈希但允许旧 schema 迁移读取 |
| `DeviceConfigurationBootstrapper.cs` | 自动生成 v4 新 Revision |
| `IDeviceRuntime.cs` | ActivationReport、批读、缓存读取 |
| `PointValue.cs` | RawValue |
| `DeviceRuntimeInfo.cs` | 最近成功/失败、连续失败、降级截止、PDU |
| `SiemensS7DriverDescriptor.cs` | 复用 Parser/Capabilities |
| `S7NetPlusDeviceRuntime.cs` | 设备 endpoint、显式 Rack/Slot、批读、真实错误分类 |
| `S7NetPlusValueCodec.cs` | 与能力矩阵一致，补换算边界测试 |
| `DeviceRuntimeFactory.cs` | 注入 S7 客户端工厂、事件出口并创建新运行时合同 |
| `SimulationDeviceRuntime.cs` | 实现新增的启动报告、批读和缓存接口，保持仿真兼容 |
| `ChannelManager.cs` | 可重启、按 Serial Channel/TCP Device 调度 |
| `DeviceSession.cs` | 缓存、PollLoop、重试、降级、状态机 |
| `MultiDeviceRuntime.cs` | 启动报告、缓存聚合、生命周期锁 |
| `DeviceConfigurationService.cs` | 离线确认、明确回滚和错误返回 |
| `DeviceModeController.cs` | 状态同步、重连后报告、故障事件 |
| `DeviceWritePipeline.cs` | 统一 Converter、RawValue 回读比较 |
| `DevicePointDialogViewModel.cs` | 类型与 RawDataType 同步 |
| `DeviceConfigurationViewModels.cs` | S7 配置字段和测试连接命令 |
| `DeviceEditorDialogWindow.axaml` | 通信/扫描/降级真实控件 |
| `DevicePointManagementViewModel.cs` | 配置/实时模式、缓存显示、状态拆分 |
| `DevicePointManagementView.axaml` | 双表模式、右侧状态、底部事件日志 |
| `ProcessMonitorViewModel.cs` | 读取缓存，不逐点直连 PLC |
| `ProcessMonitorView.axaml.cs` | 防重入或仅触发缓存刷新 |
| `ShellServices.cs` | 注入 ConnectionTester/EventSink |
| `AppComposition.cs` | 单例服务和工厂装配 |

## 16. 直接编码顺序

### 阶段 0：锁定失败合同

先写失败测试，不改 XAML：

1. V 区解析失败复现；
2. M/I/Q 大小语法；
3. Stop→Start 后通道不可用复现；
4. MultiDeviceRuntime 丢弃 SessionStartResult 复现；
5. 编辑类型未更新 RawDataType 复现；
6. 读取量程不换算复现；
7. Poll/Retry/Stale 未消费配置的行为测试。

完成条件：新增测试按预期失败，失败原因与本方案一致。

### 阶段 1：配置与解析

1. 新增配置类型；
2. Device schema 4；
3. v3→v4 迁移；
4. S7AddressParser；
5. SiemensS7TypeCapabilities；
6. 修复点位类型保存；
7. 更新默认/示例配置，但不得覆盖用户实际运行配置。

完成条件：Core/Integration 配置和解析测试通过，尚不要求硬件连接。

### 阶段 2：S7 适配和生命周期

1. IS7PlcClient/S7NetPlusPlcClient；
2. S7 runtime 改用设备 endpoint；
3. 显式 Rack/Slot 和连接超时；
4. ChannelManager 可重启；
5. MultiDeviceRuntime 生命周期锁和 ActivationReport；
6. DeviceConfigurationService 离线确认与回滚。

完成条件：Fake PLC client 可覆盖连接成功、超时、异常、重启和回滚。

### 阶段 3：采集与数据正确性

1. ReadManyFreshAsync；
2. S7ReadBatchPlanner；
3. DeviceSession cache/PollLoop；
4. 重试和通信降级；
5. 状态与事件；
6. 工程换算和 RawValue；
7. ProcessMonitor 改为缓存读取。

完成条件：无 UI 也能通过状态机、批读、陈旧和换算测试。

### 阶段 4：UI

1. DeviceEditor ViewModel 测试；
2. DeviceEditor XAML；
3. 主页面配置/实时模式；
4. 右侧属性与运行状态；
5. 底部事件日志；
6. 离线应用二次确认；
7. 文案和自动化名称。

完成条件：App tests 先通过，再运行单独 Headless 页面用例。

### 阶段 5：文档和分层验收

1. 更新支持矩阵和操作说明；
2. 清点第三方声明；
3. 记录构建、Core、App、Integration、Headless；
4. 另行执行真实 S7-1200/1500；
5. 另行执行真实 S7-200 SMART V 区；
6. 另行执行 UOS/Linux。

## 17. 自动化测试清单

### 17.1 Core Tests

目标文件：

```text
tests/XXX.TestBench.Core.Tests/DeviceConfigurationV4Tests.cs
tests/XXX.TestBench.Core.Tests/DeviceConfigurationServiceTests.cs
tests/XXX.TestBench.Core.Tests/DevicePointValueConverterTests.cs
tests/XXX.TestBench.Core.Tests/DeviceModeControllerTests.cs
```

必须覆盖：

- v4 字段范围；
- stale 与 timeout 关系；
- 离线应用确认；
- oldRevision 回滚；
- 工程/原始换算；
- 整数取整；
- ReadBackEqual 使用 RawValue；
- 活动试验拒绝连接测试和配置应用。

### 17.2 Integration Tests

目标文件：

```text
tests/XXX.TestBench.Integration.Tests/S7AddressParserTests.cs
tests/XXX.TestBench.Integration.Tests/S7NetPlusIntegrationTests.cs
tests/XXX.TestBench.Integration.Tests/S7ReadBatchPlannerTests.cs
tests/XXX.TestBench.Integration.Tests/MultiDeviceRuntimeTests.cs
tests/XXX.TestBench.Integration.Tests/DeviceConfigurationStoreTests.cs
tests/XXX.TestBench.Integration.Tests/DeviceConfigurationBootstrapperTests.cs
```

必须覆盖：

- `V0.1/VB/VW/VD → DB2`；
- DB/M/I/Q 全地址族；
- 错误地址不访问网络；
- 两台 TCP PLC 并行、单台串行；
- 批量读取和 bit 合并；
- 请求超时、重试、降级、恢复；
- Stop→Start；
- schema 3 active Revision 迁移为 schema 4；
- manifest/hash 和 active.previous 恢复不退化。

### 17.3 App Tests

目标文件：

```text
tests/XXX.TestBench.App.Tests/DeviceConfigurationViewModelTests.cs
tests/XXX.TestBench.App.Tests/DevicePointDialogViewModelTests.cs
tests/XXX.TestBench.App.Tests/DevicePointManagementViewModelTests.cs
tests/XXX.TestBench.App.Tests/DeviceEventLogViewModelTests.cs
tests/XXX.TestBench.App.Tests/DevicePointDiagnosticsViewModelTests.cs
```

必须覆盖：

- S7 Hardware 时显示设备 endpoint；
- Simulation 不要求 PLC IP；
- 两个真实扫描模式；
- 不再显示假“按客户端周期”；
- 测试连接不保存配置；
- 实时模式只读缓存，断言 ReadFreshAsync 调用次数为 0；
- 配置状态与运行状态分离；
- 事件日志按范围和级别过滤；
- 质量同时显示文字和颜色；
- 编辑类型同步 RawDataType；
- 离线应用确认弹窗流程。

### 17.4 Headless Tests

只运行设备点位目标用例，进程隔离并设置 hang timeout：

- 1366×768：完整页面；
- 1152×720：最低尺寸；
- Device Editor 三步向导；
- 底部日志展开/折叠；
- 配置/实时切换；
- 长中文设备名；
- 200% DPI 不作为 Headless 结论，留给人工显示器验收。

Headless 挂起、超时或没有最终测试摘要均记为“未验证”，不能记为通过。

## 18. 建议验证命令

所有输出放到工作区外：

```powershell
$root = 'D:\易峥\2026\2026-09\Avalonia_上位机通用模板\项目代码\XXX.TestBench.Template'
$out = 'D:\Codex相关\S7NetPlus运行时与UI优化\artifacts'

dotnet build "$root\XXX.TestBench.Template.sln" -c Debug --no-restore --artifacts-path $out
dotnet test "$root\tests\XXX.TestBench.Core.Tests\XXX.TestBench.Core.Tests.csproj" -c Debug --no-build --artifacts-path $out
dotnet test "$root\tests\XXX.TestBench.App.Tests\XXX.TestBench.App.Tests.csproj" -c Debug --no-build --artifacts-path $out
dotnet test "$root\tests\XXX.TestBench.Integration.Tests\XXX.TestBench.Integration.Tests.csproj" -c Debug --no-build --artifacts-path $out
```

Headless 单独执行，不与其他测试项目并行。真实 PLC 测试必须使用现场批准的测试点，不得拿生产输出点做写入试验。

## 19. 真实 PLC 验收矩阵

| 场景 | S7-1200/1500 | S7-200 SMART | 自动化能否替代 |
|---|---:|---:|---:|
| TCP 102 建连 | 必测 | 必测 | 否 |
| Rack/Slot | 必测 | 必测 | 否 |
| DB/M/I/Q 读取 | 必测 | 按现场区域 | 否 |
| V/DB2 读取 | 不适用 | 必测 | 否 |
| Bool/Byte/Word/DWord/Float | 必测 | 必测 | 否 |
| 量程换算 | 必测 | 必测 | 部分 |
| 断网降级和恢复 | 必测 | 必测 | 否 |
| 两台 PLC 并行 | 必测 | 可组合 | 否 |
| 批量读取周期 | 必测 | 必测 | 否 |
| 只读点位写入拒绝 | 必测 | 必测 | 部分 |
| 高风险 ReadBackEqual | 使用批准测试点 | 使用批准测试点 | 否 |
| UOS/Linux | 独立验收 | 独立验收 | 否 |

真实验证报告必须记录：

- PLC 型号、订货号、固件；
- TIA/STEP 7 SMART 版本；
- IP、端口、Rack、Slot；
- PUT/GET、优化块访问设置；
- 测试点地址、PLC 原始类型、预期值；
- S7netplus 包版本；
- 应用 Revision；
- 断线时间、降级时间、恢复时间；
- 读取周期、请求数和最大延迟。

## 20. 回滚方案

### 20.1 代码回滚

本任务不能使用破坏性 Git 命令。若阶段实现失败：

1. 停止继续修改；
2. 记录本阶段实际改动文件；
3. 使用评审后的反向补丁逐文件撤销本任务改动；
4. 不触碰任务开始前已有修改；
5. 重新运行上一阶段已通过的目标测试。

### 20.2 配置回滚

- v3 Revision 永久保留；
- v4 迁移生成新 Revision；
- active 切换失败时恢复 active.previous；
- 不删除失败候选 Revision，保留 manifest 和日志用于取证；
- UI 提供“恢复上一生效版本”前必须显示 Revision 和差异，且活动试验期间禁用；
- 恢复仍通过 `DeviceConfigurationService`，不直接改 active.json。

### 20.3 运行时回滚

- 候选运行时失败时释放其所有 socket 和后台任务；
- 旧运行时必须可重新 Start；
- 旧运行时恢复失败进入明确 Faulted，不回退 Simulation；
- UI 显示故障并提供“重新连接”和“查看事件”，不得显示“已配置”冒充在线。

## 21. 最终验收标准

全部满足后才可关闭编码任务：

1. S7-200 SMART `V/VB/VW/VD` 不再交给 `DataItem.FromAddress`；
2. 两台 S7 PLC 可在同一 TCP 通道下配置不同 IP；
3. Stop→Start、Reconnect、候选失败回滚后均可继续通信；
4. 候选离线不会静默显示成功，必须要求明确确认或保持旧 Revision；
5. UI 的扫描、超时、重试、降级字段全部影响真实运行时；
6. 同一 PLC 不并发访问，不同 PLC 不被同一 TCP 通道无条件串行；
7. 点位缓存按 StaleAfterMs 产生 Stale 质量；
8. I/O 错误会更新设备状态、事件、最近失败和连续失败数；
9. 恢复成功会更新状态、时间和 ConnectionGeneration；
10. 读取返回工程值并保留 RawValue；
11. ReadBackEqual 使用原始值比较；
12. 编辑点位类型会同步 RawDataType；
13. 主页面实时监视只读缓存；
14. 活动试验期间额外连接和 Fresh 诊断被拒绝；
15. 底部事件日志可按范围和级别过滤；
16. 1152×720 Headless 目标用例正常完成；
17. 文档不再声称未实现 S7，也不把自动化结果写成现场通过；
18. 真实 PLC、UOS 和现场写入仍作为独立验收项列出。

## 22. 官方参考

- [KEPServerEX User Interface](https://support.ptc.com/help/kepware/kepware_server/en/kepware/server/navigating-the-configuration.html)
- [KEPServerEX Device General Properties](https://support.ptc.com/help/kepware/kepware_server/en/kepware/server/device-properties-general.html)
- [KEPServerEX Device Scan Mode](https://support.ptc.com/help/kepware/drivers/en/kepware/drivers/Device_Properties_Scan_Mode_8.html)
- [KEPServerEX Device Timing](https://support.ptc.com/help/kepware/kepware_server/en/kepware/server/device-properties-timing.html)
- [KEPServerEX Device Auto-Demotion](https://support.ptc.com/help/kepware/drivers/en/kepware/drivers/device-properties-auto-demotion_71.html)
- [S7netplus NuGet 0.20.0](https://www.nuget.org/packages/S7netplus)
- [S7netplus PLCAddress source](https://github.com/S7NetPlus/s7netplus/blob/main/S7.Net/PLCAddress.cs)
- [S7netplus asynchronous read source](https://github.com/S7NetPlus/s7netplus/blob/main/S7.Net/PlcAsynchronous.cs)
- [Siemens S7-200 SMART V3 System Manual](https://support.industry.siemens.com/cs/attachments/109978364/S7-200_SMART_V3_system_manual_en-HS.pdf)

---

本文给出的类型、方法、字段、实施顺序和验收规则按 2026-09-13 当前工作树编制。正式编码时如果目标文件已经被其他未提交改动继续修改，应先比较当前实现，再把本方案的行为合同合并进去，不能覆盖现有工作。
