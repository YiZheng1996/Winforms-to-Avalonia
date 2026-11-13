# XXX.TestBench.Template

基于 .NET 8 与 Avalonia 的试验台上位机模板。目标业务主线为：

**产品类型 → 产品型号 → 试验项点 → 测试记录 → Excel 报表**。

本模板提供配置化设备点位、运行模式、安全写入、SQLite 存储、权限与审计基础。**2026-11-13 审核确认 W02/W03 仅部分实现；已按用户要求删除旧分步执行和固定版式报表生成链。当前开始、执行、结束试验及生成报表入口均停用，待新版协调器和报表计算流程接入后启用。** 历史记录查询保留，实际设备和现场部署须单独确认。详见 [W02/W03 审核与旧入口清理记录](docs/evidence/W02_W03审核与旧入口清理_20261113.md)。

## 解决方案

```text
XXX.TestBench.Template.sln
src/XXX.TestBench.App          Avalonia 页面、组合根与应用流程
src/XXX.TestBench.Core         领域、用例、配置与边界接口
src/XXX.TestBench.Infrastructure 配置存储、SQLite v9、日志与持久化
src/XXX.TestBench.Devices      仿真运行时与已注册设备驱动
tests/                         Core、Integration、App、Headless 测试
tools/XXX.TestBench.Upgrade    独立离线配置与 SQLite 副本升级工具
```

## 当前配置版本

应用目录中的 `config` 文件是当前部署配置：

| 文件 | 当前版本 |
|---|---:|
| `app.json` | 1 |
| `device.json` | 5 |
| `points.json` | 4 |
| `simulation.json` | 3 |
| `signal-bindings.json` | 1 |

运行时使用完整 revision 和 `active.json` 指针加载设备、点位、仿真与信号绑定。配置版本错误、字段未知、身份悬挂或文件哈希不符时，应用报告故障；启动过程不会把旧文件自动转换成当前配置。

## SQLite 数据库

当前代码结构为 SQLite v9。新路径或真正的空数据库可以直接建立 v9；现有 v9 在启动时只读检查版本、结构及完整性。v7/v8 必须先使用离线工具生成新副本；桌面应用不会自动升级旧库。W03 的业务事务、任务领取及完整约束验收仍未完成，不能据此视为已经具备生产运行闭环。

离线工具支持明确登记的配置组合，以及 SQLite v7→v8、v8→v9 副本转换。历史转换与原值存档不构成运行时旧流程兼容；当前记录新建仅允许结果合同 2，历史合同不能由当前记录仓储推进状态。工具不覆盖来源，不访问设备或 PLC。

```powershell
$tool = 'path/to/XXX.TestBench.Upgrade.dll'
dotnet $tool check-config --source 'D:\data\config' --source-kind files `
  --report 'D:\Codex相关\upgrade\check-config.json'
dotnet $tool convert-config --source 'D:\data\config' --source-kind files `
  --output 'D:\data\config-v5' --report 'D:\Codex相关\upgrade\convert-config.json'

dotnet $tool check-config --source 'D:\data\config' --source-kind revision --pointer active `
  --report 'D:\Codex相关\upgrade\check-revision.json'
dotnet $tool check-config --source 'D:\data\config' --source-kind revision --pointer previous `
  --report 'D:\Codex相关\upgrade\check-previous.json'

dotnet $tool check-db --source 'D:\data\bench.db' `
  --report 'D:\Codex相关\upgrade\check-db.json'
dotnet $tool convert-db --source 'D:\data\bench.db' --output 'D:\data\bench-v8.db' `
  --report 'D:\Codex相关\upgrade\convert-db.json'

dotnet $tool check-db-v8-to-v9 --source 'D:\data\bench-v8.db' `
  --report 'D:\Codex相关\upgrade\check-db-v9.json'
dotnet $tool convert-db-v8-to-v9 --source 'D:\data\bench-v8.db' --output 'D:\data\bench-v9.db' `
  --legacy-bench-id '历史来源台架' --report 'D:\Codex相关\upgrade\convert-db-v9.json'
```

`files` 模式仅读取指定目录中的 `device.json`、`points.json`、`simulation.json`，以及可选的 `signal-bindings.json`。缺少绑定文件时输出空绑定并在报告中说明；试验执行所需信号仍须显式配置。`revision` 模式默认只读 `active`；只有明确传入 `--pointer previous` 才检查上一指针。转换结果写入新的配置根目录，随后才生成 `active`。

工具报告记录输入哈希、版本、行数、转换内容、警告、阻断项和输出哈希。退出码：0 成功，2 格式或版本不支持，3 数据冲突，4 完整性失败，5 文件占用或 I/O 失败，1 其他失败或取消。

## 构建、测试与发布

构建与测试脚本默认把 artifacts、NuGet 缓存、临时文件、TRX 和日志写到 `D:\Codex相关\XXX.TestBench.Template`。可通过 `-TaskRoot` 改到其他指定目录。

```powershell
pwsh -File tools/build-and-test.ps1
pwsh -File tools/build-and-test.ps1 -TaskRoot 'D:\Codex相关\my-validation'

pwsh -File tools/publish-windows.ps1
pwsh -File tools/publish-windows.ps1 -OutputPath 'D:\release\bench-win-x64'
```

测试脚本核对每个项目的 TRX 结果，并在 Headless 或测试进程超时时终止本次 dotnet 进程树并返回失败。发布脚本拒绝覆盖已有输出目录，验证五个配置文件都进入发布包，并确认离线升级工具没有被带入应用包。

## 运行与验收边界

- 设备模式来自有效配置；硬件初始化失败时保留故障状态，不自动回退到仿真。
- 手动写入经过权限、运行模式、设备状态、联锁、风险确认、写入及回读/审计链路；仿真回读不等于真实设备反馈。
- 构建、单元测试、集成测试和 Headless 测试只能证明对应软件行为。真实 PLC、串口、UOS、现场数据库副本和回滚演练需分别留存实际执行证据。

## 相关文档

- `docs/历史兼容逻辑清理与当前版本收敛_立即编码实施方案.md`
- `docs/设备点位管理与导入说明.md`
- `docs/decisions/`
- `docs/evidence/`

## 开发导航

页面按业务归入 App/Features；详见 [代码结构与修改入口](docs/代码结构与修改入口.md)。
