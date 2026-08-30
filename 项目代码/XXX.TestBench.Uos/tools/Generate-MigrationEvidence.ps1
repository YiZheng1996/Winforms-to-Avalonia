[CmdletBinding()]
param(
    [string]$LegacyRoot,
    [string]$OutputRoot
)

$ErrorActionPreference = 'Stop'

$targetRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path

if ([string]::IsNullOrWhiteSpace($LegacyRoot)) {
    $LegacyRoot = Join-Path $repositoryRoot 'XXX试验台模板'
}
if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $targetRoot 'docs'
}

$LegacyRoot = (Resolve-Path $LegacyRoot).Path
New-Item -ItemType Directory -Force $OutputRoot | Out-Null

function Get-RelativePath([string]$Base, [string]$Path) {
    return [IO.Path]::GetRelativePath($Base, $Path).Replace('\', '/')
}

function Get-PrimaryModule([string]$RelativePath) {
    if ($RelativePath -match '^src/activator/') { return 'vendor-platform' }
    if ($RelativePath -match '^src/master/Lib/') { return 'vendor-platform' }
    if ($RelativePath -match '^src/slave/') { return 'gateway-boundary' }
    if ($RelativePath -match '^src/master/DB/') { return 'persistence' }
    if ($RelativePath -match '^src/master/XXX试验台\.sln$|\.csproj$|\.csproj\.user$|\.pubxml$|\.pubxml\.user$|FodyWeavers|^src/master/MainUI/(App\.config|NLog\.config)$') { return 'build-distribution' }
    if ($RelativePath -match '^src/master/MainUI/(img|Resources)/') { return 'ui-assets' }
    if ($RelativePath -match '^src/master/MainUI/Model/') { return 'application-core' }
    if ($RelativePath -match '^src/master/MainUI/Procedure/Test/') { return 'application-core' }
    if ($RelativePath -match '^src/master/MainUI/(Service/TestFactory|Service/CountdownService|Service/TimeTrackingService)\.cs$') { return 'application-core' }
    if ($RelativePath -match '^src/master/MainUI/BLL/') { return 'persistence' }
    if ($RelativePath -match '^src/master/MainUI/(CurrencyHelper/OPCHelper\.cs|Modules/)') { return 'gateway-boundary' }
    if ($RelativePath -match '^src/master/MainUI/(CurrencyHelper|Config|Upload|Service)/') { return 'platform-boundary' }
    # Procedure/Test 已在上方归入 application-core；其余 Procedure 根控件、编辑窗体和
    # 交互辅助类都属于 Legacy UI 证据，不能因位于子目录而误落到 build-distribution。
    if ($RelativePath -match '^src/master/MainUI/(frm|uc|UcHMI|Program|InsulationWithstand|Procedure/(?!Test/)|Modules/.*\.Designer\.cs|Modules/.*\.resx)') { return 'ui-shell' }
    if ($RelativePath -match '^src/master/MainUI/Properties/(Resources|Resources\.Designer)') { return 'ui-assets' }
    if ($RelativePath -match '^src/master/MainUI/Properties/') { return 'platform-boundary' }
    return 'build-distribution'
}

function Get-ReferencedBy([string]$Module) {
    switch ($Module) {
        'application-core' { return 'XXX.TestBench.Core;UnitTests;IntegrationTests' }
        'gateway-boundary' { return 'XXX.TestBench.Gateway;IntegrationTests' }
        'platform-boundary' { return 'XXX.TestBench.Avalonia;XXX.TestBench.Gateway;IntegrationTests' }
        'persistence' { return 'XXX.TestBench.Avalonia;IntegrationTests' }
        'ui-shell' { return 'XXX.TestBench.Avalonia;HeadlessUi.Tests' }
        'ui-assets' { return 'XXX.TestBench.Avalonia' }
        'build-distribution' { return 'all projects;release packaging' }
        default { return 'migration review only' }
    }
}

function Get-Disposition([string]$RelativePath, [string]$Module, [string]$Extension) {
    if ($Module -eq 'vendor-platform') { return 'ReferenceOnly' }
    if ($RelativePath -match '^src/slave/.*\.opf$|(^|/)log\.txt$|(^|/)记录日志\.txt$|(^|/)bin/|(^|/)obj/') { return 'ReferenceOnly' }
    if ($Extension -eq '.cs') { return 'Port' }
    if ($Module -in @('ui-assets', 'platform-boundary', 'build-distribution')) { return 'Replace' }
    if ($Module -eq 'persistence') { return 'ReferenceOnly' }
    return 'Port'
}

$sourceRoot = Join-Path $LegacyRoot 'src'
$records = @()
foreach ($file in Get-ChildItem -LiteralPath $sourceRoot -Recurse -File) {
    $relative = Get-RelativePath $LegacyRoot $file.FullName
    if ($relative -match '(^|/)(bin|obj|tmp)(/|$)') { continue }

    $module = Get-PrimaryModule $relative
    $extension = $file.Extension.ToLowerInvariant()
    $notes = switch ($module) {
        'vendor-platform' { '旧 Windows/x86 或第三方二进制；禁止直接进入 UOS 发布包，先做许可证和替代品审计' }
        'gateway-boundary' { '设备/OPF/旧 OPC 边界；迁移为 DeviceGateway 或配置输入' }
        'persistence' { '数据库或 BLL 证据；先做 schema、备份、迁移和完整性验证' }
        'ui-shell' { 'WinForms/Designer/resx 证据；按行为合同重建，不机械复制控件' }
        'ui-assets' { '资源候选；确认字体、授权、触控尺寸和 UOS 渲染后再替换' }
        'platform-boundary' { '文件、进程、配置、HTTP、日志或平台适配；迁移到明确边界' }
        'build-distribution' { '构建/发布证据；重新建立跨平台项目和离线发布物' }
        default { '需要人工归属确认' }
    }

    $records += [pscustomobject]@{
        File = $relative
        PrimaryModule = $module
        ReferencedBy = Get-ReferencedBy $module
        Disposition = Get-Disposition $relative $module $extension
        SizeBytes = $file.Length
        SHA256 = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
        Notes = $notes
    }
}

$outputPath = Join-Path $OutputRoot 'legacy-coverage.csv'
$records | Sort-Object File | Export-Csv -LiteralPath $outputPath -NoTypeInformation -Encoding UTF8

$tagRecords = @()
function Add-TagRecord {
    param(
        [string]$LogicalPoint,
        [string]$SourcePath,
        [string]$SourceEvidence,
        [string]$PointKind,
        [string]$PhysicalAddress,
        [string]$Transport,
        [string]$ValueType,
        [string]$LegacySubscription,
        [string]$SourceCapability,
        [string]$LegacyRead,
        [string]$LegacyWrite,
        [string]$ScopeStatus,
        [string]$TargetWriteEntry,
        [string]$FirstSlice,
        [string]$Notes
    )
    $writePreconditions = if ($TargetWriteEntry -eq 'none (P2)') {
        'N/A; P2 only exposes a read-only DataVariable'
    } else {
        'UA certificate identity; operator permission; quality Good; expected connection generation; business interlock; timeout; no active conflicting test'
    }
    $failure = if ($TargetWriteEntry -eq 'none (P2)') {
        'Read failure changes quality to Bad/Uncertain; no default 0/false'
    } else {
        'Immediate typed failure on permission/quality/generation/disconnect/timeout/readback mismatch; no queue, retry or replay; audit result'
    }
    $script:tagRecords += [pscustomobject]@{
        LogicalPoint = $LogicalPoint
        SourcePath = $SourcePath
        SourceEvidence = $SourceEvidence
        PointKind = $PointKind
        PhysicalAddress = $PhysicalAddress
        Transport = $Transport
        ValueType = $ValueType
        LegacySubscription = $LegacySubscription
        SourceCapability = $SourceCapability
        LegacyRead = $LegacyRead
        LegacyWrite = $LegacyWrite
        ScopeStatus = $ScopeStatus
        TargetNodeId = $LogicalPoint
        TargetDataAccess = 'ReadOnlyDataVariable'
        TargetWriteEntry = $TargetWriteEntry
        WritePreconditions = $writePreconditions
        ReadbackOrFailure = $failure
        FirstSlice = $FirstSlice
        Notes = $Notes
    }
}

Add-TagRecord 'Gateway.Health.NoError' 'Modules/OpcStatusGrp.cs' 'OpcStatusGrp.cs:25-31' 'Gateway health' '' 'Gateway' 'Boolean' 'yes' 'yes' 'yes' 'no' 'Gateway health' 'none (P2)' 'yes' 'Derived from old _System._NoError; not a PLC physical point'
Add-TagRecord 'Gateway.Health.Simulated' 'Modules/OpcStatusGrp.cs' 'OpcStatusGrp.cs:25-31' 'Gateway health' '' 'Gateway' 'Boolean' 'yes' 'yes' 'yes' 'no' 'Gateway health' 'none (P2)' 'yes' 'Derived from old _System._Simulated; must be explicit in HMI'

for ($i = 0; $i -lt 20; $i++) {
    $s = $i.ToString('00')
    Add-TagRecord "SMART.PLC.AI.MAI$s" 'Modules/AIGrp.cs' 'AIGrp.cs:8-13,35-47' 'Analog input' ('VD' + (200 + $i * 4)) 'S7-200/S7-1200 via S7NetPlus' 'Double' 'yes' 'yes' 'yes' 'no' 'ActiveLegacy' 'none (P2)' ($(if ($i -eq 0) { 'yes' } else { 'no' })) 'AI group is registered dynamically as AI.MAI00..19; active CPU profile is selected in Gateway configuration'
}

for ($i = 0; $i -lt 20; $i++) {
    $s = $i.ToString('00')
    $subscription = if ($i -ge 1 -and $i -le 7) { 'yes' } else { 'no' }
    $capability = if ($i -le 11) { 'possible-write' } else { 'no' }
    $scope = if ($i -le 11) { 'SourceCapabilityOnly' } else { 'OPFOnlyReserve' }
    $write = if ($i -le 11) { 'Gateway.Commands.WriteTag(kind=calibration) (P3, signed)' } else { 'none (P2)' }
    Add-TagRecord "SMART.PLC.AI.Zero$s" 'Modules/PLCCalibration.cs;Procedure/UCCalibration.cs' 'PLCCalibration.cs:25-70,103-132; UCCalibration.cs:426-440; frmHardWare.Designer.cs:186-334' 'AI calibration zero' ('VD' + (300 + $i * 4)) 'S7-200/S7-1200 via S7NetPlus' 'Double' $subscription $capability 'read/possible' 'yes' $scope $write 'no' 'PLCCalibration subscribes indices 1..7; UI exposes AI calibration indices 0..11; 12..19 are OPF-only reserve'
    Add-TagRecord "SMART.PLC.AI.Gain$s" 'Modules/PLCCalibration.cs;Procedure/UCCalibration.cs' 'PLCCalibration.cs:25-70,122-132; UCCalibration.cs:426-440; frmHardWare.Designer.cs:186-334' 'AI calibration gain' ('VD' + (400 + $i * 4)) 'S7-200/S7-1200 via S7NetPlus' 'Double' $subscription $capability 'read/possible' 'yes' $scope $write 'no' 'PLCCalibration subscribes indices 1..7; UI exposes AI calibration indices 0..11; 12..19 are OPF-only reserve'
}

for ($i = 0; $i -lt 6; $i++) {
    $s = $i.ToString('00')
    Add-TagRecord "SMART.PLC.AO.CA$s" 'Modules/AOGrp.cs' 'AOGrp.cs:8-12,34-60' 'Analog output' ('VD' + (500 + $i * 4)) 'S7-200/S7-1200 via S7NetPlus' 'Double' 'yes' 'yes' 'yes' 'yes' 'ActiveLegacy' 'Gateway.Commands.WriteTag (P3, signed)' 'no' 'AO setters currently write directly through the old BaseModule'
}

for ($i = 0; $i -lt 6; $i++) {
    $s = $i.ToString('00')
    $write = 'Gateway.Commands.WriteTag(kind=calibration) (P3, signed)'
    Add-TagRecord "SMART.PLC.AO.Zero$s" 'Modules/PLCCalibration.cs;frmHardWare.cs' 'PLCCalibration.cs:27-32,135-150; frmHardWare.cs:142-156' 'AO calibration zero' ('VD' + (600 + $i * 4)) 'S7-200/S7-1200 via S7NetPlus' 'Double' 'no' 'possible-write' 'possible' 'yes' 'SourceCapabilityOnly' $write 'no' 'UI has six AO calibration controls; PLCCalibration chCountAO is 0, so read registration and actual driver ownership require review'
    Add-TagRecord "SMART.PLC.AO.Gain$s" 'Modules/PLCCalibration.cs;frmHardWare.cs' 'PLCCalibration.cs:27-32,154-169; frmHardWare.cs:142-156' 'AO calibration gain' ('VD' + (700 + $i * 4)) 'S7-200/S7-1200 via S7NetPlus' 'Double' 'no' 'possible-write' 'possible' 'yes' 'SourceCapabilityOnly' $write 'no' 'UI has six AO calibration controls; PLCCalibration chCountAO is 0, so read registration and actual driver ownership require review'
}

for ($i = 0; $i -lt 24; $i++) {
    $s = $i.ToString('00')
    $address = 'V' + [Math]::Floor($i / 8) + '.' + ($i % 8)
    $write = if ($i -eq 0) { 'none (P2)' } else { 'none (P2); any future write requires signed safety decision' }
    Add-TagRecord "SMART.PLC.DI.MDI$s" 'Modules/DIGrp.cs' 'DIGrp.cs:19-27,43-53' 'Digital input' $address 'S7-200/S7-1200 via S7NetPlus' 'Boolean' 'yes' 'yes' 'yes' 'yes' 'ActiveLegacy' $write ($(if ($i -eq 0) { 'yes' } else { 'no' })) 'Physical input; old index setter can call Write, but target remains read-only until an explicit electrical decision'
}

for ($i = 0; $i -lt 32; $i++) {
    $s = $i.ToString('00')
    $address = 'V' + (50 + [Math]::Floor($i / 8)) + '.' + ($i % 8)
    Add-TagRecord "SMART.PLC.DO.CDO$s" 'Modules/DOGrp.cs;Procedure/Test/GeneralBaseTest.cs' 'DOGrp.cs:19-63; GeneralBaseTest.cs:193-376' 'Digital output' $address 'S7-200/S7-1200 via S7NetPlus' 'Boolean' 'yes' 'yes' 'yes' 'yes' 'ActiveLegacy' 'Gateway.Commands.WriteTag (P3, signed)' 'no' 'All writes must be controlled methods with interlock, generation, timeout and readback'
}

for ($i = 0; $i -lt 2; $i++) {
    $s = $i.ToString('00')
    Add-TagRecord "SMART.PLC.TestCon.Test$s" 'Modules/TestConGrp.cs;ucHMI.cs' 'TestConGrp.cs:54-69; ucHMI.cs:723-741,986-1022' 'Test control' ('VW' + (1030 + $i)) 'S7-200/S7-1200 via S7NetPlus' 'Variant' 'yes' 'yes' 'yes' 'yes' 'ActiveLegacy' 'Gateway.Commands.WriteTag (P3, signed)' 'no' 'Test00 is the manual/automatic mode contract; exact PLC type conversion must be confirmed'
}

for ($i = 0; $i -lt 2; $i++) {
    $s = $i.ToString('00')
    $firstSlice = if ($i -eq 0) { 'yes' } else { 'no' }
    Add-TagRecord "Modbus.WSD.CH$s" 'Modules/WSDGrp.cs;CurrencyHelper/OPCHelper.cs' 'WSDGrp.cs:8-50; OPCHelper.cs:44-45' 'WSD instrument value' ('30000' + (1 + $i)) 'Modbus RTU/TCP via NModbus' 'Variant' 'yes' 'yes' 'yes' 'yes' 'ActiveLegacy' 'none (P2); future write requires signed device decision' $firstSlice 'OPF stores CH00/CH01 and 300001/300002; RTU/TCP transport, function code, byte order and writable semantics are configurable and require P0 device evidence'
}

$tagOutputPath = Join-Path $OutputRoot 'tag-and-write-matrix.csv'
$tagRecords | Sort-Object LogicalPoint | Export-Csv -LiteralPath $tagOutputPath -NoTypeInformation -Encoding UTF8

$production = $records | Where-Object { $_.File -match '^src/master/MainUI/' -and $_.File -notmatch '(^|/)bin/|(^|/)obj/' }
$cs = @($production | Where-Object { $_.File -match '\.cs$' })
$designer = @($cs | Where-Object { $_.File -match '(?i)\.designer\.cs$' })
$resx = @($production | Where-Object { $_.File -match '\.resx$' })

Write-Output "legacy_root=$LegacyRoot"
Write-Output "coverage_path=$outputPath"
Write-Output "coverage_rows=$($records.Count)"
Write-Output "tag_matrix_path=$tagOutputPath"
Write-Output "tag_matrix_rows=$($tagRecords.Count)"
Write-Output "legacy_subscription_points=$( @($tagRecords | Where-Object LegacySubscription -eq 'yes').Count )"
Write-Output "source_capability_points=$( @($tagRecords | Where-Object SourceCapability -ne 'no').Count )"
Write-Output "opf_only_reserve_points=$( @($tagRecords | Where-Object ScopeStatus -eq 'OPFOnlyReserve').Count )"
Write-Output "mainui_production_files=$($production.Count)"
Write-Output "mainui_cs=$($cs.Count)"
Write-Output "mainui_designer_cs=$($designer.Count)"
Write-Output "mainui_resx=$($resx.Count)"
