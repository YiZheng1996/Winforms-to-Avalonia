namespace MainUI
{
    public partial class FrmDetermination : UIForm
    {
        public FrmDetermination()
        {
            InitializeComponent();
        }

        /// <summary>
        /// 排气阀行程
        /// </summary>
        public string ExhaustValve
        {
            get => txtExhaustValve.Text;
            set => txtExhaustValve.Text = value;
        }

        /// <summary>
        /// 供给阀行程
        /// </summary>
        public string SupplyValve
        {
            get => txtSupplyValve.Text;
            set => txtSupplyValve.Text = value;
        }

        private void BtnSubmit_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(ExhaustValve))
            {
                MessageHelper.MessageOK(this, "请填写排气阀行程！", AntdUI.TType.Error);
                return;
            }

            if (string.IsNullOrEmpty(SupplyValve))
            {
                MessageHelper.MessageOK(this, "请填写供给阀行程！", AntdUI.TType.Error);
                return;
            }

            DialogResult = DialogResult.OK;
        }

    }
}