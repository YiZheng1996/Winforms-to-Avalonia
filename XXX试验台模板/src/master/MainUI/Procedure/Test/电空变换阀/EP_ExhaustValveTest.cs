namespace MainUI.Procedure.Test
{
    /// <summary>
    /// 排气阀泄漏试验
    /// </summary>
    public class EP_ExhaustValveTest : GeneralBaseTest
    {
        public override async Task<bool> Execute(CancellationToken cancellationToken)
        {
            await base.Execute(cancellationToken);
            try
            {
                double MRPressure = Read("SET1").ToDouble(); // MR路充气值
                int StabilivoltTime = Read("SET7").ToInt(); // 稳压时间
                int TestTime = Read("SET8").ToInt(); // 测试时间
                int VoltageValue = Read("SET21").ToInt(); // 电磁阀供电电压值
                int CurrentValue = Read("SET22").ToInt(); // 电磁阀输入电流值
                double JudgmentPressure = Read("SET4").ToDouble(); // PE08判断气压值

                VX05(true); //TCX(缓解C) 持续信号

                // 1.TA（MR）充气
                MRInflate(MRPressure);// MR充气

                // 2.设定电流A＝约600mA，并确认RD2.5L压力＝590 kPa
                //   设定电压电流值
                Voltage36VOutput(VoltageValue); // 电磁阀供电
                SetCurrentOutput(CurrentValue); // 电磁阀输入电流

                // 确认RD2.5L压力＝590 kPa
                // 确认PE08压力＝590±10 kPa 
                bool timeout = Delay(30, 100, () => PE08() > JudgmentPressure - 10, cancellationToken);
                if (timeout)
                {
                    TxtTips("等待PE08压力＝590±10kPa超时");
                    return false;
                }

                // 3.关闭A（VX01）
                VX01(false);

                // 4.使电流上升50 mA左右，并确认A通道压力和RD2.5L压力处于同压（供给阀处于打开状态）。
                //   使电流上升50 mA左右(650mA)，确认EP03和EP08压力处于同压
                SetCurrentOutput(CurrentValue + 50); // 电磁阀输入电流
                bool timeout2 = Delay(30, 100, () => PE03() >= PE08() - 10, cancellationToken);
                if (timeout2)
                {
                    TxtTips("等待EP03和EP08压力处于同压超时");
                    return false;
                }

                // 稳压操作
                Delay(StabilivoltTime, "稳压时间");

                // 5.关闭TA
                MRInflate(0, false);

                // 6.关闭TA
                VX12(false);

                double startPressure = PE08();
                Write("ks3", startPressure.ToString()); // 开始压力
                Delay(TestTime, "测试时间"); // 测试时间
                double stopPressure = PE08();
                Write("js3", stopPressure.ToString()); // 结束压力

                // 排气阀泄漏过程结束后，将同一小项点的起止压力合并上传。
                SetUploadData($"开始压力={startPressure};结束压力={stopPressure}", "kPa");
                return true;
            }
            catch (Exception ex)
            {
                NlogHelper.Default.Error($"EP_ExhaustValveTest：{ex.Message}");
                throw new($"EP_ExhaustValveTest：{ex.Message}");
            }
            finally
            {
                // 试验结束后的清理操作
            }
        }
    }
}
