using MainUI.Service;

namespace MainUI.BLL
{
    public class TestStepBLL
    {
        /// <summary>
        /// 获取测试步骤 - 根据试验台过滤
        /// </summary>
        public List<TestStepModel> GetTestSteps(TestStepModel Step)
        {
            var query = VarHelper.fsql.Select<TestStepModel>()
                .Where(x => x.ModelID == Step.ModelID);

            return query.ToList();
        }

        /// <summary>
        /// 删除测试步骤
        /// </summary>
        public bool DeleTestStep(int ModelID) =>
            VarHelper.fsql.Delete<TestStepModel>()
            .Where(x => x.ModelID == ModelID)
            .ExecuteAffrows() > 0;

        /// <summary>
        /// 插入测试步骤 - 自动关联当前试验台
        /// </summary>
        public bool InsertTestStep(List<TestStepModel> Steps, int ModelID)
        {
            DeleTestStep(ModelID);
            return VarHelper.fsql.Insert(Steps)
                .ExecuteAffrows() > 0;
        }

        /// <summary>
        /// 获取测试项目 - 根据试验台过滤
        /// </summary>
        public List<TestStepNewModel> GetTestItems(int ModelID)
        {
            var query = VarHelper.fsql
                .Select<TestStepModel, TestProcessModel>()
                .LeftJoin((s, p) => s.TestProcessID == p.ID)
                .Where((s, p) => p.IsVisible == true && s.ModelID == ModelID);

            return query.ToList((s, p) => new TestStepNewModel()
            {
                ID = s.ID,
                Step = s.Step,
                ProcessName = s.ProcessName,
                ModelID = s.ModelID,
                TestProcessID = p.ID,
                TestProcessName = p.ProcessName,
                IsVisible = p.IsVisible,
            });
        }
    }
}
