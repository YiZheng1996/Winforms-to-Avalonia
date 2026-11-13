# 设备点位 JSON 配置与树状界面编码设计方案

> 状态：已编码，按软件测试证据持续校验
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

“设备点位”页面内部将现有的“通道 / 设备”平面筛选列表替换为树形配置区，右侧继续复用当前点位表格和导入能力；设备、通道、分组和点位的管理动作统一从树节点右键菜单进入。

### 1.2 树形层级

```text
设备点位
└─ 全部设备
   ├─ 通信通道
   │  └─ 设备
   │     └─ 已配置分组（叶子节点）
   └─ 未关联通道（需修复）
      └─ 设备
```

实际数据关系为：

```text
ChannelId → DeviceId → GroupId → PointId
```

通道负责通信资源和协议承载，设备负责驱动和设备身份，分组只负责点位的逻辑组织，点位负责地址、类型、量程和写入属性。树形顺序与运行时关系一致，均按 `ChannelId → DeviceId → GroupId → PointId` 解析；点位不再作为左侧树节点，而是在右侧清单展示。一个通道被多个设备引用时只在树中出现一次，并在摘要中标注共享设备数；内部默认分组不渲染为“未分组”节点，未分组点位直接随设备节点显示。无效 `ChannelId` 的设备集中放入“未关联通道（需修复）”，修复前不能新增点位。

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
| 通道编辑 | `ChannelEditorDialogWindow` | 独立窗口，只填写名称、连接方式和连接参数 |
| 设备编辑 | `DeviceEditorDialogWindow` | 独立窗口，只填写名称、所属通道、驱动和设备参数 |
| 点位编辑 | 已有 `DevicePointDialogWindow` | 简化界面使用“点位名称”作为客户输入，保留所属路径、地址、类型和安全字段 |
| 配置关系 | 已有通道 → 设备 → 点位 | 增加分组层，不改变驱动路由；客户树按全部设备 → 通道 → 设备 → 已配置分组展示 |
| 配置存储 | 已有 Revision 目录、`active.json`、清单哈希 | 继续使用完整快照原子应用 |
| 数据库 | 已有 SQLite 业务数据和审计 | 不增加设备通信点位表 |
| 运行时 | 按 `PointId → DeviceId → ChannelId` 路由 | GroupId 只用于组织和筛选，不参与 I/O |

当前入口和界面文件：

- `src/XXX.TestBench.App/Views/DevicePointManagementView.axaml`
- `src/XXX.TestBench.App/ViewModels/DevicePointManagementViewModel.cs`
- `src/XXX.TestBench.App/Views/ChannelEditorDialogWindow.axaml`
- `src/XXX.TestBench.App/Views/DeviceEditorDialogWindow.axaml`
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

字段规则（持久化字段与客户填写字段分开）：

- `Id` 为持久化 GUID，改名不能改变身份；
- `DeviceId` 必须引用存在的设备；
- `GroupId` 必须引用同一 `DeviceId` 下的分组；
- 分组编码在同一设备内唯一；
- `PointId`、`Code`、`DeviceId`、`GroupId` 是系统内部稳定关联键，用于运行时路由、信号绑定、迁移、精确更新和诊断，不要求客户在新模板中维护；
- 点位 `Code` 由系统生成/保留，简化客户界面统一显示点位名称，不再把编码、标签和显示名称拆成多列；导入模板统一使用当前点位标签格式；
- 分组名称用于显示，分组编码只在内部配置、诊断和跨设备标签解析时使用；
- 分组不保存协议、地址、驱动或运行时值；
- 分组不改变点位的 `PointId`、`DeviceId`、地址和写入安全属性；
- 第一版只支持一层点位分组，不支持分组嵌套分组。

### 4.4 默认分组

运行时配置为保证点位有明确归属，每个设备至少保留一个内部默认分组。该分组只用于配置归属和安全迁移，客户界面不显示“未分组”节点；客户不必创建 AI、AO、DI 或 DO 等业务分组，未分组点位直接在设备节点的右侧清单中显示。迁移旧配置时，不根据点位编码前缀猜测类型，统一创建：

```text
Code：DEFAULT
Name：未分组
```

然后将原设备下所有未分组点位放入该分组。

新建设备时内部仍可在候选配置中保留默认归属，但不在树和清单中显示该名称；只有完整配置保存并应用后才进入生效 Revision。用户如果需要分类，再通过右键菜单新增 AI、AO、DI、DO、Fault 等业务分组。

### 4.5 JSON 示例

以下示例展示运行时 JSON 的内部字段；客户下载/导入模板不直接暴露这些 ID 和编码字段，而是使用“分组.点位”标签，系统在边界处解析并维护内部关联：

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
      "isWritable": false,
      "riskLevel": "Normal",
      "description": "PLC1 仿真压力"
    }
  ]
}
```

### 4.6 配置校验

`MultiDeviceConfigurationValidator`、`PointsConfig.Validate()` 和导入预校验共同执行以下校验：

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
- 继续显示配置来源、配置状态和点位总数；内部修订号只保留在日志和诊断信息中，不作为客户操作提示；
- 继续使用当前白色卡片、浅蓝灰背景、蓝色主操作和轻边框；
- 底部继续显示 PLC 连接、急停回路和仿真/硬件模式状态；
- 不增加“项目”“OPC 项目”或第二套主窗口。

### 6.2 页面布局

目标布局：

```text
┌──────────────────────────────────────────────────────────────────────┐
│ 设备点位                             点位数  信号绑定  导入          │
│ 管理通道、设备、分组和点位                                          │
├───────────────────────┬──────────────────────────────────────────────┤
│ 设备树                 │ 设备点位清单                                 │
│ 新增、右键节点         │ 当前范围：仿真通道 / 设备 A / 压力            │
│ 更多：导入、下载、绑定 │                                             │
│ 搜索通道、设备或分组   │ ┌──────────────────────────────────────────┐ │
│                       │ │设备│分组│点位名称│地址│数据类型│...│ │
│ ▼ 全部设备             │ │...                                       │ │
│   ▼ 仿真通道            │ └──────────────────────────────────────────┘ │
│     ▼ 设备 A            │                                              │
│       └─ 压力           │                                              │
│     ▼ 设备 B            │                                              │
│       （设备点位直接显示在右侧）                                      │
│   ▶ 备用通道            │                                              │
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

调整为可拖动的 `0.36*,8,0.64*` 三列布局。左右两侧保留最小宽度，最终宽度以 1100px 最小窗口仍能完整显示右侧关键列为准。

左侧面板要求：

- 面板标题：`设备树`；
- 说明：`选择设备或分组查看点位`；
- 提供可见的“新增”入口，按根、通道、设备和分组上下文执行对应新增；
- 右键“全部设备”、通道、设备或分组时，只显示当前节点可执行的动作；
- 选中节点后可按 `Shift+F10` 或菜单键打开同一菜单；左侧不再提供重复的“节点操作”按钮；
- 搜索框：`搜索通道、设备或分组`；
- 使用 Avalonia `TreeView`；
- 显示展开/折叠状态；
- 节点之间显示明确的层级连接线或缩进；
- 根节点、通道、设备和分组节点只显示业务名称；通道和设备可显示通信/驱动摘要、状态和点位数，具体点位地址不在左树展开；
- 选中节点使用当前项目的浅蓝色选中背景；
- 不出现“项目”或“OPC 项目”根节点；
- 不使用拖拽修改配置作为第一版必需能力，分组移动通过明确的“移动到分组”操作完成。

树节点示例：

```text
全部设备
├─ 仿真通道              已配置 · 共享通道 · 2 个设备
│  ├─ 设备 A              在线 · 2 个点位
│  │  └─ 压力              1 个点位
│  └─ 设备 B              在线 · 1 个点位
│     （未分组点位随设备显示在右侧）
└─ 备用通道              已配置 · 0 个设备
```

运行状态只能来自实际运行时状态。当前未实现的 Modbus/S7 驱动不能显示为“在线”，只能显示“未实现/已停用/不可应用”等明确状态。

### 6.4 右侧点位区

保留当前页面的 `DataGrid` 风格，但收敛为现场清点所需的关键字段，在“设备”后显示“分组”和“点位名称”。设备编码、通信方式、采集周期、量程/缩放和说明等辅助配置不在清点表中重复占用表头，详细内容通过对应编辑页面查看：

| 顺序 | 列名 | 内容 |
|---:|---|---|
| 1 | 设备 | 设备名称；全部点位/通道范围下用于区分同名点位 |
| 2 | 分组 | 业务分组名称；没有业务分组时留空 |
| 3 | 点位名称 | 客户维护的点位名称；内部 `PointId` 和 `Code` 由系统保留 |
| 4 | 地址 | 可读地址 |
| 5 | 数据类型 | 中文数据类型 |
| 6 | 写入权限 | 只读、普通、高风险 |

`采集周期`仍对应参考 CSV 的 `Scan Rate`，但它属于设备配置，不复制为点位字段；修改入口仍是设备编辑器。量程/缩放和说明仍保留在点位模型与编辑器中，只从清点表移出。点位一旦配置即参与运行，单位和点位启用状态不再作为清点表字段。参考 CSV 的 `Clamp`、`Negate` 等字段当前没有运行时语义，不在项目模板中增加假配置。

右侧标题和范围文字根据树节点动态变化：

| 选中节点 | 标题 | 点位范围 |
|---|---|---|
| `全部设备` | `全部点位` | 全部已配置点位 |
| 通道 | `{通道名称} 点位` | 该通道下全部设备点位 |
| 设备 | `{设备名称} 点位` | 该设备下全部点位（含未分组点位） |
| 分组 | `{分组名称} 点位` | 该分组下全部点位 |
| 点位（不在新树生成） | `点位详情` | 该点位所在设备/分组，表格选中对应行 |

当选中通道或根节点时，表格必须保留“设备”和“分组”列，避免不同设备的同名点位难以区分。

### 6.5 顶部操作区

保留当前页面已有操作：

- `信号绑定`；
- `更多`中的下载模板、导入点位和信号绑定；
- 左侧可见的“新增”入口；
- 点位编辑、删除和移动仍通过右侧清单行菜单进入。

移除原页面顶部的“设备 / 通道”按钮，因为设备和通道入口已经进入左侧树形区。

模板下载必须直接使用当前树节点生成下拉项和示例。选择设备时只准备该设备，选择通道时准备通道下全部设备，选择分组时默认预填该分组；未关联通道虚拟节点不允许下载模板。

所有配置管理动作统一由树节点上下文菜单承担：

- 根节点：新增通道；
- 设备节点：新增分组、新增点位、编辑设备、删除设备；
- 通道节点：新增设备、编辑通道、删除通道；
- 分组节点：新增点位、编辑分组、删除分组；
- 点位节点不在新树生成，点位动作从右侧清单行执行；
- 菜单不显示当前节点不适用的动作，服务仍负责权限、运行状态和引用校验。

右侧点位清单同样支持右键当前行；双击或按 Enter 直接进入该行点位编辑。清单空白处右键沿用左侧当前节点菜单，保证新增入口始终跟随当前上下文。

### 6.6 右键操作矩阵

| 当前节点 | 可用操作 |
|---|---|
| 根节点“全部设备” | 新增通道 |
| 通道 | 新增设备、编辑通道、删除通道 |
| 设备 | 新增分组、新增点位、编辑、删除 |
| 分组 | 新增点位、编辑、删除 |
| 右侧点位行 | 编辑、移动到分组、删除 |

菜单不显示不适用动作；动作执行失败时必须给出原因，例如：

- `该分组仍有点位，不能删除`；
- `该设备仍被信号绑定引用，不能删除`；
- `存在活动试验，暂不能修改设备配置`。

### 6.7 编辑弹窗

通道和设备分别使用独立编辑弹窗，避免在同一个页面混填两类参数：

#### 通道编辑

- `ChannelEditorDialogWindow` 只显示：

- 通道名称；
- 传输类型；
- 超时时间；
- 重试次数；
- TCP 或串口参数；
- 仿真实例标识；
- 是否启用。

树节点发起编辑时，弹窗默认定位到当前通道；新增设备时自动带入当前通道。

#### 设备编辑

- `DeviceEditorDialogWindow` 只显示：

- 设备名称；
- 所属通道；
- 设备驱动；
- 厂商、型号；
- CPU Profile 或 Modbus 站号；
- 轮询周期；
- 陈旧判定时间；
- 是否启用。

#### 分组编辑

新增轻量分组编辑模式，字段为：

- 分组名称 *；
- 分组说明。

分组编辑器不显示协议、地址、数据类型和写入权限，因为这些字段属于点位。

#### 点位编辑

当前 `DevicePointDialogWindow` 使用简化但完整的点位编辑模型：

- `点位名称`：简化弹窗只填写客户可读名称；所属设备和分组由当前树上下文确定，内部 `PointId`、`Code` 和 `GroupId` 由系统保留；
- `所属设备`：由设备节点上下文带入，也可在跨设备场景选择；
- `所属分组`：只影响树状分类和筛选；从设备新增时可直接保留设备归属，从分组新增时锁定当前分组，编辑点位时不承担跨分组移动；
- `设备地址`、`数据类型`；
- 可选的四项量程换算；
- 合并后的 `访问权限`（只读、可写普通、可写高风险）；已配置点位固定参与运行；
- `说明`。

新增点位从设备节点可不设置业务分组，从分组节点默认选择当前分组；修改标签不会要求客户输入点位编码，系统继续保留原 `PointId` 和运行时关联键。修改设备时重新加载分组并重新校验地址；修改分组只改变 `GroupId`，不改变 `PointId`、设备地址和写入安全属性。

### 6.8 树节点 ViewModel

当前使用独立的 `DevicePointTreeNodeViewModel`，不把树节点状态继续塞入 `DevicePointDeviceFilter`：

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

树节点负责显示、选择和发起上下文菜单动作；候选配置的修改仍通过 `DeviceConfigurationService` 统一完成。菜单按节点上下文只显示适用动作，避免页面底部堆放一组依选中项变灰的按钮。

## 7. 导入模板设计

### 7.1 固定中文模板

新下载模板采用简化中文格式，客户只填写点位标签和点位属性，不维护点位标识、点位编码、设备编码或分组编码列。第一个工作表仍为“点位模板”，表头必须严格使用以下 9 列和顺序：

```text
点位标签、地址、数据类型、访问权限、原始下限、原始上限、工程下限、工程上限、说明
```

Excel 和 CSV 使用同一套表头。继续保留“填写说明”和“填写示例”工作表，只有“点位模板”参与导入。

### 7.2 点位标签与设备归属

- 单设备模板的 `点位标签`填写 `分组.点位`，不分组时填写点位名称；
- 多设备模板的 `点位标签`填写 `设备编码/分组.点位`，设备前缀用于确定点位归属；
- 点位标签中的分组必须是对应设备已经存在的分组；没有分组前缀时使用内部 `DEFAULT / 未分组`；
- 导入不隐式创建设备和分组；需要新分组时，先在设备或通道节点的右键菜单中选择“新增分组”；
- 导入预览显示点位标签、设备和分组；点位移动分组时显示“更新分组”；
- 当前模板只按当前设备范围和点位标签匹配；其他版本模板直接拒绝，不进入更新流程；
- 文件没有出现的其他设备点位保持不变；
- 表头、列数或顺序错误时，提示重新下载新版固定模板；
- 不兼容英文表头、别名表头、自动猜测列和隐式列移动。

### 7.3 模板版本和内部编码

当前下载模板版本为 v5，导出器和导入器共同使用 `DevicePointTemplateDefinition.Columns` 的 9 列定义；该定义是唯一可导入格式，列数、中文表头和顺序任一不一致都会被拒绝。新模板导入的点位固定按已配置点位参与运行，不应用单位或点位启用状态字段。

模板省略内部编码并不等于运行时删除这些字段：`PointId` 用于保持信号绑定和修改后的身份，`Code` 用于内部冲突诊断，`DeviceId`/`GroupId` 用于路由关联。新模板导入时由设备范围和标签解析这些关系，导出时只输出客户需要维护的字段。

### 7.4 KEPServer 参考字段映射

附件中的 `Inverter.csv` 只作为字段参考，不作为英文兼容格式。项目模板保留并翻译可落地的字段：`Tag Name` 对应“点位标签”，`Address` 对应“地址”，`Data Type` 对应“数据类型”，`Client Access` 对应“访问权限”，`Scaling` 及四个范围列对应四项中文量程，`Description` 对应“说明”；`Eng Units` 不再导入。

`Scan Rate` 在设备编辑器中配置；`Respect Data Type`、`Scaled Data Type`、`Clamp Low/High`、`Eng Units`、`Negate Value` 当前没有本次模板边界的等价应用语义，因此不导入、不导出。英文 CSV 必须先整理成固定 9 列中文表头，不能根据 `H40001` 或 `H402102.3` 这类参考地址自动猜测协议偏移。

对于 S7，模板的地址列直接填写地址原文，并由设备型号/CPU Profile 校验：S7-1500 示例为 `DB144.DBD88`、`DB142.DBX22.3`；S7-200 示例为 `VW5022`、`V0.1`。格式不匹配时，导入预览会提示“无法导入该设备”。该校验只证明模板地址与设备类型相符，不代表当前项目已经实现真实 S7 通信。

## 8. 服务和代码改动清单

### 8.1 Core

| 文件 | 设计改动 |
|---|---|
| `Core/Configuration/PointsConfig.cs` | 增加 `Groups`、`PointGroupEntry`、`PointEntry.GroupId`，版本升为 3，增加引用校验 |
| `Core/Configuration/DevicePointTemplateDefinition.cs` | 当前下载模板为 v5 固定中文 9 列，作为唯一模板契约 |
| `Core/Configuration/DevicePointTag.cs` | 解析单设备 `分组.点位` 和跨设备 `设备编码/分组.点位` |
| `Core/Configuration/DevicePointImportValidator.cs` | 导入后按设备驱动、型号和规范化地址执行预校验 |
| `Core/Application/DeviceConfigurationService.cs` | 增加候选配置的分组新增、编辑、删除、移动和完整应用入口 |
| `Core/Application/DevicePointCatalogService.cs` | 当前点位快照只作为页面读取副本，保存统一交给完整配置服务 |
| `Core/Ports/IDeviceConfigurationStore.cs` | 接口保持快照语义；如不需要新增能力，不改公共接口 |
| `Core/Execution` | 不增加 GroupId 到设备 I/O 路由；运行时仍按 PointId/DeviceId 查找 |

### 8.2 Infrastructure

| 文件 | 设计改动 |
|---|---|
| `Infrastructure/Configuration/DeviceConfigurationStore.cs` | 保持 Revision、manifest、哈希和 active 指针机制；增加 v3 配置读写测试 |
| `Infrastructure/Configuration/DevicePointImporter.cs` | 只导入 v5 固定中文 9 列模板，其他模板直接拒绝 |
| `Infrastructure/Configuration/DevicePointTemplateExporter.cs` | 导出 v5 固定中文 Excel/CSV、说明页、示例和设备范围提示 |
| `Infrastructure/Persistence` | 不新增设备通信点位表，不迁移为 SQLite 点位表 |
| 配置启动/引导组件 | 增加旧 points v2 → v3 的内存迁移和默认分组生成 |

### 8.3 App

| 文件 | 设计改动 |
|---|---|
| `App/ViewModels/DevicePointManagementViewModel.cs` | 用树节点集合替换 `DeviceFilters` 的主筛选逻辑；增加选中节点和作用域过滤 |
| `App/ViewModels/DevicePointTreeNodeViewModel.cs` | 新增树节点模型 |
| `App/Views/DevicePointManagementView.axaml` | 保留页面外壳；左侧 `ListBox` 改为 `TreeView`；加入树操作和搜索；增加分组列 |
| `App/Views/DevicePointManagementView.axaml.cs` | 接入新增/编辑/删除节点的弹窗动作，移除独立“设备 / 通道”入口 |
| `App/ViewModels/DevicePointDialogViewModel.cs` | 使用简化点位名称输入，按设备驱动过滤类型并保留内部 `PointId`、`Code`、`GroupId` |
| `App/Views/DevicePointDialogWindow.axaml` | 显示所属路径、点位名称、地址、类型、可选量程和写入安全说明 |
| `App/ViewModels/DeviceConfigurationViewModels.cs` | 增加树上下文打开、分组编辑和引用检查支持 |
| `App/Views/ChannelEditorDialogWindow.axaml` | 独立通道编辑窗口；只显示通道业务名称和连接参数 |
| `App/Views/DeviceEditorDialogWindow.axaml` | 独立设备编辑窗口；只显示设备业务名称、通道、驱动和设备参数 |
| `App/ViewModels/DevicePointImportPreviewViewModel.cs` | 增加分组显示、分组冲突和更新分组结果 |
| `App/Views/DevicePointImportPreviewWindow.axaml` | 预览表增加“分组”列和相关提示 |
| `App/Composition/ShellServices.cs` | 继续注入完整配置服务，不让 View 直接访问配置文件 |

### 8.4 Drivers

| 文件 | 设计改动 |
|---|---|
| `Devices/Drivers/SiemensS7DriverDescriptor.cs` | 提供 S7-1500/S7-200 地址格式、数据类型兼容性和规范化地址预校验；不宣称真实 S7 通信已实现 |
| `Devices/Drivers/DriverRegistry.cs` | 注册 S7 描述器，Modbus/S7 的运行时实现状态仍由 `IsImplemented` 明确表达 |

### 8.5 文档和示例

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
- 点位：按配置参与运行，显示 Unknown、Good、Bad、Stale 等实际质量。

当前只有 Simulation 驱动形成软件闭环；Siemens S7 已能在导入/配置阶段按 S7-1500、S7-200 型号做地址和类型预校验，但真实 S7 通信仍未实现；Modbus TCP、Modbus RTU 仍需按驱动实现和现场条件单独验证。不能因为树上出现通道和设备就显示为已连接。

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
- 配置 JSON 变化不会新增或修改 SQLite 通信点位表；
- v5 模板按 `分组.点位` 和 `设备编码/分组.点位` 解析归属；多设备缺少设备前缀时拒绝；
- S7-1500/S7-200 地址格式和地址/数据类型不匹配时在导入预校验阶段拒绝。

### 10.3 App 单元测试

`XXX.TestBench.App.Tests` 增加：

- 树结构按“全部设备 → 通信通道 → 设备 → 已配置分组”构造，分组为叶子节点，具体点位只进入右侧清单；
- 运行时仍按“通信通道 → 设备 → 分组 → 点位”路由，客户树中的共享通道和未关联设备显示清楚，内部默认分组不渲染；
- 根节点不是“项目”或“OPC 项目”；
- 选中通道显示该通道全部点位；
- 选中设备显示该设备点位；
- 选中分组只显示该分组点位；
- 搜索只影响显示，不修改配置；
- 右键或 `Shift+F10` 可以打开节点菜单，菜单只显示当前节点适用动作；
- 新增点位从设备节点可直接保留设备归属，未分组时不显示分组文字；
- 新增点位从分组节点默认选择当前分组；
- 分组有点位时删除动作被服务拒绝并显示原因；
- 无 `ManageDevices` 权限时不显示管理动作；
- 顶层导航仍只有一个“设备点位”入口；
- 导入预览正确显示分组和“更新分组”。
- 点位清单行右键只显示该行可执行的编辑、移动和删除动作；双击或 Enter 编辑当前行。
- 下载模板的说明页包含参考 CSV 字段映射和暂不支持字段提示。

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

以下是本轮已完成的编码顺序：

1. 增加 `PointGroupEntry`、`Groups`、`GroupId` 和 v3 校验；
2. 增加 v2/v1 配置到 v3 的内存迁移和默认分组；
3. 增加分组候选编辑、引用检查和完整快照应用；
4. 更新固定 9 列 v5 导入模板、解析、预览和说明，移除旧模板解析入口；
5. 新增树节点 ViewModel 和树构建/筛选逻辑；
6. 修改 `DevicePointManagementView.axaml`，把左侧 `ListBox` 换成 `TreeView`；
7. 接入通道、设备、分组、点位的新增/编辑/删除动作；
8. 将点位编辑弹窗改为点位名称输入并保留当前树上下文的所属分组；
9. 补充权限、运行中锁定、状态和错误提示；
10. 补充 Core、Integration、App、Headless 测试；
11. 串行构建和测试，记录验证范围；
12. 再考虑树形界面的细节优化，不在第一轮加入拖拽、任意多级分组或独立 OPC Server。

## 12. 验收标准

完成后必须同时满足：

- 从当前上位机的“设备点位”菜单进入，不新增顶层页面；
- 页面没有“项目”或“OPC 项目”树节点；
- 左侧可以展开“全部设备 → 通信通道 → 设备 → 已配置分组”，分组下没有点位子节点，内部默认分组不显示；
- 右侧仍然显示当前项目的点位表格和导入能力；
- 新增、编辑、删除和移动分组都经过统一配置服务；
- 设备通信配置不进入 SQLite；
- JSON 配置按完整 Revision 原子生效；
- 旧配置可以迁移并保留稳定 ID；
- 点位分组不会影响驱动、地址唯一性、写入安全和运行时路由；
- 固定中文 9 列 v5 模板可以导入，单设备使用“分组.点位”，多设备使用“设备编码/分组.点位”，并显示分组预览；配置点位固定参与运行；
- 只接受固定中文 9 列 v5 模板；旧 v4、旧结构化和旧单设备模板均拒绝；
- 点位清单显示设备、分组、点位名称、地址、数据类型和写入权限；采集周期、量程/缩放和说明通过编辑器查看，单位和启用状态不再显示或应用；
- 活动试验期间不能修改设备配置；
- 当前 Simulation 软件闭环不回退为其他模式；
- 未实现 Modbus/S7 驱动不会被 UI 伪装成已实现；
- 构建、单元测试、集成测试和 UI 测试证据分别记录，不能混为现场验收证据。

## 13. 本次明确不做

- 不创建独立“OPC 项目”；
- 不创建独立 OPC Server 进程；
- 不把当前上位机改造成 OPC Client；
- 不把设备通信点位迁入 SQLite；
- 不实现真实 Modbus TCP、Modbus RTU 或 Siemens S7 驱动；S7 仅增加配置阶段的型号地址预校验；
- 不增加任意多级分组；
- 不以拖拽作为第一版配置修改入口；
- 不以显示名称替代稳定 ID；
- 不改变现有 `DeviceWritePipeline` 的写入安全链；
- 不把当前值、质量和采集时间写入配置 JSON。
