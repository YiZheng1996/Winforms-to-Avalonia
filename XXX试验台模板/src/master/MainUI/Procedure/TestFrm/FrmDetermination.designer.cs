namespace MainUI
{
    partial class FrmDetermination
    {
        /// <summary>
        /// 必需的设计器变量。
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// 清理所有正在使用的资源。
        /// </summary>
        /// <param name="disposing">如果应释放托管资源，为 true；否则为 false。</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows 窗体设计器生成的代码

        /// <summary>
        /// 设计器支持所需的方法 - 不要
        /// 使用代码编辑器修改此方法的内容。
        /// </summary>
        private void InitializeComponent()
        {
            uiLabel1 = new UILabel();
            txtExhaustValve = new UITextBox();
            btnSubmit = new UIButton();
            uiPanel1 = new UIPanel();
            txtSupplyValve = new UITextBox();
            uiLabel2 = new UILabel();
            uiPanel1.SuspendLayout();
            SuspendLayout();
            // 
            // uiLabel1
            // 
            uiLabel1.AutoSize = true;
            uiLabel1.BackColor = Color.Transparent;
            uiLabel1.Font = new Font("微软雅黑", 12.75F, FontStyle.Bold, GraphicsUnit.Point, 134);
            uiLabel1.ForeColor = Color.FromArgb(43, 46, 57);
            uiLabel1.Location = new Point(68, 54);
            uiLabel1.Name = "uiLabel1";
            uiLabel1.Size = new Size(95, 24);
            uiLabel1.TabIndex = 68;
            uiLabel1.Text = "排气阀行程";
            uiLabel1.TextAlign = ContentAlignment.MiddleRight;
            // 
            // txtExhaustValve
            // 
            txtExhaustValve.BackColor = Color.Transparent;
            txtExhaustValve.FillColor = Color.FromArgb(218, 220, 230);
            txtExhaustValve.FillColor2 = Color.FromArgb(218, 220, 230);
            txtExhaustValve.FillDisableColor = Color.FromArgb(218, 220, 230);
            txtExhaustValve.FillReadOnlyColor = Color.FromArgb(218, 220, 230);
            txtExhaustValve.Font = new Font("思源黑体 CN Bold", 13F, FontStyle.Bold);
            txtExhaustValve.ForeDisableColor = Color.FromArgb(218, 220, 230);
            txtExhaustValve.ForeReadOnlyColor = Color.FromArgb(218, 220, 230);
            txtExhaustValve.Location = new Point(200, 54);
            txtExhaustValve.Margin = new Padding(4, 5, 4, 5);
            txtExhaustValve.MinimumSize = new Size(1, 16);
            txtExhaustValve.Name = "txtExhaustValve";
            txtExhaustValve.Padding = new Padding(5);
            txtExhaustValve.Radius = 10;
            txtExhaustValve.RectColor = Color.FromArgb(218, 220, 230);
            txtExhaustValve.RectDisableColor = Color.FromArgb(218, 220, 230);
            txtExhaustValve.RectReadOnlyColor = Color.FromArgb(218, 220, 230);
            txtExhaustValve.ShowText = false;
            txtExhaustValve.Size = new Size(210, 29);
            txtExhaustValve.TabIndex = 70;
            txtExhaustValve.TextAlignment = ContentAlignment.MiddleLeft;
            txtExhaustValve.Watermark = "请输入";
            // 
            // btnSubmit
            // 
            btnSubmit.BackColor = Color.Transparent;
            btnSubmit.Cursor = Cursors.Hand;
            btnSubmit.FillDisableColor = Color.FromArgb(80, 160, 255);
            btnSubmit.Font = new Font("思源黑体 CN Bold", 13F, FontStyle.Bold);
            btnSubmit.Location = new Point(190, 169);
            btnSubmit.MinimumSize = new Size(1, 1);
            btnSubmit.Name = "btnSubmit";
            btnSubmit.Radius = 10;
            btnSubmit.RectDisableColor = Color.FromArgb(80, 160, 255);
            btnSubmit.Size = new Size(138, 37);
            btnSubmit.TabIndex = 397;
            btnSubmit.Text = "确定";
            btnSubmit.TipsFont = new Font("微软雅黑", 9F, FontStyle.Regular, GraphicsUnit.Point, 134);
            btnSubmit.TipsText = "1";
            btnSubmit.Click += BtnSubmit_Click;
            // 
            // uiPanel1
            // 
            uiPanel1.Controls.Add(uiLabel1);
            uiPanel1.Controls.Add(txtSupplyValve);
            uiPanel1.Controls.Add(txtExhaustValve);
            uiPanel1.Controls.Add(btnSubmit);
            uiPanel1.Controls.Add(uiLabel2);
            uiPanel1.FillColor = Color.White;
            uiPanel1.FillColor2 = Color.White;
            uiPanel1.FillDisableColor = Color.FromArgb(49, 54, 64);
            uiPanel1.Font = new Font("宋体", 12F);
            uiPanel1.ForeColor = Color.FromArgb(49, 54, 64);
            uiPanel1.ForeDisableColor = Color.FromArgb(49, 54, 64);
            uiPanel1.Location = new Point(30, 55);
            uiPanel1.Margin = new Padding(4, 5, 4, 5);
            uiPanel1.MinimumSize = new Size(1, 1);
            uiPanel1.Name = "uiPanel1";
            uiPanel1.Radius = 15;
            uiPanel1.RectColor = Color.White;
            uiPanel1.RectDisableColor = Color.White;
            uiPanel1.Size = new Size(519, 236);
            uiPanel1.TabIndex = 408;
            uiPanel1.Text = null;
            uiPanel1.TextAlignment = ContentAlignment.MiddleCenter;
            // 
            // txtSupplyValve
            // 
            txtSupplyValve.BackColor = Color.Transparent;
            txtSupplyValve.Cursor = Cursors.IBeam;
            txtSupplyValve.FillColor = Color.FromArgb(218, 220, 230);
            txtSupplyValve.FillColor2 = Color.FromArgb(218, 220, 230);
            txtSupplyValve.FillDisableColor = Color.FromArgb(42, 47, 55);
            txtSupplyValve.FillReadOnlyColor = Color.FromArgb(42, 47, 55);
            txtSupplyValve.Font = new Font("思源黑体 CN Bold", 13F, FontStyle.Bold);
            txtSupplyValve.ForeDisableColor = Color.FromArgb(235, 227, 221);
            txtSupplyValve.ForeReadOnlyColor = Color.FromArgb(235, 227, 221);
            txtSupplyValve.Location = new Point(200, 102);
            txtSupplyValve.Margin = new Padding(4, 5, 4, 5);
            txtSupplyValve.MinimumSize = new Size(1, 16);
            txtSupplyValve.Name = "txtSupplyValve";
            txtSupplyValve.Padding = new Padding(5);
            txtSupplyValve.Radius = 10;
            txtSupplyValve.RectColor = Color.FromArgb(218, 220, 230);
            txtSupplyValve.RectDisableColor = Color.FromArgb(218, 220, 230);
            txtSupplyValve.RectReadOnlyColor = Color.FromArgb(218, 220, 230);
            txtSupplyValve.ShowText = false;
            txtSupplyValve.Size = new Size(210, 29);
            txtSupplyValve.TabIndex = 69;
            txtSupplyValve.TextAlignment = ContentAlignment.MiddleLeft;
            txtSupplyValve.Watermark = "请输入";
            // 
            // uiLabel2
            // 
            uiLabel2.AutoSize = true;
            uiLabel2.BackColor = Color.Transparent;
            uiLabel2.Font = new Font("微软雅黑", 12.75F, FontStyle.Bold);
            uiLabel2.ForeColor = Color.FromArgb(43, 46, 57);
            uiLabel2.ImeMode = ImeMode.NoControl;
            uiLabel2.Location = new Point(68, 102);
            uiLabel2.Name = "uiLabel2";
            uiLabel2.Size = new Size(95, 24);
            uiLabel2.TabIndex = 73;
            uiLabel2.Text = "供给阀行程";
            uiLabel2.TextAlign = ContentAlignment.MiddleRight;
            // 
            // FrmDetermination
            // 
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Color.FromArgb(236, 236, 236);
            ClientSize = new Size(577, 309);
            ControlBox = false;
            Controls.Add(uiPanel1);
            Font = new Font("微软雅黑", 11F);
            ForeColor = Color.FromArgb(236, 236, 236);
            Margin = new Padding(3, 4, 3, 4);
            MaximizeBox = false;
            MaximumSize = new Size(0, 0);
            MinimizeBox = false;
            Name = "FrmDetermination";
            RectColor = Color.FromArgb(65, 100, 204);
            ShowIcon = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            Text = "阀升程调整";
            TitleColor = Color.FromArgb(65, 100, 204);
            TitleFont = new Font("微软雅黑", 15F, FontStyle.Bold);
            TitleForeColor = Color.FromArgb(236, 236, 236);
            ZoomScaleRect = new Rectangle(15, 15, 294, 282);
            uiPanel1.ResumeLayout(false);
            uiPanel1.PerformLayout();
            ResumeLayout(false);
        }

        #endregion
        private Sunny.UI.UILabel uiLabel1;
        private Sunny.UI.UITextBox txtExhaustValve;
        private Sunny.UI.UIButton btnSubmit;
        private UIPanel uiPanel1;
        private UITextBox txtSupplyValve;
        private UILabel uiLabel2;
    }
}