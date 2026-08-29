using AntdUI;
using MainUI.Service;
using Sunny.UI;
using Label = System.Windows.Forms.Label;

namespace MainUI
{
    public partial class UcHMI : UserControl
    {
        #region 全局变量
        private RW.UI.Controls.Report.RWReport rWReport = new();
        private readonly frmMainMenu frm = new();
        Dictionary<TaskInfo, BaseTest> DicTestItems = [];
        public delegate void RunStatusHandler(bool obj);
        public event RunStatusHandler EmergencyStatusChanged;
        private static ParaConfig paraconfig;
        private List<ItemPointModel> _itemPoints = [];
        private readonly ControlMappings controls = new();
        private readonly ControlInitializationService _controlInitService;
        public delegate void TestStateHandler(bool isTesting, bool Exit = false);
        public event TestStateHandler TestStateChanged;
        private readonly string reportPath;
        private readonly OPCEventRegistration _opcEventRegistration;
        private readonly ReportService _reportService;
        private readonly TableService _tableService;
        private readonly CountdownService _countdownService;

        #endregion

        /// <summary>
        /// 实例化服务控件
        /// </summary>
        public UcHMI()
        {
            InitializeComponent();
            _opcEventRegistration = new OPCEventRegistration(this);
            reportPath = Path.Combine(Application.StartupPath, Constants.ReportsPath);
            _reportService = new ReportService(reportPath, rWReport);
            _tableService = new TableService(TableItemPoint, _itemPoints);
            _countdownService = new CountdownService(LabTestTime);
            _controlInitService = new ControlInitializationService(controls);
        }

        #region 初始化
        public void Init()
        {
            try
            {
                InitializeOPC();  //初始化OPC连接和组
                InitializeControls();  //初始化控件和数据
                RegisterOPCHandlers();  //注册OPC组事件处理程序
                RegisterTestEventHandlers();  //注册测试状态和提示事件处理程序
                SetInitialState();  //设置初始状态
                InitializePermissions(); //初始化权限
                InitializeProcessInterface(); //加载工艺界面
                EnsureFrmHandleCreated(); //确保主窗体句柄已创建
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "系统提示", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>
        /// 加载工艺界面
        /// </summary>
        private void InitializeProcessInterface()
        {
            UcHMI_FLE ucHMI_PBU = new(frm)
            {
                // 工艺图使用固定坐标布局；在更宽的工作区中保持居中，避免元件被拉伸变形。
                //Anchor = AnchorStyles.None
            };
            grpRainy.Controls.Add(ucHMI_PBU);
        }

        /// <summary>
        /// ===== 确保主窗体句柄已创建 =====
        /// 这个方法必须在 Init() 的最后调用
        /// </summary>
        private void EnsureFrmHandleCreated()
        {
            try
            {
                // 1. 检查 frm 是否为 null
                if (frm == null)
                {
                    string error = "frmMainMenu 实例为 null，无法初始化窗体句柄";
                    NlogHelper.Default.Error(error);
                    throw new InvalidOperationException(error);
                }

                // 2. 检查句柄是否已创建
                if (!frm.IsHandleCreated)
                {
                    // 访问 Handle 属性会强制创建窗口句柄
                    var handle = frm.Handle;
                    NlogHelper.Default.Info($"主窗体句柄已创建: {handle}");
                }
                else
                {
                    NlogHelper.Default.Info($"主窗体句柄已存在: {frm.Handle}");
                }

                // 3. 验证句柄确实已创建
                if (!frm.IsHandleCreated)
                {
                    string error = "主窗体句柄创建失败";
                    NlogHelper.Default.Error(error);
                    throw new InvalidOperationException(error);
                }

                // 默认自动状态
                OPCHelper.TestCongrp[0] = true;
            }
            catch (Exception ex)
            {
                NlogHelper.Default.Error($"确保主窗体句柄创建失败: {ex.Message}", ex);
                throw;
            }
        }

        /// <summary>
        /// 初始化权限
        /// </summary>
        private void InitializePermissions()
        {
            try
            {
                var currentUser = NewUsers.NewUserInfo;
                PermissionHelper.Initialize(currentUser.ID, currentUser.Role_ID);
                PermissionHelper.ApplyPermissionToControl(this, currentUser.ID);
            }
            catch (Exception ex)
            {
                NlogHelper.Default.Error($"初始化权限失败：{ex.Message}");
                throw;
            }
        }

        // 初始化OPC连接和组
        private void InitializeOPC()
        {
            OPCHelper.Init();
        }

        // 初始化控件和数据
        private void InitializeControls()
        {
            _controlInitService.InitializeAllControls(this);
            _tableService.InittializeColumns(); // 初始化表格列
        }

        private void TableItemPoint_CheckedChanged(object sender, TableCheckEventArgs e)
        {
            _tableService.HandleCheckedChanged(e);
        }

        // 注册OPC组事件处理程序
        private void RegisterOPCHandlers()
        {
            _opcEventRegistration.RegisterAll();
        }

        // 注册测试状态和提示事件处理程序
        private void RegisterTestEventHandlers()
        {
            BaseTest.TestStateChanged += BaseTest_TestStateChanged;
            BaseTest.TipsChanged += BaseTest_TipsChanged;
            BaseTest.WaitTick += BaseTest_WaitTick;
            BaseTest.WaitStepTick += BaseTest_WaitStepTick;
            BaseTest.TimingChanged += BaseTest_TimingChanged;
        }

        // 测试计时变更事件
        private void BaseTest_TimingChanged(object sender, int e) { }

        // 测试等待步骤进度事件，使用等待的毫秒数及步骤名称
        private void BaseTest_WaitStepTick(int arg1, string arg2)
        {
            // 可用于显示等待步骤的进度
            // 使用UI更新逻辑，例如进度条或标签
            // 禁止使用LabTestTime控件进行刷新，此控件只做试验总时间用
            // uiPanel2.Text = $"{arg2}:{arg1} 秒";  // 显示具体时间
        }

        // 测试等待进度事件，使用等待的毫秒数
        private void BaseTest_WaitTick(int obj)
        {
            // 可用于显示等待的进度
            // 使用UI更新逻辑，例如进度条或标签
            // 禁止使用LabTestTime控件进行刷新，此控件只做试验总时间用
            // uiPanel2.Text = $"{arg2}:{arg1} 秒";  // 显示具体时间
        }

        // 设置初始状态
        private void SetInitialState()
        {
            btnStopTest.Enabled = false;
        }

        // 测试提示信息变更事件处理程序
        private void BaseTest_TipsChanged(object sender, object info)
        {
            AppendText(info.ToString());
        }

        // 测试状态变更事件处理程序
        private void BaseTest_TestStateChanged(bool isTesting)
        {
            Disable(isTesting);
        }

        /// <summary>
        /// 根据测试状态启用或禁用控件
        /// </summary>
        /// <param name="isTesting">是否正在测试中</param>
        private void Disable(bool isTesting)
        {
            btnStopTest.Enabled = isTesting;  // 停止按钮在测试时启用
            btnStartTest.Enabled = !isTesting; // 开始按钮在测试时禁用

            // 在测试进行时禁用的控件组
            var controlsToDisable = new Control[]
            {
                btnProductSelection, // 产品选择按钮
                TableItemPoint,    // 测试项表格
                panelHand,        // 手动控制面板
                grpRainy            // 
            };

            // 批量设置控件状态
            foreach (var control in controlsToDisable)
            {
                control.Enabled = !isTesting;
            }

            Refresh(); // 触发UI更新
        }

        /// <summary>
        /// 初始化报表
        /// </summary>
        /// <param name="reportFileName">报表文件名</param>
        private void InitializeReport(string reportFileName)
        {
            try
            {
                if (string.IsNullOrEmpty(reportFileName))
                    return;

                reportFileName = ReportService.GetDefaultReportPath() + reportFileName;

                // 检查文件是否存在
                if (!_reportService.FileExists(reportFileName))
                {
                    MessageHelper.MessageOK($"报表文件不存在：{reportFileName}", TType.Error);
                    return;
                }

                string workingPath = ReportService.GetWorkingReportPath();

                // 如果当前加载的不是这个文件，则重新加载
                //if (rWReport.Filename != workingPath)
                {
                    // 准备报表控件
                    rWReport.Dock = DockStyle.Fill;
                    if (!panelReport.Controls.Contains(rWReport))
                        panelReport.Controls.Add(rWReport);

                    // 关闭Excel进程
                    ProcessHelper.KillProcess("EXCEL");
                    Thread.Sleep(200);

                    // 复制文件到工作目录
                    _reportService.CopyReportFile(reportFileName, workingPath);

                    // 初始化报表控件
                    BaseTest.Frm = frm;
                    BaseTest.Report = rWReport;
                    rWReport.Filename = workingPath;
                    rWReport.Init();

                }
            }
            catch (Exception ex)
            {
                NlogHelper.Default.Error("报表加载错误：", ex);
                MessageHelper.MessageOK($"报表加载错误：{ex.Message}", TType.Error);
            }
        }
        #endregion

        #region 值改变事件
        public void TestCongrp_TestConGroupChanged(object sender, int index, object value)
        {
            if (controls.DigitalOutputs.TryGetValue(index, out UISwitch iSwitch))
            {
                iSwitch.Active = value.ToBool();
            }
            if (controls.DigitalOutputButtons.TryGetValue(index, out UIButton btn))
            {
                NavigationButtonStyles.BtnColor(btn, value.ToBool());
            }
        }

        public void Currentgrp_CurrentvalueGrpChaned(object sender, int index, double value)
        {
            if (controls.TemperaturePoints.TryGetValue(index, out Label label))
            {
                label.Text = value.ToString("f1");
            }
        }
        public void AIgrp_AIvalueGrpChanged(object sender, int index, double value)
        {
            if (controls.AnalogInputs.TryGetValue(index, out Label label))
            {
                label.Text = value.ToString("f1");
            }
        }

        public void AOgrp_AOvalueGrpChanged(object sender, int index, double value)
        {
            switch (index)
            {
                default:
                    break;
            }
        }
        public void DIgrp_DIGroupChanged(object sender, int index, bool value)
        {
            try
            {
                if (index == 0)
                {
                    if (!value) IsTestEnd();
                    EmergencyStatusChanged?.Invoke(value);
                }
            }
            catch (Exception ex)
            {
                NlogHelper.Default.Error("DI模块事件报错：" + ex.Message);
            }
        }

        public void DOgrp_DOgrpChanged(object sender, int index, bool value) { }

        public void WSDCongrp_WSDConGroupChanged(object sender, int index, object value)
        {
            if (controls.DigitalOutputs.TryGetValue(index, out UISwitch iSwitch))
            {
                iSwitch.Active = value.ToBool();
            }
            if (controls.DigitalOutputButtons.TryGetValue(index, out UIButton btn))
            {
                NavigationButtonStyles.BtnColor(btn, value.ToBool());
            }
        }
        #endregion

        #region 参数
        private void InitParaConfig()
        {
            try
            {
                if (VarHelper.TestViewModel == null) return;

                // 初始化和加载参数配置
                paraconfig = new ParaConfig();
                paraconfig.SetSectionName(VarHelper.ModelTypeName);
                paraconfig.Load();
                BaseTest.ParaConfig = paraconfig;

                // 初始化测试项
                _tableService.LoadTestItems();

                // 初始化报表
                InitializeReport(paraconfig.RptFile);
            }
            catch (Exception ex)
            {
                MessageHelper.MessageOK($"加载参数错误：{ex.Message}");
                NlogHelper.Default.Error($"加载参数错误", ex);
            }
        }

        //刷新型号
        public void ParaRefresh()
        {
            try
            {
                if (string.IsNullOrEmpty(txtModel.Text)) return;
                InitParaConfig();
            }
            catch (Exception ex)
            {
                MessageHelper.MessageYes("刷新型号错误：" + ex.Message);
            }
        }
        #endregion

        #region 自动试验
        private CancellationTokenSource _cancellationTokenSource = new();
        private async void btnStartTest_Click(object sender, EventArgs e)
        {
            try
            {
                // 产品编号、车号、备注赋值
                VarHelper.TestViewModel.MakeNumber = txtMakeNumber.Text;
                VarHelper.TestViewModel.CarNumber = txtCarNumber.Text;
                VarHelper.TestViewModel.Remarks = txtRemarks.Text;

                // 1. 检查前置条件
                (bool Result, string txt) = FrmText();
                if (!Result)
                {
                    MessageHelper.MessageOK(txt, TType.Error);
                    return;
                }

                // 2. 整场自动试验只初始化一次上传会话。
                // BeginTest 会在这里清空上一场试验的 testData；后续每个小项点只追加，绝不单独清空。
                UploadService.Instance.BeginTest(
                    VarHelper.TestViewModel.CarNumber,
                    VarHelper.TestViewModel.Remarks);
                Disable(true);
                TestStateChanged?.Invoke(true, true);

                // 3. 初始化取消计时器令牌
                _cancellationTokenSource = new CancellationTokenSource();

                // 4. 正计时（不知道要运行多久，从0开始计时）
                _ = _countdownService.StartCountup(_cancellationTokenSource.Token);

                // 5. 开始测试
                await Task.Factory.StartNew(() => BackgroundWorker(_cancellationTokenSource),
                   _cancellationTokenSource.Token,
                   TaskCreationOptions.LongRunning,
                   TaskScheduler.Default);
            }
            catch (TaskCanceledException ex)
            {
                Debug.WriteLine($"试验已取消：{ex.Message}");
            }
            catch (OperationCanceledException ex)
            {
                Debug.WriteLine($"试验已取消: {ex}");
            }
            catch (Exception ex)
            {
                MessageHelper.MessageOK($"试验开始错误：{ex.Message}");
            }
        }

        // 试验项点启动
        private async void BackgroundWorker(CancellationTokenSource source)
        {
            // 只有循环正常走完才在最后触发 AddProcessRecord。
            // 用户取消、未找到试验实现或执行异常均会把该标志设置为 false。
            bool completedNormally = true;
            try
            {
                _itemPoints.ForEach(i => { i.ColorState = 0; });
                TableItemPoint.Invalidate(); // 刷新表格显示

                // 动态初始化所有项点类
                DicTestItems = CreateDynamicTestItemsAsync();

                if (DicTestItems.Count == 0)
                {
                    NlogHelper.Default.Debug("未找到可执行的测试项");
                    return;
                }

                // 在测试开始前调用一次全局初始化
                GeneralBaseTest.GlobalInit();

                NlogHelper.Default.Debug($"已加载 {DicTestItems.Count} 个测试项");

                // 根据Key找到对应的测试项并执行
                foreach (var item in _itemPoints.Where(p => p.Check))
                {
                    if (source.IsCancellationRequested)
                    {
                        completedNormally = false;
                        NlogHelper.Default.Debug("测试被取消");
                        break;
                    }

                    var taskInfo = DicTestItems.Keys.FirstOrDefault(t => t.TaskName == item.ItemName);
                    if (taskInfo != null && DicTestItems.TryGetValue(taskInfo, out var test))
                    {
                        // 每个小项点都有自己独立的测试类实例和上传值。
                        // Execute 完成后在 finally 中统一追加，确保一个小项点最多生成一条 testData。
                        bool result = false;
                        try
                        {
                            TableColor(item, 1); // 将当前项点设置为黄色(1 = 执行中)
                            NlogHelper.Default.Debug($"开始执行：{item.ItemName}");

                            result = await test.Execute(taskInfo.CancellationTokenSource.Token);

                            TableColor(item, result ? 2 : 3); // 设置为成功或失败状态
                            NlogHelper.Default.Debug($"完成执行：{item.ItemName} - {(result ? "成功" : "失败")}");
                        }
                        catch (OperationCanceledException)
                        {
                            completedNormally = false;
                            TableColor(item, 3); // 设置为取消状态
                            NlogHelper.Default.Debug($"测试项被取消：{item.ItemName}");
                        }
                        catch (Exception ex)
                        {
                            completedNormally = false;
                            TableColor(item, 3); // 设置为错误状态
                            NlogHelper.Default.Debug($"测试项执行错误：{item.ItemName} - {ex.Message}");
                            NlogHelper.Default.Error($"测试项执行错误：{item.ItemName}", ex);
                        }
                        finally
                        {
                            // SetUploadData 由小项点在自身全部动作完成后调用。
                            // 这里只读取最终值并追加到整场试验共用的列表，不在小项点之间清空数据。
                            UploadService.Instance.AddTestData(
                                item.ItemName,
                                test.UploadValue,
                                test.UploadUnit,
                                result);
                        }
                    }
                    else
                    {
                        completedNormally = false;
                        // 配置中存在项点但找不到实现类时，也保留一条失败记录，便于服务端定位配置问题。
                        UploadService.Instance.AddTestData(item.ItemName, string.Empty, string.Empty, false);
                        NlogHelper.Default.Debug($"未找到测试项实现：{item.ItemName}");
                        TableColor(item, 3); // 设置为错误状态
                    }
                }
            }
            catch (Exception ex)
            {
                completedNormally = false;
                NlogHelper.Default.Error("测试执行发生错误", ex);
            }
            finally
            {
                // 测试结束后调用全局清理
                GeneralBaseTest.GlobalCleanup();

                // 指向同一个 CancellationTokenSource；如果在 IsTestEnd() 之后读取取消状态，
                // 正常完成的试验也会被误判成“已取消”，导致无法进入上传流程。
                bool shouldUpload = completedNormally && !source.IsCancellationRequested;

                if (shouldUpload)
                {
                    // 只有全部已选小项点正常执行结束，才把当前累加的 testData 标记为完整数据。
                    UploadService.Instance.CompleteTest();
                }

                // 恢复界面、停止计时并释放试验任务。
                // 此方法会取消令牌，因此后续只能使用上面保存的 shouldUpload 判断。
                IsTestEnd();

                // AddProcessRecord 只属于自动试验完成流程。
                // 切回 UI 线程后再弹出确认框和结果提示，避免后台线程直接操作 WinForms 控件。
                if (shouldUpload && IsHandleCreated && !IsDisposed)
                {
                    BeginInvoke(new Action(async () =>
                        await HandleCompletedTestDataUploadAsync()));
                }
            }
        }

        /// <summary>
        /// 自动试验正常结束后的试验数据上传入口。
        /// Auto 模式直接上传一次；Prompt 模式先询问操作人员；选择否时保留 JSON，
        /// 等保存报表后写入本地状态表，以便在试验报表管理页面人工补传。
        /// </summary>
        private async Task HandleCompletedTestDataUploadAsync()
        {
            try
            {
                bool autoUpload = string.Equals(
                    UploadService.Instance.Config.DataUploadMode,
                    "Auto",
                    StringComparison.OrdinalIgnoreCase);

                bool shouldUpload = autoUpload ||
                    MessageHelper.MessageYes("自动试验已完成，是否上传本次试验数据？") == DialogResult.OK;

                if (!shouldUpload)
                {
                    MessageHelper.MessageOK(
                        "本次试验数据暂未上传，保存报表后可在试验报表管理中重新上传。",
                        TType.Info);
                    return;
                }

                // 明确只请求一次；失败后不在后台自动重试。
                await UploadService.Instance.UploadCurrentTestDataOnceAsync();
                MessageHelper.MessageOK("试验数据上传成功", TType.Success);
            }
            catch (Exception ex)
            {
                NlogHelper.Default.Error("试验完成后上传数据失败", ex);
                MessageHelper.MessageOK(
                    $"试验数据上传失败：{ex.Message}\n保存报表后可在试验报表管理中重新上传。",
                    TType.Error);
            }
        }

        /// <summary>
        /// 根据TestProcessModel动态创建测试项字典
        /// </summary>
        /// <returns></returns>
        private Dictionary<TaskInfo, BaseTest> CreateDynamicTestItemsAsync()
        {
            var testItems = new Dictionary<TaskInfo, BaseTest>();

            try
            {
                // 获取当前选中的测试步骤
                TestStepBLL stepBLL = new();
                var testSteps = stepBLL.GetTestItems(VarHelper.TestViewModel.ID);

                if (testSteps?.Count == 0)
                {
                    NlogHelper.Default.Debug("未配置测试步骤");
                    return testItems;
                }

                foreach (var step in testSteps.Where(s => s.IsVisible))
                {
                    try
                    {
                        // 获取对应的TestProcess信息
                        TestProcessBLL processBLL = new();
                        var processModel = TestProcessBLL.GetTestProcess()
                            .FirstOrDefault(p => p.ID == step.TestProcessID);

                        if (processModel == null)
                        {
                            NlogHelper.Default.Debug($"未找到测试流程：{step.TestProcessName}");
                            continue;
                        }

                        if (string.IsNullOrEmpty(processModel.EntityClassName))
                        {
                            NlogHelper.Default.Debug($"测试流程 {processModel.ProcessName} 未配置实体类名");
                            continue;
                        }

                        // 检查测试类型是否已注册
                        if (!TestFactory.IsTestTypeRegistered(processModel.EntityClassName))
                        {
                            NlogHelper.Default.Debug($"未注册的测试类型：{processModel.EntityClassName}");
                            continue;
                        }

                        // 创建TaskInfo
                        var taskInfo = new TaskInfo
                        {
                            TaskName = processModel.ProcessName,
                            CancellationTokenSource = new CancellationTokenSource()
                        };

                        // 动态创建测试实例
                        var testInstance = TestFactory.CreateTest(processModel.EntityClassName);

                        testItems.Add(taskInfo, testInstance);
                        NlogHelper.Default.Debug($"已加载测试项：{processModel.ProcessName} -> {processModel.EntityClassName}");
                    }
                    catch (Exception ex)
                    {
                        NlogHelper.Default.Debug($"创建测试项失败：{step.TestProcessName} - {ex.Message}");
                        NlogHelper.Default.Error($"创建测试项失败：{step.TestProcessName}", ex);
                    }
                }
            }
            catch (Exception ex)
            {
                NlogHelper.Default.Error("加载测试配置失败", ex);
            }

            return testItems;
        }

        /// <summary>
        /// 扩展TableColor方法，支持更多状态
        /// </summary>
        /// <param name="itemPoint">项点</param>
        /// <param name="state">状态：0-默认，1-执行中，2-成功，3-失败/错误</param>
        private void TableColor(ItemPointModel itemPoint, int state)
        {
            itemPoint.ColorState = state;
            TableItemPoint.Invalidate();
        }

        // 取消DicTestItems中所有测试任务
        private void CancelAllTestTasksAsync()
        {
            try
            {
                Task.Run(() =>
                {
                    foreach (var item in DicTestItems)
                    {
                        item.Key.CancellationTokenSource.Cancel();
                        item.Key.CancellationTokenSource.Dispose();
                        Debug.WriteLine($"Task {item.Key.TaskName} 已停止并释放资源（立即取消）");
                    }
                });
            }
            catch (Exception ex)
            {
                NlogHelper.Default.Error($"Task释放资源错误：{ex}");
                MessageHelper.MessageOK($"Task释放资源错误：{ex.Message}");
            }
        }

        private void btnStopTest_Click(object sender, EventArgs e) => IsTestEnd();

        private (bool Result, string txt) FrmText()
        {
            if (!OPCHelper.DIgrp[0])
            {
                return (false, "请注意，急停情况下无法启动自动试验!");
            }
            if (string.IsNullOrEmpty(VarHelper.TestViewModel.ModelName))
            {
                return (false, "未选择型号，无法启动自动试验!");
            }
            if (!RadioAuto.Checked)
            {
                return (false, "请注意，手动情况下无法启动自动试验!");
            }
            //if (string.IsNullOrEmpty(paraconfig.SprayTime) || string.IsNullOrEmpty(paraconfig.ApplyPressure))
            //{
            //    return (false, "请注意，该型号试验参数未设置，无法启动自动试验!");
            //}
            return (true, "");
        }

        // 结束试验操作
        private void IsTestEnd()
        {
            try
            {
                Disable(false);
                AppendText("试验结束");
                TestStateChanged?.Invoke(false, false);
                CancelAllTestTasksAsync();
                _countdownService.StopCountdown();
                _cancellationTokenSource.Cancel();
            }
            catch (OperationCanceledException ex)
            {
                Debug.WriteLine($"Task被取消: {ex}");
            }
            catch (Exception ex)
            {
                NlogHelper.Default.Error($"结束试验错误：{ex.Message}", ex);
                MessageHelper.MessageOK(frm, $"结束试验错误：{ex.Message}");
            }
        }
        #endregion

        #region 模拟量设置
        private void btnNozzleMotor_Click(object sender, EventArgs e)
        {
            try
            {
                var btn = sender as UIButton;
                using frmSetOutValue fs = new(OPCHelper.TestCongrp[btn.Tag.ToInt32()].ToDouble(), btn.Text, 10000);
                VarHelper.ShowDialogWithOverlay(frm, fs);
                if (fs.DialogResult == DialogResult.OK)
                {
                    ControlHelper.ButtonClickAsync(sender, () =>
                    {
                        //LabAO01.Text = fs.OutValue.ToString();
                        OPCHelper.TestCongrp[btn.Tag.ToInt32()] = fs.OutValue;
                    });
                }
            }
            catch (Exception ex)
            {
                MessageHelper.MessageOK(frm, $"模拟量设定错误：{ex.Message}");
            }
        }
        #endregion

        #region 报表控件

        private async void btnSave_Click(object sender, EventArgs e)
        {
            try
            {
                if (!ValidateSaveParameters()) return;// 参数校验

                if (!ConfirmSaveReport()) return;  // 提示确认

                string saveFilePath = ReportService.BuildSaveFilePath(VarHelper.TestViewModel.ModelName); // 保存路径
                string productNumber = txtMakeNumber.Text.Trim();
                string carNumber = txtCarNumber.Text.Trim();
                VarHelper.TestViewModel.MakeNumber = productNumber;
                VarHelper.TestViewModel.CarNumber = carNumber;

                var testRecord = new TestRecordModel
                {
                    KindID = VarHelper.TestViewModel.ModelTypeID,
                    ModelID = VarHelper.TestViewModel.ID,
                    TestID = carNumber,
                    ProductNumber = productNumber,
                    Tester = NewUsers.NewUserInfo.Username,
                    TestTime = DateTime.Now,
                    ReportPath = saveFilePath
                };

                rWReport.SaveAS(saveFilePath);
                if (!File.Exists(saveFilePath))
                    throw new IOException($"试验报表保存后未找到文件：{saveFilePath}");

                if (ReportService.SaveTestRecord(testRecord))
                {
                    try
                    {
                        // 保存当前 AddProcessRecord 状态，然后在保存按钮内只上传一次 XLS。
                        long uploadTaskID = UploadService.Instance.CreateUploadRecord(testRecord, saveFilePath);
                        UploadTaskModel uploadTask = await UploadService.Instance.UploadFileOnceAsync(uploadTaskID);

                        if (!uploadTask.FileUploaded)
                        {
                            MessageHelper.MessageOK(
                                $"报表本地保存成功，但文件上传失败：{uploadTask.LastError}\n" +
                                "可在试验报表管理中重新上传。",
                                TType.Warn);
                        }
                        else if (!uploadTask.DataUploaded)
                        {
                            MessageHelper.MessageOK(
                                "报表保存并上传成功；试验数据尚未上传，可在试验报表管理中重新上传。",
                                TType.Warn);
                        }
                        else if (string.IsNullOrWhiteSpace(uploadTask.PayloadJson))
                        {
                            MessageHelper.MessageOK("报表保存并上传成功", TType.Success);
                        }
                        else
                        {
                            MessageHelper.MessageOK("报表保存成功，试验数据和文件均已上传", TType.Success);
                        }
                    }
                    catch (Exception uploadEx)
                    {
                        NlogHelper.Default.Error("创建上传任务失败", uploadEx);
                        MessageHelper.MessageOK(
                            $"本地保存成功，但创建上传任务失败：{uploadEx.Message}", TType.Warn);
                    }
                }
                else
                {
                    MessageHelper.MessageOK("保存失败", TType.Error);
                }
            }
            catch (Exception ex)
            {
                MessageHelper.MessageOK($"保存失败: {ex.Message}", TType.Error);
            }
        }

        // 保存试验报告前参数校验
        private static bool ValidateSaveParameters()
        {
            if (string.IsNullOrEmpty(paraconfig?.RptFile))
            {
                MessageHelper.MessageOK("报表模板未设置，无法保存！", TType.Warn);
                return false;
            }

            if (string.IsNullOrEmpty(VarHelper.TestViewModel.ModelName))
            {
                MessageHelper.MessageOK("型号未选择！", TType.Warn);
                return false;
            }
            return true;
        }

        // 确认保存报表
        private static bool ConfirmSaveReport() =>
            MessageHelper.MessageYes("是否保存试验结果？") == DialogResult.OK;

        public int curRows = 1; //当前行数
        public int MaxcurRows = 1000; //默认最大行数
        /// <summary>
        /// 向上翻页
        /// </summary>
        private void BtnPageUp_Click(object sender, EventArgs e)
        {
            try
            {
                int pageSize = Convert.ToInt32(inputNumber.Value);
                var (currentRows, upEnabled, downEnabled) = _reportService.PageUp(pageSize);

                // 更新按钮状态
                btnPageUp.Enabled = upEnabled;
                btnPageDown.Enabled = downEnabled;

                // 显示当前页信息
                // XX.Text = $"第 {currentRows} 行";
            }
            catch (Exception ex)
            {
                MessageHelper.MessageOK($"向上翻页失败：{ex.Message}", TType.Error);
                NlogHelper.Default.Error("报表向上翻页失败", ex);
            }
        }

        /// <summary>
        /// 向下翻页
        /// </summary>
        private void BtnPageDown_Click(object sender, EventArgs e)
        {
            try
            {
                int pageSize = Convert.ToInt32(inputNumber.Value);
                var (currentRows, upEnabled, downEnabled) = _reportService.PageDown(pageSize);

                // 更新按钮状态
                btnPageUp.Enabled = upEnabled;
                btnPageDown.Enabled = downEnabled;

                // 显示当前页信息
                // XX.Text = $"第 {currentRows} 行";
            }
            catch (Exception ex)
            {
                MessageHelper.MessageOK($"向下翻页失败：{ex.Message}", TType.Error);
                NlogHelper.Default.Error("报表向下翻页失败", ex);
            }
        }

        private void btnPrintReport_Click(object sender, EventArgs e)
        {
            rWReport.Print();
        }
        #endregion

        #region 其他
        private void AppendText(string text)
        {
            // 由于分辨率过小原因，暂时没有使用试验消息提示

            //txtTestRecord.AppendText($"{DateTime.Now:HH:mm:ss}：{text}\n");
            //txtTestRecord.ScrollToCaret();
        }

        private void btnTechnology_Click(object sender, EventArgs e)
        {
            tabs1.SelectedIndex = 0;
            NavigationButtonStyles.UpdateNavigationButtons
                (tabs1.SelectedIndex, controls);
        }

        private void btnCurve_Click(object sender, EventArgs e)
        {
            tabs1.SelectedIndex = 1;
            NavigationButtonStyles.UpdateNavigationButtons
                (tabs1.SelectedIndex, controls);
        }

        private void btnProductSelection_Click(object sender, EventArgs e)
        {
            using FrmSpec frmSpec = new();
            VarHelper.ShowDialogWithOverlay(frm, frmSpec);
            if (frmSpec.DialogResult == DialogResult.OK)
            {
                txtModel.Text = VarHelper.TestViewModel.ModelName;
                txtType.Text = VarHelper.TestViewModel.ModelTypeName;
                // 产品编号属于本次试验的临时输入值，不随型号选择带入。
                txtMakeNumber.Text = string.Empty;
                txtCarNumber.Text = VarHelper.TestViewModel.CarNumber ?? string.Empty;
                ParaRefresh();
            }
        }

        private void btnWaterPumpStart_Click(object sender, EventArgs e)
        {
            var btn = sender as UIButton;
            OPCHelper.TestCongrp[btn.Tag.ToInt32()] =
                !OPCHelper.TestCongrp[btn.Tag.ToInt32()].ToBool();
        }

        private async void btnFaultRemoval_Click(object sender, EventArgs e)
        {
            var btn = sender as UIButton;
            await FaultClearingAsync(btn);
        }
        private async Task FaultClearingAsync(UIButton btn)
        {
            OPCHelper.TestCongrp[btn.Tag.ToInt32()] = true;
            await Task.Delay(1000);
            OPCHelper.TestCongrp[btn.Tag.ToInt32()] = false;
        }

        private void uiSwitch_MouseDown(object sender, MouseEventArgs e)
        {
            var sder = sender as UISwitch;
            OPCHelper.TestCongrp[sder.Tag.ToInt32()] = true;
        }

        private void uiSwitch_MouseUp(object sender, MouseEventArgs e)
        {
            var sder = sender as UISwitch;
            OPCHelper.TestCongrp[sder.Tag.ToInt32()] = false;
        }

        #endregion

        private void RadioAuto_CheckedChanged(object sender, EventArgs e)
        {
            OPCHelper.TestCongrp[0] = RadioAuto.Checked;
        }
    }
}
