# XXX.TestBench.Template 通用上位机框架（新主线）

- 依据：《docs\XXX.TestBench.Template-总体方案.md》V3.0（2026-09-01）
- 目标：.NET 8 + Avalonia 的“代码模板 + 配置”试验台上位机框架；第一版形成
  登录 → 产品/配方 → 任务 → 预检 → Simulation/Hardware 执行 → 记录 → Excel 报表闭环。
- 状态：阶段 0 + 2 + 3 + 4（Simulation 执行闭环与报表后端）完成；P0/P1 页面等待阶段 1 详细规格。

## 解决方案结构

    XXX.TestBench.Template.sln
    src\XXX.TestBench.App          Avalonia 外壳 + 组合根（业务页面待规格）
    src\XXX.TestBench.Core         领域实体、状态、用例、边界接口、写入安全链
    src\XXX.TestBench.Infrastructure SQLite 正式 schema、配置、日志、PBKDF2、UoW
    src\XXX.TestBench.Devices      Simulation 运行时 + 工厂（Hardware 阶段 5）
    tests\*.Tests                  Core / App / Headless / Integration

## 构建与测试

    dotnet build XXX.TestBench.Template.sln -c Release
    dotnet test  XXX.TestBench.Template.sln -c Release

## 配置

config\app.json / device.json / points.json / simulation.json 均含 schemaVersion；
配置无效启动进入 Faulted 状态并显示诊断，不静默回退默认值、不自动切换 Simulation。

## 安全边界

- DeviceMode 是启动配置；切换要求维护权限 + 无活动任务 + 重新初始化。
- 写入统一经过 DeviceWritePipeline：权限→模式→连接/质量→活动任务→联锁→风险确认→写入→回读(Hardware)→审计。
- Hardware 初始化/连接失败保留 Hardware 模式并进入 Faulted，禁止回退 Simulation。

## 文档

- docs\decisions\0001-新主线立项与阶段0保护.md
- docs\decisions\0002-阶段2基础骨架与持久化.md
- docs\reuse-ledger.csv（Uos 能力复用登记）
- docs\evidence\（构建/测试证据）
