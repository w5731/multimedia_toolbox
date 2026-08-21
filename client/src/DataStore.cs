using System;
using System.Collections.Generic;
using System.Globalization;

namespace MultimediaClient
{
    /// <summary>服务器时间同步:教室电脑时钟不准时,看板时间以服务器为准</summary>
    internal static class TimeSync
    {
        private static TimeSpan _offset = TimeSpan.Zero;

        public static DateTime Now
        {
            get { return DateTime.Now.Add(_offset); }
        }

        /// <summary>用服务器时间字符串("yyyy-MM-dd HH:mm:ss")校准本地时间</summary>
        public static void Update(string serverTime)
        {
            DateTime st;
            if (DateTime.TryParseExact(serverTime, "yyyy-MM-dd HH:mm:ss",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out st))
            {
                _offset = st - DateTime.Now;
            }
        }
    }

    /// <summary>客户端当前数据(任务/通知/设置),UI 线程读取</summary>
    internal static class DataStore
    {
        public static Settings Settings = new Settings();
        public static Notice Notice = new Notice();
        public static List<TaskItem> Tasks = new List<TaskItem>();
        public static int DataVersion = -1;
        public static string ServerTime = "";

        /// <summary>数据变化时在 UI 线程触发</summary>
        public static event Action Updated;

        public static void RaiseUpdated()
        {
            Action a = Updated;
            if (a != null) a();
        }
    }
}
