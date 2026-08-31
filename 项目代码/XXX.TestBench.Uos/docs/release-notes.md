# Avalonia 界面迁移发布说明

日期：2026-08-31
发布性质：Windows 离线演示/开发维护版本；Linux x64 为 UOS 候选包
状态：`UI_COVERAGE_VERIFIED + P2_OFFLINE_VERIFIED + PACKAGE_REVALIDATED + CUTOVER_NOT_READY`

## 本轮内容

- 对 Legacy 51 个 Designer 建立唯一覆盖矩阵，修复 `Procedure` 根目录 7 个 UserControl 的分类遗漏。
- Avalonia 从最小五点窗口扩展为总览、试验作业、工艺监视、参数管理、数据报表、硬件校准、日志诊断、绝缘耐压 8 个任务页。
- 覆盖 Legacy 21 个具体试验类、9 个管理入口、12 个输入校准通道和 6 个独立 AO 槽位。
- 增加九类管理专项字段/权限勾选/项点有序双列表/型号会话发布、会话 CRUD/唯一性、权限控件名可空、同名权限稳定 Id 关联与精确删除级联、已发布型号过滤、查询筛选/分页、本地校准公式、Legacy 日志日期/“全部等级”及其余六级/别名/倒序/500 条边界、仪器参数校验、启动取消/Faulted 和生命周期处理；日志关键字明确登记为 Avalonia 增强。
- 增加 Ctrl+1～Ctrl+8、44 高度触控目标、滚动布局、AutomationProperties 以及 960×560/1600×900 Headless 行为验证。
- 新增/扩展 VM 与独立 Headless 测试；界面验收不依赖截图或文件存在性检查。

## 安全边界

- Gateway 仍为 `ReadOnly`，默认配置仍为 `OfflineSimulation`，本轮未修改设备配置。
- 不实现或启用 Zero/Gain 自动写入、VX/AO/DO 手动输出、复位、Test00/自动试验设备动作、绝缘仪器通信、离线写队列或断线重放。
- 报表删除/重传、真实认证、SQLite 持久化、打印/上传等外部效果继续阻断；会话数据退出即丢弃。
- 信捷 `192.168.0.51:502` 只证明候选 UnitId=1 下 Modbus TCP 仅读协议可响应；PLC 系列、UnitId、HD1074 字序、M400 语义和 CH00/P2 映射均未确认。

## 验证摘要

- 证据：393 行文件矩阵、51 行 UI 矩阵、140 行点表矩阵及 `ui-test-evidence.csv` 的证据 ID/真实文件/唯一 Marker 一致性检查通过。
- 最终证据脚本、Release、Core/Gateway/Avalonia/Headless、Host `--once` 已针对管理/日志最终源码按顺序复验通过；Release 为 9 个项目、0 警告、0 错误。
- 已从本轮验证基线 `92df619e62b3ee76e59630ba7b734ddd36592cf1` 重新生成 Avalonia/Gateway Host 的 `win-x64` 和 `linux-x64` 四个自包含包；Windows Host 包内 `--once`、HMI 3 秒存活烟测、7 个禁止 Legacy 依赖精确扫描和 SHA-256 均已归档。
- 独立 clone 在本轮验证基线复现证据脚本、Release、Core/Gateway/Avalonia/Headless 与 Host `--once`；Linux 包仍只是 Windows 构建机发布结果，UOS 现场保持未验收。

## 已知未完成项

- 真实登录/权限、Legacy SQLite 与旧数据兼容、报表文件/打印/上传。
- 完整 B11/EP 设备动作、取消与物理安全收尾。
- 本机 OPC UA Server/证书、UOS 非 root systemd、字体/触控现场验收。
- S7/Modbus 业务点位、字节序、连续运行和拔插恢复。
- 任意写入路径的安全矩阵、权限、质量、连接代次、联锁、超时、回读和审计。

## 回滚

旧 WinForms 源码、OPF、报表/数据库基线和回滚材料全部保留；不得删除或让新旧系统同时拥有写权限。本轮实现中文提交 `92cdec56a3a93132fd5258446729fafedd9bc631` 是可回滚恢复点；完整 cutover 条件见 `phase-e-cutover-checklist.md`。
