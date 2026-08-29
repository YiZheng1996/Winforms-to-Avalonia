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
foreach ($requiredText in @('TestBenchStateMachine','SafetySignalInvalid','SafetyInterlockOpen','NeedsOperatorAdjustment','无真机')) {
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

Write-Output "PASS coverage_rows=$($coverage.Count) tag_rows=$($tags.Count) legacy_subscription=102 source_capability=124 opf_only_reserve=16 public_excluded_coverage_paths=$($publicExcludedCoveragePaths.Count)"
