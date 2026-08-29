namespace MainUI.InsulationWithstand
{
    public partial class FrmInsulationWithstand : UIForm
    {
        #region 私有字段
        private readonly WithstandTesterClient _client;
        private string _currentTestType = "绝缘耐压";
        #endregion

        #region 构造函数
        public FrmInsulationWithstand()
        {
            InitializeComponent();
            _client = WithstandTesterClient.Instance;
            InitializeUI();
            InitializeClientEvents();
        }
        #endregion

        #region 初始化
        /// <summary>
        /// 初始化界面
        /// </summary>
        private void InitializeUI()
        {
            try
            {
                // 设置默认选中的测试类型
                rbtnInsulationWithstand.Checked = true;
                _currentTestType = "绝缘耐压";

                // 按钮状态初始化
                UpdateButtonStates(false, false, false);

                // 显示初始状态
                UpdateStatus("请连接服务器");

                // 设置默认参数显示
                ShowParameterPanel(_currentTestType);
            }
            catch (Exception ex)
            {
                NlogHelper.Default.Error($"初始化界面失败: {ex.Message}");
                MessageHelper.MessageOK(this, $"初始化界面失败: {ex.Message}", AntdUI.TType.Error);
            }
        }

        /// <summary>
        /// 初始化客户端事件
        /// </summary>
        private void InitializeClientEvents()
        {
            try
            {
                // 连接状态变化
                _client.ConnectionStatusChanged += OnConnectionStatusChanged;

                // 连接丢失
                _client.ConnectionLost += OnConnectionLost;

                // 状态消息
                _client.StatusMessageReceived += OnStatusMessageReceived;

                // 服务器消息
                _client.ServerMessageReceived += OnServerMessageReceived;

                // 获得测试权限
                _client.PermissionGranted += OnPermissionGranted;

                // 测试开始
                _client.TestStarted += OnTestStarted;

                // 测试完成
                _client.TestCompleted += OnTestCompleted;

                // 加入队列
                _client.QueuedInLine += OnQueuedInLine;

                // 测试会话结束
                _client.TestSessionFinished += OnTestSessionFinished;

                // 测试取消
                _client.TestCanceled += OnTestCanceled;

                // 测试进度
                _client.TestProgressUpdated += OnTestProgressUpdated;
            }
            catch (Exception ex)
            {
                NlogHelper.Default.Error($"初始化客户端事件失败: {ex.Message}");
            }
        }
        #endregion

        #region 客户端事件处理
        /// <summary>
        /// 连接状态变化
        /// </summary>
        private void OnConnectionStatusChanged(bool isConnected)
        {
            if (InvokeRequired)
            {
                Invoke(new Action<bool>(OnConnectionStatusChanged), isConnected);
                return;
            }

            try
            {
                if (isConnected)
                {
                    btnConnect.Text = "断开连接";
                    btnConnect.Type = AntdUI.TTypeMini.Error;
                    btnRequestTest.Enabled = true;
                }
                else
                {
                    btnConnect.Text = "连接服务器";
                    btnConnect.Type = AntdUI.TTypeMini.Primary;
                    UpdateButtonStates(false, false, false);
                }
            }
            catch (Exception ex)
            {
                NlogHelper.Default.Error($"处理连接状态变化失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 连接丢失
        /// </summary>
        private void OnConnectionLost(string reason)
        {
            if (InvokeRequired)
            {
                Invoke(new Action<string>(OnConnectionLost), reason);
                return;
            }

            UpdateStatus($"连接丢失: {reason}");
            MessageHelper.MessageOK(this, reason, AntdUI.TType.Error);
        }

        /// <summary>
        /// 状态消息接收
        /// </summary>
        private void OnStatusMessageReceived(string message)
        {
            if (InvokeRequired)
            {
                Invoke(new Action<string>(OnStatusMessageReceived), message);
                return;
            }

            UpdateStatus(message);
        }

        /// <summary>
        /// 服务器消息接收
        /// </summary>
        private void OnServerMessageReceived(string message)
        {
            // 可以在这里添加额外的消息处理逻辑
            NlogHelper.Default.Debug($"服务器消息: {message}");
        }

        /// <summary>
        /// 获得测试权限
        /// </summary>
        private void OnPermissionGranted()
        {
            if (InvokeRequired)
            {
                Invoke(new Action(OnPermissionGranted));
                return;
            }

            UpdateButtonStates(true, true, true);
        }

        /// <summary>
        /// 测试开始
        /// </summary>
        private void OnTestStarted()
        {
            if (InvokeRequired)
            {
                Invoke(new Action(OnTestStarted));
                return;
            }

            btnSendTestParameters.Enabled = false;
        }

        /// <summary>
        /// 测试完成
        /// </summary>
        private void OnTestCompleted(string result)
        {
            if (InvokeRequired)
            {
                Invoke(new Action<string>(OnTestCompleted), result);
                return;
            }

            btnSendTestParameters.Enabled = true;
            MessageHelper.MessageOK(this, $"测试完成: {result}", AntdUI.TType.Success);
        }

        /// <summary>
        /// 加入队列
        /// </summary>
        private void OnQueuedInLine(string queueInfo)
        {
            if (InvokeRequired)
            {
                Invoke(new Action<string>(OnQueuedInLine), queueInfo);
                return;
            }

            UpdateStatus($"已加入队列: {queueInfo}");
        }

        /// <summary>
        /// 测试会话结束
        /// </summary>
        private void OnTestSessionFinished()
        {
            if (InvokeRequired)
            {
                Invoke(new Action(OnTestSessionFinished));
                return;
            }

            UpdateButtonStates(false, false, false);
            MessageHelper.MessageOK(this, "测试会话已结束", AntdUI.TType.Info);
        }

        /// <summary>
        /// 测试取消
        /// </summary>
        private void OnTestCanceled()
        {
            if (InvokeRequired)
            {
                Invoke(new Action(OnTestCanceled));
                return;
            }

            btnSendTestParameters.Enabled = true;
            btnCancelTest.Enabled = false;
        }

        /// <summary>
        /// 测试进度更新
        /// </summary>
        private void OnTestProgressUpdated(int secondsElapsed)
        {
            if (InvokeRequired)
            {
                Invoke(new Action<int>(OnTestProgressUpdated), secondsElapsed);
                return;
            }

            if (secondsElapsed <= 60)
            {
                UpdateStatus($"测试进行中... {secondsElapsed}/60秒");
            }
            else
            {
                UpdateStatus("测试时间已到,等待结果...");
            }
        }
        #endregion

        #region 界面控制方法
        /// <summary>
        /// 更新状态显示
        /// </summary>
        private void UpdateStatus(string message)
        {
            if (txtStatus.InvokeRequired)
            {
                txtStatus.Invoke(new Action<string>(UpdateStatus), message);
                return;
            }

            try
            {
                txtStatus.Text += $"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}";

                // AntdUI 的 Input 控件多行文本滚动
                if (txtStatus.Multiline && txtStatus.Text.Length > 0)
                {
                    txtStatus.SelectionStart = txtStatus.Text.Length;
                    txtStatus.ScrollToCaret();
                }
            }
            catch (Exception ex)
            {
                NlogHelper.Default.Error($"更新状态显示失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 更新按钮状态
        /// </summary>
        private void UpdateButtonStates(bool enableSend, bool enableEnd, bool enableCancel)
        {
            try
            {
                btnSendTestParameters.Enabled = enableSend;
                btnEndTest.Enabled = enableEnd;
                btnCancelTest.Enabled = enableCancel;
            }
            catch (Exception ex)
            {
                NlogHelper.Default.Error($"更新按钮状态失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 显示参数面板
        /// </summary>
        private void ShowParameterPanel(string testType)
        {
            try
            {
                switch (testType)
                {
                    case "绝缘耐压":
                        panelInsulationWithstandParams.Visible = true;
                        panelInsulationResistanceParams.Visible = true;
                        panelResistanceParams.Visible = false;
                        break;

                    case "绝缘电阻":
                        panelInsulationWithstandParams.Visible = true;
                        panelInsulationResistanceParams.Visible = true;
                        panelResistanceParams.Visible = false;
                        break;

                    case "线圈电阻":
                        panelInsulationWithstandParams.Visible = false;
                        panelInsulationResistanceParams.Visible = false;
                        panelResistanceParams.Visible = true;
                        break;
                }
            }
            catch (Exception ex)
            {
                NlogHelper.Default.Error($"显示参数面板失败: {ex.Message}");
            }
        }
        #endregion

        #region 按钮事件处理
        /// <summary>
        /// 连接/断开按钮点击
        /// </summary>
        private async void btnConnect_Click(object sender, EventArgs e)
        {
            try
            {
                if (!_client.IsConnected)
                {
                    // 连接服务器
                    btnConnect.Enabled = false;
                    btnConnect.Text = "连接中...";

                    bool success = await _client.ConnectAsync();

                    if (!success)
                    {
                        MessageHelper.MessageOK(this, "连接服务器失败", AntdUI.TType.Error);
                    }

                    btnConnect.Enabled = true;
                }
                else
                {
                    // 断开连接
                    if (MessageHelper.MessageYes(this, "确定要断开连接吗?\n正在进行的测试将被中断。") == DialogResult.OK)
                    {
                        _client.Disconnect();
                    }
                }
            }
            catch (Exception ex)
            {
                NlogHelper.Default.Error($"连接操作失败: {ex.Message}");
                MessageHelper.MessageOK(this, $"操作失败: {ex.Message}", AntdUI.TType.Error);
                btnConnect.Enabled = true;
            }
        }

        /// <summary>
        /// 请求测试按钮点击
        /// </summary>
        private async void btnRequestTest_Click(object sender, EventArgs e)
        {
            try
            {
                if (!_client.IsConnected)
                {
                    MessageHelper.MessageOK(this, "请先连接服务器", AntdUI.TType.Warn);
                    return;
                }

                if (_client.IsWaitingForPermission || _client.IsTesting)
                {
                    MessageHelper.MessageOK(this, "已在队列中或正在测试,请等待", AntdUI.TType.Info);
                    return;
                }

                bool success = await _client.RequestTestAsync(_currentTestType);
                if (!success)
                {
                    MessageHelper.MessageOK(this, "发送测试请求失败", AntdUI.TType.Error);
                }
            }
            catch (Exception ex)
            {
                NlogHelper.Default.Error($"请求测试失败: {ex.Message}");
                MessageHelper.MessageOK(this, $"请求测试失败: {ex.Message}", AntdUI.TType.Error);
            }
        }

        /// <summary>
        /// 发送测试参数按钮点击
        /// </summary>
        private async void btnSendTestParameters_Click(object sender, EventArgs e)
        {
            try
            {
                if (!_client.IsConnected || _client.IsTesting)
                {
                    return;
                }

                // 根据测试类型获取参数
                string[] parameters = GetCurrentTestParameters();
                if (parameters == null || parameters.Length == 0)
                {
                    MessageHelper.MessageOK(this, "请输入测试参数", AntdUI.TType.Warn);
                    return;
                }

                // 验证参数
                if (!ValidateParameters(parameters))
                {
                    MessageHelper.MessageOK(this, "参数格式不正确,请检查输入", AntdUI.TType.Warn);
                    return;
                }

                bool success = await _client.SendTestParametersAsync(_currentTestType, parameters);
                if (!success)
                {
                    MessageHelper.MessageOK(this, "发送测试参数失败", AntdUI.TType.Error);
                }
            }
            catch (Exception ex)
            {
                NlogHelper.Default.Error($"发送测试参数失败: {ex.Message}");
                MessageHelper.MessageOK(this, $"发送参数失败: {ex.Message}", AntdUI.TType.Error);
            }
        }

        /// <summary>
        /// 结束测试按钮点击
        /// </summary>
        private async void btnEndTest_Click(object sender, EventArgs e)
        {
            try
            {
                if (!_client.IsConnected)
                {
                    MessageHelper.MessageOK(this, "请先连接服务器", AntdUI.TType.Warn);
                    return;
                }

                if (MessageHelper.MessageYes(this, "确定要结束当前测试吗?") == DialogResult.OK)
                {
                    bool success = await _client.EndTestAsync();
                    if (!success)
                    {
                        MessageHelper.MessageOK(this, "结束测试失败", AntdUI.TType.Error);
                    }
                }
            }
            catch (Exception ex)
            {
                NlogHelper.Default.Error($"结束测试失败: {ex.Message}");
                MessageHelper.MessageOK(this, $"结束测试失败: {ex.Message}", AntdUI.TType.Error);
            }
        }

        /// <summary>
        /// 取消测试按钮点击
        /// </summary>
        private async void btnCancelTest_Click(object sender, EventArgs e)
        {
            try
            {
                if (!_client.IsConnected)
                {
                    MessageHelper.MessageOK(this, "请先连接服务器", AntdUI.TType.Warn);
                    return;
                }

                if (!_client.IsTesting)
                {
                    MessageHelper.MessageOK(this, "当前没有正在进行的测试", AntdUI.TType.Info);
                    return;
                }

                if (MessageHelper.MessageYes(this, "确定要取消当前测试吗?") == DialogResult.OK)
                {
                    bool success = await _client.CancelTestAsync();
                    if (!success)
                    {
                        MessageHelper.MessageOK(this, "取消测试失败", AntdUI.TType.Error);
                    }
                }
            }
            catch (Exception ex)
            {
                NlogHelper.Default.Error($"取消测试失败: {ex.Message}");
                MessageHelper.MessageOK(this, $"取消测试失败: {ex.Message}", AntdUI.TType.Error);
            }
        }

        /// <summary>
        /// 测试类型单选框变化
        /// </summary>
        private void rbtnTestType_CheckedChanged(object sender, EventArgs e)
        {
            try
            {
                if (rbtnInsulationWithstand.Checked)
                {
                    _currentTestType = "绝缘耐压";
                }
                else if (rbtnInsulationResistance.Checked)
                {
                    _currentTestType = "绝缘电阻";
                }
                else if (rbtnResistance.Checked)
                {
                    _currentTestType = "线圈电阻";
                }

                ShowParameterPanel(_currentTestType);
                UpdateStatus($"切换测试类型: {_currentTestType}");
            }
            catch (Exception ex)
            {
                NlogHelper.Default.Error($"切换测试类型失败: {ex.Message}");
            }
        }
        #endregion

        #region 参数处理方法
        /// <summary>
        /// 获取当前测试参数
        /// </summary>
        private string[] GetCurrentTestParameters()
        {
            try
            {
                switch (_currentTestType)
                {
                    case "绝缘耐压":
                        return
                        [
                            txtWithstandVoltage.Text,
                            txtWithstandDuration.Text,
                            txtWithstandLimit.Text
                        ];

                    case "绝缘电阻":
                        return
                        [
                            txtResistanceVoltage.Text,
                            txtResistanceDuration.Text,
                            txtResistanceLimit.Text
                        ];

                    case "线圈电阻":
                        return
                        [
                            txtLowResistanceLimit.Text
                        ];

                    default:
                        return null;
                }
            }
            catch (Exception ex)
            {
                NlogHelper.Default.Error($"获取测试参数失败: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 验证参数
        /// </summary>
        private bool ValidateParameters(string[] parameters)
        {
            try
            {
                if (parameters == null || parameters.Length == 0)
                    return false;

                foreach (var param in parameters)
                {
                    if (string.IsNullOrWhiteSpace(param))
                        return false;

                    if (!double.TryParse(param, out _))
                        return false;
                }

                return true;
            }
            catch
            {
                return false;
            }
        }
        #endregion

        #region 窗体事件
        /// <summary>
        /// 窗体关闭事件
        /// </summary>
        private void DeviceClientForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            try
            {
                // 断开连接并清理资源
                if (_client.IsConnected)
                {
                    _client.Disconnect();
                }

                // 取消事件订阅
                _client.ConnectionStatusChanged -= OnConnectionStatusChanged;
                _client.ConnectionLost -= OnConnectionLost;
                _client.StatusMessageReceived -= OnStatusMessageReceived;
                _client.ServerMessageReceived -= OnServerMessageReceived;
                _client.PermissionGranted -= OnPermissionGranted;
                _client.TestStarted -= OnTestStarted;
                _client.TestCompleted -= OnTestCompleted;
                _client.QueuedInLine -= OnQueuedInLine;
                _client.TestSessionFinished -= OnTestSessionFinished;
                _client.TestCanceled -= OnTestCanceled;
                _client.TestProgressUpdated -= OnTestProgressUpdated;
            }
            catch (Exception ex)
            {
                NlogHelper.Default.Error($"窗体关闭处理失败: {ex.Message}");
            }
        }
        #endregion
    }
}