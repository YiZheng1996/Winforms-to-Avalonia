namespace MainUI.Procedure.Test
{
    /// <summary>
    /// 重叠位置泄露试验
    /// </summary>
    public class EP_OverlapPositionTest : GeneralBaseTest
    {
        public override async Task<bool> Execute(CancellationToken cancellationToken)
        {
            await base.Execute(cancellationToken);
            try
            {
                double MRPressure = Read("SET1").ToDouble(); // MR路充气值
                int StabilivoltTime = Read("SET5").ToInt(); // 稳压时间
                int TestTime = Read("SET6").ToInt(); // 测试时间
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

                // 3.稳定时间＝30秒放置
                Delay(StabilivoltTime, "稳压时间"); // 稳压时间

                // 4.关闭TA
                //   关闭 VX03
                VX03(false);

                // 5.并测定30秒钟的压力下降量。
                double startPressure = PE08();
                Write("ks2", startPressure.ToString()); // 开始压力

                Delay(StabilivoltTime, "测试时间"); //测试时间

                double stopPressure = PE08();
                Write("js2", stopPressure.ToString()); // 结束压力

                // 重叠位置试验完成后才设置本项点接口值。
                SetUploadData($"开始压力={startPressure};结束压力={stopPressure}", "kPa");
                return true;
            }
            catch (Exception ex)
            {
                NlogHelper.Default.Error($"EP_OverlapPositionTest：{ex.Message}");
                throw new($"EP_OverlapPositionTest：{ex.Message}");
            }
            finally
            {
                // 试验结束后的清理操作
            }
        }
    }
}
