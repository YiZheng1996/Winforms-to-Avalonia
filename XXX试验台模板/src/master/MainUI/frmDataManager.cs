using AntdUI;

namespace MainUI
{
    public partial class FrmDataManager : UIForm
    {
        private TestRecordModelDto RecordModel = new();
        private readonly TestRecordNewBLL _testRecordBLL = new();
        private bool _isInitializing;

        public FrmDataManager() => InitializeComponent();

        private void frmDataManager_Load(object sender, EventArgs e)
        {
            Init();
            LoadData();
        }

        private new void Init()
        {
            try
            {
                _isInitializing = true;
                dtpStartBig.Value = DateTime.Now.AddDays(-3);
                dtpStartEnd.Value = DateTime.Now;
                InitializeModelTypeComboBox();
                InitializeModelComboBox();
            }
            finally
            {
                _isInitializing = false;
            }
        }

        private void InitializeModelTypeComboBox()
        {
            var modelTypes = new ModelTypeBLL().GetModels();
            modelTypes.Insert(0, new ModelsType { ID = -1, ModelTypeName = "请选择" });
            cboType.DisplayMember = "ModelTypeName";
            cboType.ValueMember = "ID";
            cboType.DataSource = modelTypes;
            cboType.SelectedValue = -1;
        }

        private void InitializeModelComboBox(int typeID = -1)
        {
            var models = typeID == -1
                ? new List<NewModels>()
                : ModelBLL.GetNewModels(typeID, IsRelease: false);
            models.Insert(0, new NewModels { ID = -1, ModelName = "请选择" });
            cboModel.ValueMember = "ID";
            cboModel.DisplayMember = "ModelName";
            cboModel.DataSource = models;
            cboModel.SelectedValue = -1;
        }

        private void CboType_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (!_isInitializing) InitializeModelComboBox(cboType.SelectedValue.ToInt32());
        }

        private void LoadData()
        {
            try
            {
                var searchModel = new TestRecordModel
                {
                    KindID = cboType.SelectedValue.ToInt32(),
                    ModelID = cboModel.SelectedValue.ToInt32(),
                    TestID = txtNumber.Text.Trim(),
                    ProductNumber = txtProductNumber.Text.Trim(),
                    TestTime = dtpStartBig.Value
                };

                // 上传错误保留在数据源中但默认隐藏，避免长文本挤压报表列表。
                Tables.Columns =
                [
                    new Column("ID", "ID") { Align = ColumnAlign.Center, Visible = false },
                    new Column("KindID", "车型ID") { Align = ColumnAlign.Center, Visible = false },
                    new Column("ModelTypeName", "车型") { Align = ColumnAlign.Center },
                    new Column("ModelID", "型号ID") { Align = ColumnAlign.Center, Visible = false },
                    new Column("ProductNumber", "产品编号") { Align = ColumnAlign.Center },
                    new Column("TestID", "车号") { Align = ColumnAlign.Center },
                    new Column("ModelName", "产品型号") { Align = ColumnAlign.Center },
                    new Column("TestTime", "测试时间") { Align = ColumnAlign.Center },
                    new Column("Tester", "试验员") { Align = ColumnAlign.Center },
                    new Column("UploadStatus", "上传状态") { Align = ColumnAlign.Center, Visible = false },
                    new Column("UploadedTime", "上传时间") { Align = ColumnAlign.Center, Visible = false },
                    new Column("UploadError", "上传错误") { Align = ColumnAlign.Left, Visible = false },
                    new Column("ReportPath", "保存路径") { Align = ColumnAlign.Center, Visible = false }
                ];
                Tables.DataSource = _testRecordBLL.GetTestRecord(searchModel, dtpStartEnd.Value.AddDays(1));
            }
            catch (Exception ex)
            {
                MessageHelper.MessageOK($"加载数据出现错误：{ex.Message}");
            }
        }

        private void View()
        {
            if (string.IsNullOrWhiteSpace(RecordModel.ReportPath))
            {
                MessageBox.Show("请先选择一条记录。", "系统提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (!File.Exists(RecordModel.ReportPath))
            {
                MessageBox.Show($"报表文件不存在：{RecordModel.ReportPath}", "系统提示",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            using var report = new frmDispReport(RecordModel.ReportPath);
            VarHelper.ShowDialogWithOverlay(this, report);
        }

        private void btnSearch_Click(object sender, EventArgs e) => LoadData();
        private void btnView_Click(object sender, EventArgs e) => View();

        /// <summary>
        /// 人工重新上传当前选中的试验记录。
        /// 每次点击只执行一次：已成功的部分跳过，失败的部分尝试后立即向操作人员反馈结果。
        /// </summary>
        private async void btnUpload_Click(object sender, EventArgs e)
        {
            if (RecordModel.ID <= 0)
            {
                MessageHelper.MessageOK("请先选择一条试验记录", TType.Warn);
                return;
            }

            // 防止操作人员连续点击造成并发重复请求。
            btnUpload.Enabled = false;
            try
            {
                // 人工点击时仅尝试一次，不启动后台任务，也不会安排后续自动重试。
                UploadTaskModel task = await UploadService.Instance.RetryOnceAsync(RecordModel.ID);
                LoadData();

                if (task.DataUploaded && task.FileUploaded)
                {
                    string successMessage = string.IsNullOrWhiteSpace(task.PayloadJson)
                        ? "报表上传成功"
                        : "试验数据和报表上传成功";
                    MessageHelper.MessageOK(successMessage, TType.Success);
                }
                else
                {
                    MessageHelper.MessageOK($"上传失败：{task.LastError}", TType.Error);
                }
            }
            catch (Exception ex)
            {
                MessageHelper.MessageOK($"重新上传失败：{ex.Message}", TType.Error);
            }
            finally
            {
                btnUpload.Enabled = true;
            }
        }

        private void btnRemove_Click(object sender, EventArgs e)
        {
            if (RecordModel.ID <= 0) return;
            if (MessageHelper.MessageYes(this, "删除后无法恢复，确定要删除该条记录吗？") == DialogResult.OK)
            {
                _testRecordBLL.DeleteTestRecord(RecordModel.ID);
                LoadData();
            }
        }
        private void Tables_CellClick(object sender, TableClickEventArgs e)
        {
            if (e.Record is TestRecordModelDto model) RecordModel = model;
        }
        private void Tables_CellDoubleClick(object sender, TableClickEventArgs e) => View();
        private void btnExit_Click(object sender, EventArgs e) => Close();
    }
}
