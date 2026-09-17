using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace MultimediaClient
{
    /// <summary>本地缓存:断网时看板照常显示(任务/通知/设置持久化)</summary>
    internal static class CacheService
    {
        private static string CachePath
        {
            get { return Path.Combine(Config.AppDir, "cache.json"); }
        }

        public static void Save()
        {
            try
            {
                Dictionary<string, object> d = new Dictionary<string, object>();
                d["data_version"] = DataStore.DataVersion;
                d["server_time"] = DataStore.ServerTime;
                d["cached_at"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                d["settings"] = DataStore.Settings.ToJson();
                d["notice"] = DataStore.Notice.ToJson();
                List<object> tasks = new List<object>();
                foreach (TaskItem t in DataStore.Tasks) tasks.Add(t.ToJson());
                d["tasks"] = tasks;
                Config.AtomicWrite(CachePath, Json.Serialize(d));
            }
            catch (Exception ex)
            {
                Logger.Error("保存缓存失败", ex);
            }
        }

        public static void Load()
        {
            try
            {
                if (!File.Exists(CachePath)) return;
                Dictionary<string, object> d = Json.ParseObject(File.ReadAllText(CachePath, Encoding.UTF8));
                if (d == null) return;
                DataStore.DataVersion = Json.GetInt(d, "data_version", -1);
                DataStore.ServerTime = Json.GetString(d, "server_time", "");
                DataStore.Settings = Settings.FromJson(Json.GetObject(d, "settings"));
                DataStore.Notice = Notice.FromJson(Json.GetObject(d, "notice"));
                List<TaskItem> tasks = new List<TaskItem>();
                foreach (object o in Json.GetArray(d, "tasks"))
                {
                    Dictionary<string, object> td = o as Dictionary<string, object>;
                    if (td != null) tasks.Add(TaskItem.FromJson(td));
                }
                DataStore.Tasks = tasks;
                // 新版缓存同时记录本机写入时刻。离线启动时按经过时长推进服务器时间，
                // 避免把时钟冻结在上次拉取数据的时刻；旧版缓存没有该字段则等待心跳校准。
                string cachedAtText = Json.GetString(d, "cached_at", "");
                DateTime cachedAt;
                DateTime serverAt;
                if (DateTime.TryParseExact(cachedAtText, "yyyy-MM-dd HH:mm:ss",
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out cachedAt) &&
                    DateTime.TryParseExact(DataStore.ServerTime, "yyyy-MM-dd HH:mm:ss",
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out serverAt))
                {
                    DateTime estimatedServerNow = serverAt.Add(DateTime.Now - cachedAt);
                    TimeSync.Update(estimatedServerNow.ToString("yyyy-MM-dd HH:mm:ss"));
                }
            }
            catch (Exception ex)
            {
                Logger.Error("读取缓存失败", ex);
            }
        }
    }
}
