using AntdUI;

namespace MainUI.Procedure.Test
{
    /// <summary>
    /// 高压侧泄露试验
    /// </summary>
    public class B11_HighLeakTest : GeneralBaseTest
    {
        public override async Task<bool> Execute(CancellationToken cancellationToken)
        {
            await base.Execute(cancellationToken);
            try
            {
                double MRPressure = Read("SET1").ToDouble(); //MR路充气值
                double TestVoltage = Read("GDDY").ToDouble(); //试验电压

                BCRoadExhaust(true);  // BC电磁阀打开
                Voltage160VOutput(TestVoltage);   // 输出电压100V
                Voltage160VControl(false); // 电压输出关闭
                MRInflate(MRPressure);// MR充气

                Delay(30, "充气时间");

                VX03(false);
                VX08(false);
                double StartPressure = PE05();
                Write("ks4", StartPressure.ToString("f1"));

                Delay(30, "稳压时间");

                double StopPressure = PE05();
                Write("js4", StopPressure.ToString("f1"));
                //double Pressure = StartPressure - StopPressure;

                // 高压侧泄漏试验完成后再设置接口值，外层流程负责追加到整场 testData。
                SetUploadData($"开始压力={StartPressure:F1};结束压力={StopPressure:F1}", "kPa");
                return true;
            }
            catch (Exception ex)
            {
                NlogHelper.Default.Error($"B11_HighLeakTest：{ex.Message}");
                throw new($"B11_HighLeakTest：{ex.Message}");
            }
            finally
            {
                // 试验结束后的清理操作
            }
        }
    }
}
