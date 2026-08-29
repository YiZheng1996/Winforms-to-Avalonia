namespace MainUI.Procedure.Test
{
    /// <summary>
    /// 测试项点
    /// </summary>
    public class CS_Test01 : GeneralBaseTest
    {
        public override async Task<bool> Execute(CancellationToken cancellationToken)
        {
            await base.Execute(cancellationToken);
            try
            {
                // 使用示例
                // 简单延时 2 秒
                Delay(2.0);

                // 等待条件满足（最多10秒）
                // wait() 返回 true 表示条件满足，退出等待
                bool timeout = Delay(10.0, 100, () => OPCHelper.AIgrp[0] > 100.0);
                if (timeout)
                {
                    TxtTips("等待超时");
                    return false;
                }

                if (!ShowConfirmDialog("是否继续?"))
                {
                    return false;
                }

                // 带图标类型的确认
                if (!ShowConfirmDialog("检测到异常，是否继续?", AntdUI.TType.Warn))
                {
                    return false;
                }

                // 显示各种提示
                ShowSuccessDialog("操作成功");
                ShowWarningDialog("注意检查");
                ShowErrorDialog("操作失败");
                ShowInfoDialog("提示信息");

                //  等待多个条件之一满足（最多5秒）
                Delay(5, 100,
                    () => OPCHelper.DIgrp[1],           // 条件1：DI[1] 为 true
                    () => OPCHelper.AIgrp[0] > 50.0     // 条件2：AI[0] > 50
                );

                // 带步骤名称的延时（显示倒计时）--缩写版 
                Delay(30, "延时名称");

                // 带步骤名称的延时（显示倒计时）
                Delay(30, 100, () => false, "预热阶段");

                // 某个结果值
                var pressure = PE01();
                //  当前小项点的合格判断
                bool isOk = pressure >= 580 && pressure <= 600;

                // 当前小项点的所有试验动作完成后，再设置本项点需要上传的最终值。
                // SetUploadData 只保存当前小项点的值，不会清空其他项点，也不会立即调用上传接口。
                // 外层自动试验流程将在 Execute 返回后，把该值追加到整场试验共用的 testData 列表中。
                SetUploadData(
                    value: "",   // TODO：填写当前小项点最终试验值
                    unit: "");   // TODO：填写单位，例如 kPa、V、mA、s；没有单位时留空

                return isOk;
            }
            catch (Exception ex)
            {
                NlogHelper.Default.Error($"CS_Test01：{ex.Message}");
                throw new($"CS_Test01：{ex.Message}");
            }
            finally
            {
                // 试验结束后的清理操作
            }
        }
    }
}
