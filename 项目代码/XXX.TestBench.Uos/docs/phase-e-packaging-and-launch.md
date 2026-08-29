# Phase E：Windows/UOS 离线发布与启动说明

日期：2026-08-29  
解目录：`D:\易峥\2026\2026-09\Avalonia_上位机通用模板\项目代码\XXX.TestBench.Uos`  
当前实际目标：`net8.0`；Avalonia 11.3.9；自包含发布 RID 为 `win-x64`、`linux-x64`  
配置：`config\gatewaysettings.json`；默认 `ReadOnly + OfflineSimulation`。信捷 `192.168.0.51:502` 仅完成独立仅读协议探针，未写入此配置。
发布/临时目录：`D:\Codex相关\phase-e\publish`（不属于项目源目录）

## 1. 已验证发布物

| 程序 | RID | 发布目录 | 已验证 |
|---|---|---|---|
| Avalonia HMI | win-x64 | `D:\Codex相关\phase-e\publish\avalonia-win-x64` | 自包含发布成功；apphost 启动存活约 3 秒 |
| Avalonia HMI | linux-x64 | `D:\Codex相关\phase-e\publish\avalonia-linux-x64` | 自包含发布成功；未在本机启动 |
| Gateway Host | win-x64 | `D:\Codex相关\phase-e\publish\gateway-host-win-x64` | 自包含发布成功；包内 config `--once` 退出码 0 |
| Gateway Host | linux-x64 | `D:\Codex相关\phase-e\publish\gateway-host-linux-x64` | 自包含发布成功；未在本机启动 |

Avalonia 和 Host 项目均显式设置 `CopyToPublishDirectory="PreserveNewest"`，四个发布目录都包含 `config\gatewaysettings.json`。发布目录精确文件名扫描未发现旧 `Interop.OPCAutomation.dll`、`SunnyUI.dll`、`AntdUI.dll`、`rw3.dll`、`rwdsl2.dll`、`office.dll` 或 `Report.dll`。

本轮 apphost/config SHA-256 记录：`D:\Codex相关\phase-e\logs\package-hashes-final.txt`。该记录供现场介质归档时比对，不能替代现场签名/许可证/SBOM 记录。

## 2. 可重复的 Windows 发布命令

在 PowerShell 中执行：

```powershell
$repo = 'D:\易峥\2026\2026-09\Avalonia_上位机通用模板\项目代码\XXX.TestBench.Uos'
$out = 'D:\Codex相关\phase-e\publish\avalonia-win-x64'
dotnet publish "$repo\src\XXX.TestBench.Avalonia\XXX.TestBench.Avalonia.csproj" -c Release -r win-x64 --self-contained true -o $out

$out = 'D:\Codex相关\phase-e\publish\gateway-host-win-x64'
dotnet publish "$repo\src\XXX.TestBench.Gateway.Host\XXX.TestBench.Gateway.Host.csproj" -c Release -r win-x64 --self-contained true -o $out
```

启动 Avalonia HMI：

```powershell
Set-Location 'D:\Codex相关\phase-e\publish\avalonia-win-x64'
.\XXX.TestBench.Avalonia.exe
```

启动 Gateway Host 的一次离线采样：

```powershell
Set-Location 'D:\Codex相关\phase-e\publish\gateway-host-win-x64'
.\XXX.TestBench.Gateway.Host.exe --config .\config\gatewaysettings.json --once
```

预期：Host 结构化 JSON 中 `transportMode=OfflineSimulation`、`writesEnabled=False`、`isSimulated=true`、`isHealthy=true`，并有五个点；退出码为 0。HMI 的 `Gateway.Health.Simulated` 和“写入能力：禁用”必须可见。

## 3. UOS 候选包发布与启动命令

在 Windows 构建机生成 UOS AMD64 候选包：

```powershell
$repo = 'D:\易峥\2026\2026-09\Avalonia_上位机通用模板\项目代码\XXX.TestBench.Uos'
$out = 'D:\Codex相关\phase-e\publish\avalonia-linux-x64'
dotnet publish "$repo\src\XXX.TestBench.Avalonia\XXX.TestBench.Avalonia.csproj" -c Release -r linux-x64 --self-contained true -o $out

$out = 'D:\Codex相关\phase-e\publish\gateway-host-linux-x64'
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

- Avalonia 当前在同一进程直接创建只读 Runtime；本轮没有实现“DeviceGateway → 本机 OPC UA → HMI”的生产 UA Server、证书和权限链。
- 没有 `systemd` unit、安装脚本、升级替换脚本或回滚脚本；不应把 Gateway Host 候选包直接登记为已验收生产服务。
- `gatewaysettings.json` 默认只能保持 `OfflineSimulation`。切换 `ConfiguredDevices` 是现场动作，必须先完成 S7/Modbus 地址、类型、字节序、权限和 P0/G0 记录；不能用本机 Windows 结果替代。
- 信捷现场探针在 UnitId=1 候选下收到 M400、HD1074 候选、D0 和输入寄存器 0 的合法读取响应，但 CSV 未提供当前 P2 的 `CH00/WSD` 映射；不能把该探针当作发布包现场验收，也不能据此改写默认配置。
- 所有写入仍关闭，包括 Zero/Gain、手动输出、Test00/自动试验、复位和校准；不提供写入开关绕过安全门。
- 旧 WinForms、OPC DA、Office/报表和私有 DLL 仍作为对照/回滚基线保留，不复制进 UOS 发布包，也不删除。

## 5. UOS 现场关闭条件

现场需归档 `dotnet --info`、`uname -m`、`cat /etc/os-release`、桌面/分辨率、启动/退出码、包 SHA-256、systemd 用户和权限、S7/Modbus 报文、质量/时间戳/连接代次、至少 2 小时连续运行、5 次以上拔插恢复、许可证/依赖扫描和旧系统回滚演练。完成前，发布状态保持 `PENDING-FIELD`。
