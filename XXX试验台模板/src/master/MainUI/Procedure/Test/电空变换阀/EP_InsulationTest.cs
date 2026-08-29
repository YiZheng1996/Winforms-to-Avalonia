using MainUI.InsulationWithstand;

namespace MainUI.Procedure.Test
{
    /// <summary>
    /// 绝缘试验 - 包含耐压测试和电阻测试
    /// </summary>
    public class EP_InsulationTest : GeneralBaseTest
    {
        #region 私有字段
        private WithstandTesterClient _client = WithstandTesterClient.Instance;
        private bool _permissionGranted = false;
        private bool _testCompleted = false;
        private string _testResult = string.Empty;
        private string _resistanceResult = string.Empty;
        private string _withstandResult = string.Empty;
        #endregion

        #region 测试类型枚举
        /// <summary>
        /// 转换测试类型
        /// </summary>
        public enum ConvertTestType
        {
            /// <summary>
            /// 绝缘耐压测试
            /// </summary>
            Withstand,

            /// <summary>
            /// 绝缘电阻测试
            /// </summary>
            Resistance
        }
        #endregion

        public override async Task<bool> Execute(CancellationToken cancellationToken)
        {
            await base.Execute(cancellationToken);

            try
            {
                // ========== 步骤 1: 确保连接 ==========
                TxtTips("检查绝缘耐压仪连接状态...");
                if (!_client.IsConnected)
                {
                    TxtTips("正在连接绝缘耐压仪...");
                    bool connectionStatus = await _client.ConnectAsync();
                    if (!connectionStatus)
                    {
                        ShowErrorDialog("绝缘耐压仪访问失败!试验自动结束!");
                        return false;
                    }
                    TxtTips("连接成功");
                }

                // ========== 执行两个测试 ==========
                bool resistanceTestResult = false;
                bool withstandTestResult = false;

                // 1. 绝缘电阻测试
                TxtTips("===== 开始绝缘电阻测试 =====");
                resistanceTestResult = await ExecuteTest(ConvertTestType.Resistance, cancellationToken);

                if (!resistanceTestResult)
                {
                    TxtTips("绝缘电阻测试失败,跳过耐压测试");
                    return false;
                }

                TxtTips("绝缘电阻测试完成,准备进行耐压测试...");

                // 等待5秒
                Delay(5, "设备稳定中");

                // 2. 绝缘耐压测试
                TxtTips("===== 开始绝缘耐压测试 =====");
                withstandTestResult = await ExecuteTest(ConvertTestType.Withstand, cancellationToken);

                if (!withstandTestResult)
                {
                    TxtTips("绝缘耐压测试失败");
                    return false;
                }

                TxtTips("===== 所有绝缘测试完成 =====");

                // 电阻和耐压均完成后再形成绝缘试验这一小项点的最终接口值。
                SetUploadData($"绝缘电阻={_resistanceResult};绝缘耐压={_withstandResult}");
                return true;
            }
            catch (OperationCanceledException)
            {
                TxtTips("测试被取消");
                await _client.CancelTestAsync();
                return false;
            }
            catch (Exception ex)
            {
                TxtTips($"测试异常: {ex.Message}");
                NlogHelper.Default.Error($"EP_InsulationTest异常: {ex.Message}", ex);
                ShowErrorDialog($"测试异常: {ex.Message}");
                return false;
            }
        }

        #region 核心测试方法
        /// <summary>
        /// 执行指定类型的测试
        /// </summary>
        /// <param name="testType">测试类型</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>测试是否成功</returns>
        private async Task<bool> ExecuteTest(ConvertTestType testType, CancellationToken cancellationToken)
        {
            // 订阅事件
            SubscribeEvents();

            try
            {
                // 根据测试类型获取配置
                string deviceType = GetDeviceType(testType);
                string[] parameters = GetTestParameters(testType);
                string testName = GetTestName(testType);

                // ========== 步骤 1: 请求测试权限 ==========
                TxtTips($"请求 {testName} 测试权限...");
                bool requestSuccess = await _client.RequestTestAsync(deviceType);

                if (!requestSuccess)
                {
                    TxtTips($"{testName} 发送请求失败");
                    return false;
                }

                // ========== 步骤 2: 等待权限授予 ==========
                TxtTips($"等待服务器授予 {testName} 权限...");

                // 使用 Delay 检测权限 (30秒超时, 每100ms检查)
                bool timeout = Delay(30, 100, () => _permissionGranted, cancellationToken);

                if (timeout)
                {
                    TxtTips($"{testName} 等待权限超时(30秒)");
                    return false;
                }

                TxtTips($"{testName} 已获得测试权限");

                // ========== 步骤 3: 发送测试参数 ==========
                TxtTips($"发送 {testName} 测试参数: {string.Join(", ", parameters)}");
                bool paramsSent = await _client.SendTestParametersAsync(deviceType, parameters);

                if (!paramsSent)
                {
                    TxtTips($"{testName} 发送参数失败");
                    return false;
                }

                TxtTips($"{testName} 参数已发送");

                // ========== 步骤 4: 等待测试完成 ==========
                TxtTips($"{testName} 测试进行中,请等待...");

                // 根据测试类型设置超时时间
                int testTimeout = GetTestTimeout(testType, parameters);

                // 使用 Delay 检测测试完成 (每500ms检查)
                timeout = Delay(testTimeout, 500, () => _testCompleted, cancellationToken);

                if (timeout)
                {
                    TxtTips($"{testName} 测试超时({testTimeout}秒)");
                    return false;
                }

                // ========== 步骤 5: 处理测试结果 ==========
                TxtTips($"{testName} 测试完成: {_testResult}");

                if (string.IsNullOrEmpty(_testResult))
                {
                    TxtTips($"{testName} 未收到测试结果");
                    return false;
                }

                // 保存测试结果
                SaveTestResult(testType, _testResult);

                // 判断测试是否合格
                bool isPass = IsTestPass(_testResult);

                if (isPass)
                {
                    TxtTips($"✓ {testName} 测试合格: {_testResult}");
                }
                else
                {
                    TxtTips($"✗ {testName} 测试不合格: {_testResult}");
                }

                // ========== 步骤 6: 结束测试会话 ==========
                await _client.EndTestAsync();

                return isPass;
            }
            finally
            {
                // ========== 步骤 7: 清理资源 ==========
                UnsubscribeEvents();
            }
        }
        #endregion

        #region 辅助方法
        /// <summary>
        /// 获取设备类型字符串
        /// </summary>
        private string GetDeviceType(ConvertTestType testType)
        {
            return testType switch
            {
                ConvertTestType.Withstand => "Withstand",
                ConvertTestType.Resistance => "Resistance",
                _ => throw new ArgumentException($"未知的测试类型: {testType}")
            };
        }

        /// <summary>
        /// 获取测试名称
        /// </summary>
        private string GetTestName(ConvertTestType testType)
        {
            return testType switch
            {
                ConvertTestType.Withstand => "绝缘耐压",
                ConvertTestType.Resistance => "绝缘电阻",
                _ => "未知测试"
            };
        }

        /// <summary>
        /// 获取测试参数
        /// </summary>
        private string[] GetTestParameters(ConvertTestType testType)
        {
            switch (testType)
            {
                case ConvertTestType.Withstand:
                    // 耐压测试参数: 电压, 时间
                    string voltage = Read("SET31");  // 测试电压 (例如: 1500V)
                    string time = Read("SET32");     // 测试时间 (例如: 60秒)
                    return [voltage, time];

                case ConvertTestType.Resistance:
                    // 电阻测试参数: 限值
                    string limitValue = Read("SET30"); // 阻值限值 (例如: 100MΩ)
                    return [limitValue];

                default:
                    throw new ArgumentException($"未知的测试类型: {testType}");
            }
        }

        /// <summary>
        /// 获取测试超时时间(秒)
        /// </summary>
        private int GetTestTimeout(ConvertTestType testType, string[] parameters)
        {
            switch (testType)
            {
                case ConvertTestType.Withstand:
                    // 耐压测试: 测试时间 + 60秒缓冲
                    if (parameters.Length >= 2 && int.TryParse(parameters[1], out int testTime))
                    {
                        return testTime + 60;
                    }
                    return 120; // 默认120秒

                case ConvertTestType.Resistance:
                    // 电阻测试: 固定60秒
                    return 60;

                default:
                    return 120;
            }
        }

        /// <summary>
        /// 保存测试结果
        /// </summary>
        private void SaveTestResult(ConvertTestType testType, string result)
        {
            switch (testType)
            {
                case ConvertTestType.Withstand:
                    // 保存耐压测试结果
                    _withstandResult = result;
                    Write("InsulationWithstandResult", result);
                    NlogHelper.Default.Info($"绝缘耐压测试结果: {result}");
                    break;

                case ConvertTestType.Resistance:
                    // 保存电阻测试结果
                    _resistanceResult = result;
                    Write("InsulationResistanceResult", result);
                    NlogHelper.Default.Info($"绝缘电阻测试结果: {result}");
                    break;
            }
        }

        /// <summary>
        /// 判断测试是否合格
        /// </summary>
        private bool IsTestPass(string result)
        {
            if (string.IsNullOrEmpty(result))
                return false;

            // 解析结果格式: "PASS|值" 或 "FAIL|值"
            string[] parts = result.Split('|');
            if (parts.Length >= 1)
            {
                string status = parts[0].Trim().ToUpper();
                return status == "PASS" || status == "合格";
            }

            return false;
        }

        /// <summary>
        /// 读取参数配置
        /// </summary>
        private new string Read(string key)
        {
            try
            {
                // 从 ParaConfig 或其他配置源读取
                // return ParaConfig.GetValue(key);

                // 临时默认值 (根据实际情况修改)
                return key switch
                {
                    "SET30" => "100",   // 电阻限值 100MΩ
                    "SET31" => "1500",  // 耐压电压 1500V
                    "SET32" => "60",    // 耐压时间 60秒
                    _ => "0"
                };
            }
            catch (Exception ex)
            {
                NlogHelper.Default.Warn($"读取参数 {key} 失败: {ex.Message}");
                return "0";
            }
        }

        /// <summary>
        /// 写入参数结果
        /// </summary>
        private new void Write(string key, string value)
        {
            try
            {
                // 写入到数据库或其他存储
                // ParaConfig.SetValue(key, value);
                NlogHelper.Default.Debug($"保存参数: {key} = {value}");
            }
            catch (Exception ex)
            {
                NlogHelper.Default.Error($"写入参数 {key} 失败: {ex.Message}");
            }
        }
        #endregion

        #region 事件订阅和处理
        private void SubscribeEvents()
        {
            // 重置标志
            _permissionGranted = false;
            _testCompleted = false;
            _testResult = string.Empty;

            // 订阅关键事件
            _client.PermissionGranted += OnPermissionGranted;
            _client.TestCompleted += OnTestCompleted;
            _client.TestCanceled += OnTestCanceled;
        }

        private void UnsubscribeEvents()
        {
            _client.PermissionGranted -= OnPermissionGranted;
            _client.TestCompleted -= OnTestCompleted;
            _client.TestCanceled -= OnTestCanceled;
        }

        private void OnPermissionGranted()
        {
            _permissionGranted = true;
            TxtTips("→ 权限已授予");
        }

        private void OnTestCompleted(string result)
        {
            _testCompleted = true;
            _testResult = result;
            TxtTips($"→ 测试完成: {result}");
        }

        private void OnTestCanceled()
        {
            TxtTips("→ 测试被服务器取消");
        }
        #endregion
    }
}
