namespace MainUI.Procedure.Test
{
    /// <summary>
    /// 作用试验
    /// </summary>
    public class EP_EffectTest : GeneralBaseTest
    {
        public override async Task<bool> Execute(CancellationToken cancellationToken)
        {
            await base.Execute(cancellationToken);
            try
            {
                // 上升作用试验步骤
                double MRPressure = Read("SET1").ToDouble(); //MR路充气值
                int VoltageValue = Read("SET21").ToInt(); // 电磁阀供电电压值
                int CurrentValue = Read("SET22").ToInt(); // 电磁阀输入电流值
                double SetCurrent = Read("SET9").ToDouble(); //上升电流初始值

                SetCurrentOutput(0); //电流给0
                Voltage36VOutput(VoltageValue);// 电磁阀供电
                VX12(true);
                MRInflate(MRPressure);// MR充气

                // 2.使电流从150 mA开始上升 (约2mA/S)，测定RD2.5L（PE08）压力开始上升时的电流
                SetCurrentOutput(SetCurrent); // 电磁阀输入电流

                int lastSecond = 0;
                Stopwatch sw = Stopwatch.StartNew();
                double StartPressure = PE08();
                bool timeout = Delay(90, 100,
                    () =>
                    {
                        int currentSecond = (int)(sw.ElapsedMilliseconds / 1000);
                        // 每过1秒，电流增加2mA
                        if (currentSecond > lastSecond)
                        {
                            SetCurrent += 2;
                            SetCurrentOutput(SetCurrent);
                            lastSecond = currentSecond;
                        }
                        // 1.PE08压力开始上升时的电流退出检测，2.电流超过500时退出检测，防止电流过高
                        return (PE08() > StartPressure + 5 || GetCurrentOutput() > 500);
                    },
                    cancellationToken);
                sw.Stop();// 停止计时
                if (timeout)
                {
                    TxtTips("等待PE08压力＝590±10kPa超时");
                    return false;
                }

                Delay(10);
                double risingPressure = PE08();
                Write("val4", risingPressure.ToString());


                // 下降作用试验步骤
                double DownSetCurrent = Read("SET10").ToDouble(); //下降电流值

                lastSecond = 0;
                sw = Stopwatch.StartNew();
                bool timeout2 = Delay(90, 100,
                   () =>
                   {
                       int currentSecond = (int)(sw.ElapsedMilliseconds / 1000);
                       // 每过1秒，电流减少2mA
                       if (currentSecond > lastSecond)
                       {
                           SetCurrent -= 2;
                           SetCurrentOutput(SetCurrent);
                           lastSecond = currentSecond;
                       }
                       return GetCurrentOutput() <= DownSetCurrent;
                   },
                   cancellationToken);
                sw.Stop();// 停止计时
                if (timeout2)
                {
                    TxtTips("等待PE08压力<10kPa超时");
                    return false;
                }
                Delay(10);
                double fallingPressure = PE08();
                Write("val5", fallingPressure.ToString());

                // 上升、下降两个阶段都完成后，作为同一个作用试验项上传。
                SetUploadData($"上升阶段压力={risingPressure};下降阶段压力={fallingPressure}", "kPa");
                return true;
            }
            catch (Exception ex)
            {
                NlogHelper.Default.Error($"EP_UpEffectTest：{ex.Message}");
                throw new($"EP_UpEffectTest：{ex.Message}");
            }
            finally
            {
                // 试验结束后的清理操作
            }
        }
    }
}
