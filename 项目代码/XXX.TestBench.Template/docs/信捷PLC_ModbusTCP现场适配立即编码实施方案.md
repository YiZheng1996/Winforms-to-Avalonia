# 信捷 PLC Modbus TCP 现场适配立即编码实施方案

编制日期：2026-09-23  
目标项目：`D:\易峥\2026\2026-09\Avalonia_上位机通用模板\项目代码\XXX.TestBench.Template`  
参考项目：`D:\易峥\2026\2026-09\Avalonia_上位机通用模板\同事代码参考\统信桌面OPC数据采集系统\ModbusAvalonia`  
方案版本：`v1.1`

方案性质：可以直接进入编码的实施合同；本文不代表功能已经实现或当前模板已经通过信捷 PLC 现场验收。

修订说明：v1.1 根据当时的工作树和参考源码补齐设备上下文、导入双侧规范化、连续批读、原生地址结果、占用范围校验、界面初始化、测试覆盖及程序/配置配套回滚；2026-09-23 的文档修订没有实施业务代码。2026-09-24 当前阶段的授权与边界见下方用户确认。设备系列键仍为 `XINJE_FIELD_V1`，文档版本变化不改变系列键。

## 2026-09-24 用户确认的本阶段范围

当前为模板开发和测试点位场景。用户不要求旧程序及旧配置回退演练；该演练不作为本阶段软件完成或当前开发交付的前置条件。配置候选应用失败保护必须保留。最新模板的真实 PLC 验证仍单独记录，不能用同事程序既有现场结果代替。该范围只调整本阶段验收边界，不授权修改同事代码、UOS 工程、真实配置或 PLC。

## 1. 一页结论

本次不复制同事项目的程序结构，也不新增第二套通信线程。正确做法是在模板现有 `modbus-tcp` 驱动下增加一个受限设备系列：

```text
Modbus TCP
├─ 通用 Modbus TCP 设备（保持现状）
└─ 信捷 PLC（现场验证配置 V1）
   ├─ 客户地址：D / HD / M / HM
   ├─ 运行地址：HR / C 的零基偏移
   ├─ 字节序：大端模式
   └─ 多寄存器字序：低字在前
```

固定实施原则：

1. 唯一修改主线是 `XXX.TestBench.Template`；参考项目只读，不修改、不编译进主线。
2. 继续使用 `DriverKeyCatalog.ModbusTcp`，不新造 `xinje-tcp` 协议枚举，不复制 `XinjePlcCommunicator`。
3. 使用 `DeviceEntry.Model = XINJE_FIELD_V1` 区分信捷地址方言；这表示“当前现场验证配置”，不表示支持全部信捷 XD/XL 型号。
4. 第一版只接收现场点表已经出现的 `D`、`HD`、`M`、`HM` 四类地址。
5. 第一版不接收 `FD`、`SD`、`SM`、`X`、`Y`、`S`，也不接收 `D10.0` 这类寄存器位地址。
6. 现有中文九列点位导入模板保持不变；不开发旧 CSV 转换器，不兼容同事项目旧表头。
7. 信捷地址只在配置和界面层保留；发送请求前必须解析为标准 Modbus 区域和零基偏移。
8. 读取复用现有批量规划器，信捷 V1 只合并连续点位、不跨地址空隙；通用 Modbus 策略保持原状。写入继续走 `DeviceWritePipeline` 的权限、风险确认、写入、回读、比对和审计链。
9. TCP 连接成功仍只表示传输层成功；至少一个配置点读取成功后才能判定协议在线。
10. 软件测试、离线测试、现场 PLC 测试、UOS 测试分别记录，不能互相替代。

## 2. 证据、判断和边界

### 2.1 当前已确认事实

当前模板源码已经具备：

- `modbus-tcp` 硬件驱动和 NModbus 连接适配；
- 设备级 `ModbusUnitId`；
- 设备级 `ModbusDataOrder`；
- `C:offset`、`DI:offset`、`HR:offset`、`IR:offset` 标准地址；
- 最大 2000 位、125 个寄存器的批量读取规划；
- TCP 设备独立连接、超时失效、状态恢复和连接代次；
- `DeviceConfigurationService` 候选配置、运行时激活、提交与回滚；
- `DeviceWritePipeline` 权限、高风险确认、写后新鲜读取和审计；
- 当前中文九列导入、预览和驱动校验链。

参考项目源码和现场点表显示：

- 地址前缀共四类：`D`、`HD`、`M`、`HM`；
- 现场点表共 59 个点：D 13 个、HD 21 个、M 21 个、HM 4 个；
- 类型为 Float 32 个、Boolean 25 个、DWord 1 个、Word 1 个；
- 当前点表没有 `D10.0` 一类寄存器位地址；
- 参考实现采用 Modbus TCP、端口 502、站号 1；
- Float 和 32 位整数采用低字在前、高字在后。
- `XDPModbus.cs` 的 DWord 读取明确执行 `unchecked((int)(...))`，参考代码按有符号 32 位整数解释；这不等于 PLC 变量的业务定义已经确认。
- 参考读取按点执行，每次读取 1 位或 1～2 个寄存器；当前模板规划器允许跨地址空隙合并。这两种请求形态的现场证据不能互相替代。

参考证据定位（均相对上述参考项目根目录）：`XDPModbus.cs` 的 `GetModbusAddress()`、逐点读取及写入实现，以及 `publish/linux-x64/OPC/PLC_XDP.csv`。现场点表仅作为地址/类型与离线测试样本，不作为默认生产配置，也不直接继承其中的 R/W 权限。

### 2.2 用户提供的现场证据

用户确认该信捷 PLC 已经在现场实测，通信和收发正常。

该证据足以把参考实现提升为“当前现场兼容性基线”，但还不能自动证明：

- 支持所有信捷 PLC 型号和固件；
- `FD/SD/SM/X/Y/S` 地址也已验证；
- 寄存器位地址已验证；
- 所有 59 个点都做过双向写入；
- 断网重连、长时间运行和 UOS 部署均已验证；
- DWord 的有符号/无符号语义已经明确。
- 连续批读、跨空隙批读和协议允许的最大请求数量均已在该 PLC 上通过。

### 2.3 本方案的工程判断

本方案选择“Modbus TCP 设备系列适配”，而不是“独立信捷驱动”，原因如下：

- 信捷现场链路实际使用标准 Modbus TCP 功能码；
- 当前模板已经有完整、质量更高的 Modbus 运行时；
- 差异主要是地址命名和设备级字序，不是新的传输协议；
- 独立驱动会重复连接、重试、批读、状态和安全写入代码，维护成本更高。

## 3. 强制范围

### 3.1 本次必须完成

1. 在 Modbus TCP 设备系列中增加“信捷 PLC（现场验证配置 V1）”。
2. 支持原生地址 `D<number>`、`HD<number>`、`M<number>`、`HM<number>`。
3. 将原生地址稳定转换为标准 Modbus 地址。
4. 点位编辑界面对信捷系列显示一个原生地址输入框，不要求客户手算 41088 或 49408。
5. 当前中文九列模板可以在目标设备为信捷系列时直接填写上述原生地址。
6. 配置校验、重复地址校验、批量读取、单点读取和写入使用同一套地址转换结果。
7. 信捷系列强制使用大端字节、低字在前；端口默认建议 502，站号默认建议 1。
8. 写入仍通过现有安全管线，不增加任何旁路按钮或直接 NModbus 调用。
9. 增加单元、集成、App/ViewModel 和必要的 Headless 测试。
10. 更新驱动支持矩阵和设备点位导入说明。

### 3.2 明确不做

- 不修改参考项目。
- 不把参考项目命名中的“OPC”带入模板；本次不是 OPC UA/DA 功能。
- 不开发同事项目 CSV 到模板 CSV 的转换工具。
- 不修改当前九列表头、列顺序或导入入口。
- 不新增 NuGet 包；继续使用当前 `NModbus 3.0.83`。
- 不新增配置 Schema 版本，不修改历史迁移器。
- 不支持 Modbus RTU、RTU over TCP 或串口服务器模式下的信捷适配。
- 不支持自动扫描 IP、站号、地址、类型或字序。
- 不支持广播写、批量跨点写、掩码写或厂家扩展功能码。
- 不支持未现场出现的 `FD/SD/SM/X/Y/S`。
- 不支持 `D10.0`、`HD20.3` 等寄存器位地址。
- 不因连接失败自动回退到 Simulation。
- 不将 TCP Socket 建立成功显示为“PLC 通信正常”。
- 不在模板默认配置中硬编码现场 IP。
- 不默认导入参考项目的 59 个业务点位。

## 4. 地址和类型合同

### 4.1 第一版地址映射

| 信捷地址 | 标准 Modbus 地址 | 数据区 | 读取 | 写入 |
|---|---:|---|---|---|
| `D100` | `HR:100` | 保持寄存器 | 03 | 06/16 |
| `HD1000` | `HR:42088` | 保持寄存器 | 03 | 06/16 |
| `M400` | `C:400` | 线圈 | 01 | 05 |
| `HM40` | `C:49448` | 线圈 | 01 | 05 |

换算公式固定为：

```text
D(n)  = HR:n
HD(n) = HR:(41088 + n)
M(n)  = C:n
HM(n) = C:(49408 + n)
```

### 4.2 解析规则

允许：

- 前缀大小写不敏感，保存时统一大写；
- 地址整体前后空白可以清理；
- 数字部分必须为十进制非负整数。

拒绝：

- 空地址；
- 缺少前缀，例如 `100`；
- 包含内部空格，例如 `HD 1000`；
- 负数、小数、十六进制字符串；
- `D10.0` 一类位地址；
- 换算结果超过 0～65535；
- 第一版范围外前缀。

注意：当前尚未取得精确 PLC 型号及对应手册版本，因此第一版只校验前缀和协议偏移，不宣称已经完成厂家软元件上限校验。取得准确型号后，应另加型号范围表，不能把参考源码中的宽范围注释直接当成事实。

### 4.3 数据类型合同

| 地址区 | 第一版允许类型 | 现场证据 | 第一版处理 |
|---|---|---|---|
| M/HM | `Bool` | 25 个 Boolean 点 | 允许读写线圈 |
| D/HD | `UInt16` | 1 个 Word 点 | 允许 |
| D/HD | `Float32` | 32 个 Float 点 | 允许，大端字节、低字在前 |
| D/HD | `Int32` / `UInt32` | 1 个 DWord 点；参考代码按 Int32 解释，PLC 业务定义待确认 | 两种都允许显式选择，禁止自动猜测 |
| D/HD | `Int16` | 无当前现场证据 | 第一版信捷系列拒绝 |
| D/HD | `Double` | 无当前现场证据 | 第一版信捷系列拒绝 |
| D/HD | `Bool` | 当前点表无寄存器位地址 | 第一版拒绝 |

旧表中的 `DWord` 不自动转换成有符号或无符号。使用当前模板导入时，实施人员必须在“数据类型”列明确选择“长整型”或“双字”。

### 4.4 设备级参数合同

```text
DriverKey        = modbus-tcp
Model            = XINJE_FIELD_V1
ModbusTcp.Port   = 502（建议默认，可编辑）
ModbusUnitId     = 1（建议默认，可编辑 1-247）
ByteOrder        = BigEndian
WordOrder        = LowWordFirst
```

信捷系列的字节序和字序属于该现场兼容配置的固定合同。配置校验必须拒绝其他组合，不能只在界面上给出提示后继续保存。

轮询周期继续使用现有设备级 `PollIntervalMs`。不要照搬参考项目固定 50 ms 线程循环；新建设备仍使用模板当前默认值，现场确认负载后再调整。

### 4.5 地址占用范围与重复规则

- 单点完整范围必须满足 `offset + length <= 65536`，加法采用不会先溢出的整数检查；线圈长度为 1，UInt16 为 1 个寄存器，Float32/Int32/UInt32 为 2 个寄存器。
- 同设备、同标准数据区按半开区间 `[offset, offset + length)` 判定重叠。第一版拒绝重叠，不引入同地址多类型视图或别名功能。
- `D100/Float32` 与 `D101/UInt16` 必须在保存和预览时拒绝；`D65535/Float32` 必须拒绝，`D65535/UInt16` 可通过协议边界检查，但不代表厂家地址范围有效。
- 按当前宽范围公式，`D42088` 与 `HD1000`、`M49448` 与 `HM40` 分别是同一协议地址，必须报冲突；这是公式别名测试，不表示这些 D/M 编号经过厂家手册确认。
- 范围检查在完整配置校验和导入合并预览共用；更新点位时排除被替换的旧身份，再与其他点比较，不能把正常更新误判为与自身重叠。
- 本次新增范围校验针对信捷系列；通用 Modbus 保留现有校验和运行时重叠拒绝行为，不借本次适配扩大其他系列的配置限制。

### 4.6 信捷 V1 批读合同

- 复用 `ModbusReadBatchPlanner`，增加内部策略参数，默认策略保持当前通用 Modbus 的跨空隙合并行为；信捷显式选择仅连续合并策略。
- 按数据区及原生地址区 D/HD/M/HM 分组后排序，只在下一点起始偏移等于当前结束偏移时合并；不跨原生区边界，不拆开一个多寄存器值。
- 仍受现有 2000 位/125 寄存器软件上限约束。这是规划器上限，不是已确认的该 PLC 最大能力；实际配置产生的连续请求长度必须列入现场只读验收。
- 例如 HD1000/Float32 与 HD1002/Float32 可合为 4 个寄存器，HD1008 与 HD1020 之间存在空隙，必须拆批。不得默认形成覆盖 HD1000～HD1075 的 76 寄存器请求。
- 第一版不新增客户可编辑的批读参数、不升 Schema、不做自动探测或失败后自动扩大/改变读取范围。跨空隙策略及更大范围的现场优化另行评审。
- 批次出现协议异常、超时或取消时，沿用现有连接失效和失败传播；不把未读点伪造为成功，不在此适配中引入部分成功缓存或逐点自动降级。单点读取成功不能替代整组批读验收。

## 5. 目标调用链

```text
设备向导选择 Modbus TCP / 信捷现场配置 V1
                    ↓
点位编辑或当前九列模板导入 D/HD/M/HM 地址
                    ↓
XinjeFieldV1AddressParser
原生地址 → ModbusArea + 零基 Offset
                    ↓
ModbusTcpDriverDescriptor
型号、地址、类型、字序、重复地址校验
                    ↓
DeviceConfigurationService
权限 → 活动试验锁 → 候选配置 → 候选运行时 → CommitActive → Publish/回滚
                    ↓
ModbusDeviceRuntimeBase（配置激活后的运行时）
批量读取 / 连接失效 / 状态 / 质量 / 时间戳

运行期间的写入入口：
界面命令 → DeviceWritePipeline
权限 → 可写性 → 高风险确认 → 运行时写入 → ReadFresh → 相等比对 → 审计
```

任何 ViewModel 都不得持有 `TcpClient`、`IModbusMaster` 或直接发送功能码。

## 6. 新增代码合同

### 6.1 设备系列键

文件：`src/XXX.TestBench.Core/Domain/Devices/DevicePointTypes.cs`

新增：

```csharp
public static class DeviceModelKeyCatalog
{
    public const string XinjeFieldV1 = "XINJE_FIELD_V1";
}
```

不要新增 `DevicePointProtocol.Xinje`，因为线路协议仍是 Modbus TCP。

### 6.2 信捷地址解析器

新增文件：`src/XXX.TestBench.Devices/Modbus/XinjeFieldV1AddressParser.cs`

建议公开合同：

```csharp
public enum XinjeFieldV1Area
{
    D,
    HD,
    M,
    HM
}

public readonly record struct XinjeFieldV1Address(
    XinjeFieldV1Area Area,
    int Number,
    ModbusAddress ModbusAddress)
{
    public string Canonical => $"{Area}{Number}";
}

public static class XinjeFieldV1AddressParser
{
    public static bool TryParse(
        string? text,
        out XinjeFieldV1Address address,
        out string? error);

    public static XinjeFieldV1Address Parse(string text);
}
```

实现要求：

- 先解析和校验，再做加法，再检查 0～65535；禁止先强转 `ushort` 造成溢出回绕。
- 数字解析及基址加法都必须防溢出，超长数字和 `HD2147483647` 等输入由 TryParse 返回 false 与中文错误，不向 UI 抛出未处理的 OverflowException。原生规范文本去除前导零，使用不受区域设置影响的十进制格式。
- 错误信息使用客户可读中文，并指出支持的四类地址。
- 解析器是纯函数，不读取配置文件，不访问网络，不依赖 UI。

### 6.3 统一 Modbus 点位地址解析

新增文件：`src/XXX.TestBench.Devices/Modbus/ModbusPointAddressResolver.cs`

建议公开合同：

```csharp
public static class ModbusPointAddressResolver
{
    public static bool IsXinjeFieldV1(string? model);

    public static bool TryResolve(
        DeviceConfig.DeviceEntry device,
        PointsConfig.PointEntry point,
        out ModbusAddress address,
        out string? error);

    public static bool TryResolve(
        DeviceConfig.DeviceEntry device,
        DevicePoint point,
        out ModbusAddress address,
        out string? error);

    public static ModbusAddress Resolve(
        DeviceConfig.DeviceEntry device,
        DevicePoint point);
}
```

行为：

- 仅 `DriverKey=modbus-tcp` 且 `Model=XINJE_FIELD_V1` 使用 `XinjeFieldV1AddressParser`；型号匹配采用统一的大小写不敏感比较，不能仅凭地址前缀识别系列；
- 其他 Modbus TCP/RTU 型号继续使用现有 `ModbusAddressParser`；
- 如果同时存在 `AddressDefinition.Area/Offset`，必须与解析结果一致，不一致直接报错；
- 返回结果是后续校验、批读、读写和重复地址判断的唯一依据。

### 6.4 运行时规范地址副本

不要改变 `ListPointsAsync()` 返回给上层的信捷可读地址。运行时内部需要标准地址时，创建记录副本：

```csharp
private DevicePoint ToProtocolPoint(DevicePoint point)
{
    var address = ModbusPointAddressResolver.Resolve(_device, point);
    return point with { Address = address.Canonical };
}
```

该副本只用于 `ModbusReadBatchPlanner` 和 `ModbusValueCodec`；缓存主键仍为 `PointId`，界面和配置仍显示 `D100`、`HD1000` 等原生地址。

当前 `ReadManyFreshAsync()` 直接用 `item.Point` 调用 `CreateGoodValue/CreateBadValue`。改造后必须按 `item.Point.PointId` 从运行时原始点位集合取回原点，再生成 `PointValue`、业务日志和错误显示；不能把规范副本直接返回上层。写入结果同样使用原点。保留 PointId、DeviceId、原生 Address、当前连接代次、配置 Revision、RawValue 和工程值的既有语义，不修改共享的 `AddressDefinition` 对象。

### 6.5 带设备上下文的描述器与范围合同

在 `src/XXX.TestBench.Core/Ports/IDeviceDriverDescriptor.cs` 保留现有单参数方法，增加默认实现的重载及可选范围入口：

```csharp
string NormalizeAddress(PointsConfig.PointEntry point, DeviceConfig.DeviceEntry device)
    => NormalizeAddress(point);

DevicePointAddressRange? GetAddressRange(
    PointsConfig.PointEntry point, DeviceConfig.DeviceEntry device) => null;
```

新增 `src/XXX.TestBench.Core/Domain/Devices/DevicePointAddressRange.cs`，定义驱动无关的 `record DevicePointAddressRange(string Area, int Offset, int Length)`；不引用 Devices 中的 Modbus 类型，不写入 JSON。设备身份由调用方提供，Area 由描述器返回规范字符串。

- Modbus TCP 描述器显式实现新重载，以 resolver 返回 `HR:n/C:n`；对信捷有效点返回占用范围，其他型号返回 null，维持原有行为。
- 无设备参数的旧方法只保留原来的通用地址行为，不自动接受 D/HD/M/HM。所有需要识别信捷的生产调用必须改用带设备重载。
- `MultiDeviceConfigurationValidator` 和 `DevicePointImportValidator` 当前都是先 NormalizeAddress 再 ValidatePoint；两处均须传入已解析的所属设备，并在单点校验通过后收集范围冲突。
- 新增 `src/XXX.TestBench.Core/Configuration/DevicePointAddressRangeValidator.cs`：只负责同设备、同区间的越界/重叠计算，错误需包含冲突点名称和范围。范围由描述器提供，Core 不重写信捷公式和 Modbus 类型长度表。
- Simulation、Siemens、RTU、未实现驱动描述器及测试替身通过默认实现保持原行为；对所有 NormalizeAddress 调用做定向搜索，验证没有漏掉上下文入口。不要为本次适配强行重构全部驱动。

### 6.6 导入双侧规范化与快照约束

`PointEntry.CanonicalAddress` 为 `[JsonIgnore]` 内存字段；重新加载配置后不能依赖它。`AddressDefinition.ToCanonical()` 生成的是结构键（如 `area=hr;offset=42088`），不能与 `HR:42088` 混合作为同一比较空间的键。

1. 获取同一份 Active Snapshot，深复制已有点与导入点；不为预览原地改写共享配置、运行时点位或可变的 AddressDefinition。
2. 解析所属设备后，对两侧全部点使用带设备重载重新生成规范键；忽略并覆盖旧的内存 CanonicalAddress，不信任 CSV 或旧内存值。
3. 对信捷新点保存大写、去前导零的原生 Address；OriginalAddress 保留用户输入供预览。派生 Area/Offset 由 resolver 写入候选对象。已有结构化字段与文本不一致时拒绝，不静默修正。
4. `DevicePointImportPlanner.Build` 增加用于设备上下文规范化和范围查询的可选委托参数/重载（只读、无 I/O，传入已加载的设备与描述器映射）。正式页面导入必须传入；旧调用保持既有行为。在合并候选生成后重新计算候选规范键和范围，不能沿用可能失效的旧缓存。
5. 先按原有 PointId、同设备编码、同设备标签规则确定替换关系，再做规范地址及范围冲突检查。已有点、新行、同一文件后续行均纳入；冲突落在对应预览行并阻止应用。规范化失败不得对信捷回退为原始文本继续比较。
6. `SourceRevision` 与 `SourceCatalogFingerprint` 仍从原始 Active Snapshot 计算，不能从补写派生字段后的副本计算。应用前沿用修订/指纹检查和完整配置校验，不能信任先前的预览缓存代替最终校验。

该流程同时覆盖保存后重新加载、重复导入、普通 Modbus 与信捷设备并存，不增加旧表头转换器。

委托类型使用 `Func<PointsConfig.PointEntry, string>` 和 `Func<PointsConfig.PointEntry, DevicePointAddressRange?>`；闭包通过候选 DeviceId 查找同一快照内设备，不能捕获会变化的当前 UI 选择。规范化异常转换为对应行冲突，不中断成未处理异常。对于无关非信捷点，范围委托返回 null。

## 7. 逐文件编码清单

### 7.1 必须新增

| 文件 | 内容 |
|---|---|
| `src/XXX.TestBench.Devices/Modbus/XinjeFieldV1AddressParser.cs` | 四类信捷地址解析、换算、错误信息 |
| `src/XXX.TestBench.Devices/Modbus/ModbusPointAddressResolver.cs` | 按设备系列选择标准或信捷地址解析 |
| `src/XXX.TestBench.Core/Domain/Devices/DevicePointAddressRange.cs` | 不持久化的驱动无关地址范围 |
| `src/XXX.TestBench.Core/Configuration/DevicePointAddressRangeValidator.cs` | 同设备范围越界/重叠共用校验 |
| `tests/XXX.TestBench.Integration.Tests/XinjeFieldV1ModbusTests.cs` | 地址、描述器、批读、读写、错误传播测试 |
| `tests/XXX.TestBench.Core.Tests/DevicePointAddressRangeValidatorTests.cs` | 范围边界、重叠及不同设备隔离测试 |

其余测试优先补入第 9.6 节现有测试类；59 点样本可在新增集成测试中以内存数据固化地址和类型，不复制发布目录或新增生产 CSV 转换入口。

### 7.2 必须修改

#### `src/XXX.TestBench.Core/Domain/Devices/DevicePointTypes.cs`

- 增加 `DeviceModelKeyCatalog.XinjeFieldV1`。
- 不新增协议枚举，不改变存储协议字符串。

#### `src/XXX.TestBench.Devices/Drivers/ModbusTcpDriverDescriptor.cs`

- 在 `DeviceModels` 增加：

```text
Key: XINJE_FIELD_V1
DisplayName: 信捷 PLC（现场验证配置 V1）
Watermark: 如：D100、HD1000、M400、HM40
Hint: 仅支持现场已验证的 D/HD/M/HM；不支持寄存器位地址
```

- `ValidateDevice()` 对该系列强制：`BigEndian + LowWordFirst`。
- `ValidatePoint()` 改为使用 `ModbusPointAddressResolver`。
- 对信捷系列增加类型白名单；不得沿用通用 Modbus 的 `Double/Int16` 全集合。
- `NormalizeAddress(point, device)` 返回标准 `HR:n` 或 `C:n`；`GetAddressRange()` 为信捷点提供范围，供重复/重叠校验使用。
- 通用 Modbus TCP 和 Modbus RTU 行为保持不变。

#### `src/XXX.TestBench.Core/Ports/IDeviceDriverDescriptor.cs`

- 按第 6.5 节添加有默认实现的带设备重载和范围入口，保持旧描述器与测试替身兼容。

#### `src/XXX.TestBench.Core/Configuration/MultiDeviceConfigurationValidator.cs`

- `ValidatePointReferences()` 改为带设备规范化，先保留原有点位和协议校验，再执行信捷范围校验。
- 配置保存、启动加载及应用均不得只依赖 ViewModel 校验；错误包含冲突双方点位，配置无效时禁止激活。

#### `src/XXX.TestBench.Core/Configuration/DevicePointImportValidator.cs`

- `Validate()` 改为带设备规范化并调用共用范围校验；导入行之间的错误在预览前暴露。
- 对已有配置点的冲突由 Planner 的合并候选检查补齐，不能只校验本次文件内部。

#### `src/XXX.TestBench.Devices/Modbus/ModbusReadBatchPlanner.cs`

- 增加内部连续合并策略参数，默认值保持现有行为，具体规则按第 4.6 节。
- 原生区分组由运行时在生成规范副本前完成，不让通用规划器依赖信捷地址字符串。
- 保留最大请求长度、值不可拆分、协议边界和重叠拒绝检查。

#### `src/XXX.TestBench.Devices/Runtime/ModbusDeviceRuntimeBase.cs`

- `TryValidatePoint()` 使用统一 resolver，并复用信捷类型/字序及完整占用范围校验；不得只依赖界面筛选。合同纯函数由描述器和运行时复用，不复制两份白名单。
- `ReadManyFreshAsync()` 在批量规划前按原生区分组并生成内部规范副本，信捷显式使用连续合并策略，其他设备保持原策略。
- 成功/失败 `PointValue` 与业务日志按 PointId 使用原始点位生成，详见第 6.4 节。
- `WriteAsync()` 使用 resolver 取得真实区域和偏移。
- `ModbusValueCodec.EncodeRegisters()` 接收规范地址副本。
- 写失败继续抛出，禁止吞异常后显示成功。
- 不改变 TCP 连接所有权、请求超时、取消失效和状态机。

#### `src/XXX.TestBench.App/ViewModels/DeviceConfigurationViewModels.cs`

- 增加 `IsXinjeFieldV1` 派生属性。
- 增加明确的初始化保护，区分“加载已有设备”和“用户主动选择系列”；构造/回填过程中不得执行用户选择的默认值逻辑。
- `OnSelectedModelChanged()` 仅在用户主动选择该系列时：
  - 站号为空才建议填入 `1`；
  - 端口为空才建议填入 `502`；
  - 设置大端模式、低字在前；
  - 不填写 IP；
  - 不更改轮询周期。
- 编辑已有设备时以已保存值为准；如果字序不符合系列合同，显示错误并禁止保存，不要静默改配置。
- 合法信捷配置显示固定字序只读说明。历史异常配置显示保存值及错误，提供明确的“恢复信捷规定字序”修正动作，仅修改编辑候选，保存仍走配置服务。
- 切换离开该系列时不自动重写用户其他 Modbus 参数。
- 已有点位时切换系列，完整候选校验必须列出不兼容点并阻止保存；本次不自动把 HR/C 地址转为 D/HD/M/HM，也不反向转换。

#### `src/XXX.TestBench.App/Views/DeviceEditorDialogWindow.axaml`

- 信捷系列显示固定字序说明及异常配置修正入口；通用 Modbus 的字序选择控件保持原状。
- 初始加载不触发修正，修正动作不访问 PLC，不增加独立设备编辑页面。

#### `src/XXX.TestBench.App/ViewModels/DevicePointDialogViewModel.cs`

新增或调整属性：

```csharp
public bool IsXinjeFieldV1 { get; }
public bool IsStructuredModbusAddressVisible => IsModbus && !IsXinjeFieldV1;
public bool IsStandardAddressVisible => !IsModbus || IsXinjeFieldV1;
```

改造要求：

- 信捷系列显示原生地址文本框；通用 Modbus 继续显示“数据区 + 零基偏移”。
- `TryGetModbusAddress()` 对信捷系列解析 `Address`，其他系列保持现有结构化表单。
- 地址前缀变化后刷新可选数据类型：M/HM 只显示 Bool；D/HD 只显示 V1 寄存器类型。
- `IsModbusBitArea/IsModbusRegisterArea/IsModbusWritableArea/IsModbusReadOnlyArea`、`RefreshModbusDataTypeOptions()`、完整性校验及预览统一消费解析后的实际区域，不能继续依赖已隐藏的 `SelectedModbusArea`。
- `OnAddressChanged()`、所属设备改变、编辑初始化和剪贴板回填后刷新同一组派生属性与命令可用性；空地址或不完整前缀显示错误/待输入并禁止保存，不沿用上一个有效区域。
- `AddressWatermark/AddressHint` 对信捷显示 D/HD/M/HM 示例。切换前缀导致当前类型不兼容时给出明确提示，不把旧数值/量程悄悄解释为新类型；沿用现有量程保存校验。
- `TryBuildSimplifiedResult()` 对信捷系列：
  - `Address` 保存大写规范原文；
  - `AddressDefinition.Area/Offset` 保存换算后的标准区域和偏移；
  - `Protocol` 仍保存 `ModbusTcp`；
  - `S7DecodeOptions` 必须为空。
- 访问权限继续使用现有只读、读写、读写高风险选项。
- 保存点位只生成候选配置，不向 PLC 写值。

#### `src/XXX.TestBench.App/Views/DevicePointDialogWindow.axaml`

- 将现有 Modbus 结构化地址区域的可见性改为 `IsStructuredModbusAddressVisible`。
- 原生地址文本框继续绑定 `Address`、`AddressWatermark`、`AddressHint`。
- 不新增单独的信捷页面或第二个点位弹窗。
- 不调整无关布局、字号和主题。

#### `src/XXX.TestBench.App/ViewModels/DevicePointManagementViewModel.Import.cs`

- `ValidateImportedDriverAddresses()` 的底层导入校验先接通带设备重载；不能只在其返回成功后补写地址而遗漏前面的标准解析入口。
- `BuildImportPipelineAsync/BuildImportPlan` 按第 6.6 节对已有点与导入点的独立副本统一规范化，向 Planner 提供已捕获设备映射的只读规范化/范围委托。
- 原始输入保存在 `OriginalAddress`，候选 `Address` 保存规范原生地址，`CanonicalAddress` 保存标准协议键；结构化字段仅对候选对象派生并校验一致性。
- 修订及指纹从未加工的 Active Snapshot 取值，预览同时显示原始输入和规范结果。
- 不增加旧 CSV 表头兼容。

#### `src/XXX.TestBench.Core/Application/DevicePointImportPlanner.cs`

- 增加第 6.6 节的上下文委托重载；正式导入路径通过统一规范化计算已有点、导入点及合并候选的键。
- 信捷路径不允许将缺失 CanonicalAddress 回退为结构键/原始文本继续比较；无上下文的旧重载只保留原有调用行为。Core 不直接引用 Devices 的解析器。
- 范围检查使用第 6.5 节共用校验器；同一身份更新先替换旧项，冲突归入相应行，预览与应用一致拒绝。
- 确保 `D100` 与 `d100` 在同一信捷设备下被规范为同一协议地址并报冲突；信捷系列不接受客户混填 `HR:100`。
- 不改变 PointId、同设备编码、同设备标签的既有匹配顺序。

#### `src/XXX.TestBench.App/ViewModels/DevicePointImportPreviewViewModel.cs` 及 `Views/DevicePointImportPreviewWindow.axaml`

- 沿用现有 OriginalAddress/CanonicalAddress 字段和展示，不新增第二套预览模型；核对规范键、冲突点名称和行号显示。已有绑定满足要求时不必修改 XAML。
- 兼容构造函数的直接 Planner 调用不作为信捷正式导入入口；新增信捷测试使用已完成上下文规范化的计划。

#### 测试与兼容调用方

- 定向检查 Simulation、Siemens、RTU、DriverRegistry 中描述器及测试替身的接口兼容性；默认实现满足需求时不改这些文件。
- 核对点位剪贴板回填及导出链是否保留原生 Address；仅在缺失通知/字段复制时修改相应文件，不扩大到无关布局或格式调整。

#### 文档

- `docs/设备驱动支持矩阵.md`：增加信捷现场配置 V1，并标注受限地址区和现场证据边界。
- `docs/设备点位管理与导入说明.md`：补充当前九列模板中的信捷地址填写示例。
- 不修改 `docs/设备点位导入模板.csv` 的表头和结构；如需示例，仅替换文档示例，不向默认模板塞入现场业务点。

### 7.3 不应修改

- `DeviceConfigurationMigrator.cs`：配置结构没有变化，不升 Schema。
- `NModbusClientAdapter.cs`：功能码已经满足 V1。
- `ChannelManager.cs`：信捷仍是普通 Modbus TCP 独立设备连接。
- `DeviceWritePipeline.cs`：复用既有安全链，不为信捷开特例。
- `DevicePointImporter.cs` 的九列表头合同：保持当前导入格式。
- 参考项目下的任何源码、CSV、JSON、发布文件。

## 8. 编码阶段

### 阶段 A：保护工作树并锁定失败测试

1. 执行 `git status --short --branch`。
2. 对第 7 节所有目标现有文件执行定向 `git diff`。
3. 保留当前未提交修改；不得 reset、clean 或整体覆盖。
4. 先添加解析器和描述器失败测试，再实现功能。
5. 当前工作树中的未跟踪 `项目代码/XXX.TestBench.Devices` 不属于本方案，不得误改。

完成条件：现有差异已记录，新测试能准确暴露“信捷地址不能解析”的当前缺口。

### 阶段 B：实现地址解析和描述器合同

1. 添加设备系列键。
2. 实现纯地址解析器。
3. 实现统一地址 resolver。
4. 在 Modbus TCP 描述器中注册信捷系列。
5. 接通带设备的描述器重载及两个核心校验器，完成型号、字序、类型、重复地址和占用范围校验。

完成条件：无需网络即可验证全部映射、拒绝规则和通用 Modbus 回归。

### 阶段 C：接入现有 Modbus 运行时

1. 读批次前生成规范地址副本。
2. 单点读取使用相同 resolver。
3. 写入使用相同 resolver。
4. Float32、Int32、UInt32 继续使用现有设备级字序 codec。
5. 写入异常继续向 `DeviceWritePipeline` 传播。
6. 信捷按原生区仅连续合并，结果与日志恢复原始点位身份；验证单点、批读及写后回读的一致性。

完成条件：假客户端可以验证正确功能码、地址、寄存器顺序和失败传播。

### 阶段 D：设备和点位界面

1. 设备系列下拉增加信捷系列。
2. 新建设备建议站号 1、端口 502、大端、低字在前。
3. 信捷点位使用原生地址输入；通用 Modbus 界面不变。
4. 地址前缀驱动数据类型筛选。
5. 保存结果同时保留原生地址和结构化 Modbus 地址。
6. 验证加载已有配置不触发默认值覆盖；前缀切换、错误输入、修正固定字序和已有点切换系列均有确定行为。

完成条件：客户不需要手算 41088/49408，且不能保存不受支持的地址、类型或字序。

### 阶段 E：接通当前导入流程

1. 保持当前九列格式。
2. 在目标设备解析完成后，使用同一描述器与设备快照规范化已有点和导入点的副本。
3. 预览显示原始地址和规范地址。
4. 导入计划按合并后的候选使用规范地址和占用范围做冲突检查，覆盖配置重新加载后的重复导入。
5. 应用仍走 `DeviceConfigurationService`。

完成条件：使用当前模板填写 `D100/HD1000/M400/HM40` 可以预览和应用；旧项目 CSV 仍被明确拒绝。

### 阶段 F：文档、回归和现场验证准备

1. 更新支持矩阵和导入说明。
2. 完成分层自动化测试。
3. 生成现场只读验证清单。
4. 未获得现场写入授权前，不执行真实 PLC 写入。

## 9. 自动化测试矩阵

### 9.1 地址解析

| 编号 | 用例 | 期望 |
|---|---|---|
| T01 | `D100` | `HR:100` |
| T02 | `HD1000` | `HR:42088` |
| T03 | `M400` | `C:400` |
| T04 | `HM40` | `C:49448` |
| T05 | 小写 `hd1000` | 解析成功，保存为 `HD1000` |
| T06 | `HD24448` 导致偏移 65536 | 拒绝，不得溢出回绕 |
| T07 | `D10.0` | 拒绝 |
| T08 | `FD0/SD0/SM0/X0/Y0/S0` | 第一版全部拒绝 |
| T09 | `100`、`D-1`、`HD 1`、超长数字、`HD2147483647` | 拒绝并返回明确中文错误，TryParse 不向外泄漏溢出异常 |

### 9.2 配置与描述器

| 编号 | 用例 | 期望 |
|---|---|---|
| T10 | 信捷系列 + TCP 通道 | 允许 |
| T11 | 信捷系列 + 串口通道 | 拒绝 |
| T12 | 信捷系列 + 大端/低字在前 | 允许 |
| T13 | 信捷系列 + 其他字序组合 | 拒绝 |
| T14 | M/HM + Bool | 允许 |
| T15 | M/HM + Float32 | 拒绝 |
| T16 | D/HD + Float32/UInt16/Int32/UInt32 | 允许 |
| T17 | D/HD + Bool/Double/Int16 | 第一版拒绝 |
| T18 | 同设备 `D100` 与 `d100` | 规范地址重复，拒绝 |
| T19 | 通用 Modbus TCP `HR:100` | 行为保持不变 |

### 9.3 运行时与字序

| 编号 | 用例 | 期望 |
|---|---|---|
| T20 | Float32 寄存器 `[0x0000, 0x3F80]` | 低字在前解码为 `1.0f` |
| T21 | 32 位值寄存器 `[0x5678, 0x1234]` | 组合为 `0x12345678` |
| T22 | D/HD 读 | 调用保持寄存器读取 |
| T23 | M/HM 读 | 调用线圈读取 |
| T24 | D/HD 单寄存器写 | 调用单寄存器写 |
| T25 | D/HD 多寄存器写 | 调用多寄存器写，低字在前 |
| T26 | M/HM 写 | 调用单线圈写 |
| T27 | 请求异常或超时 | 连接失效、状态降级、异常向上层传播 |
| T28 | 写失败 | 不得产生成功结果或伪造回读 |

### 9.4 界面与导入

| 编号 | 用例 | 期望 |
|---|---|---|
| T29 | 设备系列列表 | 出现“信捷 PLC（现场验证配置 V1）” |
| T30 | 选择信捷系列新建设备 | 建议站号 1、端口 502、大端、低字在前 |
| T31 | 编辑已有信捷设备错误字序 | 显示错误并禁止保存，不静默重写 |
| T32 | 信捷点位编辑 | 显示原生地址文本框，隐藏标准 Modbus 区域/偏移表单 |
| T33 | 输入 M/HM | 类型列表只保留 Bool |
| T34 | 输入 D/HD | 类型列表只保留 V1 寄存器类型 |
| T35 | 保存 `HD1000` | Address 保留 `HD1000`，AddressDefinition 为 HR/42088 |
| T36 | 当前九列模板导入 `D100` | 预览通过并显示规范地址 HR:100 |
| T37 | 旧项目英文表头 CSV | 按现有规则拒绝，不做转换 |
| T38 | 导入预览后 Active Revision 变化 | 拒绝旧计划，要求重新预览 |

### 9.5 v1.1 闭环与边界测试

| 编号 | 用例 | 期望 |
|---|---|---|
| T39 | 同一 D100 分别传入信捷 TCP、通用 TCP、RTU 设备上下文 | 仅信捷 TCP 接受，不按前缀猜型号 |
| T40 | 从完整配置与导入预校验进入 HD1000 | 两处均走带设备重载，不被旧 NormalizeAddress 提前拒绝 |
| T41 | 保存 HD1000，序列化并重新加载，再以新身份导入同地址 | 预览报冲突；不能出现预览通过、应用才报重复 |
| T42 | 已有 HD1000 与以新身份导入 HD01000 或 hd1000 | 规范为同一地址并报冲突；已有内存 CanonicalAddress 即使伪造/过期也不影响判断 |
| T43 | D42088 与 HD1000；M49448 与 HM40 | 在当前协议范围模型下识别别名冲突，不作为厂家范围证据 |
| T44 | D100/Float32 与 D101/UInt16 | 保存和导入预览均拒绝范围重叠 |
| T45 | D65535/UInt16、D65535/Float32；HD24447 同类型组合 | 单寄存器通过协议边界检查，双寄存器越界被拒绝 |
| T46 | 同身份点位更新；不同设备同地址；同设备不同区相同偏移 | 正常更新不与自身冲突，设备/数据区相互隔离 |
| T47 | HD1000、HD1002、HD1008、HD1020 均为 Float32 | 前两点合并，空隙拆批；请求不覆盖未配置空隙 |
| T48 | 假客户端对空隙返回非法地址，配置点本身可读 | 信捷连续策略不访问空隙；通用 Modbus 默认策略保持原样 |
| T49 | 连续点达到/超过批次上限，且含双寄存器值 | 不超过上限，不拆值，不跨原生区；结果覆盖每个配置点一次 |
| T50 | 多批次中的一个请求异常、超时或取消 | 沿用失败传播和连接失效，不返回未完成点的 Good，不自动重试写入 |
| T51 | HD1000 单读、批读、写返回、写后回读及解码失败 | 上层 Address 始终是 HD1000；PointId/DeviceId/Revision/连接代次符合当前运行时合同 |
| T52 | Address=HD1000，结构字段却为 HR/1000 | 编辑候选、完整配置及导入均拒绝不一致，不静默修复 |
| T53 | 新建、加载合法/错误字序、主动选择/离开信捷系列 | 加载不覆盖；固定字序只读；明确修正动作后才能保存错误配置 |
| T54 | 原生输入 D→M→空值→非法前缀，再恢复 HD；编辑与剪贴板回填 | 类型/权限/提示/保存命令同步刷新，不残留隐藏下拉区域 |
| T55 | 有点设备从通用切换信捷，或反向切换 | 不兼容原地址被完整校验拒绝，不自动转换或丢点 |
| T56 | 寄存器 [0x0000,0x8000] 和 [0xFFFF,0xFFFF] 按 Int32/UInt32 读写 | 分别得到 -2147483648/2147483648、-1/4294967295，编码逆向一致 |
| T57 | 59 点只读内存样本进入规划器和假客户端 | 59 点均正确覆盖，区/偏移/类型一致，无跨空隙请求；不写 PLC、不导入默认配置 |
| T58 | 预览前后比较原始配置；导出后再导入信捷点 | 预览不修改共享快照及其指纹；导出保留原生地址，再导入匹配同一身份无新增重复 |
| T59 | SourceRevision 不变但目录指纹变化；接口兼容与既有回归 | 旧计划被拒绝，S7/Simulation/通用 TCP/RTU 的原有合同不变 |
| T60 | 新版本通过 ApplyAsync 恢复旧版支持的完整快照，再用旧版验证加载 | 候选应用失败保护属于本阶段保留的软件条件；按用户 2026-09-24 确认，旧程序加载及配套回退演练是未来真实部署参考，不是当前开发阶段条件 |

所有样本仅为软件测试，非现场验证结论。

### 9.6 测试落点与结果追踪

- `Integration.Tests/XinjeFieldV1ModbusTests.cs`：解析、描述器、核心校验集成、假客户端、批读、编解码、结果身份与 59 点内存样本。
- `Core.Tests/DevicePointAddressRangeValidatorTests.cs`、`DevicePointImportPlannerTests.cs`：范围冲突、候选更新、自身排除、双侧规范化、重新加载后的判重。用描述器/委托替身，Core.Tests 不为测试引入 Devices 层依赖。
- `App.Tests/DeviceConfigurationViewModelTests.cs`、`DevicePointDialogViewModelTests.cs`：初始化、系列切换、固定字序、前缀状态及保存行为。
- `App.Tests/DevicePointManagementViewModelTests.cs`、`DevicePointRemainingFunctionsTests.cs`：实际导入入口、预览、修订/指纹失效、复制粘贴及导出往返；不能只测试孤立 Planner。
- `App.Headless.Tests/ShellHeadlessTests.cs`：信捷/通用地址控件可见性和字序控件状态，不重复全部协议测试。
- T60 中候选应用失败保护使用现有配置服务测试夹具，仍需保留。用户确认本阶段不执行旧程序加载及回退演练；未来真实升级部署时再评估并记录程序与完整配置的配套回退证据。
- 实施时记录 T01～T60 对应测试方法、命令与实际结果。缺少用例或现场/发布条件未就绪时列为未完成，不以“适用”名义略过新加的闭环测试。

## 10. 构建和测试命令

所有临时输出放在 `D:\Codex相关\信捷PLC_ModbusTCP现场适配`，不写入项目目录。

```powershell
$projectRoot = 'D:\易峥\2026\2026-09\Avalonia_上位机通用模板\项目代码\XXX.TestBench.Template'
$artifactRoot = 'D:\Codex相关\信捷PLC_ModbusTCP现场适配\artifacts'
$resultRoot = 'D:\Codex相关\信捷PLC_ModbusTCP现场适配\test-results'
$scratchRoot = 'D:\Codex相关\信捷PLC_ModbusTCP现场适配\temp'
New-Item -ItemType Directory -Force -Path $artifactRoot,$resultRoot,$scratchRoot | Out-Null
$env:TEMP = $scratchRoot
$env:TMP = $scratchRoot
# 仅在本次独立 PowerShell 会话生效；不修改用户/系统永久环境变量。

dotnet restore "$projectRoot\XXX.TestBench.Template.sln" --artifacts-path $artifactRoot

dotnet build "$projectRoot\XXX.TestBench.Template.sln" `
  -c Debug --no-restore --artifacts-path $artifactRoot

dotnet test "$projectRoot\tests\XXX.TestBench.Core.Tests\XXX.TestBench.Core.Tests.csproj" `
  -c Debug --no-restore --artifacts-path $artifactRoot `
  --results-directory "$resultRoot\Core" --logger trx

dotnet test "$projectRoot\tests\XXX.TestBench.Integration.Tests\XXX.TestBench.Integration.Tests.csproj" `
  -c Debug --no-restore --artifacts-path $artifactRoot `
  --results-directory "$resultRoot\Integration" --logger trx

dotnet test "$projectRoot\tests\XXX.TestBench.App.Tests\XXX.TestBench.App.Tests.csproj" `
  -c Debug --no-restore --artifacts-path $artifactRoot `
  --results-directory "$resultRoot\App" --logger trx

dotnet test "$projectRoot\tests\XXX.TestBench.App.Headless.Tests\XXX.TestBench.App.Headless.Tests.csproj" `
  -c Debug --no-restore --artifacts-path $artifactRoot `
  --results-directory "$resultRoot\Headless" --logger trx --blame-hang-timeout 60s
```

测试项目按顺序执行，避免共享输出导致 `CS2012`。只有命令明确完成并返回成功，才能记录为通过；挂起、超时或取消均记为未验证。

以上为完成阶段的全项目测试命令，避免按类名过滤漏掉点位管理、修订失效、复制粘贴及其他描述器回归。开发阶段允许定向执行新增测试，但不能替代最终命令。每条命令非零退出即先处理失败，不把后续成功覆盖前序失败；保存 TRX 并核对实际执行数量，零测试不算通过。旧程序与配置配套回退仅作为未来真实部署参考，本阶段不执行，也不作为当前开发交付条件。

## 11. 现场验收方案

### 11.1 前置资料

编码不必等待这些资料，但现场放开范围前必须补齐：

- PLC 精确型号和固件版本；
- PLC 程序版本或校验信息；
- 实测使用的 IP、端口和 UnitId；
- 允许验证的只读点位；
- 经现场负责人批准的安全可写测试点；
- Float、Word、DWord 的 PLC 监视值和工程含义；
- DWord 是有符号还是无符号。

### 11.2 只读验证

1. 先将设备设为 Hardware，但所有点位保持只读。
2. 分别选取 D、HD、M、HM 至少一个已知点。
3. 使用现有点位诊断 `ReadFreshAsync`，不创建第二连接。
4. 对照 PLC 监控或已知 HMI 值，记录原始寄存器、解析值、质量、时间戳和连接代次。
5. Float32 必须选一个非零、非整数特例值，避免字序错误被 0 值掩盖。
6. DWord 必须明确有符号/无符号后再判定。
7. 对实际点表生成连续批次清单，记录每批原生区、标准起始偏移、数量和结果；验证单点及连续批读结果一致。当前 V1 不发跨空隙请求，125/2000 上限不能写成已通过的现场能力。

只读通过条件：四类地址中实际使用的类别及实际连续请求长度均能稳定读取，值与 PLC 监控一致，断开网络后质量和状态正确降级，恢复后由读取恢复在线。若连续请求失败，记录功能码/范围/错误，保持未验收，按证据修订策略，不自动猜测或静默退回逐点读取。

### 11.3 写入验证

真实写入必须单独获得现场授权，并满足：

- 只使用明确标记的安全测试点；
- 不写 DO、安全互锁、启停、急停、复位、配方或自动流程关键点；
- 写前记录原值，写后执行新鲜读取并比对，再恢复原值；
- 高风险点必须经过模板现有二次确认；
- 写入失败、超时或回读不一致必须显示失败并写审计日志；
- 不自动重试写操作，避免结果未知时重复动作。

### 11.4 建议验收时长

以下是方案建议值，不是当前已完成事实：

- 功能验证：连续 30 分钟，覆盖读取、一次安全写入、断网和恢复；
- 交付前稳定性：连续 2 小时，记录请求数、失败数、重连次数和最大恢复时间；
- UOS 验收另列，Windows 现场通过不能替代 UOS 运行证据。

## 12. 风险、隐藏变量和控制措施

| 风险或变量 | 影响 | 控制措施 |
|---|---|---|
| PLC 精确型号未知 | 地址范围可能随系列/版本不同 | 配置命名为 FIELD_V1；只开放四类现场前缀；补型号后再扩范围 |
| 现场“收发正常”未明确覆盖每种类型 | Float/DWord/Word 证据强度不同 | 现场验收按类型逐项记录 |
| DWord 有符号性不明确 | 大数值可能解释错误 | 当前模板要求显式选 Int32 或 UInt32，禁止自动转换 |
| 当前工作树大量未提交改动 | 目标文件可能与其他工作重叠 | 逐文件 diff，小补丁，禁止覆盖和格式化无关代码 |
| 59 点轮询负载 | 过短周期可能增加 PLC 和网络负载 | 复用批读；不复制 50 ms 循环；轮询周期现场测量后设置 |
| 原生地址与标准地址可能重复 | 两个标签可能指向同一物理点 | 描述器统一规范化，导入和配置校验按规范地址判重 |
| 字序配置被误改 | Float/DWord 值错误但通信仍成功 | 信捷系列强制大端、低字在前；非零特例值现场比对 |
| 只测试 TCP 连接 | 可能把端口开放误判为 PLC 正常 | UI 保留“传输连接成功，尚未读取点位”分级 |
| 写异常被吞 | UI 可能误报成功 | 运行时必须抛出；只由 WritePipeline 完成成功判定 |
| UOS 安装目录不可写 | 配置/日志运行失败 | 属于独立 UOS 部署整改，不在本次信捷适配中混做 |
| 规范化接口缺少型号上下文 | 信捷被标准解析提前拒绝，或通用设备错误接受信捷地址 | 带设备重载贯穿完整校验、导入及运行时 |
| CanonicalAddress 不持久化或旧值失效 | 重启后预览漏报重复 | 原始快照复制后双侧重新规范化，合并候选再次计算 |
| 原生点与协议副本混用 | 读取结果地址变成 HR/C，日志与配置不一致 | 按 PointId 使用原点生成对外结果 |
| 连续请求与逐点请求现场能力不同 | 单点正常但批读失败 | 初始仅连续合并，实际请求长度逐项现场验收 |
| 双寄存器跨界或部分重叠 | 保存成功后轮询才失败 | 保存、预览、运行时复用完整范围校验 |
| 未来真实部署时配置已含新型号但仅回退程序 | 旧程序可能拒绝加载 | 作为未来部署风险，部署前再验证完整配置与程序配套回退；按用户确认不阻塞当前模板开发阶段 |

## 13. 回滚方案

本节只作为未来真实升级部署时的回退参考。本阶段按用户 2026-09-24 确认不执行旧程序/旧配置演练，不要求恢复当前测试点位，也不新增恢复工具；现有配置候选应用失败保护继续保留。未来部署仍需注意：本次不升 Schema，但新系列与原生地址是新增语义，不代表旧程序可以读取新配置。

1. 部署前保存旧程序完整发布包、版本标识及当时完整配置快照/Revision，含 manifest 和配置引用关系；在隔离目录验证配套关系。正式回退包是交付物，演练缓存放在 `D:\Codex相关\信捷PLC_ModbusTCP现场适配\rollback-check`。
2. 仅编码阶段尚未应用新配置时，以本次变更清单逐项撤销本次补丁；包括公共接口、核心校验、Planner、运行时、界面及测试，不得 reset/clean 或覆盖其他未提交工作。Git 逆操作不等于现场配置回滚。
3. 已应用信捷配置时，先停止活动试验并禁止手动设备动作，核对旧快照是否会撤销上线后的其他业务配置。存在后续改动时先生成兼容旧版的完整候选并审查差异，不能盲目恢复历史快照。
4. 在支持新系列的当前版本中，通过 `DeviceConfigurationService.ApplyAsync(actor, snapshot, ...)` 的正常权限、完整校验、候选激活及提交链应用已核对的旧版兼容快照；保留当前版本生成的修订与审计记录，不强行复用历史 Revision 标识。相关点位、绑定及仿真等引用必须作为完整快照保持一致。
5. 确认配置应用成功，再正常停止新程序、切换已保留的旧发布包，用旧程序验证兼容配置加载及启动。演练不执行真实 PLC 写入，旧程序对 Hardware 的启动行为必须在隔离环境下受控。
6. 如果旧兼容快照应用失败，保留当前程序及当前 Active Revision，不继续切换旧程序；使用配置服务的既有回滚机制，不手工覆盖 active.json，不自动降级信捷为通用设备。
7. 如果未来真实部署时新程序已无法启动，且没有可用的既有受控恢复入口，则该次发布回退条件尚未满足；先恢复可运行的新版本维护入口。不得把手工改 JSON 指针当成已实现的恢复能力。此条件适用于实际部署放行，不阻塞当前开发阶段。
8. 回退验收记录程序版本、回退前后 Revision、配置验证结果、旧版启动结果及失败保护。软件实现测试与发布回退演练分别记录；未经用户授权不执行 Git 提交、推送或真实 PLC 写入。

## 14. 完成定义

满足以下全部条件后，才能宣布“信捷现场配置 V1 软件实现完成”：

- [ ] 四类地址解析和拒绝规则已实现。
- [ ] 通用 Modbus TCP/RTU 原有行为无回归。
- [ ] 信捷系列的字序和类型合同由核心校验强制执行。
- [ ] 点位编辑器可直接填写 D/HD/M/HM。
- [ ] 当前九列导入可以校验信捷原生地址。
- [ ] 旧 CSV 转换功能没有被加入。
- [ ] 读取继续批量执行，没有逐点固定 50 ms 线程。
- [ ] 信捷仅连续合并且不跨原生区，通用 Modbus 默认策略不变。
- [ ] 规范化带设备上下文；保存后重新加载再导入仍正确判重。
- [ ] 多寄存器越界、同区占用重叠及公式别名在保存和预览时拒绝。
- [ ] 协议副本不泄漏到对外 PointValue 地址，原点身份和运行时元数据保持正确。
- [ ] 已有设备加载不覆盖参数，原生地址编辑所有派生状态同步。
- [ ] 写入只经过 `DeviceWritePipeline`。
- [ ] 写失败和回读不一致不会显示成功。
- [ ] 配置仍走候选 Revision、激活、提交和回滚。
- [ ] T01～T60 的软件自动化部分完成映射并通过；T60 候选应用失败保护保留，旧程序回退演练按用户确认列为未来部署参考，不冒充自动化结果。
- [ ] 驱动支持矩阵和导入说明已同步。
- [ ] 构建与测试结果按实际命令记录，没有把历史结果算作本轮结果。

本阶段开发交付不要求保留旧发布包、恢复测试点位或通过第 13 节配套回退演练。未来真实升级部署时，应按部署范围评估旧发布包、兼容配置快照和配套回退演练；这与当前软件修正完成状态分别记录。

满足下列条件后，才能宣布“现场验证完成”：

- [ ] PLC 型号、固件、程序版本和通信参数已记录。
- [ ] 实际使用的 D/HD/M/HM 类型逐项比对通过。
- [ ] Float32 字序使用非零特例值确认。
- [ ] DWord 有符号性确认。
- [ ] 实际连续批读请求长度逐项验证，结果与单点读取一致。
- [ ] 经授权的安全写入完成写后回读并恢复原值。
- [ ] 断网、超时、恢复行为符合状态和质量合同。
- [ ] 现场测试记录与软件自动化证据分开保存。

## 15. 实施顺序摘要

```text
A. 工作树保护与失败测试
→ B. 地址解析器与描述器
→ C. 复用现有 Modbus 运行时
→ D. 设备/点位界面
→ E. 当前九列导入链
→ F. 分层测试与文档
→ G. 现场只读验证
→ H. 经授权的安全写入验证
```

编码过程中如果发现必须新增协议枚举、绕过现有运行时、修改九列表头或引入旧 CSV 转换，视为超出本方案边界，应立即停止并重新评审，而不是边写边扩大范围。
