# Phase E：Windows/UOS 离线发布与启动说明

日期：2026-08-31
解目录：`D:\易峥\2026\2026-09\Avalonia_上位机通用模板\项目代码\XXX.TestBench.Uos`  
当前实际目标：`net8.0`；Avalonia 11.3.9；自包含发布 RID 为 `win-x64`、`linux-x64`  
配置：`config\gatewaysettings.json`；默认 `ReadOnly + OfflineSimulation`。信捷 `192.168.0.51:502` 仅完成独立仅读协议探针，未写入此配置。
发布/临时目录：`D:\Codex相关\phase-e-final-2026-08-31\publish`（不属于项目源目录）

## 1. 本轮发布复验状态

| 程序 | RID | 目标发布目录 | 当前状态 |
|---|---|---|---|
| Avalonia HMI | win-x64 | `D:\Codex相关\phase-e-final-2026-08-31\publish\avalonia-win-x64` | `PASS-WINDOWS-PROCESS-START`；由本轮验证基线生成，隐藏启动后存活 3 秒并定向结束 |
| Avalonia HMI | linux-x64 | `D:\Codex相关\phase-e-final-2026-08-31\publish\avalonia-linux-x64` | `PASS-PUBLISH-ONLY`；包已生成，UOS 启动仍为 `PENDING-FIELD` |
| Gateway Host | win-x64 | `D:\Codex相关\phase-e-final-2026-08-31\publish\gateway-host-win-x64` | `PASS-OFFLINE`；包内 config 执行 `--once` 退出码 0 |
| Gateway Host | linux-x64 | `D:\Codex相关\phase-e-final-2026-08-31\publish\gateway-host-linux-x64` | `PASS-PUBLISH-ONLY`；包已生成，UOS 启动仍为 `PENDING-FIELD` |

Avalonia 和 Host 项目均显式设置 `CopyToPublishDirectory="PreserveNewest"`。本轮四包均由验证基线 `92df619e62b3ee76e59630ba7b734ddd36592cf1` 重新生成；四个目录均含 `config\gatewaysettings.json`。精确扫描 `Interop.OPCAutomation.dll`、`SunnyUI.dll`、`AntdUI.dll`、`rw3.dll`、`rwdsl2.dll`、`office.dll` 与 `Report.dll`，四包命中数均为 0。

当前 apphost/config SHA-256 与扫描结果如下；这些哈希只能用于介质比对，不能替代现场签名、许可证或 SBOM 记录。

| 发布物 | apphost SHA-256 | config SHA-256 | 禁止依赖命中 |
|---|---|---|---:|
| Avalonia Windows | `D81303C90F743F244DEA4B5F97EC3B9E30098A718E57719196DFD60C5FE485EB` | `B4344D50C8B8E25F714DAD5477539A027FEB508C3D005DC54F82F6E14BBBEA26` | 0 |
| Avalonia Linux | `A603336CF5C561A861A48142DD1877AECBA03C887D40066302F1256BA2A1E590` | `B4344D50C8B8E25F714DAD5477539A027FEB508C3D005DC54F82F6E14BBBEA26` | 0 |
| Gateway Host Windows | `172189EB897016B4C68A6EC6CD2B6BD2D9008BF811A7200A92850D29F26C5891` | `B4344D50C8B8E25F714DAD5477539A027FEB508C3D005DC54F82F6E14BBBEA26` | 0 |
| Gateway Host Linux | `A4DCB00CBB84ADD3B77C87C0F7F673571FA0D694F5C014FF31546C2E4F11B869` | `B4344D50C8B8E25F714DAD5477539A027FEB508C3D005DC54F82F6E14BBBEA26` | 0 |

## 2. 可重复的 Windows 发布命令

在 PowerShell 中执行：

```powershell
$repo = 'D:\易峥\2026\2026-09\Avalonia_上位机通用模板\项目代码\XXX.TestBench.Uos'
$out = 'D:\Codex相关\phase-e-final-2026-08-31\publish\avalonia-win-x64'
dotnet publish "$repo\src\XXX.TestBench.Avalonia\XXX.TestBench.Avalonia.csproj" -c Release -r win-x64 --self-contained true -o $out

$out = 'D:\Codex相关\phase-e-final-2026-08-31\publish\gateway-host-win-x64'
dotnet publish "$repo\src\XXX.TestBench.Gateway.Host\XXX.TestBench.Gateway.Host.csproj" -c Release -r win-x64 --self-contained true -o $out
```

启动 Avalonia HMI：

```powershell
Set-Location 'D:\Codex相关\phase-e-final-2026-08-31\publish\avalonia-win-x64'
.\XXX.TestBench.Avalonia.exe
```

启动 Gateway Host 的一次离线采样：

```powershell
Set-Location 'D:\Codex相关\phase-e-final-2026-08-31\publish\gateway-host-win-x64'
.\XXX.TestBench.Gateway.Host.exe --config .\config\gatewaysettings.json --once
```

预期：Host 结构化 JSON 中 `transportMode=OfflineSimulation`、`writesEnabled=False`、`isSimulated=true`、`isHealthy=true`，并有五个点；退出码为 0。HMI 的 `Gateway.Health.Simulated` 和“写入能力：禁用”必须可见。apphost 存活烟测只证明进程可启动；8 页交互、快捷键、绑定和危险控件禁用由独立 Headless 行为测试证明，不以 3 秒存活代替界面验收。

## 3. UOS 候选包发布与启动命令

在 Windows 构建机生成 UOS AMD64 候选包：

```powershell
$repo = 'D:\易峥\2026\2026-09\Avalonia_上位机通用模板\项目代码\XXX.TestBench.Uos'
$out = 'D:\Codex相关\phase-e-final-2026-08-31\publish\avalonia-linux-x64'
dotnet publish "$repo\src\XXX.TestBench.Avalonia\XXX.TestBench.Avalonia.csproj" -c Release -r linux-x64 --self-contained true -o $out

$out = 'D:\Codex相关\phase-e-final-2026-08-31\publish\gateway-host-linux-x64'
dotnet publish "$repo\src\XXX.TestBench.Gateway.Host\XXX.TestBench.Gateway.Host.csproj" -c Release -r linux-x64 --self-contained true -o $out
```

复制到 UOS 目标机后，先以普通运维用户执行：

```bash
cd /path/to/avalonia-linux-x64
chmod +x ./XXX.TestBench.Avalonia
./XXX.TestBench.Avalonia
```

离线 Host 烟测：

```bash
cd /path/to/gateway-host-linux-x64
chmod +x ./XXX.TestBench.Gateway.Host
./XXX.TestBench.Gateway.Host --config ./config/gatewaysettings.json --once
```

上述 UOS 命令是候选启动说明，不是现场验收结果。本机没有 UOS，尚未验证桌面会话、X11/Wayland、字体、触控、系统库、冷启动、非 root 权限或退出码。自包含只覆盖 .NET 运行时，不自动证明 UOS 图形栈和硬件驱动可用。

## 4. 当前不能作为生产部署的部分

- 本轮四包、Windows 进程烟测、包内 Host 离线采样、禁止依赖扫描和哈希已经完成；这只关闭当前提交的离线发布复验，不关闭 UOS/设备/生产部署验收。
- Avalonia 当前在同一进程直接创建只读 Runtime；本轮没有实现“DeviceGateway → 本机 OPC UA → HMI”的生产 UA Server、证书和权限链。
- 没有 `systemd` unit、安装脚本、升级替换脚本或回滚脚本；不应把 Gateway Host 候选包直接登记为已验收生产服务。
- `gatewaysettings.json` 默认只能保持 `OfflineSimulation`。切换 `ConfiguredDevices` 是现场动作，必须先完成 S7/Modbus 地址、类型、字节序、权限和 P0/G0 记录；不能用本机 Windows 结果替代。
- 信捷现场探针在 UnitId=1 候选下收到 M400、HD1074 候选、D0 和输入寄存器 0 的合法读取响应，但 CSV 未提供当前 P2 的 `CH00/WSD` 映射；不能把该探针当作发布包现场验收，也不能据此改写默认配置。
- 所有写入仍关闭，包括 Zero/Gain、手动输出、Test00/自动试验、复位和校准；不提供写入开关绕过安全门。
- 旧 WinForms、OPC DA、Office/报表和私有 DLL 仍作为对照/回滚基线保留，不复制进 UOS 发布包，也不删除。

## 5. UOS 现场关闭条件

现场需归档 `dotnet --info`、`uname -m`、`cat /etc/os-release`、桌面/分辨率、启动/退出码、包 SHA-256、systemd 用户和权限、S7/Modbus 报文、质量/时间戳/连接代次、至少 2 小时连续运行、5 次以上拔插恢复、许可证/依赖扫描和旧系统回滚演练。完成前，发布状态保持 `PENDING-FIELD`。
