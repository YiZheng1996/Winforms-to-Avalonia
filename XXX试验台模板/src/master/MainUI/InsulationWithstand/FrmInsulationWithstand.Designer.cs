namespace MainUI.InsulationWithstand
{
    partial class FrmInsulationWithstand
    {
        private System.ComponentModel.IContainer components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        private void InitializeComponent()
        {
            dividerTop = new AntdUI.Divider();
            panelConnection = new AntdUI.Panel();
            lblConnectionTitle = new AntdUI.Label();
            lblServerIp = new AntdUI.Label();
            txtServerIp = new AntdUI.Input();
            lblServerPort = new AntdUI.Label();
            txtServerPort = new AntdUI.Input();
            btnConnect = new AntdUI.Button();
            lblConnectionTip = new AntdUI.Label();
            panelTestControl = new AntdUI.Panel();
            lblTestControlTitle = new AntdUI.Label();
            lblTestType = new AntdUI.Label();
            rbtnInsulationWithstand = new AntdUI.Radio();
            rbtnInsulationResistance = new AntdUI.Radio();
            rbtnResistance = new AntdUI.Radio();
            panelInsulationWithstandParams = new AntdUI.Panel();
            lblWithstandParamsTitle = new AntdUI.Label();
            lblWithstandVoltage = new AntdUI.Label();
            txtWithstandVoltage = new AntdUI.Input();
            lblWithstandDuration = new AntdUI.Label();
            txtWithstandDuration = new AntdUI.Input();
            lblWithstandLimit = new AntdUI.Label();
            txtWithstandLimit = new AntdUI.Input();
            panelInsulationResistanceParams = new AntdUI.Panel();
            lblResistanceParamsTitle = new AntdUI.Label();
            lblResistanceVoltage = new AntdUI.Label();
            txtResistanceVoltage = new AntdUI.Input();
            lblResistanceDuration = new AntdUI.Label();
            txtResistanceDuration = new AntdUI.Input();
            lblResistanceLimit = new AntdUI.Label();
            txtResistanceLimit = new AntdUI.Input();
            panelResistanceParams = new AntdUI.Panel();
            lblResistanceTestParamsTitle = new AntdUI.Label();
            lblLowResistanceLimit = new AntdUI.Label();
            txtLowResistanceLimit = new AntdUI.Input();
            btnRequestTest = new AntdUI.Button();
            btnSendTestParameters = new AntdUI.Button();
            btnEndTest = new AntdUI.Button();
            btnCancelTest = new AntdUI.Button();
            panelStatus = new AntdUI.Panel();
            lblStatusTitle = new AntdUI.Label();
            txtStatus = new AntdUI.Input();
            panelConnection.SuspendLayout();
            panelTestControl.SuspendLayout();
            panelInsulationWithstandParams.SuspendLayout();
            panelInsulationResistanceParams.SuspendLayout();
            panelResistanceParams.SuspendLayout();
            panelStatus.SuspendLayout();
            SuspendLayout();
            // 
            // dividerTop
            // 
            dividerTop.Dock = DockStyle.Top;
            dividerTop.Location = new Point(0, 35);
            dividerTop.Name = "dividerTop";
            dividerTop.Size = new Size(1059, 1);
            dividerTop.TabIndex = 2;
            // 
            // panelConnection
            // 
            panelConnection.Back = Color.White;
            panelConnection.BorderColor = Color.FromArgb(240, 240, 240);
            panelConnection.BorderWidth = 1F;
            panelConnection.Controls.Add(lblConnectionTitle);
            panelConnection.Controls.Add(lblServerIp);
            panelConnection.Controls.Add(txtServerIp);
            panelConnection.Controls.Add(lblServerPort);
            panelConnection.Controls.Add(txtServerPort);
            panelConnection.Controls.Add(btnConnect);
            panelConnection.Controls.Add(lblConnectionTip);
            panelConnection.Location = new Point(20, 53);
            panelConnection.Name = "panelConnection";
            panelConnection.Radius = 8;
            panelConnection.Shadow = 10;
            panelConnection.Size = new Size(1023, 120);
            panelConnection.TabIndex = 3;
            // 
            // lblConnectionTitle
            // 
            lblConnectionTitle.BackColor = Color.Transparent;
            lblConnectionTitle.Font = new Font("Microsoft YaHei UI", 12F, FontStyle.Bold);
            lblConnectionTitle.ForeColor = Color.FromArgb(51, 51, 51);
            lblConnectionTitle.Location = new Point(20, 15);
            lblConnectionTitle.Name = "lblConnectionTitle";
            lblConnectionTitle.Size = new Size(200, 30);
            lblConnectionTitle.TabIndex = 0;
            lblConnectionTitle.Text = "服务器连接";
            // 
            // lblServerIp
            // 
            lblServerIp.BackColor = Color.Transparent;
            lblServerIp.Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold);
            lblServerIp.Location = new Point(30, 59);
            lblServerIp.Name = "lblServerIp";
            lblServerIp.Size = new Size(80, 30);
            lblServerIp.TabIndex = 1;
            lblServerIp.Text = "IP地址:";
            lblServerIp.TextAlign = ContentAlignment.MiddleRight;
            // 
            // txtServerIp
            // 
            txtServerIp.Font = new Font("Microsoft YaHei UI", 10F);
            txtServerIp.Location = new Point(120, 55);
            txtServerIp.Name = "txtServerIp";
            txtServerIp.PlaceholderText = "请输入服务器IP";
            txtServerIp.Size = new Size(180, 40);
            txtServerIp.TabIndex = 2;
            txtServerIp.Text = "192.168.1.199";
            // 
            // lblServerPort
            // 
            lblServerPort.BackColor = Color.Transparent;
            lblServerPort.Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold);
            lblServerPort.Location = new Point(330, 60);
            lblServerPort.Name = "lblServerPort";
            lblServerPort.Size = new Size(80, 30);
            lblServerPort.TabIndex = 3;
            lblServerPort.Text = "端口号:";
            lblServerPort.TextAlign = ContentAlignment.MiddleRight;
            // 
            // txtServerPort
            // 
            txtServerPort.Font = new Font("Microsoft YaHei UI", 10F);
            txtServerPort.Location = new Point(420, 55);
            txtServerPort.Name = "txtServerPort";
            txtServerPort.PlaceholderText = "端口";
            txtServerPort.Size = new Size(120, 40);
            txtServerPort.TabIndex = 4;
            txtServerPort.Text = "8888";
            // 
            // btnConnect
            // 
            btnConnect.Font = new Font("Microsoft YaHei UI", 10F);
            btnConnect.Location = new Point(566, 56);
            btnConnect.Name = "btnConnect";
            btnConnect.Size = new Size(120, 40);
            btnConnect.TabIndex = 5;
            btnConnect.Text = "连接服务器";
            btnConnect.Type = AntdUI.TTypeMini.Primary;
            btnConnect.Click += btnConnect_Click;
            // 
            // lblConnectionTip
            // 
            lblConnectionTip.Font = new Font("Microsoft YaHei UI", 10F);
            lblConnectionTip.ForeColor = Color.Red;
            lblConnectionTip.Location = new Point(720, 60);
            lblConnectionTip.Name = "lblConnectionTip";
            lblConnectionTip.Size = new Size(270, 30);
            lblConnectionTip.TabIndex = 6;
            lblConnectionTip.Text = "提示: 断开连接将中止当前试验";
            // 
            // panelTestControl
            // 
            panelTestControl.Back = Color.White;
            panelTestControl.BorderColor = Color.FromArgb(240, 240, 240);
            panelTestControl.BorderWidth = 1F;
            panelTestControl.Controls.Add(lblTestControlTitle);
            panelTestControl.Controls.Add(lblTestType);
            panelTestControl.Controls.Add(rbtnInsulationWithstand);
            panelTestControl.Controls.Add(rbtnInsulationResistance);
            panelTestControl.Controls.Add(rbtnResistance);
            panelTestControl.Controls.Add(panelInsulationWithstandParams);
            panelTestControl.Controls.Add(panelInsulationResistanceParams);
            panelTestControl.Controls.Add(panelResistanceParams);
            panelTestControl.Controls.Add(btnRequestTest);
            panelTestControl.Controls.Add(btnSendTestParameters);
            panelTestControl.Controls.Add(btnEndTest);
            panelTestControl.Controls.Add(btnCancelTest);
            panelTestControl.Location = new Point(20, 168);
            panelTestControl.Name = "panelTestControl";
            panelTestControl.Radius = 8;
            panelTestControl.Shadow = 10;
            panelTestControl.Size = new Size(1023, 420);
            panelTestControl.TabIndex = 4;
            // 
            // lblTestControlTitle
            // 
            lblTestControlTitle.BackColor = Color.Transparent;
            lblTestControlTitle.Font = new Font("Microsoft YaHei UI", 12F, FontStyle.Bold);
            lblTestControlTitle.ForeColor = Color.FromArgb(51, 51, 51);
            lblTestControlTitle.Location = new Point(20, 15);
            lblTestControlTitle.Name = "lblTestControlTitle";
            lblTestControlTitle.Size = new Size(200, 30);
            lblTestControlTitle.TabIndex = 0;
            lblTestControlTitle.Text = "试验控制";
            // 
            // lblTestType
            // 
            lblTestType.BackColor = Color.Transparent;
            lblTestType.Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold);
            lblTestType.Location = new Point(30, 55);
            lblTestType.Name = "lblTestType";
            lblTestType.Size = new Size(100, 30);
            lblTestType.TabIndex = 1;
            lblTestType.Text = "试验类型:";
            lblTestType.TextAlign = ContentAlignment.MiddleRight;
            // 
            // rbtnInsulationWithstand
            // 
            rbtnInsulationWithstand.BackColor = Color.Transparent;
            rbtnInsulationWithstand.Checked = true;
            rbtnInsulationWithstand.Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold);
            rbtnInsulationWithstand.Location = new Point(140, 55);
            rbtnInsulationWithstand.Name = "rbtnInsulationWithstand";
            rbtnInsulationWithstand.Size = new Size(120, 30);
            rbtnInsulationWithstand.TabIndex = 2;
            rbtnInsulationWithstand.Text = "绝缘耐压";
            rbtnInsulationWithstand.CheckedChanged += rbtnTestType_CheckedChanged;
            // 
            // rbtnInsulationResistance
            // 
            rbtnInsulationResistance.BackColor = Color.Transparent;
            rbtnInsulationResistance.Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold);
            rbtnInsulationResistance.Location = new Point(280, 55);
            rbtnInsulationResistance.Name = "rbtnInsulationResistance";
            rbtnInsulationResistance.Size = new Size(120, 30);
            rbtnInsulationResistance.TabIndex = 3;
            rbtnInsulationResistance.Text = "绝缘电阻";
            rbtnInsulationResistance.CheckedChanged += rbtnTestType_CheckedChanged;
            // 
            // rbtnResistance
            // 
            rbtnResistance.BackColor = Color.Transparent;
            rbtnResistance.Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold);
            rbtnResistance.Location = new Point(420, 55);
            rbtnResistance.Name = "rbtnResistance";
            rbtnResistance.Size = new Size(120, 30);
            rbtnResistance.TabIndex = 4;
            rbtnResistance.Text = "电阻测试";
            rbtnResistance.CheckedChanged += rbtnTestType_CheckedChanged;
            // 
            // panelInsulationWithstandParams
            // 
            panelInsulationWithstandParams.Back = Color.FromArgb(250, 250, 250);
            panelInsulationWithstandParams.BorderColor = Color.FromArgb(230, 230, 230);
            panelInsulationWithstandParams.BorderWidth = 1F;
            panelInsulationWithstandParams.Controls.Add(lblWithstandParamsTitle);
            panelInsulationWithstandParams.Controls.Add(lblWithstandVoltage);
            panelInsulationWithstandParams.Controls.Add(txtWithstandVoltage);
            panelInsulationWithstandParams.Controls.Add(lblWithstandDuration);
            panelInsulationWithstandParams.Controls.Add(txtWithstandDuration);
            panelInsulationWithstandParams.Controls.Add(lblWithstandLimit);
            panelInsulationWithstandParams.Controls.Add(txtWithstandLimit);
            panelInsulationWithstandParams.Location = new Point(30, 100);
            panelInsulationWithstandParams.Name = "panelInsulationWithstandParams";
            panelInsulationWithstandParams.Size = new Size(960, 110);
            panelInsulationWithstandParams.TabIndex = 5;
            // 
            // lblWithstandParamsTitle
            // 
            lblWithstandParamsTitle.BackColor = Color.Transparent;
            lblWithstandParamsTitle.Font = new Font("Microsoft YaHei UI", 11F, FontStyle.Bold);
            lblWithstandParamsTitle.ForeColor = Color.FromArgb(22, 119, 255);
            lblWithstandParamsTitle.Location = new Point(15, 10);
            lblWithstandParamsTitle.Name = "lblWithstandParamsTitle";
            lblWithstandParamsTitle.Size = new Size(150, 25);
            lblWithstandParamsTitle.TabIndex = 0;
            lblWithstandParamsTitle.Text = "绝缘耐压参数";
            // 
            // lblWithstandVoltage
            // 
            lblWithstandVoltage.BackColor = Color.Transparent;
            lblWithstandVoltage.Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold);
            lblWithstandVoltage.Location = new Point(30, 50);
            lblWithstandVoltage.Name = "lblWithstandVoltage";
            lblWithstandVoltage.Size = new Size(80, 30);
            lblWithstandVoltage.TabIndex = 1;
            lblWithstandVoltage.Text = "电压(V):";
            lblWithstandVoltage.TextAlign = ContentAlignment.MiddleRight;
            // 
            // txtWithstandVoltage
            // 
            txtWithstandVoltage.Font = new Font("Microsoft YaHei UI", 10F);
            txtWithstandVoltage.Location = new Point(120, 46);
            txtWithstandVoltage.Name = "txtWithstandVoltage";
            txtWithstandVoltage.PlaceholderText = "电压";
            txtWithstandVoltage.Size = new Size(120, 40);
            txtWithstandVoltage.TabIndex = 2;
            txtWithstandVoltage.Text = "1000";
            // 
            // lblWithstandDuration
            // 
            lblWithstandDuration.BackColor = Color.Transparent;
            lblWithstandDuration.Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold);
            lblWithstandDuration.Location = new Point(270, 50);
            lblWithstandDuration.Name = "lblWithstandDuration";
            lblWithstandDuration.Size = new Size(80, 30);
            lblWithstandDuration.TabIndex = 3;
            lblWithstandDuration.Text = "时间(S):";
            lblWithstandDuration.TextAlign = ContentAlignment.MiddleRight;
            // 
            // txtWithstandDuration
            // 
            txtWithstandDuration.Font = new Font("Microsoft YaHei UI", 10F);
            txtWithstandDuration.Location = new Point(360, 46);
            txtWithstandDuration.Name = "txtWithstandDuration";
            txtWithstandDuration.PlaceholderText = "时间";
            txtWithstandDuration.Size = new Size(120, 40);
            txtWithstandDuration.TabIndex = 4;
            txtWithstandDuration.Text = "10";
            // 
            // lblWithstandLimit
            // 
            lblWithstandLimit.BackColor = Color.Transparent;
            lblWithstandLimit.Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold);
            lblWithstandLimit.Location = new Point(510, 50);
            lblWithstandLimit.Name = "lblWithstandLimit";
            lblWithstandLimit.Size = new Size(90, 30);
            lblWithstandLimit.TabIndex = 5;
            lblWithstandLimit.Text = "限值(mA):";
            lblWithstandLimit.TextAlign = ContentAlignment.MiddleRight;
            // 
            // txtWithstandLimit
            // 
            txtWithstandLimit.Font = new Font("Microsoft YaHei UI", 10F);
            txtWithstandLimit.Location = new Point(610, 46);
            txtWithstandLimit.Name = "txtWithstandLimit";
            txtWithstandLimit.PlaceholderText = "限值";
            txtWithstandLimit.Size = new Size(120, 40);
            txtWithstandLimit.TabIndex = 6;
            txtWithstandLimit.Text = "10";
            // 
            // panelInsulationResistanceParams
            // 
            panelInsulationResistanceParams.Back = Color.FromArgb(250, 250, 250);
            panelInsulationResistanceParams.BackColor = Color.Transparent;
            panelInsulationResistanceParams.BorderColor = Color.FromArgb(230, 230, 230);
            panelInsulationResistanceParams.BorderWidth = 1F;
            panelInsulationResistanceParams.Controls.Add(lblResistanceParamsTitle);
            panelInsulationResistanceParams.Controls.Add(lblResistanceVoltage);
            panelInsulationResistanceParams.Controls.Add(txtResistanceVoltage);
            panelInsulationResistanceParams.Controls.Add(lblResistanceDuration);
            panelInsulationResistanceParams.Controls.Add(txtResistanceDuration);
            panelInsulationResistanceParams.Controls.Add(lblResistanceLimit);
            panelInsulationResistanceParams.Controls.Add(txtResistanceLimit);
            panelInsulationResistanceParams.Location = new Point(30, 220);
            panelInsulationResistanceParams.Name = "panelInsulationResistanceParams";
            panelInsulationResistanceParams.Size = new Size(960, 110);
            panelInsulationResistanceParams.TabIndex = 6;
            // 
            // lblResistanceParamsTitle
            // 
            lblResistanceParamsTitle.BackColor = Color.Transparent;
            lblResistanceParamsTitle.Font = new Font("Microsoft YaHei UI", 11F, FontStyle.Bold);
            lblResistanceParamsTitle.ForeColor = Color.FromArgb(22, 119, 255);
            lblResistanceParamsTitle.Location = new Point(15, 10);
            lblResistanceParamsTitle.Name = "lblResistanceParamsTitle";
            lblResistanceParamsTitle.Size = new Size(150, 25);
            lblResistanceParamsTitle.TabIndex = 0;
            lblResistanceParamsTitle.Text = "绝缘电阻参数";
            // 
            // lblResistanceVoltage
            // 
            lblResistanceVoltage.BackColor = Color.Transparent;
            lblResistanceVoltage.Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold);
            lblResistanceVoltage.Location = new Point(30, 50);
            lblResistanceVoltage.Name = "lblResistanceVoltage";
            lblResistanceVoltage.Size = new Size(80, 30);
            lblResistanceVoltage.TabIndex = 1;
            lblResistanceVoltage.Text = "电压(V):";
            lblResistanceVoltage.TextAlign = ContentAlignment.MiddleRight;
            // 
            // txtResistanceVoltage
            // 
            txtResistanceVoltage.Font = new Font("Microsoft YaHei UI", 10F);
            txtResistanceVoltage.Location = new Point(120, 44);
            txtResistanceVoltage.Name = "txtResistanceVoltage";
            txtResistanceVoltage.PlaceholderText = "电压";
            txtResistanceVoltage.Size = new Size(120, 40);
            txtResistanceVoltage.TabIndex = 2;
            txtResistanceVoltage.Text = "500";
            // 
            // lblResistanceDuration
            // 
            lblResistanceDuration.BackColor = Color.Transparent;
            lblResistanceDuration.Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold);
            lblResistanceDuration.Location = new Point(270, 50);
            lblResistanceDuration.Name = "lblResistanceDuration";
            lblResistanceDuration.Size = new Size(80, 30);
            lblResistanceDuration.TabIndex = 3;
            lblResistanceDuration.Text = "时间(S):";
            lblResistanceDuration.TextAlign = ContentAlignment.MiddleRight;
            // 
            // txtResistanceDuration
            // 
            txtResistanceDuration.Font = new Font("Microsoft YaHei UI", 10F);
            txtResistanceDuration.Location = new Point(360, 44);
            txtResistanceDuration.Name = "txtResistanceDuration";
            txtResistanceDuration.PlaceholderText = "时间";
            txtResistanceDuration.Size = new Size(120, 40);
            txtResistanceDuration.TabIndex = 4;
            txtResistanceDuration.Text = "10";
            // 
            // lblResistanceLimit
            // 
            lblResistanceLimit.BackColor = Color.Transparent;
            lblResistanceLimit.Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold);
            lblResistanceLimit.Location = new Point(510, 50);
            lblResistanceLimit.Name = "lblResistanceLimit";
            lblResistanceLimit.Size = new Size(90, 30);
            lblResistanceLimit.TabIndex = 5;
            lblResistanceLimit.Text = "限值(MΩ):";
            lblResistanceLimit.TextAlign = ContentAlignment.MiddleRight;
            // 
            // txtResistanceLimit
            // 
            txtResistanceLimit.Font = new Font("Microsoft YaHei UI", 10F);
            txtResistanceLimit.Location = new Point(610, 44);
            txtResistanceLimit.Name = "txtResistanceLimit";
            txtResistanceLimit.PlaceholderText = "限值";
            txtResistanceLimit.Size = new Size(120, 40);
            txtResistanceLimit.TabIndex = 6;
            txtResistanceLimit.Text = "1000";
            // 
            // panelResistanceParams
            // 
            panelResistanceParams.Back = Color.FromArgb(250, 250, 250);
            panelResistanceParams.BackColor = Color.Transparent;
            panelResistanceParams.BorderColor = Color.FromArgb(230, 230, 230);
            panelResistanceParams.BorderWidth = 1F;
            panelResistanceParams.Controls.Add(lblResistanceTestParamsTitle);
            panelResistanceParams.Controls.Add(lblLowResistanceLimit);
            panelResistanceParams.Controls.Add(txtLowResistanceLimit);
            panelResistanceParams.Location = new Point(30, 100);
            panelResistanceParams.Name = "panelResistanceParams";
            panelResistanceParams.Size = new Size(960, 110);
            panelResistanceParams.TabIndex = 7;
            panelResistanceParams.Visible = false;
            // 
            // lblResistanceTestParamsTitle
            // 
            lblResistanceTestParamsTitle.BackColor = Color.Transparent;
            lblResistanceTestParamsTitle.Font = new Font("Microsoft YaHei UI", 11F, FontStyle.Bold);
            lblResistanceTestParamsTitle.ForeColor = Color.FromArgb(22, 119, 255);
            lblResistanceTestParamsTitle.Location = new Point(15, 10);
            lblResistanceTestParamsTitle.Name = "lblResistanceTestParamsTitle";
            lblResistanceTestParamsTitle.Size = new Size(150, 25);
            lblResistanceTestParamsTitle.TabIndex = 0;
            lblResistanceTestParamsTitle.Text = "电阻测试参数";
            // 
            // lblLowResistanceLimit
            // 
            lblLowResistanceLimit.BackColor = Color.Transparent;
            lblLowResistanceLimit.Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold);
            lblLowResistanceLimit.Location = new Point(30, 50);
            lblLowResistanceLimit.Name = "lblLowResistanceLimit";
            lblLowResistanceLimit.Size = new Size(80, 30);
            lblLowResistanceLimit.TabIndex = 1;
            lblLowResistanceLimit.Text = "限值(Ω):";
            lblLowResistanceLimit.TextAlign = ContentAlignment.MiddleRight;
            // 
            // txtLowResistanceLimit
            // 
            txtLowResistanceLimit.Font = new Font("Microsoft YaHei UI", 10F);
            txtLowResistanceLimit.Location = new Point(120, 45);
            txtLowResistanceLimit.Name = "txtLowResistanceLimit";
            txtLowResistanceLimit.PlaceholderText = "限值";
            txtLowResistanceLimit.Size = new Size(120, 40);
            txtLowResistanceLimit.TabIndex = 2;
            txtLowResistanceLimit.Text = "100";
            // 
            // btnRequestTest
            // 
            btnRequestTest.Font = new Font("Microsoft YaHei UI", 10F);
            btnRequestTest.Location = new Point(30, 347);
            btnRequestTest.Name = "btnRequestTest";
            btnRequestTest.Size = new Size(130, 45);
            btnRequestTest.TabIndex = 8;
            btnRequestTest.Text = "请求试验";
            btnRequestTest.Type = AntdUI.TTypeMini.Primary;
            btnRequestTest.Click += btnRequestTest_Click;
            // 
            // btnSendTestParameters
            // 
            btnSendTestParameters.Enabled = false;
            btnSendTestParameters.Font = new Font("Microsoft YaHei UI", 10F);
            btnSendTestParameters.Location = new Point(186, 347);
            btnSendTestParameters.Name = "btnSendTestParameters";
            btnSendTestParameters.Size = new Size(130, 45);
            btnSendTestParameters.TabIndex = 9;
            btnSendTestParameters.Text = "发送试验参数";
            btnSendTestParameters.Type = AntdUI.TTypeMini.Success;
            btnSendTestParameters.Click += btnSendTestParameters_Click;
            // 
            // btnEndTest
            // 
            btnEndTest.Enabled = false;
            btnEndTest.Font = new Font("Microsoft YaHei UI", 10F);
            btnEndTest.Location = new Point(342, 347);
            btnEndTest.Name = "btnEndTest";
            btnEndTest.Size = new Size(130, 45);
            btnEndTest.TabIndex = 10;
            btnEndTest.Text = "结束试验";
            btnEndTest.Type = AntdUI.TTypeMini.Warn;
            btnEndTest.Click += btnEndTest_Click;
            // 
            // btnCancelTest
            // 
            btnCancelTest.Enabled = false;
            btnCancelTest.Font = new Font("Microsoft YaHei UI", 10F);
            btnCancelTest.Location = new Point(498, 347);
            btnCancelTest.Name = "btnCancelTest";
            btnCancelTest.Size = new Size(130, 45);
            btnCancelTest.TabIndex = 11;
            btnCancelTest.Text = "取消试验";
            btnCancelTest.Type = AntdUI.TTypeMini.Error;
            btnCancelTest.Click += btnCancelTest_Click;
            // 
            // panelStatus
            // 
            panelStatus.Back = Color.White;
            panelStatus.BorderColor = Color.FromArgb(240, 240, 240);
            panelStatus.BorderWidth = 1F;
            panelStatus.Controls.Add(lblStatusTitle);
            panelStatus.Controls.Add(txtStatus);
            panelStatus.Location = new Point(20, 584);
            panelStatus.Name = "panelStatus";
            panelStatus.Radius = 8;
            panelStatus.Shadow = 10;
            panelStatus.Size = new Size(1023, 288);
            panelStatus.TabIndex = 5;
            // 
            // lblStatusTitle
            // 
            lblStatusTitle.BackColor = Color.Transparent;
            lblStatusTitle.Font = new Font("Microsoft YaHei UI", 12F, FontStyle.Bold);
            lblStatusTitle.ForeColor = Color.FromArgb(51, 51, 51);
            lblStatusTitle.Location = new Point(20, 15);
            lblStatusTitle.Name = "lblStatusTitle";
            lblStatusTitle.Size = new Size(200, 30);
            lblStatusTitle.TabIndex = 0;
            lblStatusTitle.Text = "运行状态";
            // 
            // txtStatus
            // 
            txtStatus.Font = new Font("Consolas", 9F);
            txtStatus.Location = new Point(20, 55);
            txtStatus.Multiline = true;
            txtStatus.Name = "txtStatus";
            txtStatus.ReadOnly = true;
            txtStatus.Size = new Size(970, 208);
            txtStatus.TabIndex = 1;
            // 
            // FrmInsulationWithstand
            // 
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Color.FromArgb(245, 245, 245);
            ClientSize = new Size(1059, 885);
            Controls.Add(panelStatus);
            Controls.Add(panelTestControl);
            Controls.Add(panelConnection);
            Controls.Add(dividerTop);
            Font = new Font("Microsoft YaHei UI", 10F);
            MaximizeBox = false;
            MinimizeBox = false;
            Name = "FrmInsulationWithstand";
            RectColor = Color.Transparent;
            ShowIcon = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            Text = "绝缘耐压电阻测试界面";
            TitleColor = Color.FromArgb(65, 100, 204);
            TitleFont = new Font("微软雅黑", 15F, FontStyle.Bold);
            ZoomScaleRect = new Rectangle(15, 15, 1200, 970);
            FormClosing += DeviceClientForm_FormClosing;
            panelConnection.ResumeLayout(false);
            panelTestControl.ResumeLayout(false);
            panelInsulationWithstandParams.ResumeLayout(false);
            panelInsulationResistanceParams.ResumeLayout(false);
            panelResistanceParams.ResumeLayout(false);
            panelStatus.ResumeLayout(false);
            ResumeLayout(false);
        }

        #endregion
        private AntdUI.Divider dividerTop;

        // 服务器连接面板控件
        private AntdUI.Panel panelConnection;
        private AntdUI.Label lblConnectionTitle;
        private AntdUI.Label lblServerIp;
        private AntdUI.Input txtServerIp;
        private AntdUI.Label lblServerPort;
        private AntdUI.Input txtServerPort;
        private AntdUI.Button btnConnect;
        private AntdUI.Label lblConnectionTip;

        // 试验控制面板控件
        private AntdUI.Panel panelTestControl;
        private AntdUI.Label lblTestControlTitle;
        private AntdUI.Label lblTestType;
        private AntdUI.Radio rbtnInsulationWithstand;
        private AntdUI.Radio rbtnInsulationResistance;
        private AntdUI.Radio rbtnResistance;

        // 绝缘耐压参数面板
        private AntdUI.Panel panelInsulationWithstandParams;
        private AntdUI.Label lblWithstandParamsTitle;
        private AntdUI.Label lblWithstandVoltage;
        private AntdUI.Input txtWithstandVoltage;
        private AntdUI.Label lblWithstandDuration;
        private AntdUI.Input txtWithstandDuration;
        private AntdUI.Label lblWithstandLimit;
        private AntdUI.Input txtWithstandLimit;

        // 绝缘电阻参数面板
        private AntdUI.Panel panelInsulationResistanceParams;
        private AntdUI.Label lblResistanceParamsTitle;
        private AntdUI.Label lblResistanceVoltage;
        private AntdUI.Input txtResistanceVoltage;
        private AntdUI.Label lblResistanceDuration;
        private AntdUI.Input txtResistanceDuration;
        private AntdUI.Label lblResistanceLimit;
        private AntdUI.Input txtResistanceLimit;

        // 电阻测试参数面板
        private AntdUI.Panel panelResistanceParams;
        private AntdUI.Label lblResistanceTestParamsTitle;
        private AntdUI.Label lblLowResistanceLimit;
        private AntdUI.Input txtLowResistanceLimit;

        // 操作按钮
        private AntdUI.Button btnRequestTest;
        private AntdUI.Button btnSendTestParameters;
        private AntdUI.Button btnEndTest;
        private AntdUI.Button btnCancelTest;

        // 状态信息面板
        private AntdUI.Panel panelStatus;
        private AntdUI.Label lblStatusTitle;
        private AntdUI.Input txtStatus;
    }
}