using Newtonsoft.Json;
using StreamDanmuku_Server.SocketIO;
using System;
using System.Collections.Generic;

namespace StreamDanmuku_Server.Data
{
    [JsonObject(MemberSerialization.OptOut)]

    public class Room
    {
        public string Title { get; set; }
        public int RoomID { get; set; }
        public string CreatorName {get;set;}
        public bool IsPublic { get; set; }
        public bool PasswordNeeded { get => !string.IsNullOrWhiteSpace(Password); }
        [JsonIgnore]
        public string Password { get; set; }
        public int Max { get; set; }
        public DateTime CreateTime { get; set; }
        [JsonIgnore]
        public List<Server.MsgHandler> Clients { get; set; } = new();
        public int ClientCount { get => Clients.Count; }
    }
}
