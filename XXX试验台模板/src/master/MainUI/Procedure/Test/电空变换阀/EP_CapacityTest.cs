namespace MainUI.Procedure.Test
{
    /// <summary>
    /// 容量试验
    /// </summary>
    public class EP_CapacityTest : GeneralBaseTest
    {
        public override async Task<bool> Execute(CancellationToken cancellationToken)
        {
            await base.Execute(cancellationToken);
            try
            {
                double MRPressure = Read("SET1").ToDouble(); //MR路充气值
                int VoltageValue = Read("SET21").ToInt(); // 电磁阀供电电压值

                SetCurrentOutput(0); //电流给0
                Voltage36VOutput(VoltageValue);// 电磁阀供电
                VX12(true);
                VX05(true); //打开EX(缓解C)路
                MRInflate(MRPressure);// MR充气

                // 1.操作电流A，设定RD2.5L压力＝500 kPa。
                // 暂时从400mA电流开始输出
                int InitialCurrent = 400;
                int lastSecond = 0;
                Stopwatch sw = Stopwatch.StartNew();
                bool timeout = Delay(90, 100,
                    () =>
                    {
                        int currentSecond = (int)(sw.ElapsedMilliseconds / 1000);
                        // 每过1秒，电流增加2mA
                        if (currentSecond > lastSecond)
                        {
                            InitialCurrent += 2;
                            SetCurrentOutput(InitialCurrent);
                            lastSecond = currentSecond;
                        }
                        // PE08压力上升至500kPa退出检测
                        return (PE08() >= 500);
                    },
                    cancellationToken);
                sw.Stop();// 停止计时
                if (timeout)
                {
                    TxtTips("等待PE08压力＝500kPa超时");
                    return false;
                }

                Delay(10); //稳压10S

                int DownStart = Read("SET17").ToInt(); // 气压开始标准
                int DownEnd = Read("SET18").ToInt();   // 气压结束标准
                double startTime = 0; // 记录开始时间（秒）
                double endTime = 0;   // 记录结束时间（秒）
                bool started = false; // 是否已开始计时
                bool finished = false; // 是否已完成计时

                // 2.测定“试验电压”SW＝OFF、RD2.5L(PE08)压力的下降时间。
                Voltage36VControl(false);

                // 开始计时，检测压力从490降到50的时间
                sw = Stopwatch.StartNew();
                bool downtimeout = Delay(120, 100,
                    () =>
                    {
                        double currentPressure = PE08();
                        double elapsedSeconds = sw.ElapsedMilliseconds / 1000.0;

                        // 当压力首次低于490kPa时，记录开始时间
                        if (!started && currentPressure <= DownStart)
                        {
                            started = true;
                            startTime = elapsedSeconds;
                            TxtTips($"压力开始下降，当前: {currentPressure:F1} kPa，开始时间: {startTime}s");
                        }

                        // 当压力降到50kPa以下时，记录结束时间
                        if (started && !finished && currentPressure <= DownEnd)
                        {
                            finished = true;
                            endTime = elapsedSeconds;
                            TxtTips($"压力降至50kPa，当前: {currentPressure:F1} kPa，结束时间: {endTime}s");
                            return true; // 退出Delay
                        }

                        // 每秒输出一次状态
                        if ((int)(elapsedSeconds * 10) % 10 == 0) // 每秒
                        {
                            TxtTips($"检测中... 当前压力: {currentPressure:F1} kPa, 已过时间: {elapsedSeconds:F1}s");
                        }

                        return false;
                    },
                    cancellationToken);
                sw.Stop();

                if (timeout)
                {
                    TxtTips("检测PE08压力下降超时（超过120秒）");
                    return false;
                }

                // 计算从490kPa降到50kPa的时间
                double dropTime = endTime - startTime;
                Write("val19", dropTime.ToString("F1")); // 保存时间，单位：秒

                Delay(10); // 排气时间


                // 3.供给容量 - 测定"试验电压"SW＝ON、RD2.5L(PE08)压力的上升时间(从0到390)
                TxtTips("====== 开始检测压力上升时间 (0→390) ======");
                Voltage36VControl(true);

                int UPStart = Read("SET19").ToInt(); // 气压开始标准
                int UPEnd = Read("SET20").ToInt();   // 气压结束标准
                double upStartTime = 0; // 上升开始时间
                double upEndTime = 0;   // 上升结束时间
                bool upStarted = false; // 是否已开始上升计时
                bool upFinished = false; // 是否已完成上升计时

                sw = Stopwatch.StartNew();
                bool uptimeout = Delay(120, 100,
                    () =>
                    {
                        double currentPressure = PE08();
                        double elapsedSeconds = sw.ElapsedMilliseconds / 1000.0;

                        // 当压力首次超过0kPa时，记录开始时间
                        if (!upStarted && currentPressure >= UPStart)
                        {
                            upStarted = true;
                            upStartTime = elapsedSeconds;
                            TxtTips($"压力开始上升，当前: {currentPressure:F1} kPa，开始时间: {upStartTime:F1}s");
                        }

                        // 当压力上升到390kPa及以上时，记录结束时间
                        if (upStarted && !upFinished && currentPressure >= UPEnd)
                        {
                            upFinished = true;
                            upEndTime = elapsedSeconds;
                            TxtTips($"压力升至390kPa，当前: {currentPressure:F1} kPa，结束时间: {upEndTime:F1}s");
                            return true; // 退出Delay
                        }
                        return false;
                    },
                    cancellationToken);
                sw.Stop();
                if (uptimeout)
                {
                    TxtTips("检测PE08压力上升390kPa超时（超过120秒）");
                    return false;
                }

                // 计算从0kPa升到390kPa的时间
                double riseTime = upEndTime - upStartTime;
                Write("val20", riseTime.ToString("F1")); // 保存时间，单位：秒

                TxtTips("====== 容量试验完成 ======");

                // 本项点全部动作完成后，集中设置接口值；两个时间量单位相同，合并为一条小项点数据。
                SetUploadData($"下降时间={dropTime:F1};上升时间={riseTime:F1}", "s");

                return true;
            }
            catch (Exception ex)
            {
                NlogHelper.Default.Error($"EP_ExhaustCapacityTest：{ex.Message}");
                throw new($"EP_ExhaustCapacityTest：{ex.Message}");
            }
            finally
            {
                // 试验结束后的清理操作
            }
        }
    }
}
