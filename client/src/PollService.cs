using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows.Threading;

namespace MultimediaClient
{
    /// <summary>
    /// 轮询服务:每 3 秒心跳一次;发现数据版本变化时重新拉取;有待接收叫号时通知 UI。
    /// 网络失败自动退避(3s → 最长 30s),恢复后立即回到 3s。
    /// 所有事件均通过 Dispatcher 转到 UI 线程。
    /// </summary>
    internal class PollService
    {
        public event Action<CallInfo> CallReceived;
        public event Action<bool> OnlineChanged;
        public event Action AuthInvalid;
        public event Action<string> UpdateAvailable;

        private readonly ApiClient _api;
        private readonly Dispatcher _ui;
        private Thread _thread;
        private volatile bool _stop;
        private int _delayMs = 3000;
        private int _lastPendingCallId = 0;
        private bool _online;

        public bool Online
        {
            get { return _online; }
        }

        public PollService(ApiClient api, Dispatcher uiDispatcher)
        {
            _api = api;
            _ui = uiDispatcher;
        }

        public void Start()
        {
            _stop = false;
            _thread = new Thread(Loop);
            _thread.IsBackground = true;
            _thread.Name = "PollService";
            _thread.Start();
        }

        public void Stop()
        {
            _stop = true;
        }

        /// <summary>立即触发一次心跳(教师端改了设置后手动刷新)</summary>
        public void Kick()
        {
            _delayMs = 0;
        }

        private void Loop()
        {
            while (!_stop)
            {
                if (_delayMs > 0) Sleep(_delayMs);
                if (_stop) break;

                try
                {
                    Tick();
                }
                catch (Exception ex)
                {
                    Logger.Error("心跳循环异常", ex);
                    SetOnline(false);
                    _delayMs = NextBackoff();
                }
            }
        }

        private void Tick()
        {
            Dictionary<string, object> body = new Dictionary<string, object>();
            body["client_id"] = Config.ClientId;
            body["token"] = Config.Token;
            body["version"] = AppHost.Version;

            ApiResult r = _api.Post("/api/client/heartbeat", body);

            if (r.HttpStatus == 401)
            {
                // 凭据失效(教师端解绑):回到配对流程
                Logger.Info("凭据失效,需要重新配对");
                Action a = AuthInvalid;
                if (a != null) _ui.BeginInvoke(a);
                _delayMs = 30000;
                return;
            }

            if (!r.Ok)
            {
                SetOnline(false);
                _delayMs = NextBackoff();
                return;
            }

            SetOnline(true);
            _delayMs = 3000;
            TimeSync.Update(Json.GetString(r.Data, "server_time", ""));

            int version = Json.GetInt(r.Data, "data_version", -1);
            if (version != DataStore.DataVersion)
            {
                FetchData(version);
            }

            // 自动更新:服务器发布了更高版本时通知主程序(旧服务器无该字段,自动跳过)
            string latest = Json.GetString(r.Data, "latest_version", "");
            if (SelfUpdate.ShouldUpdate(latest))
            {
                Action<string> ua = UpdateAvailable;
                if (ua != null) _ui.BeginInvoke(new Action(delegate() { ua(latest); }));
            }

            Dictionary<string, object> call = Json.GetObject(r.Data, "pending_call");
            if (call != null)
            {
                CallInfo info = CallInfo.FromJson(call);
                if (info.Id > 0 && info.Id != _lastPendingCallId)
                {
                    _lastPendingCallId = info.Id;
                    Action<CallInfo> a = CallReceived;
                    if (a != null) _ui.BeginInvoke(new Action(delegate() { a(info); }));
                }
            }
        }

        private void FetchData(int version)
        {
            Dictionary<string, string> q = new Dictionary<string, string>();
            q["client_id"] = Config.ClientId.ToString();
            q["token"] = Config.Token;
            ApiResult r = _api.Get("/api/client/data", q);
            if (!r.Ok)
            {
                Logger.Info("拉取数据失败:" + r.Error);
                return;
            }
            DataStore.Settings = Settings.FromJson(Json.GetObject(r.Data, "settings"));
            DataStore.Notice = Notice.FromJson(Json.GetObject(r.Data, "notice"));
            List<TaskItem> tasks = new List<TaskItem>();
            foreach (object o in Json.GetArray(r.Data, "tasks"))
            {
                Dictionary<string, object> td = o as Dictionary<string, object>;
                if (td != null) tasks.Add(TaskItem.FromJson(td));
            }
            DataStore.Tasks = tasks;
            DataStore.DataVersion = Json.GetInt(r.Data, "data_version", version);
            DataStore.ServerTime = Json.GetString(r.Data, "server_time", "");
            TimeSync.Update(DataStore.ServerTime);
            CacheService.Save();
            Logger.Info("数据已更新 v" + DataStore.DataVersion + " 任务数:" + tasks.Count);
            _ui.BeginInvoke(new Action(DataStore.RaiseUpdated));
        }

        private int NextBackoff()
        {
            int next = _delayMs <= 0 ? 3000 : _delayMs * 2;
            return next > 30000 ? 30000 : next;
        }

        private void SetOnline(bool online)
        {
            if (_online == online) return;
            _online = online;
            Action<bool> a = OnlineChanged;
            if (a != null) _ui.BeginInvoke(new Action(delegate() { a(online); }));
        }

        private void Sleep(int ms)
        {
            int elapsed = 0;
            while (elapsed < ms && !_stop)
            {
                Thread.Sleep(200);
                elapsed += 200;
            }
        }

        public void AckCall(int callId, string callEvent)
        {
            // 回执失败不打断流程,仅记录日志
            ThreadPool.QueueUserWorkItem(delegate
            {
                Dictionary<string, object> body = new Dictionary<string, object>();
                body["client_id"] = Config.ClientId;
                body["token"] = Config.Token;
                body["call_id"] = callId;
                body["event"] = callEvent;
                ApiResult r = _api.Post("/api/client/call-ack", body);
                if (!r.Ok) Logger.Info("叫号回执失败(" + callEvent + "):" + r.Error);
            });
        }
    }
}
