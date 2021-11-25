using SqlSugar;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace StreamDanmuku_Server.Data
{
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
            //instance.CodeFirst.InitTables(typeof(Online));
            //instance.CodeFirst.InitTables(typeof(Room));
            instance.CodeFirst.InitTables(typeof(User));
        }
    }
}
