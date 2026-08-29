namespace MainUI.Service
{
    internal static class LocalDatabaseInitializer
    {
        public static void Initialize(IFreeSql database)
        {
            database.CodeFirst.SyncStructure<RoleModel>();
            database.CodeFirst.SyncStructure<OperateUserModel>();
            database.CodeFirst.SyncStructure<TestRecordModel>();

            // 上传状态单独建表，不修改原 Record 表结构；旧项目数据库启动时会自动新增该表。
            database.CodeFirst.SyncStructure<UploadTaskModel>();

            if (!database.Select<RoleModel>().Any())
            {
                database.Insert(new RoleModel
                {
                    RoleName = "系统管理员",
                    Describe = "本机系统管理员",
                    IsDelete = 0
                }).ExecuteAffrows();
            }

            if (!database.Select<OperateUserModel>().Any())
            {
                int administratorRoleId = database.Select<RoleModel>()
                    .Where(role => role.RoleName == "系统管理员")
                    .First()?.ID ?? 1;

                database.Insert(new OperateUserModel
                {
                    Username = "admin",
                    Password = "123456",
                    Role_ID = administratorRoleId,
                    Sort = 1
                }).ExecuteAffrows();
            }
        }
    }
}
