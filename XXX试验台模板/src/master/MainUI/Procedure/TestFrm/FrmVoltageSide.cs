namespace MainUI
{
    public partial class FrmVoltageSide : UIForm
    {
        public FrmVoltageSide()
        {
            InitializeComponent();
        }

        /// <summary>
        /// 提示消息
        /// </summary>
        public string PromptMessage
        {
            get => LabPromptMessage.Text;
            set => LabPromptMessage.Text = value;
        }

        private void BtnSubmit_Click(object sender, EventArgs e)
        {
            var Result = MessageHelper.MessageYes(this, "确认提交当前压力值？", AntdUI.TType.Warn);
            if (Result != DialogResult.OK)
            {
                return;
            }
            DialogResult = DialogResult.OK;
        }

        private void FrmVoltageSide_Load(object sender, EventArgs e)
        {
            OPCHelper.AIgrp.AIvalueGrpChanged += AIgrp_AIvalueGrpChanged;
        }

        private void AIgrp_AIvalueGrpChanged(object sender, int index, double value)
        {
            // PE05压力值
            if (index == 4)
            {
                LabPE03.Value = Math.Round(value, 1);
            }
        }
    }
}