namespace MainUI.Procedure.Test
{
    /// <summary>
    /// 灵敏度试验（高压）
    /// </summary>
    public class B11_HighVoltageSensitivityTest : GeneralBaseTest
    {
        public override async Task<bool> Execute(CancellationToken cancellationToken)
        {
            await base.Execute(cancellationToken);
            try
            {
                // 供给灵敏度（高压)
                double MRPressure = Read("SYQY").ToDouble(); //MR路充气值
                double TestVoltage = Read("GDDY").ToDouble(); //试验电压

                BCRoadExhaust(true);  // BC电磁阀打开
                Voltage160VOutput(TestVoltage);   // 输出电压
                Voltage160VControl(false); // 电压输出关闭
                VX06(true);
                Delay(5);
                VX06(false);
                MRInflate(MRPressure);// MR充气

                double StartHighkPa = PE06();
                VX10(true);
                // 测定B通道压力下降停止时的B通道压力，判断是否气压下降，下降至排不动情况
                // 每1秒检查,每次至少下降1kPa,最多监测30秒
                bool success = CheckPressureFalling(30, 1, 1, cancellationToken);
                if (!success)
                {
                    ShowErrorDialog("气压下降异常!");
                }
                double StopHighkPa = PE06();
                double HighkPa = StartHighkPa - StopHighkPa; //x1
                VX10(false);

                Write("val2", HighkPa.ToString("f1"));

                // 排气灵敏度（高压）
                Delay(20); //稳压
                StartHighkPa = PE06();
                VX07(true);

                // 每1秒检查,每次至少上升1kPa,最多监测30秒
                success = CheckPressureRising(30, 1, 1, cancellationToken);
                if (!success)
                {
                    ShowErrorDialog("气压上升异常!");
                }

                StopHighkPa = PE06();
                double HighkPa2 = StartHighkPa - StopHighkPa; //Y1
                Write("val3", HighkPa2.ToString("f1"));

                // 滞后（高压）
                VX07(false);
                double YX = HighkPa - HighkPa2; // 滞后 Y1－X1
                Write("val4", YX.ToString("f1"));

                // 过度充气灵敏度（高压）
                VX03(true);
                Delay(5);
                double PE06kPa = PE06();
                VX06(true);

                //将B通道压力减压约35kPa。
                Delay(10.0, 50, () => (PE06()) <= PE06kPa - 35.0);
                VX06(false);
                VX03(true);

                Delay(10); //测定B通道压力稳定时的B通道压力

                double overchargePressure = PE06();
                Write("val5", overchargePressure.ToString("f1"));

                // 四个阶段共同组成一个高压灵敏度小项点，所有动作完成后只赋值一次。
                SetUploadData(
                    $"供给灵敏度={HighkPa:F1};排气灵敏度={HighkPa2:F1};滞后={YX:F1};过度充气压力={overchargePressure:F1}",
                    "kPa");
                return true;
            }
            catch (Exception ex)
            {
                NlogHelper.Default.Error($"B11_HighVoltageSensitivityTest：{ex.Message}");
                throw new($"B11_HighVoltageSensitivityTest：{ex.Message}");
            }
            finally
            {
                // 试验结束后的清理操作
            }
        }

        /// <summary>
        /// 判断气压是否持续上升
        /// </summary>
        /// <param name="timeout">超时时间(秒)</param>
        /// <param name="checkInterval">检查间隔(秒)</param>
        /// <param name="minRisePerCheck">每次检查的最小上升值(kPa)</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>true=正常上升, false=上升异常</returns>
        private bool CheckPressureRising(
            double timeout, double checkInterval, double minRise, CancellationToken ct)
        {
            double prev = PE06();

            bool timeout_occurred = Delay(timeout, (int)(checkInterval * 1000), () =>
            {
                double curr = PE06();
                double change = curr - prev;  // 当前 - 上次

                if (change >= minRise)
                {
                    prev = curr;
                    return false;  // 继续
                }
                return true;  // 退出
            }, ct);

            return timeout_occurred;
        }

        /// <summary>判断气压下降</summary>
        private bool CheckPressureFalling(
            double timeout, double checkInterval, double minFall, CancellationToken ct)
        {
            double prev = PE06();

            bool timeout_occurred = Delay(timeout, (int)(checkInterval * 1000), () =>
            {
                double curr = PE06();
                double change = prev - curr;  // 上次 - 当前

                if (change >= minFall)
                {
                    prev = curr;
                    return false;
                }
                return true;
            }, ct);

            return timeout_occurred;
        }

    }
}
