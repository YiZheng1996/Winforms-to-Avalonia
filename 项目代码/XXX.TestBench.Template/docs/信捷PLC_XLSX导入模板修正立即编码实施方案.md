# 信捷 PLC XLSX 导入模板修正立即编码实施方案

版本：v1.0  
编制日期：2026-09-24  
目标项目：`D:\易峥\2026\2026-09\Avalonia_上位机通用模板\项目代码\XXX.TestBench.Template`  
正式方案位置：`D:\易峥\2026\2026-09\Avalonia_上位机通用模板\项目代码\XXX.TestBench.Template\docs\信捷PLC_XLSX导入模板修正立即编码实施方案.md`

> 本文是可直接进入编码的实施方案，不是实施完成报告。本次编制仅新增本文件，没有修改业务代码、现有方案或测试记录，也没有运行构建、测试或操作 PLC。文中“新增”“修改”“验收”均是后续实施要求。

## 1. 本次要解决什么问题

当前信捷运行时已经接受原生地址，但下载给用户填写的 XLSX 模板还按通用 Modbus 生成，导致“按模板提示填写，却被导入器拒绝”。本次只修复这条用户操作链的不一致。

| 编号 | 当前问题 | 可观察的结果 | 本次目标 |
|---|---|---|---|
| F01 | 信捷设备的模板示例仍为 `HR:0`、`C:1`，说明和地址列批注也未针对信捷区分 | 用户照抄示例后，信捷地址校验拒绝导入 | 信捷示例、填写说明、列批注统一使用 D/HD/M/HM 原生地址规则 |
| F02 | 下载模板按通用 Modbus 驱动类型集合生成下拉项，未结合信捷型号筛选 | 信捷模板可以选“短整型”“双精度”，导入时却不支持 | 信捷模板仅提供当前 V1 支持的五种类型；混合设备模板取各设备有效类型的并集 |
| Q01 | 现有测试没有覆盖“从信捷设备下载空白 XLSX，再填入示例并导入”的完整流程 | 既有测试通过也不能发现 F01、F02 | 补充实际生成工作簿的检查，以及下载、填写、预览、应用、重新导入测试 |
| D01 | 原方案仍把旧程序配套回退演练列为当前完成条件 | 与用户已经确认的开发用途不一致 | 按本次用户确认调整阶段范围，保留原有软件失败保护 |

## 2. 已确认的用户要求与证据边界

### 2.1 本次采用的业务前提

1. 用户明确说明：同事代码已在真实 PLC 上进行输入、输出测试，可以使用。该信息作为已测参考实现的现场依据。
2. 用户明确说明：最新模板尚未进行真实 PLC 验证。不能将同事程序的测试结果直接记为最新模板的实机通过结果。
3. 当前点位是开发期间自行创建的测试点位，没有要求保留和恢复这些点位。
4. 用户明确不要求本阶段做“恢复旧配置、切换旧程序、验证旧版启动”的演练。该项不再阻塞本阶段软件修正完成。
5. 用户实际使用的下载模板是 `设备点位导入模板.xlsx`。CSV 示例文件的历史差异不是本次已确认故障。

### 2.2 本次完成不依赖的事项

- 不要求准备旧程序包、历史配置快照或 `rollback-check` 演练目录。
- 不要求为本次 XLSX 修正连接真实 PLC，也不把实机测试编入自动化脚本。
- 不要求修改 UOS 工程或开展 UOS 部署整改；目标环境验证另行记录。
- 不把工作区已有未提交修改全部归因于本次任务。

以上只缩小本阶段范围，不表示删除 `DeviceConfigurationService` 的候选应用失败保护、取消权限校验或放松导入规则。用户“不需要保留测试点位”也不是让本次实施主动删除配置或点位。

## 3. 当前源码基线

以下是 2026-09-24 实际读取的源码事实。行号仅辅助定位，编码时以类名和方法名为准。

| 文件 / 方法 | 当前行为 | 实施含义 |
|---|---|---|
| `DevicePointTemplateDefinition.cs`：`CurrentTemplateVersion`、`DefaultFileName`、`Columns` | v7、默认 XLSX、固定中文九列 | 保留版本和文件合同，不升版本、不改列 |
| `DevicePointManagementView.axaml.cs`：下载模板入口 | 保存对话框只提供 `.xlsx` | 不改 UI 入口或文件类型 |
| `DevicePointManagementViewModel.Export.cs`：`DownloadTemplateAsync()` | 加载当前配置，构建范围请求，调用模板导出器 | 继续使用真实页面入口验收 |
| `DevicePointManagementViewModel.cs`：`CreateTemplateTarget()`，约 2002 行 | 已传递 `device.Model`，类型直接取 `descriptor.SupportedDataTypes` | 型号不缺失，缺的是类型筛选 |
| `DevicePointTemplateExportRequest.cs`：`DevicePointTemplateTarget` | 已有 `Protocol`、`Model`、`SupportedDataTypes` | 无需新增 DTO 字段或改接口签名 |
| `DevicePointTemplateExporter.cs`：`GetAddressExample()`，约 497 行 | 只特殊处理 S7-200；全部 Modbus 返回 `HR:0/C:1` | 在通用 Modbus 分支前增加信捷分支 |
| 同文件：`ResolveRawDataChoices()`，约 162 行 | 对请求目标类型取并集，再映射固定中文选项 | 保留并集策略，输入应为型号筛选后的集合 |
| 同文件：`WriteHeader()`、`WriteInstructions()` | 列说明引用静态 `column.Description/Example`；规则直接写通用 Modbus 地址 | 所有可见提示都必须覆盖，不能只改示例页 |
| `ModbusPointAddressResolver.cs`：`IsSupportedXinjeDataType()` | 已定义 D/HD 和 M/HM 各自允许的类型 | App 直接复用此规则，避免另造通信类型矩阵 |
| `DevicePointImporter.cs`：`ImportAsync()`、`ReadExcel()` | 支持 XLSX/CSV，XLSX 读取首个“点位模板”工作表，再做固定列解析 | 保留严格导入规则，不接受通用地址冒充信捷原生地址 |
| `DevicePointCatalogExporter.cs` | 真实点位导出直接保留 `point.Address` | 本次不修改真实点位导出逻辑，做回归验证 |
| `XinjeImportWorkflowTests.cs` | 已有手工生成 CSV 的导入，以及真实点位 XLSX 导出再导入 | 不能替代“下载空白信捷 XLSX”测试，需要补充 |

当前工作树存在多项已有修改，包括上述部分文件及迁移器、CSV 示例等。实施必须在当前工作树上做局部补丁，不得恢复至 HEAD、覆盖整个文件或批量格式化。

### 3.1 依赖关系

- App 已引用 Core、Devices、Infrastructure，可以调用现有信捷类型规则。
- Infrastructure 当前只引用 Core；Excel 生成器位于 Infrastructure。
- Core 已定义 `DeviceModelKeyCatalog.XinjeFieldV1`，可以供生成器比较型号。
- ClosedXML 0.105.0 已是项目依赖，本次复用，不新增依赖。

本方案选择：**App 负责按真实设备型号构造有效类型集合；Infrastructure 根据请求中的型号生成提示和示例，根据有效类型集合生成下拉选项。** 不为 XLSX 修正让 Infrastructure 新增对 Devices 的引用。

## 4. 固定的文件和数据合同

### 4.1 不变的工作簿结构

| 项目 | 必须保留的值 |
|---|---|
| 默认文件名 | `设备点位导入模板.xlsx` |
| 模板版本 | `CurrentTemplateVersion = 7` |
| 工作表 1 | 点位模板；仅该表参与导入 |
| 工作表 2 | 填写说明 |
| 工作表 3 | 填写示例 |
| 表头 | 第 1 行，固定 9 列 |
| 填写区 | 沿用第 2～1001 行 |
| 字节序、字序 | 仍在设备属性中维护，不加入点位模板 |

固定九列顺序：

```text
点位标签,地址,数据类型,访问权限,原始下限,原始上限,工程下限,工程上限,说明
```

本次修改不涉及 JSON Schema、数据库结构、Revision 格式或历史配置迁移。

### 4.2 信捷型号的识别

模板生成器中的判断必须同时满足：

```csharp
target.Protocol == DevicePointProtocol.ModbusTcp
&& string.Equals(
    target.Model?.Trim(),
    DeviceModelKeyCatalog.XinjeFieldV1,
    StringComparison.OrdinalIgnoreCase)
```

不能按设备名称包含“信捷”、地址以 D 开头、IP、驱动显示文本或 TCP 协议本身推断型号。Modbus RTU 不因此获得信捷 V1 支持。

### 4.3 信捷 V1 类型表

| 地址区 | 可填写的中文类型 | 内部类型 |
|---|---|---|
| D、HD | 字 | UInt16 |
| D、HD | 长整型 | Int32 |
| D、HD | 双字 | UInt32 |
| D、HD | 浮点型 | Float32 |
| M、HM | 布尔量 | Bool |

单个信捷目标或全部为信捷的多设备范围，下拉项按现有全局中文选项顺序过滤后应为：

```text
字、长整型、双字、浮点型、布尔量
```

不得提供“短整型”“双精度”“字符”“字节”。本阶段不做 Excel 地址单元格与类型单元格的动态联动，也不加 VBA、宏或外部链接。通过五项并集、明确说明和现有导入校验共同实现约束。

混合信捷与其他设备时：先筛选每个设备自己的有效类型，再取并集；不得把其他通用 Modbus 或 S7 设备允许的类型从整份模板中删除。说明中必须写清：下拉项是当前设备范围的类型并集，实际每行仍由所属设备型号和地址校验。

### 4.4 示例行

信捷设备仍生成两行示例，不在真实数据页预填任何点位：

| 示例用途 | 地址 | 数据类型 | 访问权限 | 量程 |
|---|---|---|---|---|
| 试验压力 | HD1000 | 浮点型 | 只读 | 沿用 0、10000、0、10 的格式示例 |
| 启动命令 | M400 | 布尔量 | 读写 | 四项全部留空 |

沿用当前 `DevicePointTag.Format()`、当前范围设备前缀及分组生成规则。地址和量程仅用于展示填写格式，不表示用户 PLC 的真实点表；不得自动导入或发送读写请求。示例的“读写”也不代表执行了真实写入，点位权限和写入流程仍由现有代码管理。

填写说明和地址批注额外展示 D100、HM40，从而覆盖四种支持的地址前缀。真实地址转换继续由 Devices 层完成，Excel 生成器不复制 `41088/49408` 的换算算法。

## 5. 逐文件修改清单

本节相对路径均以文档开头列出的目标项目根目录为基准，现有文件链接为绝对定位；标为“新增”的文件尚不存在。

| 类型 | 文件 | 必须做的工作 |
|---|---|---|
| 功能修改 | [DevicePointManagementViewModel.cs](D:/易峥/2026/2026-09/Avalonia_上位机通用模板/项目代码/XXX.TestBench.Template/src/XXX.TestBench.App/ViewModels/DevicePointManagementViewModel.cs) | `CreateTemplateTarget()` 按驱动和型号过滤类型 |
| 功能修改 | [DevicePointTemplateExporter.cs](D:/易峥/2026/2026-09/Avalonia_上位机通用模板/项目代码/XXX.TestBench.Template/src/XXX.TestBench.Infrastructure/Configuration/DevicePointTemplateExporter.cs) | 修正信捷示例、说明、批注；继续从目标有效类型集合生成下拉项 |
| 合同注释 | [DevicePointTemplateExportRequest.cs](D:/易峥/2026/2026-09/Avalonia_上位机通用模板/项目代码/XXX.TestBench.Template/src/XXX.TestBench.Core/Ports/DevicePointTemplateExportRequest.cs) | 明确 `SupportedDataTypes` 是调用方按型号筛选后的集合；不改成员或构造签名 |
| 修改测试 | [DevicePointManagementViewModelTests.cs](D:/易峥/2026/2026-09/Avalonia_上位机通用模板/项目代码/XXX.TestBench.Template/tests/XXX.TestBench.App.Tests/DevicePointManagementViewModelTests.cs) | 检查单信捷、全信捷、混合范围的请求类型集合 |
| 修改测试 | [XinjeImportWorkflowTests.cs](D:/易峥/2026/2026-09/Avalonia_上位机通用模板/项目代码/XXX.TestBench.Template/tests/XXX.TestBench.App.Tests/XinjeImportWorkflowTests.cs) | 新增实际下载 XLSX、填值、预览和应用的测试 |
| 新增测试 | `tests/XXX.TestBench.Integration.Tests/XinjeDevicePointTemplateTests.cs` | 在目标项目中新增，专门检查生成工作簿的地址、说明、批注、验证列表与其他型号回归 |
| 文档同步 | `docs/设备点位管理与导入说明.md` | 说明信捷原生地址、型号类型筛选和混合模板规则 |
| 文档同步 | `docs/信捷PLC_ModbusTCP现场适配立即编码实施方案.md` | 按第 9 节修正本阶段回退范围，引用本方案 |
| 文档同步 | `docs/信捷PLC_ModbusTCP现场适配测试追踪.md` | 保留历史证据，添加用户范围确认和本次实际结果 |

### 5.1 明确不改

- `DeviceConfigurationMigrator.cs`：不因它存在旧差异而回退或再次修改。
- `DevicePointTemplateDefinition.Columns`、模板版本、默认文件名、三个工作表名称。
- `docs/设备点位导入模板.csv`：现有九列示例与本次格式一致，不改回十一列、不删除 CSV 导入。
- `DevicePointImporter`、`DevicePointImportPlanner`、`DevicePointImportValidator` 的接受规则和冲突规则。
- `ModbusPointAddressResolver`、`XinjeFieldV1AddressParser`、批读、编解码和通信运行时的行为。
- `DeviceWritePipeline`、权限、安全写入和配置失败保护。
- `DevicePointCatalogExporter` 的实际点位导出行为。
- `XXX.TestBench.Uos`、同事参考代码、未跟踪的同级 `项目代码/XXX.TestBench.Devices`。
- 现有设备/点位 JSON、用户数据、数据库以及真实 PLC 参数。

如果测试发现另一处独立缺陷，记录文件、触发条件和证据，再单列处理范围；不要用本次模板修正顺手改通信或历史迁移代码。

## 6. 方法级实现步骤

### 6.1 App：构造型号感知的模板目标

修改 `DevicePointManagementViewModel.CreateTemplateTarget(DeviceConfig.DeviceEntry device)`。

1. 沿用现有描述器查找、协议转换、设备编码和名称计算。
2. 先取得 `descriptor?.SupportedDataTypes`；保持原有描述器缺失时的行为。
3. 仅在驱动是 `DriverKeyCatalog.ModbusTcp` 且 `ModbusPointAddressResolver.IsXinjeFieldV1(device.Model)` 时筛选。
4. 对描述器中的每个类型，复用 `IsSupportedXinjeDataType()` 判断它是否适用于任一 `XinjeFieldV1Area`。
5. 建立独立集合，不修改共享描述器的集合；将结果传入 `DevicePointTemplateTarget.SupportedDataTypes`。
6. 非信捷设备保留原有类型语义；型号仍原样规范化后传入请求。

可直接采用以下局部写法，变量名按现有方法调整：

```csharp
var supportedTypes = descriptor?.SupportedDataTypes
    ?? new HashSet<DevicePointDataType>();

if (string.Equals(device.DriverKey, DriverKeyCatalog.ModbusTcp,
        StringComparison.OrdinalIgnoreCase)
    && ModbusPointAddressResolver.IsXinjeFieldV1(device.Model))
{
    supportedTypes = supportedTypes
        .Where(type => Enum.GetValues<XinjeFieldV1Area>()
            .Any(area => ModbusPointAddressResolver.IsSupportedXinjeDataType(area, type)))
        .ToHashSet();
}

// 使用 supportedTypes 替换原构造参数 descriptor?.SupportedDataTypes ...。
// 不修改 descriptor.SupportedDataTypes 本身。
```

文件需要的 `using XXX.TestBench.Devices.Modbus;` 按当前文件实际情况补充一次。无需增加驱动公共接口或全局类型集合。

### 6.2 Infrastructure：识别目标并生成示例

在 `DevicePointTemplateExporter` 内新增私有方法 `IsXinjeTarget(DevicePointTemplateTarget target)`，实现第 4.2 节条件。

修改：

- `GetAddressExample()`：最先判断信捷目标，采集示例返回 `HD1000`，命令示例返回 `M400`；后续 S7、Simulation、通用 Modbus 分支保持原有顺序与结果。
- `GetRawDataExample()`：信捷采集示例使用 `Float32`，命令示例使用 `Bool`，通过 `DevicePointTypeCatalog.ToDisplayName()` 输出中文，不手写第二套中文映射。
- 信捷示例所需类型应存在于请求的有效类型集合。若直接调用导出器的测试/调用方传入不完整的信捷目标，应明确报模板目标能力不完整，不能静默回退成 `Int16` 或挑选集合第一项。正常页面构造的信捷目标包含全部五种类型。
- `WriteExamples()`：仍使用现有两行生成流程、设备标签前缀、样式和空量程规则；不把示例自动写进“点位模板”。

### 6.3 下拉项：保留集合合并，修正输入合同

`ResolveRawDataChoices()` 继续：

```text
各目标的 SupportedDataTypes → 并集 → 按 RawDataTypeChoices 原顺序过滤 → 中文下拉项
```

这里不再复制信捷的通信类型矩阵。`DevicePointTemplateTarget` 的注释需明确：调用方负责根据设备协议及型号提供有效集合，不能把通用驱动能力直接当作特定型号能力。直接调用模板导出器的新增测试也必须按此合同构造目标。

必须避免：

- 在并集完成后发现“存在一个信捷”就删除所有 `Int16/Double`，这会损坏混合模板。
- 修改 `ModbusTcpDriverDescriptor.SupportedDataTypes`，这会让通用 Modbus 设备失去原有类型。
- 为了让错误示例导入通过，放宽核心地址/类型校验。
- 因提示和示例变化而升级模板版本，导致仍合法的九列文件被人为淘汰。

### 6.4 所有提示使用同一套按范围生成的文字

本次不能仅改“填写示例”。需要覆盖五个位置：

1. “点位模板”地址列的表头批注。
2. “点位模板”数据类型列的表头批注和输入提示。
3. “填写说明”的地址规则和设备字序规则。
4. “填写说明”字段说明表中“地址”“数据类型”两行。
5. “填写示例”页的范围说明。

建议在生成器内增加两个私有辅助方法，避免五处文案分叉：

```csharp
private static (string Description, string Example) GetColumnHelp(
    DevicePointTemplateDefinition.ColumnDefinition column,
    DevicePointTemplateExportRequest? request);

private static string GetAddressRuleText(
    DevicePointTemplateExportRequest? request);
```

具体要求：

- `WriteHeader()` 增加 `request` 参数，`ExportCore()` 传入；只替换批注文本，不改列名、样式、列顺序。
- `WriteInstructions()` 的字段说明表也调用 `GetColumnHelp()`，不能继续把静态 `column.Example = HR:0` 作为信捷推荐值写回。
- `WriteScopeComments()` 当前主要处理点位标签批注，保持其原有行为；若同一单元格已有批注，不重复创建而覆盖先前内容。
- 非“地址”“数据类型”列沿用静态列定义。没有目标上下文时沿用现有通用说明。
- 全部目标为信捷时，地址帮助使用信捷规则；混合范围时按设备类型分别说明，不能写成所有设备都接受同一地址格式。
- 含信捷目标时，明确其设备属性固定使用“大端模式、低字在前”。通用 Modbus 仍按设备属性设置，不给它套用信捷固定排列。
- 用换行和自动换行显示较长规则，必要时仅调整对应说明行高；不整体重做工作簿布局。

可采用的中文文案：

> 信捷 PLC 请填写 D、HD、M、HM 原生地址，例如 D100、HD1000、M400、HM40。D/HD 可选字、长整型、双字、浮点型；M/HM 只能选布尔量。不填写 HR:0、C:1 这类通用 Modbus 地址，也不填写 D10.0 等寄存器位地址。

混合模板补充：

> 本模板包含多台设备，下拉项是各设备可用类型的并集。请按点位标签中的设备编码选择对应地址和类型；导入时会按所属设备逐行校验。

注意：可以在“禁止填写”的解释中出现 `HR:0/C:1`，测试不能笼统断言整个信捷工作簿绝不包含这两个字符串。应检查示例单元格不是它们，且帮助文字没有将其推荐给信捷设备。

## 7. 自动化测试设计

### 7.1 测试位置与职责

1. App 层请求测试：必须从 `CreateTemplateExportRequest()` 检查真实页面构造的 `Targets`，不能只手工构造一个已经正确的 DTO 来证明 F02 修复。
2. Integration 层 XLSX 测试：调用真实 `DevicePointTemplateExporter.ExportAsync()` 写盘，再用 ClosedXML 打开；检查文件内容、验证列表和批注。
3. App 层流程测试：调用真实 `DownloadTemplateAsync()`，随后填写该文件并调用 `CreateImportPlanAsync()`、`ApplyImportedPointsAsync()`。
4. 已有导出再导入、类型拒绝、配置失败保护测试保留，不为本次改测试断言以掩盖其他问题。

### 7.2 统一测试夹具

复用 `AppTestHarness.Create(grouped: true)` 和现有 `XinjeImportWorkflowTests` 的设备配置方式：

- 驱动 `modbus-tcp`，型号 `XINJE_FIELD_V1`，站号 1，大端、低字在前。
- 测试使用 `DeviceMode.Simulation`；不得改为 Hardware 或尝试连接 `127.0.0.1:502`。
- 使用配置服务添加设备，保留其自动建立的 DEFAULT 分组；选择真实树中的信捷设备节点。
- 新点位名称保持唯一，避免把既有点位地址冲突误判为 XLSX 缺陷。
- 混合测试使用已配置的合法设备及其已有分组；每行带正确设备前缀，不能用另一设备不存在的分组制造无关失败。
- 新增临时文件放 `D:\Codex相关\信捷PLC_XLSX导入模板修正\test-files\<唯一编号>`；`finally` 中只清理本用例创建的文件。
- 现有夹具已经将内部配置、数据库放在 `D:\Codex相关` 下，本次不为了改目录而重构所有旧测试。

### 7.3 最低测试矩阵

以下 X01～X22 是本方案的验收条目，不等于必须有 22 个独立测试方法。允许同一测试方法覆盖连续步骤，但追踪表必须能映射每项。

| 编号 | 输入 / 操作 | 必须断言 | 建议落点 |
|---|---|---|---|
| X01 | 页面选单个信捷设备，创建模板请求 | 目标型号正确；类型集合精确为五种；没有 Int16/Double | App 请求测试 |
| X02 | 页面选多个信捷设备的范围 | 每个目标均已收窄；合并后仍为五项 | App 请求测试 |
| X03 | 页面选信捷 + 通用 Modbus 的范围 | 信捷目标五项；通用目标仍允许 Int16/Double；共享描述器未被修改 | App 请求测试 |
| X04 | 页面下载单信捷 XLSX | 文件存在；三个工作表、九列表头和顺序不变；真实数据页没有示例数据 | App / Integration |
| X05 | 读取信捷“填写示例” | 采集为 HD1000 + 浮点型；命令为 M400 + 布尔量；命令无量程 | Integration |
| X06 | 读取 C2:C1001 类型验证列表 | 顺序为字、长整型、双字、浮点型、布尔量；不含短整型/双精度 | Integration |
| X07 | 读取 B1/C1 批注、填写说明各规则及字段说明表 | 四类地址说明齐全；M/HM 仅布尔；信捷固定字序；不存在矛盾的推荐示例 | Integration |
| X08 | 型号大小写或首尾空白变化，协议仍为 TCP | 信捷示例识别一致 | Integration Theory |
| X09 | 通用 Modbus TCP、RTU 目标 | 示例仍是 HR:0/C:1；类型仍包含各自原有合法项 | Integration Theory |
| X10 | S7-200 SMART、S7-1200/1500、Simulation 目标 | 各自示例维持原行为；不误套信捷地址 | Integration Theory |
| X11 | 不带请求的默认 ExportAsync 重载 | 原默认模板仍可生成；不误标为信捷 | Integration |
| X12 | 混合设备模板 | 每设备示例按各自型号生成；类型取并集；多设备标签有设备前缀；说明提示按设备逐行校验 | Integration + App |
| X13 | 从下载 XLSX 复制两条示例数据到“点位模板” | 文件解析成功；预览成功；预览不改变当前配置和 Revision | App 流程 |
| X14 | 在下载 XLSX 中分别填写 D100/字、HD1000/浮点型、M400/布尔量、HM40/布尔量 | 四行预览通过；规范地址分别为 HR:100、HR:42088、C:400、C:49448 | App 流程 |
| X15 | 应用 X14 计划并重新加载配置 | 成功；原生 Address 保留；结构化区域/偏移正确；没有真实硬件请求 | App 流程 |
| X16 | 再导入相同 XLSX | 行为为 Unchanged；应用不增加点位或生成新 Revision | App 流程 |
| X17 | 导出已导入的实际点位，再导入导出 XLSX | 原生地址保持；身份不重复；原有实际点位导出行为不退化 | App 流程 |
| X18 | 绕过 Excel 下拉，手工写信捷短整型、双精度或 M400/浮点型 | 预览被拒绝，错误明确；当前配置不变 | App Theory |
| X19 | 手工写信捷 HR:0、C:1、D10.0、FD100 | 预览被拒绝，不转换成另一地址 | App Theory |
| X20 | 删除、增加或调换列；错误首表名；旧十一列表头 | 仍按现有规则拒绝；不能因本次修正放宽格式 | 复用现有合同测试并补必要 XLSX 用例 |
| X21 | 预览后取消 / 预览后配置版本变化 | 取消不修改配置；旧计划仍被拒绝；复用既有对应测试 | App 回归 |
| X22 | 直接向生成器传入缺少 Float32 或 Bool 的信捷目标 | 明确报告目标能力不完整，不生成与目标类型不一致的示例 | Integration |

建议方法名：

```text
TemplateRequest_XinjeTargetsUseModelSpecificTypesWithoutChangingGenericDescriptor
Export_XinjeWorkbookUsesNativeExamplesAndFiveTypeChoices
Export_MixedTargetsKeepPerDeviceExamplesAndUnionChoices
Export_XinjeHintsAreConsistentAcrossCommentsAndInstructions
DownloadedXinjeTemplate_CanPreviewApplyAndReimportWithoutChanges
DownloadedXinjeTemplate_StillRejectsUnsupportedAddressOrType
```

读取数据验证时使用项目现有 ClosedXML 版本提供的公开 API，或读取工作簿压缩包中的 worksheet XML；不要臆造库 API。断言应面向输出的列表、示例和导入结果，不通过反射测试私有辅助方法。

### 7.4 避免测试假通过

- 不能把测试中新建一个九列表格称作“下载模板测试”，必须消费 `DownloadTemplateAsync()` 生成的文件。
- 不能只断言表头存在；F01/F02 就发生在表头正确的情况下。
- 不能把 CSV 导入成功当作本次 XLSX 流程成功。
- 复制示例时只复制数据行和九个单元格值，不复制底部说明、合并区域或示例页表名。
- XLSX 写盘后关闭 `XLWorkbook` 再导入，避免文件锁掩盖业务结果。
- 无效类型用例需要绕过 Excel 交互校验直接写单元格，验证服务端仍严格拒绝。
- 测试结果必须核对实际执行数和失败数，零用例、超时、取消均不是通过。

## 8. 编码实施顺序

### 阶段 A：保留当前工作树，建立本次基线

1. 在目标项目运行 `git status --short --branch`。
2. 对第 5 节将修改的现有文件执行定向 `git diff`，记录已有修改。
3. 需要留补丁/命令输出时写入 `D:\Codex相关\信捷PLC_XLSX导入模板修正`，不写入项目临时目录。
4. 不执行 reset、clean、整体 checkout/restore，不删除未跟踪文件。

完成条件：明确本次是在已有实现上修复模板生成，不重新做信捷通信移植。

### 阶段 B：补充能暴露问题的测试

1. 添加 X01、X05、X06、X13 的关键断言。
2. 先定向运行并记录修复前失败原因：示例地址是通用地址，或类型集合含不支持项。
3. 如果失败来自夹具配置或文件锁，先修测试设置，不能把无关失败当作缺陷证据。

### 阶段 C：修复模板请求与生成器

1. 按 6.1 修正目标类型集合。
2. 按 6.2 修正型号示例和类型对应关系。
3. 按 6.4 统一批注、填写说明、字段说明表和范围说明。
4. 补齐混合范围、默认重载、S7/通用 Modbus 回归用例。
5. 复核列数、表名、v7、实际点位导出行为不变。

### 阶段 D：验证完整用户流程

1. 执行 X13～X21 的下载 XLSX 流程及非法输入检查。
2. 保存单信捷、混合范围、通用 Modbus 三份检查样本到外部任务目录。
3. 用可用的 Excel/LibreOffice 查看工作簿：说明可读、下拉正常、表头批注可见，无文本遮挡。若当前没有可用图形表格软件，记录“工作簿结构及导入已验证，图形显示未验证”，不伪称视觉验收完成。
4. 本阶段不启动 Hardware 模式、不发送 PLC 写请求。

### 阶段 E：按真实结果同步文档

按第 9 节更新原方案范围和测试追踪，再执行本次相关全量回归；填写第 11 节完成清单。用户只要求方案时不提前执行这些实施步骤。

## 9. 文档同步规则

### 9.1 原信捷方案

文件：`docs/信捷PLC_ModbusTCP现场适配立即编码实施方案.md`。

在文档开头加入“2026-09-24 用户确认的本阶段范围”，引用本方案，明确：

> 当前为模板开发和测试点位场景，用户不要求旧程序及旧配置回退演练。本阶段取消该演练作为软件完成和当前开发交付的前置条件；原有配置候选应用失败保护继续保留。最新模板的真实 PLC 验证仍单独记录。

同步检查所有相关位置，不能只改一处导致文档互相矛盾：

- 第 9 节 T60 行：区分“已有软件失败保护”与“本阶段不要求的旧程序演练”。
- 第 9.6 节：取消当前必须准备旧程序包的措辞。
- 第 10 节测试说明：回退演练不再作为本阶段额外必做项。
- 第 12 节相关风险：标为未来实际升级部署时的参考事项，不是本次阻塞。
- 第 13 节：保留为未来部署参考，明确本阶段不执行，不新增恢复工具。
- 第 14 节完成定义：去掉当前必须通过旧程序回退演练的放行条件。

### 9.2 原测试追踪

文件：`docs/信捷PLC_ModbusTCP现场适配测试追踪.md`。

- 保留 2026-09-23 的历史命令、410/410 记录和当时未进行回退演练的事实，不改造成“回退已通过”。
- 增加当前范围说明：用户已确认本阶段不要求该演练，历史“发布回退条件未完成”不再作为当前开发阶段阻塞项。
- 添加本次 X01～X22 映射表，记录方法名、命令、实际结果和 TRX 位置。
- 本次测试数量按实际输出写，不预填为 410，也不把过去的通过数当成本轮执行结果。
- 保留“同事实现已由用户确认真机测试可用；当前模板实机验证未完成”的区分。

### 9.3 导入说明

文件：`docs/设备点位管理与导入说明.md`。

- 在通用 Modbus 地址说明旁补充信捷原生地址说明，避免概括成“所有 Modbus 都填写 HR/C”。
- 说明信捷单设备模板五种类型、地址区对应关系及混合设备并集规则。
- 保留 XLSX 为下载模板、CSV 为同格式可导入文件的现状。
- 不把“默认 CSV 历史十一列变九列”列为本次故障或恢复项。

## 10. 可复制执行的构建和测试命令

以下命令供后续编码执行。本次编制方案没有运行它们。使用独立 PowerShell 会话执行，所有新增构建产物、还原包缓存、临时文件和 TRX 位于 `D:\Codex相关` 下。

```powershell
$ErrorActionPreference = 'Stop'
$projectRoot = 'D:\易峥\2026\2026-09\Avalonia_上位机通用模板\项目代码\XXX.TestBench.Template'
$taskRoot = 'D:\Codex相关\信捷PLC_XLSX导入模板修正'
$runRoot = Join-Path $taskRoot ('run-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
$artifactRoot = Join-Path $runRoot 'artifacts'
$resultRoot = Join-Path $runRoot 'results'
$scratchRoot = Join-Path $runRoot 'tmp'
$packageRoot = Join-Path $taskRoot 'nuget-packages'
$httpCacheRoot = Join-Path $taskRoot 'nuget-http-cache'
$pluginCacheRoot = Join-Path $taskRoot 'nuget-plugin-cache'
$dotnetCliRoot = Join-Path $taskRoot 'dotnet-cli'

New-Item -ItemType Directory -Force -Path $artifactRoot,$resultRoot,$scratchRoot,$packageRoot,$httpCacheRoot,$pluginCacheRoot,$dotnetCliRoot | Out-Null
$env:TEMP = $scratchRoot
$env:TMP = $scratchRoot
$env:NUGET_PACKAGES = $packageRoot
$env:NUGET_HTTP_CACHE_PATH = $httpCacheRoot
$env:NUGET_PLUGINS_CACHE_PATH = $pluginCacheRoot
$env:DOTNET_CLI_HOME = $dotnetCliRoot
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
Set-Location -LiteralPath $projectRoot

dotnet restore '.\XXX.TestBench.Template.sln' --artifacts-path $artifactRoot
if ($LASTEXITCODE -ne 0) { throw 'Restore failed' }

dotnet build '.\XXX.TestBench.Template.sln' --no-restore --artifacts-path $artifactRoot --verbosity minimal
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }

# 开发阶段定向检查；修复前预期失败应单独记录，不得混记为最终验证。
dotnet test '.\tests\XXX.TestBench.Integration.Tests\XXX.TestBench.Integration.Tests.csproj' --no-restore --artifacts-path $artifactRoot --results-directory (Join-Path $resultRoot 'integration-targeted') --logger 'trx;LogFileName=integration-targeted.trx' --filter 'FullyQualifiedName~XinjeDevicePointTemplateTests' --verbosity minimal
if ($LASTEXITCODE -ne 0) { throw 'Targeted integration tests failed' }

dotnet test '.\tests\XXX.TestBench.App.Tests\XXX.TestBench.App.Tests.csproj' --no-restore --artifacts-path $artifactRoot --results-directory (Join-Path $resultRoot 'app-targeted') --logger 'trx;LogFileName=app-targeted.trx' --filter 'FullyQualifiedName~XinjeImportWorkflowTests|FullyQualifiedName~DevicePointManagementViewModelTests' --verbosity minimal
if ($LASTEXITCODE -ne 0) { throw 'Targeted app tests failed' }

# 最终相关全量回归。顺序执行，避免共享 artifacts 输出竞争。
$testProjects = [ordered]@{
    core = '.\tests\XXX.TestBench.Core.Tests\XXX.TestBench.Core.Tests.csproj'
    integration = '.\tests\XXX.TestBench.Integration.Tests\XXX.TestBench.Integration.Tests.csproj'
    app = '.\tests\XXX.TestBench.App.Tests\XXX.TestBench.App.Tests.csproj'
}
foreach ($suite in $testProjects.Keys) {
    dotnet test $testProjects[$suite] --no-restore --artifacts-path $artifactRoot --results-directory (Join-Path $resultRoot $suite) --logger "trx;LogFileName=$suite.trx" --verbosity minimal
    if ($LASTEXITCODE -ne 0) { throw "Test suite failed: $suite" }
}
```

补充执行要求：

1. `--artifacts-path` 隔离目录必须先 restore，再使用 `--no-restore`，避免缺少 assets 文件。
2. 本次不改 Avalonia 视图或 UI 控件布局，Headless 不作为新增测试的主要落点。若实施越过本方案改动了 UI 绑定或控件行为，再单独串行执行 Headless 项目，并记录原因。
3. 最终三套相关回归通过后，不在没有新改动或失败依据时重复全量运行。
4. 新测试临时路径必须在 `D:\Codex相关` 下；现有测试的外部任务目录可继续沿用，不把旧夹具目录改造纳入此次任务。
5. 若已有进程锁定项目 Debug 输出，继续使用隔离 artifacts，不结束用户正在使用的程序。
6. 命令返回成功还要核对 TRX 的总数、执行数、失败数和跳过项；若关键新增测试被过滤掉或跳过，不算覆盖完成。

## 11. 完成定义与验收记录

### 11.1 软件修正完成条件

- [x] 单信捷模板示例为 HD1000/浮点型、M400/布尔量。
- [x] 信捷模板说明和批注覆盖 D、HD、M、HM；没有推荐信捷填写通用 Modbus 地址的矛盾提示。
- [x] 单信捷和全信捷范围下拉项恰好为五种。
- [x] 混合范围按各目标能力取并集，通用 Modbus 的短整型/双精度没有被错误删除。
- [x] 九列、列顺序、v7、文件名及三个工作表名称不变。
- [x] 下载的真实 XLSX 可以填写、预览、应用、重新导入；无变化导入不增加点位或 Revision。
- [x] 非法地址/类型仍拒绝；不存在为了迁就错误模板而放宽解析器的修改。
- [x] S7、Simulation、通用 TCP/RTU 模板及实际点位导出回归通过。
- [x] 构建和三套相关测试有本轮真实结果，X01～X22 有逐项对应证据。
- [x] 原方案和追踪已记录本阶段不要求旧程序回退演练，未把“免做”写成“测试通过”。
- [x] 没有修改真实配置、同事代码、UOS 项目或无关已有差异。

### 11.2 交付报告写法

完成后应分别报告：

| 内容 | 应写的事实 |
|---|---|
| 功能变化 | 信捷 XLSX 示例、说明、批注和类型选项已按设备型号一致化 |
| 软件验证 | 本轮构建结果、实际测试数量、关键下载导入流程是否通过 |
| 工作簿查看 | 已用何种软件查看；或明确图形显示未验证 |
| PLC | 本次未执行真实 PLC 验证；同事程序既有现场依据与最新模板实机结果分开记录 |
| 回退 | 根据用户确认，本阶段不要求旧程序/旧测试配置回退演练；已有软件失败保护保留 |

### 11.3 后续实机验证的边界

本方案可以在不连接 PLC 的情况下完成软件修正。以后验证最新模板时，应使用现场确认的真实地址和类型，核对读取值、受控写入反馈、批读及连接恢复。不能使用本文示例地址直接推定现场安全可写，也不能把这一后续工作写成当前已经发现的代码故障。

### 11.4 本轮实施记录（2026-09-28）

| 项目 | 本轮结果 |
|---|---|
| 解决方案构建 | 成功；0 警告、0 错误 |
| Core | 158/158 通过；TRX：`D:\Codex相关\信捷PLC_XLSX导入模板修正\run-20260924-174736\results\core-full-final\core-full-final.trx` |
| Integration | 141/141 通过；TRX：`D:\Codex相关\信捷PLC_XLSX导入模板修正\run-20260924-174736\results\integration-full-final-v3\integration-full-final-v3.trx` |
| App | 一轮全量 117/117 通过。后续复跑出现 SQLite 锁；最新 XLSX 流程 2/2、模板请求 1/1、版本失效用例 1/1 定向通过。详细记录见测试追踪。 |
| 样例与显示 | 三份样例在任务目录；结构及导入验证通过。无 Excel/LibreOffice，图形显示未验证。 |
| PLC / 回退 | 未操作 PLC；按用户确认，本阶段不要求旧程序/旧配置回退演练。配置候选失败保护保留。 |
## 12. 风险和最小撤销方式

| 风险 | 控制办法 |
|---|---|
| 修好示例页但忘记说明和批注 | 五处提示统一辅助方法，X07 检查全部输出位置 |
| 混合范围被信捷五种类型全局限制 | 每设备先筛选，再做并集；X03、X12 验证 |
| 直接导出器测试绕过页面的错误类型集合 | App 测试真实请求，流程测试真实下载文件 |
| 通过新增项目引用把 Excel 层耦合到通信层 | 保持 Infrastructure 只引用 Core；App 复用 Devices 已有规则 |
| 文案修复误升模板或配置版本 | 固定 v7 和九列，不做迁移 |
| 历史测试结果被当成本轮结果 | 本轮独立结果目录和 TRX，保留历史记录的日期 |
| 当前工作树已有改动被覆盖 | 按本次差异局部撤销，不使用 reset/clean 或整个文件恢复 |

本节的“撤销”仅指开发中撤销本次局部代码补丁：恢复本次新加的分支、帮助函数、目标类型筛选和对应文档修改到实施前状态，并保留其他已有工作。**不要求旧程序/旧配置现场回退演练，不要求备份或恢复当前测试点位。** 不因撤销模板生成补丁而改动 `active.json`。

## 13. 可直接交给编码任务的执行说明

> 在 `D:\易峥\2026\2026-09\Avalonia_上位机通用模板\项目代码\XXX.TestBench.Template` 按本方案实施。先保留当前工作树已有修改，补充实际下载信捷 XLSX 的失败测试，再修改 `CreateTemplateTarget()` 的型号类型筛选和 `DevicePointTemplateExporter` 的信捷示例、批注、说明。保持 v7 九列、现有导入校验和通信实现不变，Infrastructure 不新增对 Devices 的引用。覆盖单信捷、全信捷、混合设备和其他驱动回归，完成下载、填写、预览、应用、重新导入的自动化验证。按用户确认同步取消本阶段旧程序回退演练的阻塞条件，但保留配置失败保护。所有临时文件、缓存和测试结果放在 `D:\Codex相关`。不修改同事代码、UOS 工程、真实配置或 PLC，不把软件结果写成实机通过；未经另外要求，不提交或推送 Git。完成后给出本轮实际修改、构建和测试结果及剩余实机验证边界。
