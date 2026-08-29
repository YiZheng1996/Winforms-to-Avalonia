# Phase E：Cutover Checklist

日期：2026-08-29  
当前判定：`CUTOVER_NOT_READY`  
允许范围：Avalonia P2 只读离线仿真作为 Windows 开发/演示维护线  
不允许范围：现场生产唯一实现、启用写入、删除旧 WinForms、宣称 UOS/PLC/Modbus 兼容已验收

## 1. Trunk switch / Legacy freeze

- [x] 当前 P2 只读切片的新工作集中在 `项目代码\XXX.TestBench.Uos`。
- [x] Core/Gateway/Avalonia 的职责边界和五点只读合同已落到源码与测试。
- [x] 旧 WinForms 仍保留在 `XXX试验台模板`，作为对照和回滚基线。
- [ ] 将 Avalonia 设为完整产品默认维护线；登录、数据库、完整 B11、报表和现场边界尚未闭合。
- [ ] 冻结旧 WinForms 的发布版本并建立可恢复标签/分支；当前工作区不是 Git checkout，远端仓库为空。
- [x] 记录“新旧系统不可同时拥有写权限”；当前两侧都不通过本迁移解启用新写入。

## 2. Required parity gates

- [x] P2 五点首次采样、值类型、时间戳、质量和连接代次有自动化证据。
- [x] `Unknown` 首样本、`Bad` 断线、仿真标识和 `WritesEnabled=false` 有自动化证据。
- [x] DI00 Unknown/Bad/false 与 Test00 手动门禁有 Core/UI 证据。
- [x] B11 纯范围判定、人工调整、通过和最大次数失败有合同测试。
- [x] 读异常后的 Bad→断开→下一轮重连/新代次有 Runtime 测试。
- [x] Windows Release 构建、Windows Host 包内 config 启动和 Windows Avalonia apphost 启动已验证。
- [ ] 真实登录、权限、型号/参数数据库和旧数据迁移验证。
- [ ] 完整 B11 设备动作、时序、人工确认、取消、收尾、记录与报表验证。
- [ ] OPC UA Server/证书/只读地址空间/权限和 HMI 链路验证。
- [ ] UOS AMD64 启动、触控、字体、非 root systemd、冷启动和依赖验证。
- [ ] S7/Modbus 真实地址、类型、字节序、报文和拔插恢复验证。
- [ ] 写入安全矩阵、质量/代次/联锁/超时/回读/审计及现场回滚演练。

## 3. Packaging / operations

- [x] Avalonia `win-x64` 自包含包生成于 `D:\Codex相关\phase-e\publish\avalonia-win-x64`。
- [x] Avalonia `linux-x64` 自包含候选包生成于 `D:\Codex相关\phase-e\publish\avalonia-linux-x64`。
- [x] Gateway Host `win-x64`/`linux-x64` 自包含包已生成，包内包含 `config\gatewaysettings.json`。
- [x] 发布目录精确旧依赖名扫描未发现 `Interop.OPCAutomation.dll`、SunnyUI、AntdUI、rw3、rwdsl2、Office 或 Report.dll。
- [ ] 目标 UOS 现场安装/启动/卸载或升级替换策略。
- [ ] systemd unit、专用非 root 用户、权限、自动重启和日志轮转。
- [ ] 包哈希、SBOM、许可证扫描和现场介质归档。
- [ ] 旧系统 30 天观察、RTO≤30 分钟回滚演练和异常隔离手册。

## 4. Delete legacy gate

- [ ] 没有未修复 Blocker，或每个 Blocker 有正式签字豁免。
- [ ] 所有实际需要的能力已实现，未实现能力已由产品负责人明确退休。
- [ ] 配置、数据库、报表模板和历史数据迁移策略已验证且可恢复。
- [ ] UOS 发布/升级/回滚路径已演练。
- [ ] 许可证和第三方依赖在无旧目录时仍合法、完整。
- [ ] 旧测试夹具和现场回滚材料已迁移/归档。
- [ ] 利益相关方接受所有有意差异。
- [ ] 从解决方案/CI 移除旧工程。
- [ ] 标记/分支保存最后 Legacy 树。
- [ ] 删除目录后全局搜索无残留引用，并提交 ChangeLog cutover 记录。

当前禁止执行 Delete sequence。不存在已批准的 waiver；延期项和关闭条件见 `phase-e-parity-and-cutover.md` 第 7 节。

## 5. Cutover 触发条件

只有同时满足以下条件才可重新评审：

1. E-BLK-01～E-BLK-04 的目标机、真实设备和 OPC UA 证据全部归档；
2. E-HIGH-01～E-HIGH-04 的认证、完整 B11、写安全、数据库/报表链路完成并通过回归；
3. Phase A 全部必需能力逐项有自动化或现场记录，所有有意差异有审批；
4. 发布、升级、回滚、旧系统观察期和异常处置演练完成；
5. 形成可追踪的 Git 提交/分支基线。当前给定 GitHub 远端为空，不能满足该项。
