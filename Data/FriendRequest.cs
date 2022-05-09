using Newtonsoft.Json.Linq;
using SqlSugar;
using StreamDanmaku_Server.Enum;
using System;
using System.Linq;
using static StreamDanmaku_Server.SocketIO.Server;

namespace StreamDanmaku_Server.Data
{
    public class FriendRequest
    {
        [SugarColumn(IsPrimaryKey = true, IsIdentity = true)]
        public int RowID { get; set; }
        public int From { get; set; }
        public int To { get; set; }
        public bool Result { get; set; }
        public bool Handled { get; set; }
        public DateTime CreateTime { get; set; }

        public static void GetFriendRequestCount(MsgHandler socket, JToken data, string onName, User user)
        {
            using var db = SQLHelper.GetInstance();
            int count = db.Queryable<FriendRequest>().Where(x => x.To == user.Id && x.Handled == false).Count();
            socket.Emit(onName, Helper.SetOK(count));
        }
        public static void GetFriendRequestList(MsgHandler socket, JToken data, string onName, User user)
        {
            using var db = SQLHelper.GetInstance();
            var q1 = db.Queryable<User>();
            var q2 = db.Queryable<FriendRequest>().Where(x => x.To == user.Id && x.Handled == false);
            var list = db.Queryable(q1, q2, (x, o) => x.Id == o.From).Select((x, o) => new { Id = o.RowID, x.NickName, o.CreateTime }).ToList();
            socket.Emit(onName, Helper.SetOK(list));
        }
        public static void CreateFriendRequest(MsgHandler socket, JToken data, string onName, User user)
        {
            int to = data.Value<int>("to");
            using var db = SQLHelper.GetInstance();
            if (user.Friends.Contains(to))
            {
                RuntimeLog.WriteUserLog(user, onName, $"好友请求已建立无法再次添加，to={to}", false);
                socket.Emit(onName, Helper.SetError(ErrorCode.AlreadyFriend));
                return;
            }
            if (db.Queryable<User>().Any(x => x.Id == to) == false)
            {
                RuntimeLog.WriteUserLog(user, onName, $"好友请求对方不存在，to={to}", false);
                socket.Emit(onName, Helper.SetError(ErrorCode.InvalidUser));
                return;
            }
            if (db.Queryable<FriendRequest>().Any(x => x.From == user.Id && x.To == to && x.Handled == false))
            {
                RuntimeLog.WriteUserLog(user, onName, $"重复申请忽略，to={to}", false);
                socket.Emit(onName, Helper.SetOK());
                return;
            }
            var friendRequest = new FriendRequest
            {
                From = user.Id,
                To = to,
                CreateTime = DateTime.Now
            };
            db.Insertable(friendRequest).ExecuteCommand();
            var target = Online.Users.Find(x => x.Id == to);
            if(target != null)
            {
                target.WebSocket.Emit("OnFriendRequest", "");
            }
            RuntimeLog.WriteUserLog(user, onName, $"创建好友请求成功，from={user.Id}", true);
            socket.Emit(onName, Helper.SetOK());
        }
        public static void HandleFriendRequest(MsgHandler socket, JToken data, string onName, User user)
        {
            int id = (int)data["request_id"];
            bool result = (bool)data["agree"];
            using var db = SQLHelper.GetInstance();
            User from, to;
            var request = db.Queryable<FriendRequest>().Where(x => x.RowID == id).First();
            if (request == null)
            {
                RuntimeLog.WriteUserLog(user, onName, $"好友请求不存在，id={id}", false);
                socket.Emit(onName, Helper.SetError(Enum.ErrorCode.NoFriendRequest));
                return;
            }
            if (user.Id == request.From)
            {
                from = user;
                var t = Online.Users.Find(x => x.Id == request.To);
                if (t == null) to = db.Queryable<User>().Where(x => x.Id == request.To).First();
                else to = t;
            }
            else
            {
                var t = Online.Users.Find(x => x.Id == request.From);
                if (t == null) from = db.Queryable<User>().Where(x => x.Id == request.From).First();
                else from = t;
                to = user;
            }
            if (from == null || to == null)
            {
                RuntimeLog.WriteUserLog(user, onName, $"好友请求一方用户不存在，from={request.From}，to={request.To}", false);
                socket.Emit(onName, Helper.SetError(ErrorCode.InvalidUser));
                return;
            }
            request.Result = result;
            request.Handled = true;
            db.Updateable(request).ExecuteCommand();
            if (result)
            {
                from.Friends.Add(to.Id);
                from.Friends = from.Friends.Distinct().ToList();
                to.Friends.Add(from.Id);
                to.Friends = to.Friends.Distinct().ToList();
                from.WebSocket?.Emit("FriendAdd", new { friend_id = to.Id });
                to.WebSocket?.Emit("FriendAdd", new { friend_id = from.Id });
                from.UpdateUser();
                to.UpdateUser();
            }
            RuntimeLog.WriteUserLog(user, onName, $"处理好友请求成功，ID={id}，结果={result}", true);
            socket.Emit(onName, Helper.SetOK());
        }
    }
}
