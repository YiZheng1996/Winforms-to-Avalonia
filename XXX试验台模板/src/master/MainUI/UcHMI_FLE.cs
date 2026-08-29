using MainUI.InsulationWithstand;
using System.Windows.Forms;

namespace MainUI.Procedure.User
{
    public partial class UcHMI_FLE : UserControl
    {

        Dictionary<int, UIValve> rbtDCF = [];//气控阀

        Dictionary<int, UILabel> rbtCGQ = [];//传感器

        Dictionary<int, UISwitch> rbtDO = [];//DO控制点

        Dictionary<int, UILedBulb> rbtDI = [];//DI检测

        private Form _form;
        public UcHMI_FLE(Form form)
        {
            InitializeComponent();
            _form = form;
        }
        private void UcHMI_PBU_Load(object sender, EventArgs e)
        {
            LoaddicDCF();
            LoaddicCGQ();
            LoaddicDO();
            LoaddicDI();

            OPCHelper.DOgrp.DOgrpChanged += DOgrp_DOgrpChanged;
            OPCHelper.DOgrp.Fresh();

            OPCHelper.AIgrp.AIvalueGrpChanged += AIgrp_AIvalueGrpChanged;
            OPCHelper.AIgrp.Fresh();

            OPCHelper.DIgrp.DigitalValueChanged += DIgrp_DIGroupChanged;
            OPCHelper.DIgrp.Fresh();

            OPCHelper.AOgrp.AOvalueGrpChaned += AOgrp_AOvalueGrpChaned;
            OPCHelper.AOgrp.Fresh();
        }


        private void LoaddicDCF()
        {
            rbtDO.Clear();
            try
            {
                foreach (var item in this.Controls)
                {
                    if (item is UIValve)
                    {
                        UIValve sp = item as UIValve;
                        rbtDCF.Add(Convert.ToInt32(sp.Tag), sp);
                    }
                }
            }
            catch (Exception ex)
            {
                NlogHelper.Default.Error("加载气控阀信号控件异常！：", ex);
            }
        }
        private void LoaddicCGQ()
        {
            rbtCGQ.Clear();
            try
            {
                rbtCGQ.Add(Convert.ToInt32(LabPE01.Tag), LabPE01);
                rbtCGQ.Add(Convert.ToInt32(LabPE02.Tag), LabPE02);
                rbtCGQ.Add(Convert.ToInt32(LabPE03.Tag), LabPE03);
                rbtCGQ.Add(Convert.ToInt32(LabPE04.Tag), LabPE04);
                rbtCGQ.Add(Convert.ToInt32(LabPE05.Tag), LabPE05);
                rbtCGQ.Add(Convert.ToInt32(LabPE06.Tag), LabPE06);
                rbtCGQ.Add(Convert.ToInt32(LabPE07.Tag), LabPE07);
                rbtCGQ.Add(Convert.ToInt32(LabPE08.Tag), LabPE08);
                rbtCGQ.Add(Convert.ToInt32(LabPE09.Tag), LabPE09);
                // 电压传感器
                rbtCGQ.Add(Convert.ToInt32(LabVoltageSensor1.Tag), LabVoltageSensor1);
                rbtCGQ.Add(Convert.ToInt32(LabVoltageSensor2.Tag), LabVoltageSensor2);
                rbtCGQ.Add(Convert.ToInt32(LabVoltageSensor3.Tag), LabVoltageSensor3);
            }
            catch (Exception ex)
            {
                NlogHelper.Default.Error("加载传感器信号控件异常！：", ex); ;
            }
        }
        private void LoaddicDO()
        {
            rbtDO.Clear();
            try
            {
                foreach (var item in grpDO.Controls)
                {
                    if (item is UISwitch)
                    {
                        UISwitch sp = item as UISwitch;
                        rbtDO.Add(Convert.ToInt32(sp.Tag), sp);
                    }
                }
            }
            catch (Exception ex)
            {
                NlogHelper.Default.Error("加载DO控制信号控件异常！：", ex); ;
            }
        }

        private void LoaddicDI()
        {
            rbtDI.Clear();
            try
            {
                //foreach (var item in grpDI.Controls)
                //{
                //    if (item is UILedBulb)
                //    {
                //        UILedBulb sp = item as UILedBulb;
                //        rbtDI.Add(Convert.ToInt32(sp.Tag), sp);
                //    }
                //}
            }
            catch (Exception ex)
            {
                NlogHelper.Default.Error("加载DI检测信号控件异常！：", ex); ;
            }
        }

        // DO值改变事件
        private void DOgrp_DOgrpChanged(object sender, int index, bool value)
        {
            if (rbtDCF.TryGetValue(index, out UIValve UIvalue))
            {
                UIvalue.Active = value;
            }
            if (rbtDO.TryGetValue(index, out UISwitch UISwitch))
            {
                UISwitch.Active = value;
            }
        }

        // AI值改变事件
        private void AIgrp_AIvalueGrpChanged(object sender, int index, double value)
        {
            if (rbtCGQ.TryGetValue(index, out UILabel UILabel))
            {
                UILabel.Text = value.ToString("f1");
            }
        }

        private void AOgrp_AOvalueGrpChaned(object sender, int index, double value)
        {
            switch (index)
            {
                case 0:
                    LabCurrentOut.Value = value;
                    break;
                case 1: break;
                case 2:
                    EP01.Text = value.ToString("f1");
                    break;
                case 3: break;
                case 4:
                    LabVoltageOut160V.Value = value;
                    break;
                case 5:
                    LabVoltageOut36V.Value = value;
                    break;
                default:
                    break;
            }
        }

        //DI模块事件
        private void DIgrp_DIGroupChanged(object sender, int index, bool value)
        {
            try
            {
                if (rbtDI.TryGetValue(index, out UILedBulb UILedBulb))
                {
                    UILedBulb.On = value;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("DI模块事件报错：" + ex.Message);
            }
        }

        double Maxset = 0;
        private void TimeSetting_Click(object sender, EventArgs e)
        {
            try
            {
                UIPanel lab = sender as UIPanel;
                Maxset = 1000; //EP阀设定值 上限
                using frmSetOutValue fs = new(0, "气压调节(kPa)", Maxset);
                VarHelper.ShowDialogWithOverlay(_form, fs);
                if (fs.DialogResult == DialogResult.OK)
                {
                    ControlHelper.ButtonClickAsync(sender, () =>
                    {
                        OPCHelper.AOgrp.CA02 = fs.OutValue;
                    });
                }
            }
            catch (Exception ex)
            {
                NlogHelper.Default.Error("调节EP阀设置输出时发生异常", ex);
            }
        }


        private void LabVoltageOut160V_ValueChanged(object sender, EventArgs e)
        {
            try
            {
                frmSetOutValue fs = new(0, "160V电压调节(V)", 160);
                VarHelper.ShowDialogWithOverlay(_form, fs);
                if (fs.DialogResult == DialogResult.OK)
                {
                    ControlHelper.ButtonClickAsync(sender, () =>
                    {
                        OPCHelper.AOgrp.CA04 = fs.OutValue;
                    });
                }
            }
            catch (Exception ex)
            {
                NlogHelper.Default.Error("调节电源设置输出时发生异常", ex);
            }
        }


        private void LabCurrentOut_Click(object sender, EventArgs e)
        {
            try
            {
                // 电流不得高于750mA以上。如果高于750mA以上，则试验品有可能被烧毁。
                frmSetOutValue fs = new(0, "36V电源输出电流控制(mA)", 750);
                VarHelper.ShowDialogWithOverlay(_form, fs);
                if (fs.DialogResult == DialogResult.OK)
                {
                    ControlHelper.ButtonClickAsync(sender, () =>
                    {
                        OPCHelper.AOgrp.CA00 = fs.OutValue;
                    });
                }
            }
            catch (Exception ex)
            {
                NlogHelper.Default.Error("调节电源设置输出时发生异常", ex);
            }
        }

        private void DCF_ActiveChanged(object sender, EventArgs e)
        {
            UIValve val = sender as UIValve;
            if (val.Active)
            {
                val.ValveColor = Color.LimeGreen;
            }
            else
            {
                val.ValveColor = Color.Gray;
            }
            OPCHelper.DOgrp[Convert.ToInt32(val.Tag)] = val.Active;
            //rbtDCF[Convert.ToInt32(val.Tag)].Active = val.Active;
        }

        private void DO_Click(object sender, EventArgs e)
        {
            UISwitch pic = sender as UISwitch;
            pic.Active = !pic.Active;
            OPCHelper.DOgrp[Convert.ToInt32(pic.Tag)] = !pic.Active;
        }

        private void btnRequestTest_Click(object sender, EventArgs e)
        {
            FrmInsulationWithstand frm = new();
            VarHelper.ShowDialogWithOverlay(_form, frm);
        }

        private void uiSwitch2_MouseDown(object sender, MouseEventArgs e)
        {
            if (OPCHelper.TestCongrp[0].ToBool())
            {
                MessageHelper.MessageOK("当前为自动模式，请切换到手动模式后进行操作", AntdUI.TType.Info);
                return;
            }
        }

        private void LabVoltageOut36V_Click(object sender, EventArgs e)
        {
            try
            {
                frmSetOutValue fs = new(0, "36V电源输出电压控制(V)", 36);
                VarHelper.ShowDialogWithOverlay(_form, fs);
                if (fs.DialogResult == DialogResult.OK)
                {
                    ControlHelper.ButtonClickAsync(sender, () =>
                    {
                        OPCHelper.AOgrp.CA05 = fs.OutValue;
                    });
                }
            }
            catch (Exception ex)
            {
                NlogHelper.Default.Error("调节电源设置输出时发生异常", ex);
            }
        }

    }
}
