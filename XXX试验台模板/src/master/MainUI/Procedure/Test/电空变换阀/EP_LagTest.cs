namespace MainUI.Procedure.Test
{
    /// <summary>
    /// 滞后试验
    /// </summary>
    public class EP_LagTest : GeneralBaseTest
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

                // 报表参数
                int SET11 = Read("SET11").ToInt(); // 230
                int SET12 = Read("SET12").ToInt(); // 300
                int SET13 = Read("SET13").ToInt(); // 400
                int SET14 = Read("SET14").ToInt(); // 500
                int SET15 = Read("SET15").ToInt(); // 600
                int SET16 = Read("SET16").ToInt(); // 650

                TxtTips("====== 开始上升测压试验 ======");

                // 设置起始电流为680mA
                double currentMa = 680;
                SetCurrentOutput(currentMa);
                await Task.Delay(500, cancellationToken); // 等待500ms稳定

                // 定义需要记录PE08值的电流点（下降）- 从大到小排序
                int[] targetCurrentsDown = [SET16, SET15, SET14, SET13, SET12, SET11];

                Dictionary<int, double> recordedPressuresDown = []; // 记录的压力值（下降）
                HashSet<int> recordedPointsDown = []; // 已记录的点（下降）

                // 用于记录时间的变量
                Stopwatch swDown = Stopwatch.StartNew();
                int lastSecondDown = 0; // 记录上一次更新电流的秒数

                bool timeoutDown = Delay(90, 100,
                    () =>
                    {
                        // 每100ms执行一次这个检查
                        int currentSecond = (int)(swDown.ElapsedMilliseconds / 1000);

                        // 每过1秒，电流减少13mA
                        if (currentSecond > lastSecondDown)
                        {
                            currentMa -= 13;
                            if (currentMa < 0) currentMa = 0; // 防止负值
                            SetCurrentOutput(currentMa);
                            lastSecondDown = currentSecond;

                            Debug.WriteLine($"下降 {currentSecond}s - 当前电流: {currentMa:F1} mA, PE08压力: {PE08():F1} kPa");
                        }

                        // 检查是否达到需要记录的电流点
                        foreach (int targetCurrent in targetCurrentsDown)
                        {
                            // 如果当前电流<=目标电流，且该点还未记录
                            if (currentMa <= targetCurrent && !recordedPointsDown.Contains(targetCurrent))
                            {
                                double pe08Value = PE08();
                                recordedPressuresDown[targetCurrent] = pe08Value;
                                recordedPointsDown.Add(targetCurrent);
                                TxtTips($"下降 - 电流达到 {targetCurrent}mA，记录PE08: {pe08Value:F1} kPa");
                            }
                        }

                        // 检查是否所有点都已记录完成，或者电流低于最小值
                        bool allRecorded = recordedPointsDown.Count >= targetCurrentsDown.Length;
                        bool currentTooLow = currentMa < 230;

                        // 返回true表示条件满足，退出Delay
                        return allRecorded || currentTooLow;
                    },
                    cancellationToken);

                // 停止计时
                swDown.Stop();
                if (timeoutDown)
                {
                    TxtTips("下降测压试验超时（超过90秒）");
                    return false;
                }

                TxtTips($"下降测压完成，最终电流: {currentMa:F1} mA");

                // 报表写值（下降）- 按顺序写入：650, 600, 500, 400, 300, 230
                Write("val13", recordedPressuresDown[targetCurrentsDown[0]].ToString());
                Write("val14", recordedPressuresDown[targetCurrentsDown[1]].ToString());
                Write("val15", recordedPressuresDown[targetCurrentsDown[2]].ToString());
                Write("val16", recordedPressuresDown[targetCurrentsDown[3]].ToString());
                Write("val17", recordedPressuresDown[targetCurrentsDown[4]].ToString());
                Write("val18", recordedPressuresDown[targetCurrentsDown[5]].ToString());

                TxtTips("====== 输入电流压力试验完成 ======");

                // 六个下降测点属于同一个小项点，Execute 结束前只设置一次最终上传值。
                SetUploadData(
                    string.Join(";", targetCurrentsDown.Select(current =>
                        $"{current}mA={recordedPressuresDown[current]}")),
                    "kPa");

                return true;
            }
            catch (Exception ex)
            {
                NlogHelper.Default.Error($"EP_LagTest：{ex.Message}");
                throw new($"EP_LagTest：{ex.Message}");
            }
            finally
            {
                // 试验结束后的清理操作
            }
        }
    }
}
