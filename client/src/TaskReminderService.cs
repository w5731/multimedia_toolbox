using System;
using System.Collections.Generic;
using System.Windows.Threading;

namespace MultimediaClient
{
    /// <summary>按服务器校准时间检测任务提醒,同一任务同一天只展示一次。</summary>
    internal sealed class TaskReminderService
    {
        private sealed class PendingReminder
        {
            internal TaskItem Task;
            internal string Key;
        }

        private readonly DispatcherTimer _timer;
        private readonly Queue<PendingReminder> _queue = new Queue<PendingReminder>();
        private readonly HashSet<string> _queuedKeys = new HashSet<string>();
        private TaskReminderWindow _window;
        private bool _stopped;

        public bool IsShowing
        {
            get { return _window != null; }
        }

        public event Action<bool> ShowingChanged;

        public TaskReminderService()
        {
            CleanupHistory();
            _timer = new DispatcherTimer();
            _timer.Interval = TimeSpan.FromSeconds(1);
            _timer.Tick += delegate { CheckDueTasks(); };
        }

        public void Start()
        {
            _stopped = false;
            _timer.Start();
            CheckDueTasks();
        }

        public void Stop()
        {
            _stopped = true;
            _timer.Stop();
            if (_window != null)
            {
                _window.ForceClose();
                _window = null;
            }
            RaiseShowingChanged(false);
            _queue.Clear();
            _queuedKeys.Clear();
        }

        private void CheckDueTasks()
        {
            if (_stopped) return;
            DateTime now = TimeSync.Now;
            List<TaskItem> due = new List<TaskItem>();
            foreach (TaskItem task in DataStore.Tasks)
            {
                if (!task.ReminderEnabled || !task.AppliesOn(now.Date)) continue;
                DateTime reminderAt;
                try { reminderAt = task.ReminderOn(now.Date); }
                catch { continue; }
                TimeSpan elapsed = now - reminderAt;
                if (elapsed.TotalSeconds < 0 || elapsed.TotalMinutes >= 5) continue;
                string key = MakeKey(task, now.Date);
                if (Config.ShownTaskReminders.Contains(key) || _queuedKeys.Contains(key)) continue;
                due.Add(task);
            }
            due.Sort(delegate(TaskItem a, TaskItem b)
            {
                int byTime = DateTime.Compare(a.ReminderOn(now.Date), b.ReminderOn(now.Date));
                return byTime != 0 ? byTime : a.Id.CompareTo(b.Id);
            });
            foreach (TaskItem task in due)
            {
                string key = MakeKey(task, now.Date);
                PendingReminder pending = new PendingReminder();
                pending.Task = task;
                pending.Key = key;
                _queue.Enqueue(pending);
                _queuedKeys.Add(key);
            }
            ShowNext();
        }

        private void ShowNext()
        {
            if (_stopped || _window != null || _queue.Count == 0) return;
            PendingReminder pending = _queue.Dequeue();
            TaskItem task = pending.Task;
            string key = pending.Key;
            _queuedKeys.Remove(key);
            _window = new TaskReminderWindow(task);
            _window.ReminderClosed += delegate
            {
                _window = null;
                ShowNext();
                if (_window == null) RaiseShowingChanged(false);
            };
            RaiseShowingChanged(true);
            _window.ShowReminder();
            if (!Config.ShownTaskReminders.Contains(key))
            {
                Config.ShownTaskReminders.Add(key);
                CleanupHistory();
                Config.Save();
            }
            Logger.Info("显示任务全屏提醒 #" + task.Id + " " + task.Title);
        }

        private void RaiseShowingChanged(bool showing)
        {
            Action<bool> handler = ShowingChanged;
            if (handler != null) handler(showing);
        }

        private static string MakeKey(TaskItem task, DateTime date)
        {
            return task.Id + "|" + date.ToString("yyyy-MM-dd") + "|" + (task.IsRange ? task.EndTime : task.StartTime);
        }

        private static void CleanupHistory()
        {
            DateTime cutoff = TimeSync.Now.Date.AddDays(-14);
            List<string> kept = new List<string>();
            foreach (string key in Config.ShownTaskReminders)
            {
                string[] parts = key.Split('|');
                DateTime date;
                if (parts.Length >= 2 && DateTime.TryParseExact(parts[1], "yyyy-MM-dd",
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out date) && date >= cutoff)
                {
                    kept.Add(key);
                }
            }
            Config.ShownTaskReminders = kept;
        }
    }
}
