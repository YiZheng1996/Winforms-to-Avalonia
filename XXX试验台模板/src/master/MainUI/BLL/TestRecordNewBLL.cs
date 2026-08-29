namespace MainUI.BLL
{
    internal class TestRecordNewBLL
    {
        public bool UpdateTestRecord(TestRecordModel model) => VarHelper.fsql
            .Update<TestRecordModel>()
            .Set(a => a.KindID, model.KindID)
            .Set(a => a.ModelID, model.ModelID)
            .Set(a => a.TestID, model.TestID)
            .Set(a => a.ProductNumber, model.ProductNumber)
            .Set(a => a.Tester, model.Tester)
            .Set(a => a.TestTime, model.TestTime)
            .Set(a => a.ReportPath, model.ReportPath)
            .Where(a => a.ID == model.ID)
            .ExecuteAffrows() > 0;

        public bool SaveTestRecord(TestRecordModel model) => VarHelper.fsql
            .Insert(model)
            .ExecuteAffrows() > 0;

        public bool DeleteTestRecord(int id)
        {
            // 删除本地试验记录时同步删除上传状态，避免报表管理中留下孤立数据。
            UploadService.Instance.DeleteByRecordID(id);

            return VarHelper.fsql
                .Delete<TestRecordModel>()
                .Where(a => a.ID == id)
                .ExecuteAffrows() > 0;
        }

        public List<TestRecordModelDto> GetTestRecord(TestRecordModel model, DateTime toTime)
        {
            List<TestRecordModelDto> records = VarHelper.fsql
                .Select<TestRecordModel, ModelsType, Models>()
                .LeftJoin((t, mt, m) => t.KindID == mt.ID)
                .LeftJoin((t, mt, m) => t.ModelID == m.ID)
                .WhereIf(model.KindID != -1, (t, mt, m) => t.KindID == model.KindID)
                .WhereIf(model.ModelID != -1, (t, mt, m) => t.ModelID == model.ModelID)
                .WhereIf(!string.IsNullOrEmpty(model.TestID), (t, mt, m) => t.TestID.Contains(model.TestID))
                .WhereIf(!string.IsNullOrEmpty(model.ProductNumber),
                    (t, mt, m) => t.ProductNumber.Contains(model.ProductNumber))
                .WhereIf(!string.IsNullOrEmpty(model.Tester), (t, mt, m) => t.Tester == model.Tester)
                .Where((t, mt, m) => t.TestTime.Between(model.TestTime, toTime))
                .OrderByDescending((t, mt, m) => t.TestTime)
                .ToList((t, mt, m) => new TestRecordModelDto
                {
                    ID = t.ID,
                    KindID = t.KindID,
                    ModelID = t.ModelID,
                    TestID = t.TestID,
                    ProductNumber = t.ProductNumber,
                    Tester = t.Tester,
                    TestTime = t.TestTime,
                    ReportPath = t.ReportPath,
                    ModelTypeName = mt.ModelTypeName,
                    ModelName = m.ModelName
                });

            ApplyUploadStatus(records);
            return records;
        }

        /// <summary>
        /// 把本地上传任务状态合并到报表查询结果中。
        /// 兼容早期可能重复创建任务的情况：同一报表记录始终采用 ID 最大的一条最新状态。
        /// </summary>
        private static void ApplyUploadStatus(List<TestRecordModelDto> records)
        {
            var latestTasks = UploadService.Instance
                .GetUploadTasksByRecordIDs(records.Select(record => record.ID))
                .GroupBy(task => task.RecordID)
                .ToDictionary(
                    group => group.Key,
                    group => group.OrderByDescending(task => task.ID).First());

            foreach (TestRecordModelDto record in records)
            {
                if (!latestTasks.TryGetValue(record.ID, out UploadTaskModel task))
                {
                    // 功能升级前保存的历史报表没有上传状态记录。
                    record.UploadStatus = "未生成任务";
                    continue;
                }

                // 数据和文件状态相互独立，人工重新上传时仅处理尚未成功的部分。
                record.UploadStatus = (task.DataUploaded, task.FileUploaded) switch
                {
                    (true, true) => "已上传",
                    (true, false) => string.IsNullOrWhiteSpace(task.LastError) ? "文件待上传" : "文件上传失败",
                    (false, true) => string.IsNullOrWhiteSpace(task.LastError) ? "数据待上传" : "数据上传失败",
                    _ => string.IsNullOrWhiteSpace(task.LastError) ? "待上传" : "上传失败"
                };
                record.UploadedTime = task.CompletedTime;
                record.UploadError = task.LastError;
            }
        }
    }
}
