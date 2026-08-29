namespace MainUI.Procedure.Test
{
    /// <summary>
    /// 调压试验（临时）
    /// </summary>
    public class B11_VoltageSideTest : GeneralBaseTest
    {
        public override async Task<bool> Execute(CancellationToken cancellationToken)
        {
            await base.Execute(cancellationToken);
            try
            {
                // 高侧压试验
                double MRPressure = Read("SYQY").ToDouble(); ; //MR路充气值
                double HighPressure = Read("SET10").ToDouble(); //高压侧压力值
                double HighToPressure = Read("SET11").ToDouble(); ; //高压侧压力值
                double TestVoltage = Read("GDDY").ToDouble(); //试验电压

                BCRoadExhaust(true);  // BC电磁阀打开
                Voltage160VOutput(TestVoltage);   // 输出电压100V
                Voltage160VControl(true); // 电压输出开启
                MRInflate(MRPressure);// MR充气

                Voltage160VControl(false); // 电压输出关闭
                FrmVoltageSide frmVoltage = new()
                {
                    PromptMessage = $"请转动高压侧调整螺钉（下侧）来进行调整压力至指定值({HighPressure}～{HighToPressure})后，点击是提交。"
                };
                VarHelper.ShowDialogWithOverlay(Frm, frmVoltage);

                for (int i = 0; i < 3; i++)
                {
                    VX06(true);
                    Delay(5, "排气时间");
                    VX06(false);
                    Delay(10, "稳压时间");
                    double pressure = PE05();
                    if (pressure >= HighPressure && pressure <= HighToPressure)
                    {
                        break;
                    }
                    else if (i == 2)
                    {
                        throw new("高压侧压力值不在指定范围内，试验失败！");
                    }
                    else
                    {
                        MessageHelper.MessageOK(Frm, $"高压侧压力值不在指定范围内，请转动高压侧调整螺钉（下侧）来进行重新调节！", AntdUI.TType.Error);
                        frmVoltage.PromptMessage = $"请转动高压侧调整螺钉（下侧）来进行调整压力至指定值({HighPressure}～{HighToPressure})后，点击是提交。";
                        VarHelper.ShowDialogWithOverlay(Frm, frmVoltage);
                    }
                }

                double highSidePressure = PE05();
                Write("val1", highSidePressure.ToString("f1"));

                // 低压侧试验
                double LowPressure =  Read("SET12").ToDouble();   // 低压侧压力值
                double LowToPressure = Read("SET13").ToDouble(); ; // 低压侧压力值

                Voltage160VControl(true); // 电压输出开启
                VX06(true);
                Delay(5, "排气时间");
                VX06(false);
                Delay(10, "稳压时间");

                frmVoltage = new()
                {
                    PromptMessage = $"请转动高压侧调整螺钉（上侧）来进行调整压力至指定值({LowPressure}～{LowToPressure})后，点击是提交。"
                };
                VarHelper.ShowDialogWithOverlay(Frm, frmVoltage);

                for (int i = 0; i < 3; i++)
                {
                    VX06(true);
                    Delay(5, "排气时间");
                    VX06(false);
                    Delay(10, "稳压时间");
                    double pressure = PE05();
                    if (pressure >= LowPressure && pressure <= LowToPressure)
                    {
                        break;
                    }
                    else if (i == 2)
                    {
                        throw new("低压侧压力值不在指定范围内，试验失败！");
                    }
                    else
                    {
                        MessageHelper.MessageOK(Frm, $"低压侧压力值不在指定范围内，请转动低压侧调整螺钉（上侧）重新调节！", AntdUI.TType.Error);
                        frmVoltage.PromptMessage = $"请转动高压侧调整螺钉（上侧）来进行调整压力至指定值({LowPressure}～{LowToPressure})后，点击确定提交。";
                        VarHelper.ShowDialogWithOverlay(Frm, frmVoltage);
                    }
                }

                double lowSidePressure = PE05();
                Write("val10", lowSidePressure.ToString("f1"));

                // 高低压侧调整都确认完成后，再形成这一小项点的最终上传值。
                SetUploadData($"高压侧={highSidePressure:F1};低压侧={lowSidePressure:F1}", "kPa");
                return true;
            }
            catch (Exception ex)
            {
                NlogHelper.Default.Error($"B11_LowVoltageSideTest：{ex.Message}");
                throw new($"B11_LowVoltageSideTest：{ex.Message}");
            }
            finally
            {
                // 试验结束后的清理操作
            }
        }
    }
}
