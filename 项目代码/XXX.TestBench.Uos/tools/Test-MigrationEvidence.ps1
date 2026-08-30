[CmdletBinding()]
param(
    [string]$ArtifactRoot
)

$ErrorActionPreference = 'Stop'
$targetRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
if ([string]::IsNullOrWhiteSpace($ArtifactRoot)) {
    $ArtifactRoot = Join-Path $targetRoot 'docs'
}

$coverage = @(Import-Csv (Join-Path $ArtifactRoot 'legacy-coverage.csv'))
$tags = @(Import-Csv (Join-Path $ArtifactRoot 'tag-and-write-matrix.csv'))
$uiCoveragePath = Join-Path $ArtifactRoot 'legacy-ui-coverage.csv'
$uiCoverage = if (Test-Path -LiteralPath $uiCoveragePath -PathType Leaf) {
    @(Import-Csv $uiCoveragePath)
}
else {
    @()
}
$uiEvidencePath = Join-Path $ArtifactRoot 'ui-test-evidence.csv'
$uiEvidence = if (Test-Path -LiteralPath $uiEvidencePath -PathType Leaf) {
    @(Import-Csv $uiEvidencePath)
}
else {
    @()
}
$failures = @()
# These exact legacy artifacts remain in the coverage manifest for traceability,
# but are intentionally absent from a public clone because they are local data,
# generated/build files, user-local settings, or third-party packaging artifacts.
$publicExcludedCoveragePaths = @(
    'src/master/DB/TestBed.db',
    'src/master/DB/reports/report.xlsx',
    'src/master/DB/SumatraPDF.exe',
    'src/master/Lib/DSL_DLL/rw3.pdb',
    'src/master/Lib/DSL_DLL/RWDSLDebugger.pdb',
    'src/master/Lib/Newtonsoft.Json.pdb',
    'src/master/Lib/Report/ReportNuget.1.0.1.nupkg',
    'src/master/Lib/Report/RW.1.0.0.nupkg',
    'src/master/Lib/ReportNuget.1.0.0.nupkg',
    'src/master/MainUI/FodyWeavers.xsd',
    'src/master/MainUI/MainUI.csproj.user',
    'src/master/MainUI/Properties/PublishProfiles/FolderProfile.pubxml',
    'src/master/MainUI/Properties/PublishProfiles/FolderProfile.pubxml.user'
)

$duplicateFiles = @($coverage | Group-Object File | Where-Object Count -gt 1)
if ($duplicateFiles.Count -gt 0) { $failures += 'legacy-coverage.csv contains duplicate primary file rows' }

$legacyRoot = Join-Path $repositoryRoot 'XXX试验台模板'
foreach ($row in $coverage) {
    $path = Join-Path $legacyRoot ($row.File -replace '/', '\')
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        $allowedPublicExclusion = $row.File -in $publicExcludedCoveragePaths
        if (-not $allowedPublicExclusion) { $failures += "coverage file missing: $($row.File)" }
    }
    if ($row.File -match '(^|/)(bin|obj|tmp)(/|$)') { $failures += "generated path included: $($row.File)" }
}

foreach ($excludedPath in $publicExcludedCoveragePaths) {
    $excludedRows = @($coverage | Where-Object File -eq $excludedPath)
    if ($excludedRows.Count -ne 1) {
        $failures += "public exclusion must have exactly one coverage row: $excludedPath"
    }
}

# UI 专项矩阵逐一覆盖所有 Designer 单元；这里验证的是清点完整性，行为本身由
# Avalonia VM 和独立 Headless 进程验证，避免用源码文本冒充交互测试。
$legacyDesigners = @($coverage | Where-Object File -match '(?i)^src/master/MainUI/.*\.designer\.cs$')
if ($uiCoverage.Count -ne 51) { $failures += "expected 51 UI coverage rows, got $($uiCoverage.Count)" }
$duplicateSurfaceIds = @($uiCoverage | Group-Object SurfaceId | Where-Object Count -gt 1)
if ($duplicateSurfaceIds.Count -gt 0) { $failures += 'legacy-ui-coverage.csv contains duplicate SurfaceId rows' }
$duplicateUiDesigners = @($uiCoverage | Group-Object LegacyDesigner | Where-Object Count -gt 1)
if ($duplicateUiDesigners.Count -gt 0) { $failures += 'legacy-ui-coverage.csv contains duplicate LegacyDesigner rows' }
foreach ($designer in $legacyDesigners) {
    if (@($uiCoverage | Where-Object LegacyDesigner -eq $designer.File).Count -ne 1) {
        $failures += "UI designer must have exactly one coverage row: $($designer.File)"
    }
}
foreach ($row in $uiCoverage) {
    foreach ($field in @('SurfaceId','LegacyDesigner','SurfaceKind','LegacyCapability','LegacyEntryEvidence','AvaloniaTarget','ImplementationStatus','BehaviorEvidence','HeadlessEvidence','SafetyDisposition','RemainingGap')) {
        if ([string]::IsNullOrWhiteSpace($row.$field)) {
            $failures += "UI coverage field is empty: $($row.SurfaceId).$field"
        }
    }
    if (@($coverage | Where-Object File -eq $row.LegacyDesigner).Count -ne 1) {
        $failures += "UI coverage designer not found in legacy coverage: $($row.LegacyDesigner)"
    }
    if (-not [string]::IsNullOrWhiteSpace($row.LegacyResx) -and @($coverage | Where-Object File -eq $row.LegacyResx).Count -ne 1) {
        $failures += "UI coverage resx not found in legacy coverage: $($row.LegacyResx)"
    }
}

# 覆盖矩阵中的证据 ID 必须能回溯到真实可执行测试或本脚本。Marker 采用
# 已存在且在文件内唯一的源码字符串，防止用无法定位的自由文本冒充测试证据。
$behaviorEvidenceIds = @($uiCoverage.BehaviorEvidence | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Sort-Object -Unique)
$headlessEvidenceIds = @($uiCoverage.HeadlessEvidence | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Sort-Object -Unique)
$requiredUiEvidenceIds = @($behaviorEvidenceIds + $headlessEvidenceIds | Sort-Object -Unique)
$duplicateEvidenceIds = @($uiEvidence | Group-Object EvidenceId | Where-Object Count -gt 1)
if ($duplicateEvidenceIds.Count -gt 0) { $failures += 'ui-test-evidence.csv contains duplicate EvidenceId rows' }
$duplicateEvidenceMarkers = @($uiEvidence | Group-Object Marker | Where-Object Count -gt 1)
if ($duplicateEvidenceMarkers.Count -gt 0) { $failures += 'ui-test-evidence.csv contains duplicate Marker values' }

foreach ($evidenceId in $requiredUiEvidenceIds) {
    if (@($uiEvidence | Where-Object EvidenceId -eq $evidenceId).Count -ne 1) {
        $failures += "UI evidence ID must have exactly one registry row: $evidenceId"
    }
}
foreach ($evidenceId in $behaviorEvidenceIds) {
    $registryRow = @($uiEvidence | Where-Object EvidenceId -eq $evidenceId)
    if ($registryRow.Count -eq 1 -and $registryRow[0].EvidenceKind -notin @('Behavior','Composite','Matrix')) {
        $failures += "behavior evidence has incompatible kind: $evidenceId -> $($registryRow[0].EvidenceKind)"
    }
}
foreach ($evidenceId in $headlessEvidenceIds) {
    $registryRow = @($uiEvidence | Where-Object EvidenceId -eq $evidenceId)
    if ($registryRow.Count -eq 1 -and $registryRow[0].EvidenceKind -notin @('Headless','Composite','Matrix')) {
        $failures += "Headless evidence has incompatible kind: $evidenceId -> $($registryRow[0].EvidenceKind)"
    }
}
foreach ($row in $uiEvidence) {
    foreach ($field in @('EvidenceId','EvidenceKind','SourcePath','Marker','Behavior')) {
        if ([string]::IsNullOrWhiteSpace($row.$field)) {
            $failures += "UI evidence field is empty: $($row.EvidenceId).$field"
        }
    }
    if ($row.EvidenceId -notin $requiredUiEvidenceIds) {
        $failures += "UI evidence registry row is not referenced by coverage: $($row.EvidenceId)"
    }
    if ($row.EvidenceKind -notin @('Behavior','Headless','Composite','Matrix')) {
        $failures += "UI evidence kind is invalid: $($row.EvidenceId).$($row.EvidenceKind)"
    }

    $sourcePath = Join-Path $targetRoot ($row.SourcePath -replace '/', '\')
    if (-not (Test-Path -LiteralPath $sourcePath -PathType Leaf)) {
        $failures += "UI evidence source file missing: $($row.EvidenceId) -> $($row.SourcePath)"
        continue
    }

    $sourceText = Get-Content -Raw -LiteralPath $sourcePath
    $markerCount = [regex]::Matches($sourceText, [regex]::Escape($row.Marker)).Count
    if ($markerCount -ne 1) {
        $failures += "UI evidence marker must occur exactly once: $($row.EvidenceId) -> count=$markerCount"
    }
}

$requiredProcedureUiRoots = @(
    'src/master/MainUI/Procedure/ucBaseManagerUI.cs',
    'src/master/MainUI/Procedure/UCCalibration.cs',
    'src/master/MainUI/Procedure/ucItemConfiguration.cs',
    'src/master/MainUI/Procedure/ucItemManagerial.cs',
    'src/master/MainUI/Procedure/ucKindManage.cs',
    'src/master/MainUI/Procedure/ucModelManage.cs',
    'src/master/MainUI/Procedure/ucTestParams.cs'
)
foreach ($path in $requiredProcedureUiRoots) {
    if (@($coverage | Where-Object { $_.File -eq $path -and $_.PrimaryModule -eq 'ui-shell' }).Count -ne 1) {
        $failures += "Procedure root UI is not classified as ui-shell: $path"
    }
}

$duplicateTags = @($tags | Group-Object LogicalPoint | Where-Object Count -gt 1)
if ($duplicateTags.Count -gt 0) { $failures += 'tag-and-write-matrix.csv contains duplicate logical points' }
if ($tags.Count -ne 140) { $failures += "expected 140 tag rows, got $($tags.Count)" }
if (@($tags | Where-Object LegacySubscription -eq 'yes').Count -ne 102) { $failures += 'legacy subscription count is not 102' }
if (@($tags | Where-Object SourceCapability -ne 'no').Count -ne 124) { $failures += 'source capability count is not 124' }
if (@($tags | Where-Object ScopeStatus -eq 'OPFOnlyReserve').Count -ne 16) { $failures += 'OPF reserve count is not 16' }
if (@($tags | Where-Object TargetDataAccess -ne 'ReadOnlyDataVariable').Count -ne 0) { $failures += 'a target point is not read-only in the P1 matrix' }

$requiredFirstSlice = @('Gateway.Health.NoError','Gateway.Health.Simulated','SMART.PLC.AI.MAI00','SMART.PLC.DI.MDI00','Modbus.WSD.CH00')
$firstSlice = @($tags | Where-Object FirstSlice -eq 'yes').LogicalPoint
foreach ($point in $requiredFirstSlice) {
    if ($point -notin $firstSlice) { $failures += "first slice point missing: $point" }
}

$behavior = Get-Content -Raw (Join-Path $ArtifactRoot 'behavior-contracts.md')
$platform = Get-Content -Raw (Join-Path $ArtifactRoot 'platform-and-acceptance.md')
$communication = Get-Content -Raw (Join-Path $ArtifactRoot 'communication-decision.md')
$coreContractPath = Join-Path $ArtifactRoot 'core-contracts.md'
if (-not (Test-Path -LiteralPath $coreContractPath -PathType Leaf)) {
    $failures += 'core-contracts.md is missing'
    $coreContract = ''
}
else {
    $coreContract = Get-Content -Raw $coreContractPath
}
foreach ($requiredText in @('A02-SAFE-01','Unknown','连接代次','不重放')) {
    if ($behavior -notmatch [regex]::Escape($requiredText)) { $failures += "behavior contract missing: $requiredText" }
}
foreach ($requiredText in @('G0','G1','G2','30 分钟')) {
    if ($platform -notmatch [regex]::Escape($requiredText)) { $failures += "platform acceptance record missing: $requiredText" }
}
foreach ($requiredText in @('S7NetPlus','NModbus','Modbus RTU','Modbus TCP','PressureAdjustmentValveB11','ReadOnly','离线仿真')) {
    if ($communication -notmatch [regex]::Escape($requiredText)) { $failures += "communication decision missing: $requiredText" }
}
foreach ($requiredText in @('TestBenchStateMachine','SafetySignalInvalid','SafetyInterlockOpen','NeedsOperatorAdjustment','仅读协议探针')) {
    if ($coreContract -notmatch [regex]::Escape($requiredText)) { $failures += "core contract missing: $requiredText" }
}

$runtimePath = Join-Path $targetRoot 'src\XXX.TestBench.Gateway\Infrastructure\ReadOnlyGatewayRuntime.cs'
$hostProjectPath = Join-Path $targetRoot 'src\XXX.TestBench.Gateway.Host\XXX.TestBench.Gateway.Host.csproj'
$runtimeTestPath = Join-Path $targetRoot 'tests\XXX.TestBench.Gateway.Runtime.Tests\Program.cs'
foreach ($requiredPath in @($runtimePath, $hostProjectPath, $runtimeTestPath)) {
    if (-not (Test-Path -LiteralPath $requiredPath -PathType Leaf)) { $failures += "Phase C artifact missing: $requiredPath" }
}

$uiProjectPath = Join-Path $targetRoot 'src\XXX.TestBench.Avalonia\XXX.TestBench.Avalonia.csproj'
$uiViewPath = Join-Path $targetRoot 'src\XXX.TestBench.Avalonia\MainWindow.axaml'
$uiTestPath = Join-Path $targetRoot 'tests\XXX.TestBench.Avalonia.Tests\Program.cs'
$headlessTestPath = Join-Path $targetRoot 'tests\XXX.TestBench.Avalonia.Headless.Tests\Program.cs'
$uiContractPath = Join-Path $ArtifactRoot 'ui-contracts.md'
foreach ($requiredPath in @($uiProjectPath, $uiViewPath, $uiTestPath, $headlessTestPath, $uiContractPath)) {
    if (-not (Test-Path -LiteralPath $requiredPath -PathType Leaf)) { $failures += "Phase D artifact missing: $requiredPath" }
}
if (Test-Path -LiteralPath $uiContractPath -PathType Leaf) {
    $uiContract = Get-Content -Raw $uiContractPath
    foreach ($requiredText in @('组合根','双向绑定','AutomationProperties.Name','Test00','Zero/Gain','Headless')) {
        if ($uiContract -notmatch [regex]::Escape($requiredText)) { $failures += "UI contract missing: $requiredText" }
    }
}

$settingsPath = Join-Path $ArtifactRoot '..\config\gatewaysettings.json'
if (-not (Test-Path -LiteralPath $settingsPath -PathType Leaf)) {
    $failures += 'gatewaysettings.json is missing'
}
else {
    $settings = Get-Content -Raw $settingsPath | ConvertFrom-Json
    if ($settings.gateway.runMode -ne 'ReadOnly') { $failures += 'gateway runMode is not ReadOnly' }
    if ($settings.gateway.transportMode -ne 'OfflineSimulation') { $failures += 'gateway transportMode is not OfflineSimulation' }
    if ($settings.gateway.activeModbusTransport -notin @('Rtu','Tcp')) { $failures += 'gateway active Modbus transport is invalid' }
    if ($settings.gateway.disableCalibrationWritesOnStartup -ne $true) { $failures += 'startup calibration write guard is not enabled' }
    if (@($settings.gateway.s7Endpoints | Where-Object enabled).Count -ne 1) { $failures += 'expected exactly one enabled S7 profile' }
    if ($settings.gateway.modbusRtu.portName -ne 'COM1' -or $settings.gateway.modbusRtu.baudRate -ne 9600) { $failures += 'Modbus RTU defaults are incorrect' }
    if ($settings.gateway.modbusTcp.port -ne 502 -or $settings.gateway.modbusTcp.unitId -ne 1) { $failures += 'Modbus TCP defaults are incorrect' }
}

if ($failures.Count -gt 0) {
    $failures | ForEach-Object { Write-Error $_ }
    exit 1
}

Write-Output "PASS coverage_rows=$($coverage.Count) ui_rows=$($uiCoverage.Count) ui_evidence_rows=$($uiEvidence.Count) tag_rows=$($tags.Count) legacy_subscription=102 source_capability=124 opf_only_reserve=16 public_excluded_coverage_paths=$($publicExcludedCoveragePaths.Count)"
