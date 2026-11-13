# 信捷 PLC Modbus TCP 现场适配测试追踪

记录日期：2026-09-23

本记录按《信捷PLC_ModbusTCP现场适配立即编码实施方案.md》逐项映射 T01～T60。结果只表示本机软件自动化测试，不表示连接真实 PLC、现场读写或发布回退演练通过。所有测试结果和构建产物写在 `D:\Codex相关\信捷PLC_ModbusTCP现场适配`。

## 全量命令与实际结果

以下命令均在项目根目录 `D:\易峥\2026\2026-09\Avalonia_上位机通用模板\项目代码\XXX.TestBench.Template` 执行，使用 Debug 默认配置、`--no-restore` 和同一 artifacts 路径串行验证。

| 编号 | 实际命令 | 实际结果及证据 |
|---|---|---|
| C | `dotnet build XXX.TestBench.Template.sln --no-restore --artifacts-path 'D:\Codex相关\信捷PLC_ModbusTCP现场适配\artifacts' --verbosity minimal` | 退出码 0；0 警告、0 错误 |
| T-C | `dotnet test tests/XXX.TestBench.Core.Tests/XXX.TestBench.Core.Tests.csproj --no-restore --artifacts-path 'D:\Codex相关\信捷PLC_ModbusTCP现场适配\artifacts' --results-directory 'D:\Codex相关\信捷PLC_ModbusTCP现场适配\results\core-resume-final-full' --logger 'trx;LogFileName=core-resume-final-full.trx' --verbosity minimal` | 158/158 通过；`results\core-resume-final-full\core-resume-final-full.trx` |
| T-I | `dotnet test tests/XXX.TestBench.Integration.Tests/XXX.TestBench.Integration.Tests.csproj --no-restore --artifacts-path 'D:\Codex相关\信捷PLC_ModbusTCP现场适配\artifacts' --results-directory 'D:\Codex相关\信捷PLC_ModbusTCP现场适配\results\integration-resume-final-full' --logger 'trx;LogFileName=integration-resume-final-full.trx' --verbosity minimal` | 131/131 通过；`results\integration-resume-final-full\integration-resume-final-full.trx` |
| T-A | `dotnet test tests/XXX.TestBench.App.Tests/XXX.TestBench.App.Tests.csproj --no-restore --artifacts-path 'D:\Codex相关\信捷PLC_ModbusTCP现场适配\artifacts' --results-directory 'D:\Codex相关\信捷PLC_ModbusTCP现场适配\results\app-resume-final-full-t54' --logger 'trx;LogFileName=app-resume-final-full-t54.trx' --verbosity minimal` | 114/114 通过；`results\app-resume-final-full-t54\app-resume-final-full-t54.trx` |
| T-H | `dotnet test tests/XXX.TestBench.App.Headless.Tests/XXX.TestBench.App.Headless.Tests.csproj --no-restore --artifacts-path 'D:\Codex相关\信捷PLC_ModbusTCP现场适配\artifacts' --results-directory 'D:\Codex相关\信捷PLC_ModbusTCP现场适配\results\headless-resume-final-full' --logger 'trx;LogFileName=headless-resume-final-full.trx' --verbosity minimal` | 7/7 通过；`results\headless-resume-final-full\headless-resume-final-full.trx` |

四个全量测试项目合计 410/410 通过。T54 新增用例的定向复核命令为 `dotnet test tests/XXX.TestBench.App.Tests/XXX.TestBench.App.Tests.csproj --no-restore --artifacts-path 'D:\Codex相关\信捷PLC_ModbusTCP现场适配\artifacts' --results-directory 'D:\Codex相关\信捷PLC_ModbusTCP现场适配\results\app-resume-t54' --logger 'trx;LogFileName=app-resume-t54.trx' --filter 'FullyQualifiedName~XinjeEditorReappliesNativeAddressRulesWhenEditingOrPasting' --verbosity minimal`，1/1 通过。

## T01～T60 用例映射

`T-C`、`T-I`、`T-A`、`T-H` 对应上表实际全量命令及其 TRX。测试方法以完整类名和方法名定位；参数化 Theory 中括号用例分别对应各编号。

| 编号 | 测试方法 | 命令 | 实际结果 |
|---|---|---|---|
| T01 | `XinjeFieldV1ModbusTests.Parser_MapsOnlyTheFourFieldV1Areas`：`D100 → HR:100` | T-I | 通过 |
| T02 | `XinjeFieldV1ModbusTests.Parser_MapsOnlyTheFourFieldV1Areas`：`HD1000 → HR:42088` | T-I | 通过 |
| T03 | `XinjeFieldV1ModbusTests.Parser_MapsOnlyTheFourFieldV1Areas`：`M400 → C:400` | T-I | 通过 |
| T04 | `XinjeFieldV1ModbusTests.Parser_MapsOnlyTheFourFieldV1Areas`：`HM40 → C:49448` | T-I | 通过 |
| T05 | `XinjeFieldV1ModbusTests.Parser_MapsOnlyTheFourFieldV1Areas`：小写、前导零及首尾空格规范化 | T-I | 通过 |
| T06 | `XinjeFieldV1ModbusTests.Parser_RejectsUnsupportedOrOutOfRangeAddresses`：HD 偏移映射溢出 | T-I | 通过 |
| T07 | `XinjeFieldV1ModbusTests.Parser_RejectsUnsupportedOrOutOfRangeAddresses`：寄存器位地址 | T-I | 通过 |
| T08 | `XinjeFieldV1ModbusTests.Parser_RejectsUnsupportedOrOutOfRangeAddresses`：FD/SD/SM/X/Y/S 地址 | T-I | 通过 |
| T09 | `XinjeFieldV1ModbusTests.Parser_RejectsUnsupportedOrOutOfRangeAddresses`：空值、纯数字、负数、内嵌空格、超长数值 | T-I | 通过；TryParse 返回错误，不向外泄漏溢出异常 |
| T10 | `XinjeFieldV1ModbusTests.Descriptor_EnforcesFieldV1TransportOrderAndNativeTypeMatrix`：Modbus TCP | T-I | 通过 |
| T11 | `XinjeFieldV1ModbusTests.Descriptor_EnforcesFieldV1TransportOrderAndNativeTypeMatrix`：串口通道拒绝 | T-I | 通过 |
| T12 | `XinjeFieldV1ModbusTests.Descriptor_EnforcesFieldV1TransportOrderAndNativeTypeMatrix`：大端、低字在前 | T-I | 通过 |
| T13 | `XinjeFieldV1ModbusTests.Descriptor_EnforcesFieldV1TransportOrderAndNativeTypeMatrix`：其他字序及小端组合拒绝 | T-I | 通过 |
| T14 | `XinjeFieldV1ModbusTests.Descriptor_EnforcesFieldV1TransportOrderAndNativeTypeMatrix`：M/HM + Bool | T-I | 通过 |
| T15 | `XinjeFieldV1ModbusTests.Descriptor_EnforcesFieldV1TransportOrderAndNativeTypeMatrix`：M/HM 拒绝 Float32 | T-I | 通过 |
| T16 | `XinjeFieldV1ModbusTests.Descriptor_EnforcesFieldV1TransportOrderAndNativeTypeMatrix`：D/HD + Float32/UInt16/Int32/UInt32 | T-I | 通过 |
| T17 | `XinjeFieldV1ModbusTests.Descriptor_EnforcesFieldV1TransportOrderAndNativeTypeMatrix`：D/HD 拒绝 Bool/Double/Int16 | T-I | 通过 |
| T18 | `XinjeFieldV1ModbusTests.ConfigurationValidation_RejectsAliasesAndOverlappingRegisterRanges`：同设备 D100/d100 重复 | T-I | 通过 |
| T19 | `ModbusProtocolIntegrationTests.DriverDescriptors_ExposeOnlyMatchingTransportAndValidatePointMatrix`、`ReadBatchPlanner_GroupsByAreaAndHonorsRegisterLimit`；另由整套 Integration 回归通用 Modbus TCP/RTU | T-I | 通过 |
| T20 | `ModbusProtocolIntegrationTests.ValueCodec_UsesConfiguredByteAndWordOrderWithoutHostEndianAssumption`；`XinjeFieldV1ModbusTests.Runtime_ReadsContiguousNativeGroupsWithoutHolesAndKeepsNativeMetadata`：Float32 低字在前读为 1.0 | T-I | 通过 |
| T21 | `XinjeFieldV1ModbusTests.ValueCodec_UsesFixedLowWordFirstForSignedAndUnsigned32BitBoundaries`：`[0x5678,0x1234] → 0x12345678` | T-I | 通过 |
| T22 | `XinjeFieldV1ModbusTests.Runtime_ReadsContiguousNativeGroupsWithoutHolesAndKeepsNativeMetadata`：D/HD 调用保持寄存器读取 | T-I | 通过 |
| T23 | `XinjeFieldV1ModbusTests.Runtime_ReadsContiguousNativeGroupsWithoutHolesAndKeepsNativeMetadata`：M/HM 调用线圈读取 | T-I | 通过 |
| T24 | `XinjeFieldV1ModbusTests.Runtime_WriteUsesMappedAddressAndFixedLowWordFirstEncoding`：D 单寄存器写 | T-I | 通过 |
| T25 | `XinjeFieldV1ModbusTests.Runtime_WriteUsesMappedAddressAndFixedLowWordFirstEncoding`：HD 多寄存器写及低字在前 | T-I | 通过 |
| T26 | `XinjeFieldV1ModbusTests.Runtime_WriteUsesMappedAddressAndFixedLowWordFirstEncoding`：M 单线圈写 | T-I | 通过 |
| T27 | `XinjeFieldV1ModbusTests.Runtime_RequestFailurePropagatesAndInvalidatesConnection` | T-I | 通过；请求异常传播且连接失效 |
| T28 | `XinjeFieldV1ModbusTests.WritePipelineReturnsFreshNativePointValueAndDoesNotReadBackAfterWriteFailure` | T-I | 通过；失败不产生成果、不回读、不重试写入 |
| T29 | `DeviceConfigurationViewModelTests.SelectingXinjeModel_FillsOnlyBlankDefaultsAndFixesDataOrder`：型号选项可见 | T-A | 通过 |
| T30 | `DeviceConfigurationViewModelTests.SelectingXinjeModel_FillsOnlyBlankDefaultsAndFixesDataOrder`：主动选择时只填空白站号/端口并固定字序 | T-A | 通过 |
| T31 | `DeviceConfigurationViewModelTests.LoadingWrongXinjeOrderDoesNotRewriteItUntilExplicitRepair` | T-A | 通过；加载不静默改写，明确修复前不保存 |
| T32 | `ShellHeadlessTests.XinjePointDialogShowsNativeAddressAndHidesStructuredModbusFields`；`DevicePointDialogViewModelTests.XinjeEditor_ShowsNativeAddressAndFiltersTypesAsPrefixChanges` | T-H、T-A | 通过 |
| T33 | `DevicePointDialogViewModelTests.XinjeEditor_ShowsNativeAddressAndFiltersTypesAsPrefixChanges`：M/HM 仅 Bool | T-A | 通过 |
| T34 | `DevicePointDialogViewModelTests.XinjeEditor_ShowsNativeAddressAndFiltersTypesAsPrefixChanges`：D/HD 仅 V1 寄存器类型 | T-A | 通过 |
| T35 | `DevicePointDialogViewModelTests.XinjeEditor_PreservesNativeAddressAndWritesMappedStructure` | T-A | 通过；HD1000 保留原生地址并保存 HR/42088 |
| T36 | `XinjeImportWorkflowTests.ImportPreviewUsesDeviceContextPreservesOriginalInputAndBlocksReloadedAlias`、`DevicePointImportPreviewViewModelTests.XinjePreview_ShowsOriginalInputAndResolvedProtocolAddress`：九列模板 D100 预览 | T-A | 通过；预览显示 D100 与 HR:100 |
| T37 | `DevicePointImporterTests.EnglishHeadersAreRejectedByFixedTemplateContract` | T-I | 通过 |
| T38 | `DevicePointRemainingFunctionsTests.ImportPlanRevisionMismatch_IsRejectedBeforeApply` | T-A | 通过 |
| T39 | `XinjeFieldV1ModbusTests.Resolver_UsesModelAndDriverContextAndChecksPersistedStructure` | T-I | 通过；只按型号和驱动上下文解析 |
| T40 | `XinjeFieldV1ModbusTests.ConfigurationAndImportValidation_AcceptReloadedHdAddressAndRejectInconsistentStructure` | T-I | 通过 |
| T41 | `XinjeImportWorkflowTests.ImportPreviewUsesDeviceContextPreservesOriginalInputAndBlocksReloadedAlias`：重载后别名冲突在预览阶段拦截 | T-A | 通过 |
| T42 | `XinjeFieldV1ImportPlannerTests.Build_RecomputesStaleCanonicalKeysAndDetectsNativeAlias`、`Build_AfterSerializationDetectsDuplicateDespiteNonPersistedCanonicalKey` | T-C | 通过 |
| T43 | `XinjeFieldV1ImportPlannerTests.Build_DetectsMAndHmNativeAliasInCoilAddressSpace`；`XinjeFieldV1ModbusTests.ConfigurationValidation_RejectsAliasesAndOverlappingRegisterRanges` | T-C、T-I | 通过 |
| T44 | `XinjeFieldV1ImportPlannerTests.Build_RejectsOverlappingRangesEvenWhenStartAddressesDiffer`；`XinjeFieldV1ModbusTests.ConfigurationValidation_RejectsAliasesAndOverlappingRegisterRanges` | T-C、T-I | 通过 |
| T45 | `XinjeFieldV1ModbusTests.Descriptor_EnforcesProtocolEndForOneAndTwoRegisterPoints` | T-I | 通过 |
| T46 | `XinjeFieldV1ImportPlannerTests.Build_DoesNotReportSameIdentityUpdateAsAnAddressConflict`、`Build_SeparatesAddressRangesByDevice`；`DevicePointAddressRangeValidatorTests.EqualOffsetsInDifferentAreas_AreIndependent` | T-C | 通过 |
| T47 | `XinjeFieldV1ModbusTests.Runtime_ReadsContiguousNativeGroupsWithoutHolesAndKeepsNativeMetadata`：HD1000/1002 合并，HD1008/1020 分批 | T-I | 通过；批读未覆盖未配置空隙 |
| T48 | `XinjeFieldV1ModbusTests.Runtime_GapAddressFailureDoesNotAffectConfiguredPointReads`；`ModbusProtocolIntegrationTests.ReadBatchPlanner_GroupsByAreaAndHonorsRegisterLimit` | T-I | 通过 |
| T49 | `XinjeFieldV1ModbusTests.ReadBatchPlanner_Respects125And2000LimitsWithoutSplittingAValue`、`Runtime_ReadsReference59PointAddressCatalogWithoutGapRequests` | T-I | 通过 |
| T50 | `XinjeFieldV1ModbusTests.Runtime_RequestFailurePropagatesAndInvalidatesConnection`、`WritePipelineReturnsFreshNativePointValueAndDoesNotReadBackAfterWriteFailure` | T-I | 通过；未完成批次不回 Good，写失败不自动重试 |
| T51 | `XinjeFieldV1ModbusTests.Runtime_ReadsContiguousNativeGroupsWithoutHolesAndKeepsNativeMetadata`、`Runtime_WriteUsesMappedAddressAndFixedLowWordFirstEncoding`、`WritePipelineReturnsFreshNativePointValueAndDoesNotReadBackAfterWriteFailure`、`Runtime_DecodeFailureReturnsBadValueWithOriginalNativeMetadata` | T-I | 通过；单读/批读/写返回/写后验证/解码异常均保留原生 Address 与 PointId/DeviceId/Revision/连接代次 |
| T52 | `DevicePointDialogViewModelTests.XinjeEditor_RejectsPersistedAddressAndStructureMismatchUntilAddressIsEdited`；`XinjeFieldV1ModbusTests.ConfigurationAndImportValidation_AcceptReloadedHdAddressAndRejectInconsistentStructure` | T-A、T-I | 通过；编辑、完整配置和导入均拒绝不一致结构 |
| T53 | `DeviceConfigurationViewModelTests.SelectingXinjeModel_FillsOnlyBlankDefaultsAndFixesDataOrder`、`LoadingWrongXinjeOrderDoesNotRewriteItUntilExplicitRepair` | T-A | 通过 |
| T54 | `DevicePointDialogViewModelTests.XinjeEditor_ShowsNativeAddressAndFiltersTypesAsPrefixChanges`、`XinjeEditorReappliesNativeAddressRulesWhenEditingOrPasting` | T-A | 通过；覆盖 D→M→空值→非法前缀→HD、编辑回填和剪贴板回填；新增方法 1/1 定向通过 |
| T55 | `XinjeFieldV1ModbusTests.ChangingDeviceModelRejectsIncompatibleExistingAddressesWithoutRewritingPoints` | T-I | 通过；型号切换不会转换或删除不兼容点位 |
| T56 | `XinjeFieldV1ModbusTests.ValueCodec_UsesFixedLowWordFirstForSignedAndUnsigned32BitBoundaries` | T-I | 通过；Int32/UInt32 边界读写编码往返 |
| T57 | `XinjeFieldV1ModbusTests.Runtime_ReadsReference59PointAddressCatalogWithoutGapRequests` | T-I | 通过；59 点只读内存样本，无 PLC 写入 |
| T58 | `XinjeImportWorkflowTests.ImportPreviewUsesDeviceContextPreservesOriginalInputAndBlocksReloadedAlias`：预览快照一致、HD1000 导出/再导入为 Unchanged 且不增加修订/点位数 | T-A | 通过 |
| T59 | `XinjeImportWorkflowTests.ImportPreviewUsesDeviceContextPreservesOriginalInputAndBlocksReloadedAlias`：SourceRevision 不变但目录指纹改变时拒绝旧计划；现有 `SimulationRuntimeTests.Factory_Simulation_ReadsWritesAndMarksSimulation`、`ModbusProtocolIntegrationTests.TcpRuntime_RemainsConnectingUntilFirstValidPointResponse_ThenReadsAndWrites`、`ModbusProtocolIntegrationTests.RtuChannel_UsesOneSharedClientAndClosesOnlyAfterChannelDrain`、`S7NetPlusIntegrationTests.Descriptor_IsImplementedAfterS7NetPlusRuntimeAdded` 由 T-I 整套回归执行 | T-A、T-I | 通过 |
| T60 | `DeviceConfigurationServiceTests.CandidateRuntimeCreationFailureKeepsOldRevisionAndRestartsOldRuntime` | T-C | 软件失败保护通过：候选运行时失败时保留旧 Revision 并重启旧运行时。离线旧程序加载回退演练未执行，发布回退条件未完成 |

## 未完成的现场与发布条件

- T60 的自动化候选应用失败保护已通过；方案要求的离线旧程序加载/回退演练仍待完成。本轮未发现 `D:\Codex相关\信捷PLC_ModbusTCP现场适配\rollback-check` 演练目录，也未收到旧版程序包和配套兼容配置快照，故不将其记为通过。
- 未连接真实信捷 PLC；现场读取、PLC 写入、断线重连、真实型号/固件兼容以及现场已知值核对均未验收。
- 当前证据没有向真实 PLC 发送写请求。软件自动化结果不替代现场验收或发布放行。


## 2026-09-28 XLSX 导入模板修正阶段结果

### 本阶段范围

用户已确认本阶段不要求旧程序/旧配置回退演练；既有 T60 候选配置失败保护仍保留。历史 2026-09-23 的 410/410 记录及当时未做回退演练的事实保持原样，不能据此声称最新 XLSX 已通过现场验证。当前没有连接 PLC、发送写请求或执行 UOS 验收。

### 构建与全量测试

本轮结果使用独立目录 `D:\Codex相关\信捷PLC_XLSX导入模板修正\run-20260924-174736`，不与历史记录的 410 项混算。

| 项目 | 实际结果 | TRX / 产物 |
|---|---|---|
| 解决方案构建 | 退出码 0；0 警告、0 错误 | `artifacts` |
| Core 全量 | 158/158 通过 | `results\core-full-final\core-full-final.trx` |
| Integration 全量 | 141/141 通过，包含 X01～X22 对应合同测试 | `results\integration-full-final-v3\integration-full-final-v3.trx` |
| App 全量 | 一轮 117/117 通过 | `results\app-full-final\app-full-final.trx` |
| App 全量复跑 | 116/117；`DevicePointRemainingFunctionsTests.ImportPlanRevisionMismatch_IsRejectedBeforeApply` 遇到 SQLite `database is locked`。该用例单独运行 1/1 通过；新增下载导入流程单独运行 2/2、模板请求单独运行 1/1 通过。另一次全量复跑在重复锁错误后超过 9 分钟无进展，已中止且未生成 TRX。 | `results\app-full-final-v2\app-full-final-v2.trx`、`results\app-revision-isolated\app-revision-isolated.trx`、`results\app-xinje-workflow-relocated\app-xinje-workflow-relocated.trx`、`results\app-xinje-request-final\app-xinje-request-final.trx` |
| 重点 XLSX 合同变体 | 5/5 通过：删列、加列、换序、错误首表名、旧 v6 十一列表头均拒绝 | `results\integration-template-contract\integration-template-contract.trx` |

### X01～X22 证据映射

| 编号 | 测试方法及实际覆盖 | 结果 |
|---|---|---|
| X01 | `DevicePointManagementViewModelTests.TemplateRequest_XinjeTargetsUseModelSpecificTypesWithoutChangingGenericDescriptor`：单信捷型号和精确五类型集合 | 通过；App 定向 1/1 |
| X02 | 同上：一个范围内两个信捷设备各自类型集合及范围目标数 | 通过；App 定向 1/1 |
| X03 | 同上：信捷与通用 Modbus 混合、通用 Int16/Double 保留、共享描述器不变 | 通过；App 定向 1/1 |
| X04 | `XinjeDownloadedTemplateWorkflowTests.DownloadedXinjeTemplate_CanPreviewApplyAndReimportWithoutChanges`；`XinjeDevicePointTemplateTests.Export_XinjeWorkbookUsesNativeExamplesFiveTypeChoicesAndConsistentHints`：真实下载入口、三工作表、九列及空白填写页 | 通过；App 流程 2/2、Integration 全量通过 |
| X05 | `Export_XinjeWorkbookUsesNativeExamplesFiveTypeChoicesAndConsistentHints`：HD1000/浮点型、M400/布尔量且写入示例无量程 | 通过；Integration 全量通过 |
| X06 | 同上：下拉严格按五种中文类型顺序输出 | 通过；Integration 全量通过 |
| X07 | 同上：地址规则、类型规则、字序说明和表头批注一致 | 通过；Integration 全量通过 |
| X08 | 同上：`  xInJe_field_v1  ` 型号大小写与首尾空白仍识别为信捷 | 通过；Integration 全量通过 |
| X09 | `XinjeDevicePointTemplateTests.Export_NonXinjeTargetsKeepExistingExamplesAndDefaultWorkbook`：通用 Modbus TCP/RTU 示例和类型能力 | 通过；Integration 全量通过 |
| X10 | 同上：S7-200 SMART、S7-1200/1500、Simulation 示例保持原行为 | 通过；Integration 全量通过 |
| X11 | 同上：无请求默认导出模板仍为原仿真示例 | 通过；Integration 全量通过 |
| X12 | `Export_MixedTargetsKeepPerDeviceExamplesAndUnionChoices` 与 App 请求测试：各设备示例、设备前缀、类型并集和逐行校验提示 | 通过；Integration 全量、App 定向通过 |
| X13 | `DownloadedXinjeTemplate_CanPreviewApplyAndReimportWithoutChanges`：真实下载文件复制示例行后可预览，预览前后 Revision/点位数不变 | 通过；App 流程 2/2 |
| X14 | 同上：D100、HD1000、M400、HM40 四行均通过预览 | 通过；App 流程 2/2 |
| X15 | 同上：应用后保留原生地址并保存 HR/C 区域偏移 | 通过；App 流程 2/2 |
| X16 | 同上：重复导入为 Unchanged，应用不新增 Revision 或点位 | 通过；App 流程 2/2 |
| X17 | `XinjeImportWorkflowTests.ImportPreviewUsesDeviceContextPreservesOriginalInputAndBlocksReloadedAlias`：已保存点位实际导出再导入，HD1000 身份不重复 | 通过；App 全量通过 |
| X18 | `XinjeDownloadedTemplateWorkflowTests.DownloadedXinjeTemplate_StillRejectsUnsupportedAddressOrType`：短整型、双精度及 M 区浮点类型拒绝，配置不变 | 通过；App 流程 2/2 |
| X19 | 同上：HR:0、C:1、D10.0、FD100 拒绝，配置不变 | 通过；App 流程 2/2 |
| X20 | `XinjeDevicePointTemplateTests.Import_XinjeWorkbookRejectsFixedTemplateContractMutations`：删列、加列、换序、错误首表名、旧 11 列表头五个变体 | 通过；定向 5/5、Integration 全量 141/141 |
| X21 | 流程测试预览未应用时 Revision/点位数不变；`DevicePointRemainingFunctionsTests.ImportPlanRevisionMismatch_IsRejectedBeforeApply` 旧计划版本变化拒绝 | 通过；请求/流程定向通过，版本用例单独 1/1 |
| X22 | `XinjeDevicePointTemplateTests.Export_XinjeTargetMissingExampleTypeFailsExplicitly`：缺少 Float32 或 Bool 时明确失败 | 通过；Theory 两项均在 Integration 全量中通过 |

### 工作簿与现场边界

三份样例已生成在 `D:\Codex相关\信捷PLC_XLSX导入模板修正\samples`：单信捷、信捷+通用 Modbus 混合范围、通用 Modbus。当前环境未发现 Excel/LibreOffice 可执行程序；工作簿结构、下拉、批注和导入链由 ClosedXML 自动化验证，图形显示未验证。新模板未做 PLC 实机验证；同事程序既有现场依据与当前模板实机结果分开记录。