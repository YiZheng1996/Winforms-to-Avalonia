# 设备点位 JSON 配置与树状界面编码设计方案

> 状态：已确认，可进入编码
> 日期：2026-09-07
> 目标项目：`XXX.TestBench.Template`
> 适用范围：当前上位机的“设备点位”菜单
> 本方案不创建独立 OPC 项目，不新增顶层导航页面。

## 1. 已确认的产品决策

### 1.1 页面位置

设备、通道、点位分组和具体点位全部放入现有左侧导航的“设备点位”菜单中。

不新增以下内容：

- 不新增“项目”树节点；当前上位机本身已经是项目上下文。
- 不新增“OPC 项目”或独立 OPC 配置页面。
- 不新增第二个顶层导航菜单。
- 不把设备点位重新放回“参数管理”页面。

现有导航仍保持：

```text
设备与校准
设备点位
日志管理
```

“设备点位”页面内部将现有的“通道 / 设备”平面筛选列表替换为树形配置区，右侧继续复用当前点位表格和新增/编辑/删除/导入能力。

### 1.2 树形层级

```text
设备点位
└─ 设备与通道
   ├─ 通信通道
   │  └─ 设备
   │     └─ 点位分组
   │        └─ 具体点位
```

实际数据关系为：

```text
ChannelId → DeviceId → GroupId → PointId
```

通道负责通信资源和协议承载，设备负责驱动和设备身份，分组只负责点位的逻辑组织，点位负责地址、类型、量程和写入属性。

### 1.3 配置存储

设备通信配置继续保存为 JSON 版本快照，不新增通信点位 SQLite 表。

SQLite 继续保存：

- 用户、角色和权限；
- 试验项点、产品类型、产品型号及其关系；
- 试验记录、试验结果和报表数据；
- 审计日志和写入结果。

JSON 保存：

- 通道；
- 设备；
- 点位分组；
- 设备通信点位；
- 仿真配置；
- 业务信号绑定。

“设备通信点位”和“试验项点”必须继续使用不同的概念，避免把 PLC 地址点位和试验业务项点混成一张表。

## 2. 当前项目基线

本设计基于当前代码，而不是重新设计一个独立软件：

| 当前能力 | 现状 | 本方案处理方式 |
|---|---|---|
| 左侧导航 | 已有“设备点位”菜单 | 保持不变 |
| 页面 | `DevicePointManagementView` | 在原页面内增加树形区 |
| 点位表格 | 已有白色卡片、轻边框、蓝色主操作风格 | 保持现有列和样式，增加分组显示 |
| 设备/通道编辑 | 已有 `DeviceConfigurationDialogWindow` | 继续作为弹窗编辑器，由树节点动作打开 |
| 点位编辑 | 已有 `DevicePointDialogWindow` | 增加点位分组选择 |
| 配置关系 | 已有通道 → 设备 → 点位 | 增加分组层，不改变驱动路由 |
| 配置存储 | 已有 Revision 目录、`active.json`、清单哈希 | 继续使用完整快照原子应用 |
| 数据库 | 已有 SQLite 业务数据和审计 | 不增加设备通信点位表 |
| 运行时 | 按 `PointId → DeviceId → ChannelId` 路由 | GroupId 只用于组织和筛选，不参与 I/O |

当前入口和界面文件：

- `src/XXX.TestBench.App/Views/DevicePointManagementView.axaml`
- `src/XXX.TestBench.App/ViewModels/DevicePointManagementViewModel.cs`
- `src/XXX.TestBench.App/Views/DeviceConfigurationDialogWindow.axaml`
- `src/XXX.TestBench.App/Views/DevicePointDialogWindow.axaml`

## 3. 目标运行边界

当前阶段采用“上位机内置通信配置和驱动适配层”的方案：

```text
当前上位机项目
  → 设备点位页面
  → 完整设备配置服务
  → JSON Revision 配置
  → MultiDeviceRuntime
  → DriverRegistry / 设备驱动适配器
  → 第三方 Modbus、S7 或仿真库
```

UI 不直接读取 JSON，也不直接调用第三方协议库。UI 只通过应用服务编辑候选配置，保存并应用后由运行时重新构造设备会话。

未来如果真的拆出独立 OPC Server，应由 OPC Server 独占其配置和驱动，上位机作为 OPC Client 订阅；该服务边界不属于本次编码范围，也不在当前 UI 中增加“OPC 项目”概念。

## 4. JSON 配置设计

### 4.1 文件布局

继续使用当前配置根目录下的版本化结构：

```text
configRoot/
├─ app.json
├─ device.json                         # 仅作首次迁移种子
├─ points.json                          # 仅作首次迁移种子
├─ simulation.json                      # 仅作首次迁移种子
└─ device-config/
   ├─ active.json                       # 当前生效 Revision 指针
   ├─ active.previous.json              # 上一生效指针
   └─ revisions/
      └─ <revision>/
         ├─ device.json                 # 通道和设备
         ├─ points.json                 # 分组和点位
         ├─ simulation.json              # 仿真行为
         ├─ signal-bindings.json        # 业务信号 → PointId
         └─ manifest.json                # 文件集合和 SHA-256
```

`device.json`、`points.json` 等文件可以按职责拆分，但只能作为一个完整 Revision 一起生效。禁止单独覆盖 `points.json` 后直接更新内存并宣称配置已应用。

### 4.2 版本号

| 文件/对象 | 当前版本 | 本方案 |
|---|---:|---:|
| `device.json` | 2 | 保持 2 |
| `points.json` | 2 | 升级为 3，增加分组和 `groupId` |
| `simulation.json` | 当前已支持版本 | 保持现有版本策略 |
| `signal-bindings.json` | 1 | 保持 1 |
| `manifest.json` | 1 | 保持 1 |
| `active.json` | 1 | 保持 1 |

分组属于 `points.json`，因此本次不需要改变通道、设备和运行时的身份规则。若当前代码对整体快照单独做序列化版本校验，仅在该校验确实覆盖新字段时同步升级；不能为了分组无条件修改无关版本。

### 4.3 `points.json` 新增结构

`PointsConfig` 增加分组集合：

```csharp
public List<PointGroupEntry> Groups { get; set; } = new();
```

建议的分组模型：

```csharp
public sealed class PointGroupEntry
{
    public string Id { get; set; } = string.Empty;
    public string DeviceId { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int SortOrder { get; set; }
}
```

`PointsConfig.PointEntry` 增加：

```csharp
public string GroupId { get; set; } = string.Empty;
```

字段规则：

- `Id` 为持久化 GUID，改名不能改变身份；
- `DeviceId` 必须引用存在的设备；
- `GroupId` 必须引用同一 `DeviceId` 下的分组；
- 分组编码在同一设备内唯一；
- 点位编码继续保持项目内唯一；
- 分组名称用于显示，分组编码用于导入和诊断；
- 分组不保存协议、地址、驱动或运行时值；
- 分组不改变点位的 `PointId`、`DeviceId`、地址和写入安全属性；
- 第一版只支持一层点位分组，不支持分组嵌套分组。

### 4.4 默认分组

每个设备必须至少有一个分组。迁移旧配置时，不根据点位编码前缀猜测 AI、AO、DI 或 DO 类型，统一创建：

```text
Code：DEFAULT
Name：未分组
```

然后将原设备下所有未分组点位放入该分组。

新建设备时自动在候选配置中创建“未分组”，但只有完整配置保存并应用后才进入生效 Revision。用户可以继续新增 AI、AO、DI、DO、Fault 等业务分组。

### 4.5 JSON 示例

以下示例只展示新分组字段，点位其余字段继续沿用当前 `PointsConfig.PointEntry`：

```json
{
  "schemaVersion": 3,
  "groups": [
    {
      "id": "40000000-0000-5000-8000-000000000001",
      "deviceId": "20000000-0000-5000-8000-000000000001",
      "code": "AI",
      "name": "模拟量输入",
      "description": "压力、温度等连续量",
      "sortOrder": 10
    },
    {
      "id": "40000000-0000-5000-8000-000000000002",
      "deviceId": "20000000-0000-5000-8000-000000000001",
      "code": "DO",
      "name": "数字量输出",
      "description": "受控输出点位",
      "sortOrder": 20
    }
  ],
  "points": [
    {
      "id": "30000000-0000-5000-8000-000000000001",
      "code": "AI_PRESSURE_PLC1",
      "name": "PLC1 压力",
      "deviceId": "20000000-0000-5000-8000-000000000001",
      "groupId": "40000000-0000-5000-8000-000000000001",
      "address": "sim.pressure",
      "dataType": "Decimal",
      "rawDataType": "Decimal",
      "addressDefinition": { "logicalAddress": "sim.pressure" },
      "decodeOptions": { "byteOrder": "BigEndian", "wordOrder": "None" },
      "unit": "MPa",
      "isWritable": false,
      "isEnabled": true,
      "riskLevel": "Normal",
      "description": "PLC1 仿真压力"
    }
  ]
}
```

### 4.6 配置校验

`MultiDeviceConfigurationValidator` 和 `PointsConfig.Validate()` 必须新增以下校验：

1. 分组不能为空、分组 ID 必须是有效 GUID；
2. 分组 ID 在配置中不能重复；
3. 分组编码不能为空，并且同一设备内不能重复；
4. 分组引用的设备必须存在；
5. 点位的 `GroupId` 必须存在；
6. 点位的设备和分组必须属于同一设备；
7. 不允许孤立点位、不允许孤立分组；
8. 每个设备必须存在至少一个分组；
9. 删除设备、删除通道、删除分组时执行引用检查；
10. 地址唯一性仍按 `DeviceId + 驱动规范化地址` 校验，不加入 `GroupId`；
11. 分组变化不得使同一设备的重复地址合法化；
12. 非有限浮点、错误数据类型、错误量程和写入安全规则保持不变。

## 5. 迁移和保存流程

### 5.1 旧配置迁移

迁移顺序：

```text
旧 v1/v2 配置
  → 内存迁移为当前通道/设备/点位模型
  → 为每个设备创建 DEFAULT / 未分组
  → 给每个点位补 GroupId
  → 完整校验
  → 生成候选 Revision
  → 用户确认保存并应用
```

迁移规则：

- 保留原有 `PointId`、`DeviceId`、点位编码、地址和数据类型；
- 不根据 `AI_`、`DO_` 等字符串前缀自动分类；
- 旧文件不原地覆盖；
- 迁移失败时显示具体文件、点位和字段；
- 旧配置可继续作为回滚材料；
- 新 Revision 成功应用前，不改变当前运行时。

### 5.2 保存并应用

所有新增、编辑、删除和分组移动操作都只修改内存候选快照：

1. 从当前生效快照复制候选模型；
2. 执行权限、字段、引用、分组归属和驱动能力校验；
3. 检查当前是否存在活动试验；
4. 暂停新的试验启动和设备写入；
5. 将完整候选快照写入新的 `revisions/<revision>/`；
6. 写入 `manifest.json` 并校验文件哈希；
7. 停止旧运行时并构造新运行时；
8. 新运行时构造成功后原子替换 `active.json`；
9. 交换稳定运行时门面，公布新的 `ActiveRevision`；
10. 写入配置审计；
11. 失败时恢复旧 Revision 和旧运行时。

禁止：

- UI 直接写 `points.json`；
- 分别保存设备、分组和点位后立即刷新运行时；
- 通过修改分组绕过地址重复检查；
- 配置应用失败后继续使用未验证的新对象；
- 用 JSON 文件保存当前采集值、质量或时间戳。

## 6. 当前页面的界面设计

### 6.1 页面定位

保留当前 `DevicePointManagementView` 的外壳和样式：

- 当前主窗口顶部品牌区、用户、日期和时间不变；
- 左侧全局导航不变，“设备点位”保持高亮；
- 页面标题继续为“设备点位管理”；
- 继续显示配置来源、生效 Revision 和点位总数；
- 继续使用当前白色卡片、浅蓝灰背景、蓝色主操作和轻边框；
- 底部继续显示 PLC 连接、急停回路和仿真/硬件模式状态；
- 不增加“项目”“OPC 项目”或第二套主窗口。

### 6.2 页面布局

目标布局：

```text
┌──────────────────────────────────────────────────────────────────────┐
│ 设备点位管理                         点位数  信号绑定  导入  新增点位 │
│ 维护设备通信方式、地址、数据类型、量程和写入权限                      │
├───────────────────────┬──────────────────────────────────────────────┤
│ 设备与通道             │ 设备点位清单                                 │
│ [新增通道][新增设备]   │ 当前范围：PLC-1 / AI                         │
│ [新增分组]             │ [编辑][删除]                                 │
│ 搜索设备、分组或点位   │ ┌──────────────────────────────────────────┐ │
│                       │ │设备│分组│点位编码│名称│通信方式│地址│...│ │
│ ▼ 设备与通道           │ │...                                       │ │
│   ▼ Modbus TCP 通道    │ └──────────────────────────────────────────┘ │
│     ▼ PLC-1            │                                              │
│       ▼ AI             │                                              │
│         AI_Pressure    │                                              │
│         AI_Temperature │                                              │
│       ▼ DI             │                                              │
│         DI_Running     │                                              │
│       ▼ DO             │                                              │
│         DO_Start       │                                              │
│   ▼ S7 通道             │                                              │
│     ▼ S7-1500          │                                              │
└───────────────────────┴──────────────────────────────────────────────┘
│ 状态消息                                                       │       │
└──────────────────────────────────────────────────────────────────────┘
```

### 6.3 左侧树形区

将当前 `DevicePointManagementView.axaml` 中的：

```text
Grid ColumnDefinitions="230,*"
ListBox ItemsSource="DeviceFilters"
```

调整为约 `290~320,*` 的树形布局。最终宽度以 1100px 最小窗口仍能完整显示右侧关键列为准。

左侧面板要求：

- 面板标题：`设备与通道`；
- 说明：`选择设备或分组查看点位`；
- 顶部按钮：`新增通道`、`新增设备`、`新增分组`；
- 搜索框：`搜索设备、分组或点位`；
- 使用 Avalonia `TreeView`；
- 显示展开/折叠状态；
- 节点之间显示明确的层级连接线或缩进；
- 节点显示图标、编码/名称和简短状态；
- 选中节点使用当前项目的浅蓝色选中背景；
- 不出现“项目”或“OPC 项目”根节点；
- 不使用拖拽修改配置作为第一版必需能力，分组移动通过明确的“移动到分组”操作完成。

树节点示例：

```text
设备与通道
├─ Modbus TCP 通道       已启用 · 2 个设备
│  ├─ PLC-1              在线 · 18 个点位
│  │  ├─ AI              2 个点位
│  │  │  ├─ AI_PRESSURE
│  │  │  └─ AI_TEMPERATURE
│  │  ├─ DI              1 个点位
│  │  ├─ DO              1 个点位
│  │  └─ Fault           1 个点位
└─ S7 通道               未实现驱动 · 已停用
   └─ S7-1500            已停用 · 0 个点位
```

运行状态只能来自实际运行时状态。当前未实现的 Modbus/S7 驱动不能显示为“在线”，只能显示“未实现/已停用/不可应用”等明确状态。

### 6.4 右侧点位区

保留当前页面的 `DataGrid` 风格和现有字段，在“设备”后增加“分组”列：

| 顺序 | 列名 | 内容 |
|---:|---|---|
| 1 | 设备 | 设备编码 |
| 2 | 分组 | 分组编码或名称 |
| 3 | 点位编码 | 项目内唯一点位编码 |
| 4 | 名称 | 操作员可读名称 |
| 5 | 通信方式 | 根据设备驱动显示 |
| 6 | 地址 | 可读地址 |
| 7 | 数据类型 | 中文数据类型 |
| 8 | 单位 | 工程单位 |
| 9 | 量程 | 原始/工程范围 |
| 10 | 写入权限 | 只读、普通、高风险 |
| 11 | 状态 | 启用、停用或校验状态 |

右侧标题和范围文字根据树节点动态变化：

| 选中节点 | 标题 | 点位范围 |
|---|---|---|
| `设备与通道` | `设备点位清单` | 全部已配置点位 |
| 通道 | `通道点位清单` | 该通道下全部设备点位 |
| 设备 | `设备点位清单` | 该设备下全部点位 |
| 分组 | `分组点位清单` | 该分组下全部点位 |
| 点位 | `设备点位清单` | 该点位所在设备/分组，表格选中对应行 |

当选中通道或根节点时，表格必须保留“设备”和“分组”列，避免不同设备的同名点位难以区分。

### 6.5 顶部操作区

保留当前页面已有操作：

- `信号绑定`；
- `下载导入模板`；
- `按模板导入`；
- `新增点位`。

移除原页面顶部的“设备 / 通道”按钮，因为设备和通道入口已经进入左侧树形区。

左侧树形区承担：

- `新增通道`；
- `新增设备`；
- `新增分组`。

右侧或树节点上下文菜单承担：

- `编辑`；
- `删除`；
- `移动到分组`；
- `查看引用`（发现信号绑定或试验依赖时）。

### 6.6 选中节点与操作矩阵

| 当前选中 | 新增通道 | 新增设备 | 新增分组 | 新增点位 | 编辑 | 删除 |
|---|---:|---:|---:|---:|---:|---:|
| 根节点“设备与通道” | 是 | 否 | 否 | 否 | 否 | 否 |
| 通道 | 否 | 是 | 否 | 否 | 是 | 仅无设备时 |
| 设备 | 否 | 否 | 是 | 是 | 是 | 仅无分组/点位/引用时 |
| 分组 | 否 | 否 | 否 | 是 | 是 | 仅无点位时 |
| 点位 | 否 | 否 | 否 | 否 | 是 | 按引用规则 |

按钮不可用时必须给出原因，例如：

- `请选择一个通道后再新增设备`；
- `该分组仍有点位，不能删除`；
- `该设备仍被信号绑定引用，不能删除`；
- `存在活动试验，暂不能修改设备配置`。

### 6.7 编辑弹窗

本方案不新增顶层页面，编辑仍使用弹窗：

#### 通道编辑

继续复用 `DeviceConfigurationDialogWindow` 的通道字段：

- 通道编码；
- 显示名称；
- 传输类型；
- 超时时间；
- 重试次数；
- TCP 或串口参数；
- 仿真实例标识；
- 是否启用。

树节点发起编辑时，弹窗默认定位到当前通道；新增设备时自动带入当前通道。

#### 设备编辑

继续复用设备编辑字段：

- 设备编码；
- 显示名称；
- 所属通道；
- 设备驱动；
- 厂商、型号；
- CPU Profile 或 Modbus 站号；
- 轮询周期；
- 陈旧判定时间；
- 是否启用。

#### 分组编辑

新增轻量分组编辑模式，字段为：

- 分组编码 *；
- 分组名称 *；
- 排序号；
- 分组说明。

分组编辑器不显示协议、地址、数据类型和写入权限，因为这些字段属于点位。

#### 点位编辑

在现有 `DevicePointDialogWindow` 中增加“所属分组”下拉框：

- 设备先于分组选择；
- 分组选项只显示该设备下的分组；
- 从设备节点新增点位时默认选中该设备的“未分组”；
- 从分组节点新增点位时默认选中当前分组；
- 修改设备时重新加载分组并重新校验地址；
- 修改分组只改变 `GroupId`，不改变 `PointId` 和通信地址；
- 所属分组为必填项。

### 6.8 树节点 ViewModel

建议新增独立的 `DevicePointTreeNodeViewModel`，不要把树节点状态继续塞入当前的 `DevicePointDeviceFilter`：

```csharp
public enum DevicePointTreeNodeKind
{
    Root,
    Channel,
    Device,
    Group,
    Point
}

public sealed partial class DevicePointTreeNodeViewModel : ObservableObject
{
    public required DevicePointTreeNodeKind Kind { get; init; }
    public required string Id { get; init; }
    public string ParentId { get; init; } = string.Empty;
    public string Code { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string SummaryText { get; init; } = string.Empty;
    public ObservableCollection<DevicePointTreeNodeViewModel> Children { get; } = new();
    public bool HasChildren => Children.Count > 0;

    [ObservableProperty]
    private bool _isExpanded;
}
```

`DevicePointManagementViewModel` 增加：

```text
TreeNodes
SelectedTreeNode
TreeSearchText
VisiblePoints
SelectedScopeText
CanAddChannel
CanAddDevice
CanAddGroup
CanAddPoint
CanEditSelectedNode
CanDeleteSelectedNode
```

树节点只负责显示和选择；候选配置的修改仍通过 `DeviceConfigurationService` 统一完成。

## 7. 导入模板设计

### 7.1 固定中文模板

因为点位现在必须能够归属到分组，下载模板增加“点位分组编码”列。新模板第一个工作表仍为“点位模板”，表头必须严格使用以下 19 列和顺序：

```text
点位标识、点位编码、点位名称、设备编码、点位分组编码、地址类型、地址参数、原始数据类型、字节序、字序、单位、原始下限、原始上限、工程下限、工程上限、可写、启用、风险等级、说明
```

Excel 和 CSV 使用同一套表头。继续保留“填写说明”和“填写示例”工作表，只有“点位模板”参与导入。

### 7.2 分组导入规则

- `点位分组编码`可以留空；留空时使用当前设备的 `DEFAULT / 未分组`；
- 填写非空分组编码时，该分组必须已经存在于对应设备下；
- 导入不隐式创建设备和分组；
- 需要新分组时，先在树形区使用“新增分组”；
- 导入预览必须增加“分组”列；
- 点位被导入到不同分组时，预览显示“更新分组”；
- 按 `PointId` 更新，其次按“设备编码 + 点位编码”更新；
- 文件没有出现的其他设备点位保持不变；
- 表头、列数或顺序错误时，提示重新下载新版固定模板；
- 不兼容英文表头、别名表头、自动猜测列和隐式列移动。

### 7.3 模板编码改动

需要同步修改：


- `DevicePointTemplateDefinition` 增加 `GroupCode / 点位分组编码`；
- 导出器写出 19 列；
- Excel 说明和示例增加分组填写规则；
- CSV 示例模板同步为 19 列；
- 导入器解析 `GroupCode` 并解析为 `GroupId`；
- 导入预览显示分组、更新分组和分组冲突；
- 旧 18 列文件不作为新的下载格式，提示重新下载当前模板；
- 旧配置文件迁移和旧导入模板兼容不是同一件事，不能用宽松表头兼容替代配置迁移。

## 8. 服务和代码改动清单

### 8.1 Core

| 文件 | 设计改动 |
|---|---|
| `Core/Configuration/PointsConfig.cs` | 增加 `Groups`、`PointGroupEntry`、`PointEntry.GroupId`，版本升为 3，增加引用校验 |
| `Core/Configuration/DevicePointTemplateDefinition.cs` | 增加固定中文“点位分组编码”列 |
| `Core/Application/DeviceConfigurationService.cs` | 增加候选配置的分组新增、编辑、删除、移动和完整应用入口 |
| `Core/Application/DevicePointCatalogService.cs` | v2/v3 下不再直接保存独立点位，统一交给完整配置服务 |
| `Core/Ports/IDeviceConfigurationStore.cs` | 接口保持快照语义；如不需要新增能力，不改公共接口 |
| `Core/Execution` | 不增加 GroupId 到设备 I/O 路由；运行时仍按 PointId/DeviceId 查找 |

### 8.2 Infrastructure

| 文件 | 设计改动 |
|---|---|
| `Infrastructure/Configuration/DeviceConfigurationStore.cs` | 保持 Revision、manifest、哈希和 active 指针机制；增加 v3 配置读写测试 |
| `Infrastructure/Persistence` | 不新增设备通信点位表，不迁移为 SQLite 点位表 |
| 配置启动/引导组件 | 增加旧 points v2 → v3 的内存迁移和默认分组生成 |

### 8.3 App

| 文件 | 设计改动 |
|---|---|
| `App/ViewModels/DevicePointManagementViewModel.cs` | 用树节点集合替换 `DeviceFilters` 的主筛选逻辑；增加选中节点和作用域过滤 |
| `App/ViewModels/DevicePointTreeNodeViewModel.cs` | 新增树节点模型 |
| `App/Views/DevicePointManagementView.axaml` | 保留页面外壳；左侧 `ListBox` 改为 `TreeView`；加入树操作和搜索；增加分组列 |
| `App/Views/DevicePointManagementView.axaml.cs` | 接入新增/编辑/删除节点的弹窗动作，移除独立“设备 / 通道”入口 |
| `App/ViewModels/DevicePointDialogViewModel.cs` | 增加分组选项、默认分组和 `GroupId` 提交结果 |
| `App/Views/DevicePointDialogWindow.axaml` | 增加所属分组控件和解释文字 |
| `App/ViewModels/DeviceConfigurationViewModels.cs` | 增加树上下文打开、分组编辑和引用检查支持 |
| `App/Views/DeviceConfigurationDialogWindow.axaml` | 保持为弹窗编辑器，不作为新导航页面；支持通道/设备上下文定位 |
| `App/ViewModels/DevicePointImportPreviewViewModel.cs` | 增加分组显示、分组冲突和更新分组结果 |
| `App/Views/DevicePointImportPreviewWindow.axaml` | 预览表增加“分组”列和相关提示 |
| `App/Composition/ShellServices.cs` | 继续注入完整配置服务，不让 View 直接访问配置文件 |

### 8.4 文档和示例

同步更新：

- `docs/设备点位管理与导入说明.md`；
- `docs/设备点位导入模板.csv`；
- `config/examples/points.v3.simulation.json`；
- `docs/设备驱动支持矩阵.md`（只补充界面显示规则，不虚构驱动已实现）；
- 本设计文档对应的决策记录。

## 9. 权限、安全和状态规则

### 9.1 权限

- 进入“设备点位”菜单继续要求 `ManageDevices`；
- 新增、编辑、删除通道/设备/分组/点位继续要求 `ManageDevices`；
- 信号绑定继续使用现有信号绑定权限；
- 只读操作员不显示管理操作，不通过禁用按钮伪装为可管理；
- 所有配置修改写入当前用户、Revision 和操作结果审计。

### 9.2 删除和引用

- 删除通道前必须没有设备；
- 删除设备前必须没有分组、点位和信号绑定引用；
- 删除分组前必须没有点位；
- 删除点位前必须没有业务信号绑定和不可解除的运行依赖；
- 不允许静默级联删除；
- 提供“查看引用”或明确提示，帮助用户先解除关系。

### 9.3 运行状态

树节点状态来源：

- 通道：启用、连接中、在线、故障、停用；
- 设备：启用、在线、降级、离线、故障、停用；
- 分组：仅显示点位数量和配置校验状态，不虚构连接状态；
- 点位：启用、停用、Unknown、Good、Bad、Stale 等实际质量。

当前只有 Simulation 驱动形成软件闭环；Modbus TCP、Modbus RTU、Siemens S7 仍必须按驱动实现和现场条件单独验证，不能因为树上出现通道和设备就显示为已连接。

## 10. 测试设计

### 10.1 Core 单元测试

新增或扩展：

- `PointsConfig` v3 分组结构序列化和反序列化；
- 分组 ID 重复；
- 同设备分组编码重复；
- 点位 GroupId 不存在；
- 点位 DeviceId 与分组 DeviceId 不一致；
- 每设备缺少默认/有效分组；
- 同一设备同地址在不同分组中仍然判重；
- 不同设备相同地址仍然允许；
- 分组移动不改变 PointId、DeviceId、地址和驱动；
- 删除有点位的分组被拒绝；
- 活动试验期间所有配置修改被拒绝。

### 10.2 配置存储和迁移测试

`XXX.TestBench.Integration.Tests` 增加：

- v2 points 配置迁移为 v3 并生成每设备 `DEFAULT` 分组；
- 迁移保留 PointId 和地址；
- 损坏 `manifest.json` 时恢复上一生效 Revision；
- Revision 文件不完整时拒绝应用；
- 配置应用失败后 active 指针仍指向旧 Revision；
- 完整快照只切换一次 active 指针；
- 保存过程中不会留下可被当成生效版本的半成品目录；
- 配置 JSON 变化不会新增或修改 SQLite 通信点位表。

### 10.3 App 单元测试

`XXX.TestBench.App.Tests` 增加：

- 树结构按通道、设备、分组、点位构造；
- 根节点不是“项目”或“OPC 项目”；
- 选中通道显示该通道全部点位；
- 选中设备显示该设备点位；
- 选中分组只显示该分组点位；
- 搜索只影响显示，不修改配置；
- 新增点位从设备节点默认选择“未分组”；
- 新增点位从分组节点默认选择当前分组；
- 分组删除按钮在有点位时不可用；
- 无 `ManageDevices` 权限时不显示管理动作；
- 顶层导航仍只有一个“设备点位”入口；
- 导入预览正确显示分组和“更新分组”。

### 10.4 Headless/UI 验证

使用当前项目既有的 Avalonia 初始化方式验证：

- 主窗口可以加载设备点位页面；
- TreeView 节点可展开和选择；
- 当前页面蓝白色样式与其他参数管理页面一致；
- 最小窗口宽度下左树和右表均可用；
- 表格滚动、空状态、错误提示和权限状态可见；
- 对 Headless 进程设置明确超时；无输出或超时只能记录为未验证，不能标记为通过。

测试仍按 Core → Integration → App → Headless 顺序串行执行，避免残留 testhost、Avalonia 进程和文件锁相互影响。

## 11. 编码顺序

按以下顺序实施，每一步完成后再进入下一步：

1. 增加 `PointGroupEntry`、`Groups`、`GroupId` 和 v3 校验；
2. 增加 v2/v1 配置到 v3 的内存迁移和默认分组；
3. 增加分组候选编辑、引用检查和完整快照应用；
4. 更新固定 19 列导入模板、解析、预览和说明；
5. 新增树节点 ViewModel 和树构建/筛选逻辑；
6. 修改 `DevicePointManagementView.axaml`，把左侧 `ListBox` 换成 `TreeView`；
7. 接入通道、设备、分组、点位的新增/编辑/删除动作；
8. 将点位编辑弹窗增加所属分组；
9. 补充权限、运行中锁定、状态和错误提示；
10. 补充 Core、Integration、App、Headless 测试；
11. 串行构建和测试，记录验证范围；
12. 再考虑树形界面的细节优化，不在第一轮加入拖拽、任意多级分组或独立 OPC Server。

## 12. 验收标准

完成后必须同时满足：

- 从当前上位机的“设备点位”菜单进入，不新增顶层页面；
- 页面没有“项目”或“OPC 项目”树节点；
- 左侧可以展开“通道 → 设备 → 点位分组 → 点位”；
- 右侧仍然显示当前项目的点位表格和导入能力；
- 新增、编辑、删除和移动分组都经过统一配置服务；
- 设备通信配置不进入 SQLite；
- JSON 配置按完整 Revision 原子生效；
- 旧配置可以迁移并保留稳定 ID；
- 点位分组不会影响驱动、地址唯一性、写入安全和运行时路由；
- 固定中文 19 列模板可以导入并显示分组预览；
- 活动试验期间不能修改设备配置；
- 当前 Simulation 软件闭环不回退为其他模式；
- 未实现 Modbus/S7 驱动不会被 UI 伪装成已实现；
- 构建、单元测试、集成测试和 UI 测试证据分别记录，不能混为现场验收证据。

## 13. 本次明确不做

- 不创建独立“OPC 项目”；
- 不创建独立 OPC Server 进程；
- 不把当前上位机改造成 OPC Client；
- 不把设备通信点位迁入 SQLite；
- 不实现真实 Modbus TCP、Modbus RTU 或 Siemens S7 驱动；
- 不增加任意多级分组；
- 不以拖拽作为第一版配置修改入口；
- 不以显示名称替代稳定 ID；
- 不改变现有 `DeviceWritePipeline` 的写入安全链；
- 不把当前值、质量和采集时间写入配置 JSON。
