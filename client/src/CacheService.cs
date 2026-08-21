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
                TimeSync.Update(DataStore.ServerTime);
            }
            catch (Exception ex)
            {
                Logger.Error("读取缓存失败", ex);
            }
        }
    }
}
