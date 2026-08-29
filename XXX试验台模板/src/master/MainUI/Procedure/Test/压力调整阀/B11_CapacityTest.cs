using System.Diagnostics;

namespace MainUI.Procedure.Test
{
    /// <summary>
    /// 供给容量试验
    /// </summary>
    public class B11_CapacityTest : GeneralBaseTest
    {
        public override async Task<bool> Execute(CancellationToken cancellationToken)
        {
            await base.Execute(cancellationToken);
            try
            {
                // 供给容量
                double MRPressure = Read("SYQY").ToDouble(); ; //MR路充气值
                double TestVoltage = Read("GDDY").ToDouble(); //试验电压
                double UpkPa = Read("SET8").ToDouble(); ;   //上升至气压标准值
                double DownkPa = Read("SET9").ToDouble(); ; //下降至气压标准值
                Voltage160VOutput(TestVoltage);   // 输出电压100V

                VX01(false);
                VX02(false);
                VX09(false);

                VX03(true);
                VX08(true);
                VX06(true);
                Delay(20, 100, () => PE06() <= 10); //排空气压
                Voltage160VControl(false); // 电压输出关闭
                VX06(false);
                EP01(MRPressure);
                VX01(true);
                VX02(true);
                VX09(true);

                Stopwatch sw = Stopwatch.StartNew();
                Delay(10, 100, () => PE06() >= UpkPa); //排空气压
                sw.Stop();
                // 充气到目标值时间，Ticks以获得更高的精度
                var InflateTime = sw.ElapsedTicks / (double)Stopwatch.Frequency; 
                Write("val6", InflateTime.ToString("F1")); // 保存时间，单位：秒
                Delay(10);

                // 排气容量
                sw = Stopwatch.StartNew();
                Voltage160VControl(true); // 电压输出关闭
                Delay(10, 100, () => PE06() <= DownkPa); //排空气压
                sw.Stop();
                
                var ExhaustTime = sw.ElapsedTicks / (double)Stopwatch.Frequency;
                Write("val15", ExhaustTime.ToString("F1")); // 保存时间，单位：秒

                // 充气和排气均完成后，将同一容量试验的两个时间结果合并上传。
                SetUploadData($"充气时间={InflateTime:F1};排气时间={ExhaustTime:F1}", "s");
                return true;
            }
            catch (Exception ex)
            {
                NlogHelper.Default.Error($"B11_SupplyCapacityTest：{ex.Message}");
                throw new($"B11_SupplyCapacityTest：{ex.Message}");
            }
            finally
            {
                // 试验结束后的清理操作
            }
        }
    }
}
