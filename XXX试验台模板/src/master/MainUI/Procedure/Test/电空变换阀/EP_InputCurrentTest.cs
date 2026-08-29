namespace MainUI.Procedure.Test
{
    /// <summary>
    /// 输入电流压力试验
    /// </summary>
    public class EP_InputCurrentTest : GeneralBaseTest
    {
        public override async Task<bool> Execute(CancellationToken cancellationToken)
        {
            await base.Execute(cancellationToken);
            try
            {
                // ========== 上升测压力试验过程 ==========
                double MRPressure = Read("SET1").ToDouble(); //MR路充气值
                int VoltageValue = Read("SET21").ToInt(); // 电磁阀供电电压值

                SetCurrentOutput(0); //电流给0
                Voltage36VOutput(VoltageValue);// 电磁阀供电
                VX12(true);
                VX05(true); //打开EX(缓解C)路
                MRInflate(MRPressure);// MR充气

                int SET11 = Read("SET11").ToInt();
                int SET12 = Read("SET12").ToInt();
                int SET13 = Read("SET13").ToInt();
                int SET14 = Read("SET14").ToInt();
                int SET15 = Read("SET15").ToInt();
                int SET16 = Read("SET16").ToInt();

                TxtTips("====== 开始上升测压试验 ======");

                // 定义需要记录PE08值的电流点（上升）
                int[] targetCurrentsUp = [SET11, SET12, SET13, SET14, SET15, SET16];

                Dictionary<int, double> recordedPressuresUp = []; // 记录的压力值（上升）
                HashSet<int> recordedPointsUp = []; // 已记录的点（上升）

                // 用于记录时间的变量
                Stopwatch swUp = Stopwatch.StartNew();
                int lastSecondUp = 0; // 记录上一次更新电流的秒数
                double currentMa = 30; // 电流开始值

                bool timeoutUp = Delay(90, 100,
                    () =>
                    {
                        // 每100ms执行一次这个检查
                        int currentSecond = (int)(swUp.ElapsedMilliseconds / 1000);

                        // 每过1秒，电流增加13mA
                        if (currentSecond > lastSecondUp)
                        {
                            currentMa += 13;
                            SetCurrentOutput(currentMa);
                            lastSecondUp = currentSecond;

                            Debug.WriteLine($"上升 {currentSecond}s - 当前电流: {currentMa:F1} mA, PE08压力: {PE08():F1} kPa");
                        }

                        // 检查是否达到需要记录的电流点
                        foreach (int targetCurrent in targetCurrentsUp)
                        {
                            // 如果当前电流>=目标电流，且该点还未记录
                            if (currentMa >= targetCurrent && !recordedPointsUp.Contains(targetCurrent))
                            {
                                double pe08Value = PE08();
                                recordedPressuresUp[targetCurrent] = pe08Value;
                                recordedPointsUp.Add(targetCurrent);
                                TxtTips($"✓ 上升 - 电流达到 {targetCurrent}mA，记录PE08: {pe08Value:F1} kPa");
                            }
                        }

                        // 检查是否所有点都已记录完成，或者电流超过最大值
                        bool allRecorded = recordedPointsUp.Count >= targetCurrentsUp.Length;
                        bool currentTooHigh = currentMa > 650;

                        // 返回true表示条件满足，退出Delay
                        return allRecorded || currentTooHigh;
                    },
                    cancellationToken);

                // 停止计时
                swUp.Stop();
                if (timeoutUp)
                {
                    TxtTips("上升测压试验超时（超过90秒）");
                    return false;
                }

                TxtTips($"上升测压完成，最终电流: {currentMa:F1} mA");

                // 报表写值（上升）
                Write("val7", recordedPressuresUp[targetCurrentsUp[0]].ToString());
                Write("val8", recordedPressuresUp[targetCurrentsUp[1]].ToString());
                Write("val9", recordedPressuresUp[targetCurrentsUp[2]].ToString());
                Write("val10", recordedPressuresUp[targetCurrentsUp[3]].ToString());
                Write("val11", recordedPressuresUp[targetCurrentsUp[4]].ToString());
                Write("val12", recordedPressuresUp[targetCurrentsUp[5]].ToString());

                // 六个测点属于同一个小项点，按“电流=压力”顺序合并为一个字符串上传。
                SetUploadData(
                    string.Join(";", targetCurrentsUp.Select(current =>
                        $"{current}mA={recordedPressuresUp[current]}")),
                    "kPa");

                return true;
            }
            catch (Exception ex)
            {
                NlogHelper.Default.Error($"EP_InputCurrentTest：{ex.Message}");
                throw new($"EP_InputCurrentTest：{ex.Message}");
            }
            finally
            {
                // 试验结束后的清理操作
                SetCurrentOutput(0); // 关闭电流输出
            }
        }
    }
}
