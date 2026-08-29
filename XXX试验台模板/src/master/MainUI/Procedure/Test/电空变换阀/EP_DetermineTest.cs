using MainUI.InsulationWithstand;
namespace MainUI.Procedure.Test
{
    /// <summary>
    /// 测定试验
    /// </summary>
    public class EP_DetermineTest : GeneralBaseTest
    {
        #region 私有字段
        private WithstandTesterClient _client = WithstandTesterClient.Instance;
        private bool _permissionGranted = false;
        private bool _testCompleted = false;
        private string _testResult = string.Empty;
        #endregion

        public override async Task<bool> Execute(CancellationToken cancellationToken)
        {
            await base.Execute(cancellationToken);
            try
            {
                // ========== 步骤 1: 确保连接 ==========
                TxtTips("检查服务器连接...");
                if (!_client.IsConnected)
                {
                    TxtTips("正在连接服务器...");
                    if (!await _client.ConnectAsync())
                    {
                        TxtTips("连接服务器失败");
                        return false;
                    }
                    TxtTips("连接成功");
                }

                // 步骤 2: 订阅事件
                SubscribeEvents();

                try
                {
                    // 步骤 3: 请求测试权限
                    string deviceType = "Resistance";
                    string parameters = Read("SET30"); // 从配置读取参数

                    TxtTips($"请求 {deviceType} 测试权限...");
                    bool requestSuccess = await _client.RequestTestAsync(deviceType);
                    if (!requestSuccess)
                    {
                        TxtTips("发送请求失败");
                        return false;
                    }

                    // 步骤 4: 等待权限授予
                    TxtTips("等待服务器授予权限...");

                    // 检测权限
                    bool timeout = Delay(10, 100, () => _permissionGranted, cancellationToken);
                    if (timeout)
                    {
                        TxtTips("等待权限超时(10秒)");
                        return false;
                    }

                    TxtTips("已获得测试权限成功");

                    // 步骤 5: 发送测试参数
                    TxtTips($"发送测试参数: {parameters}");
                    bool paramsSent = await _client.SendTestParametersAsync(deviceType, parameters);

                    if (!paramsSent)
                    {
                        TxtTips("发送参数失败");
                        return false;
                    }

                    TxtTips("参数已发送成功");

                    // 步骤 6: 等待测试完成
                    TxtTips("测试进行中,请等待...");

                    // 检测测试完成
                    timeout = Delay(120.0, 500, () => _testCompleted, cancellationToken);
                    if (timeout)
                    {
                        TxtTips("测试超时(120秒)");
                        return false;
                    }

                    // 步骤 7: 处理测试结果
                    TxtTips($"测试完成: {_testResult}");
                    // 解析结果 "PASS|100.5MΩ" 或 "FAIL|0.5MΩ"
                    if (string.IsNullOrEmpty(_testResult))
                    {
                        TxtTips("未收到测试结果");
                        return false;
                    }

                    Write("val1", _testResult);

                    FrmDetermination determination = new();
                    VarHelper.ShowDialogWithOverlay(Frm, determination);
                    Write("val2", determination.ExhaustValve);
                    Write("val3", determination.SupplyValve);

                    // 对话框数据确认完成后再设置上传值，避免从报表单元格反向推断试验结果。
                    SetUploadData(
                        $"测试结果={_testResult};排气阀={determination.ExhaustValve};供给阀={determination.SupplyValve}");
                }
                finally
                {
                    // 步骤 8: 清理资源
                    UnsubscribeEvents();
                    await _client.EndTestAsync();
                }
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
                NlogHelper.Default.Error($"EP_DetermineTest异常: {ex.Message}", ex);
                return false;
            }
            finally
            {
                // 试验结束后的清理操作，步骤 8: 清理资源
                UnsubscribeEvents();
                await _client.EndTestAsync();
            }
        }

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
