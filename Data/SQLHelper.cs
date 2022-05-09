using SqlSugar;

namespace StreamDanmaku_Server.Data
{
    /// <summary>
    /// 数据库操作类
    /// </summary>
    public static class SQLHelper
    {
        public static SqlSugarClient GetInstance() => new(new ConnectionConfig()
        {
            ConnectionString = Config.GetConfig<string>("DBConnectString"),
            DbType = DbType.MySql,
            IsAutoCloseConnection = true,
            InitKeyType = InitKeyType.Attribute,
        });

        public static void Init()
        {
            var instance = GetInstance();
            instance.DbMaintenance.CreateDatabase();
            instance.CodeFirst.InitTables(typeof(RuntimeLog));
            instance.CodeFirst.InitTables(typeof(User));
            instance.CodeFirst.InitTables(typeof(FriendRequest));
        }
    }
}