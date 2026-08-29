using System.Net.Sockets;
using System.Text;

namespace MainUI.InsulationWithstand
{
    public class WithstandTesterClient
    {
        #region 单例模式
        public static WithstandTesterClient Instance { get; } = new WithstandTesterClient();

        private WithstandTesterClient()
        {
            InitializeTimer();
        }
        #endregion

        #region 私有字段
        private readonly TCPConfig TcpConfig = new();
        private TcpClient _client;
        private NetworkStream _stream;
        private bool _isConnected = false;
        private bool _isWaitingForPermission = false;
        private bool _isTesting = false;
        private string _currentDeviceType = "";
        private System.Timers.Timer _testProgressTimer;
        private int _testSecondsElapsed = 0;
        private System.Timers.Timer _heartbeatTimer;
        private DateTime _lastHeartbeatTime;
        #endregion

        #region 公开属性
        public bool IsConnected => _isConnected;
        public bool IsWaitingForPermission => _isWaitingForPermission;
        public bool IsTesting => _isTesting;
        public string CurrentDeviceType => _currentDeviceType;
        #endregion

        #region 事件定义
        /// <summary>
        /// 连接状态变化事件
        /// </summary>
        public event Action<bool> ConnectionStatusChanged;

        /// <summary>
        /// 连接丢失事件
        /// </summary>
        public event Action<string> ConnectionLost;

        /// <summary>
        /// 状态消息更新事件
        /// </summary>
        public event Action<string> StatusMessageReceived;

        /// <summary>
        /// 服务器消息接收事件
        /// </summary>
        public event Action<string> ServerMessageReceived;

        /// <summary>
        /// 获得测试权限事件
        /// </summary>
        public event Action PermissionGranted;

        /// <summary>
        /// 测试开始事件
        /// </summary>
        public event Action TestStarted;

        /// <summary>
        /// 测试完成事件
        /// </summary>
        public event Action<string> TestCompleted;

        /// <summary>
        /// 加入队列事件
        /// </summary>
        public event Action<string> QueuedInLine;

        /// <summary>
        /// 测试会话结束事件
        /// </summary>
        public event Action TestSessionFinished;

        /// <summary>
        /// 测试取消事件
        /// </summary>
        public event Action TestCanceled;

        /// <summary>
        /// 测试进度更新事件
        /// </summary>
        public event Action<int> TestProgressUpdated;

        /// <summary>
        /// 心跳响应事件
        /// </summary>
        public event Action HeartbeatReceived;
        #endregion

        #region 初始化方法
        private void InitializeTimer()
        {
            // 初始化测试进度计时器
            _testProgressTimer = new System.Timers.Timer(1000); // 1秒间隔
            _testProgressTimer.Elapsed += (s, e) =>
            {
                _testSecondsElapsed++;
                TestProgressUpdated?.Invoke(_testSecondsElapsed);
                UpdateStatus($"测试进行中... {_testSecondsElapsed}/60秒");
            };
            _testProgressTimer.AutoReset = true;
            _testProgressTimer.Enabled = false;

            // 初始化心跳计时器 - 使用配置参数
            _heartbeatTimer = new System.Timers.Timer(TcpConfig.HeartbeatInterval);
            _heartbeatTimer.Elapsed += async (s, e) => await SendHeartbeatAsync();
            _heartbeatTimer.AutoReset = true;
        }
        #endregion

        #region 心跳机制
        /// <summary>
        /// 发送心跳包
        /// </summary>
        private async Task SendHeartbeatAsync()
        {
            if (!_isConnected || _stream == null)
                return;

            try
            {
                // 检查心跳超时 - 使用配置参数
                if ((DateTime.Now - _lastHeartbeatTime).TotalMilliseconds > TcpConfig.HeartbeatTimeout)
                {
                    UpdateStatus("心跳超时,连接可能已断开");
                    HandleConnectionLost("心跳超时");
                    return;
                }

                string heartbeatMessage = "HEARTBEAT|PING";
                byte[] data = Encoding.UTF8.GetBytes(heartbeatMessage);
                await _stream.WriteAsync(data);
                UpdateStatus($"发送心跳包: {DateTime.Now:HH:mm:ss}");
            }
            catch (Exception ex)
            {
                UpdateStatus($"发送心跳包失败: {ex.Message}");
                HandleConnectionLost($"心跳发送失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 处理连接断开
        /// </summary>
        private void HandleConnectionLost(string reason = "未知原因")
        {
            _isConnected = false;
            _heartbeatTimer?.Stop();
            _testProgressTimer?.Stop();

            // 触发事件
            ConnectionStatusChanged?.Invoke(false);
            ConnectionLost?.Invoke($"与服务器的连接已断开: {reason}");

            // 清理资源
            try
            {
                _stream?.Dispose();
                _client?.Close();
            }
            catch (Exception)
            {
                // 忽略清理时的异常
            }

            UpdateStatus($"连接已断开: {reason}");
        }
        #endregion

        #region 连接管理
        /// <summary>
        /// 连接到服务器
        /// </summary>
        public async Task<bool> ConnectAsync()
        {
            if (_isConnected)
            {
                UpdateStatus("已经连接到服务器");
                return true;
            }

            try
            {
                string serverIp = TcpConfig.IP;
                int serverPort = int.Parse(TcpConfig.Port);

                _client = new TcpClient();

                // 使用配置的连接超时
                var connectTask = _client.ConnectAsync(serverIp, serverPort);
                var timeoutTask = Task.Delay(TcpConfig.ConnectionTimeout);
                var completedTask = await Task.WhenAny(connectTask, timeoutTask);

                if (completedTask == timeoutTask)
                {
                    // 连接超时
                    _client?.Close();
                    UpdateStatus($"连接超时({TcpConfig.ConnectionTimeout}ms)");
                    ConnectionStatusChanged?.Invoke(false);
                    return false;
                }

                // 检查连接是否成功
                if (!_client.Connected)
                {
                    UpdateStatus("连接失败");
                    ConnectionStatusChanged?.Invoke(false);
                    return false;
                }

                _stream = _client.GetStream();
                _isConnected = true;
                _lastHeartbeatTime = DateTime.Now;

                UpdateStatus($"已连接到服务器 {serverIp}:{serverPort}");

                // 启动心跳计时器
                _heartbeatTimer?.Start();

                // 触发连接状态变化事件
                ConnectionStatusChanged?.Invoke(true);

                // 开始监听服务器消息
                _ = ListenForServerMessages();

                return true;
            }
            catch (Exception ex)
            {
                UpdateStatus($"连接失败: {ex.Message}");
                ConnectionStatusChanged?.Invoke(false);
                return false;
            }
        }

        /// <summary>
        /// 断开连接
        /// </summary>
        public void Disconnect()
        {
            try
            {
                _isConnected = false;
                _heartbeatTimer?.Stop();
                _testProgressTimer?.Stop();

                _stream?.Dispose();
                _client?.Close();

                ConnectionStatusChanged?.Invoke(false);
                UpdateStatus("已断开与服务器的连接");
            }
            catch (Exception ex)
            {
                UpdateStatus($"断开连接出错: {ex.Message}");
            }
            finally
            {
                _isConnected = false;
                _isWaitingForPermission = false;
                _isTesting = false;
                _testSecondsElapsed = 0;
            }
        }
        #endregion

        #region 消息监听与处理
        /// <summary>
        /// 监听服务器消息
        /// </summary>
        private async Task ListenForServerMessages()
        {
            byte[] buffer = new byte[4096];
            int bytesRead;

            try
            {
                while (_isConnected && (bytesRead = await _stream.ReadAsync(buffer)) > 0)
                {
                    string message = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                    UpdateStatus($"收到服务器消息: {message}");
                    ServerMessageReceived?.Invoke(message);

                    // 处理服务器消息
                    await ProcessServerMessage(message);
                }
            }
            catch (Exception ex)
            {
                if (_isConnected)
                {
                    UpdateStatus($"与服务器通信出错: {ex.Message}");
                    HandleConnectionLost($"通信异常: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// 处理服务器消息
        /// </summary>
        private Task ProcessServerMessage(string message)
        {
            // 心跳响应
            if (message.StartsWith("HEARTBEAT|PONG"))
            {
                _lastHeartbeatTime = DateTime.Now;
                UpdateStatus($"收到心跳响应: {DateTime.Now:HH:mm:ss}");
                HeartbeatReceived?.Invoke();
                return Task.CompletedTask;
            }

            // 获得测试权限
            if (message.StartsWith("PERMISSION_GRANTED"))
            {
                _isWaitingForPermission = false;
                UpdateStatus("已获得测试权限,请输入参数并发送");
                PermissionGranted?.Invoke();
            }
            // 测试开始
            else if (message.StartsWith("TEST_STARTED"))
            {
                _isTesting = true;
                UpdateStatus("测试已开始");
                TestStarted?.Invoke();
            }
            // 测试完成
            else if (message.StartsWith("TEST_COMPLETE"))
            {
                _isTesting = false;
                _testProgressTimer.Enabled = false;
                _testSecondsElapsed = 0;

                string result = message.Contains('|') && message.Split('|').Length > 1
                    ? message.Split('|')[1]
                    : "未知结果";

                UpdateStatus($"测试完成: {result}");
                TestCompleted?.Invoke(result);
            }
            // 加入队列
            else if (message.StartsWith("QUEUED"))
            {
                _isWaitingForPermission = true;
                string queueInfo = message.Contains('|') && message.Split('|').Length > 1
                    ? message.Split('|')[1]
                    : message;

                UpdateStatus($"排队信息: {queueInfo}");
                QueuedInLine?.Invoke(queueInfo);
            }
            // 测试会话结束
            else if (message.StartsWith("TEST_FINISHED"))
            {
                _isTesting = false;
                _testProgressTimer.Enabled = false;
                _testSecondsElapsed = 0;
                UpdateStatus("测试会话已结束");
                TestSessionFinished?.Invoke();
            }
            // 测试取消
            else if (message.StartsWith("TEST_CANCELED"))
            {
                _isTesting = false;
                _testProgressTimer.Enabled = false;
                _testSecondsElapsed = 0;
                UpdateStatus("测试已被取消");
                TestCanceled?.Invoke();
            }
            // 未知消息
            else
            {
                _isWaitingForPermission = false;
                _isTesting = false;
            }

            return Task.CompletedTask;
        }
        #endregion

        #region 测试操作
        /// <summary>
        /// 转换测试类型
        /// </summary>
        private string ConvertTestType(string type)
        {
            return type switch
            {
                "绝缘耐压" => "InsulationWithstand",
                "绝缘电阻" => "InsulationResistance",
                "线圈电阻" => "Resistance",
                _ => type
            };
        }

        /// <summary>
        /// 请求测试权限
        /// </summary>
        public async Task<bool> RequestTestAsync(string testType)
        {
            if (!_isConnected)
            {
                UpdateStatus("请先连接服务器");
                return false;
            }

            if (_isWaitingForPermission || _isTesting)
            {
                UpdateStatus("已在队列中或正在测试,请等待");
                return false;
            }

            _currentDeviceType = ConvertTestType(testType);
            string command = $"REQUEST_TEST|{_currentDeviceType}";
            UpdateStatus($"已发送 {_currentDeviceType} 试验请求");

            try
            {
                byte[] data = Encoding.UTF8.GetBytes(command);
                await _stream.WriteAsync(data);
                _isWaitingForPermission = true;
                return true;
            }
            catch (Exception ex)
            {
                UpdateStatus($"发送请求出错: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 发送测试参数
        /// </summary>
        public async Task<bool> SendTestParametersAsync(string testType, params string[] parameters)
        {
            if (!_isConnected)
            {
                UpdateStatus("未连接到服务器");
                return false;
            }

            if (_isTesting)
            {
                UpdateStatus("测试正在进行中");
                return false;
            }

            try
            {
                _currentDeviceType = ConvertTestType(testType);
                string command = string.Empty;

                switch (_currentDeviceType)
                {
                    case "InsulationWithstand":
                    case "InsulationResistance":
                        if (parameters.Length >= 3)
                        {
                            string voltage = parameters[0];
                            string duration = parameters[1];
                            string limit = parameters[2];
                            string paramStr = $"Voltage={voltage};Duration={duration};Limit={limit}";
                            command = $"TEST_PARAMETERS|{_currentDeviceType}|{paramStr}";
                            UpdateStatus($"已发送 {_currentDeviceType} 测试参数: {paramStr}");
                        }
                        break;

                    case "Resistance":
                        if (parameters.Length >= 1)
                        {
                            string limit = parameters[0];
                            string paramStr = $"Limit={limit}";
                            command = $"TEST_PARAMETERS|{_currentDeviceType}|{paramStr}";
                            UpdateStatus($"已发送 {_currentDeviceType} 测试参数: {paramStr}");
                        }
                        break;
                }

                if (!string.IsNullOrEmpty(command))
                {
                    byte[] data = Encoding.UTF8.GetBytes(command);
                    await _stream.WriteAsync(data);
                    UpdateStatus("等待测试完成(2分钟)...");

                    // 开始测试进度计时
                    _isTesting = true;
                    _testSecondsElapsed = 0;
                    _testProgressTimer.Enabled = true;

                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                UpdateStatus($"发送参数出错: {ex.Message}");
                _isTesting = false;
                return false;
            }
        }

        /// <summary>
        /// 取消测试
        /// </summary>
        public async Task<bool> CancelTestAsync()
        {
            if (!_isConnected)
            {
                UpdateStatus("请先连接服务器");
                return false;
            }

            if (!_isTesting)
            {
                UpdateStatus("当前没有正在进行的测试可取消");
                return false;
            }

            string command = "CANCEL_TEST";
            UpdateStatus("已发送取消测试命令");

            try
            {
                byte[] data = Encoding.UTF8.GetBytes(command);
                await _stream.WriteAsync(data);
                return true;
            }
            catch (Exception ex)
            {
                UpdateStatus($"发送取消测试命令出错: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 结束测试
        /// </summary>
        public async Task<bool> EndTestAsync()
        {
            if (!_isConnected)
            {
                UpdateStatus("请先连接服务器");
                return false;
            }

            string command = "END_TEST";
            UpdateStatus("已发送结束测试命令");

            try
            {
                byte[] data = Encoding.UTF8.GetBytes(command);
                await _stream.WriteAsync(data);

                // 重置状态
                _isTesting = false;
                _testProgressTimer.Enabled = false;
                _testSecondsElapsed = 0;

                return true;
            }
            catch (Exception ex)
            {
                UpdateStatus($"发送结束测试命令出错: {ex.Message}");
                return false;
            }
        }
        #endregion

        #region 辅助方法
        /// <summary>
        /// 更新状态消息
        /// </summary>
        private void UpdateStatus(string message)
        {
            StatusMessageReceived?.Invoke(message);
        }

        /// <summary>
        /// 释放资源
        /// </summary>
        public void Dispose()
        {
            Disconnect();
            _testProgressTimer?.Dispose();
            _heartbeatTimer?.Dispose();
        }
        #endregion
    }
}