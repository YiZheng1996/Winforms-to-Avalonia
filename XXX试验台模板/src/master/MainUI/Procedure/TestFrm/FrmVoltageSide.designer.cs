namespace MainUI
{
    partial class FrmVoltageSide
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
            btnSubmit = new UIButton();
            uiPanel15 = new UIPanel();
            LabPE03 = new UIDigitalLabel();
            uiPanel22 = new UIPanel();
            LabPromptMessage = new UILabel();
            uiPanel15.SuspendLayout();
            SuspendLayout();
            // 
            // btnSubmit
            // 
            btnSubmit.BackColor = Color.Transparent;
            btnSubmit.Cursor = Cursors.Hand;
            btnSubmit.FillDisableColor = Color.FromArgb(80, 160, 255);
            btnSubmit.Font = new Font("思源黑体 CN Bold", 13F, FontStyle.Bold);
            btnSubmit.Location = new Point(213, 249);
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
            // uiPanel15
            // 
            uiPanel15.Controls.Add(LabPE03);
            uiPanel15.FillColor = Color.White;
            uiPanel15.Font = new Font("宋体", 12F, FontStyle.Regular, GraphicsUnit.Point, 134);
            uiPanel15.Location = new Point(43, 91);
            uiPanel15.Margin = new Padding(4, 5, 4, 5);
            uiPanel15.MinimumSize = new Size(1, 1);
            uiPanel15.Name = "uiPanel15";
            uiPanel15.RectColor = Color.White;
            uiPanel15.Size = new Size(160, 68);
            uiPanel15.TabIndex = 663;
            uiPanel15.Text = null;
            uiPanel15.TextAlignment = ContentAlignment.MiddleCenter;
            // 
            // LabPE03
            // 
            LabPE03.BackColor = Color.FromArgb(236, 236, 237);
            LabPE03.DecimalPlaces = 1;
            LabPE03.Font = new Font("宋体", 12F, FontStyle.Regular, GraphicsUnit.Point, 134);
            LabPE03.ForeColor = Color.DarkOliveGreen;
            LabPE03.Location = new Point(3, 3);
            LabPE03.MinimumSize = new Size(1, 1);
            LabPE03.Name = "LabPE03";
            LabPE03.RectSize = 2;
            LabPE03.Size = new Size(154, 63);
            LabPE03.TabIndex = 518;
            LabPE03.Tag = "4";
            LabPE03.Text = "uiDigitalLabel2";
            LabPE03.TextAlign = HorizontalAlignment.Center;
            LabPE03.Value = 1000D;
            // 
            // uiPanel22
            // 
            uiPanel22.FillColor = Color.FromArgb(236, 236, 237);
            uiPanel22.Font = new Font("微软雅黑", 12F, FontStyle.Regular, GraphicsUnit.Point, 134);
            uiPanel22.Location = new Point(43, 161);
            uiPanel22.Margin = new Padding(4, 5, 4, 5);
            uiPanel22.MinimumSize = new Size(1, 1);
            uiPanel22.Name = "uiPanel22";
            uiPanel22.Radius = 0;
            uiPanel22.RectColor = Color.FromArgb(65, 100, 204);
            uiPanel22.Size = new Size(160, 31);
            uiPanel22.TabIndex = 664;
            uiPanel22.Text = "PE05(kPa)";
            uiPanel22.TextAlignment = ContentAlignment.MiddleCenter;
            // 
            // LabPromptMessage
            // 
            LabPromptMessage.BackColor = Color.White;
            LabPromptMessage.BorderStyle = BorderStyle.FixedSingle;
            LabPromptMessage.Font = new Font("微软雅黑", 12F, FontStyle.Bold, GraphicsUnit.Point, 134);
            LabPromptMessage.ForeColor = Color.Red;
            LabPromptMessage.Location = new Point(233, 76);
            LabPromptMessage.Name = "LabPromptMessage";
            LabPromptMessage.Size = new Size(300, 137);
            LabPromptMessage.TabIndex = 665;
            LabPromptMessage.Text = "转动高压侧调整螺钉（下侧）来进行调整";
            LabPromptMessage.TextAlign = ContentAlignment.MiddleCenter;
            // 
            // FrmVoltageSide
            // 
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Color.FromArgb(236, 236, 236);
            ClientSize = new Size(577, 320);
            ControlBox = false;
            Controls.Add(LabPromptMessage);
            Controls.Add(uiPanel22);
            Controls.Add(uiPanel15);
            Controls.Add(btnSubmit);
            Font = new Font("微软雅黑", 11F);
            ForeColor = Color.FromArgb(236, 236, 236);
            Margin = new Padding(3, 4, 3, 4);
            MaximizeBox = false;
            MaximumSize = new Size(0, 0);
            MinimizeBox = false;
            Name = "FrmVoltageSide";
            RectColor = Color.FromArgb(65, 100, 204);
            ShowIcon = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            Text = "调压试验";
            TitleColor = Color.FromArgb(65, 100, 204);
            TitleFont = new Font("微软雅黑", 15F, FontStyle.Bold);
            TitleForeColor = Color.FromArgb(236, 236, 236);
            ZoomScaleRect = new Rectangle(15, 15, 294, 282);
            Load += FrmVoltageSide_Load;
            uiPanel15.ResumeLayout(false);
            ResumeLayout(false);
        }

        #endregion
        private Sunny.UI.UIButton btnSubmit;
        private UIPanel uiPanel15;
        private UIDigitalLabel LabPE03;
        private UIPanel uiPanel22;
        private UILabel LabPromptMessage;
    }
}