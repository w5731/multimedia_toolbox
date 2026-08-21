using System;
using System.Collections.Generic;

namespace MultimediaClient
{
    internal class TaskItem
    {
        public int Id;
        public string Title = "";
        public string StartTime = "";   // "HH:MM"
        public string EndTime = "";     // "" 表示时间点
        public string DateMode = "daily"; // once | range | weekly | daily
        public string DateStart = "";
        public string DateEnd = "";
        public string Weekdays = "";    // "1,2,3,4,5"

        public static TaskItem FromJson(IDictionary<string, object> d)
        {
            TaskItem t = new TaskItem();
            t.Id = Json.GetInt(d, "id", 0);
            t.Title = Json.GetString(d, "title", "");
            t.StartTime = Json.GetString(d, "start_time", "");
            t.EndTime = Json.GetString(d, "end_time", "");
            t.DateMode = Json.GetString(d, "date_mode", "daily");
            t.DateStart = Json.GetString(d, "date_start", "");
            t.DateEnd = Json.GetString(d, "date_end", "");
            t.Weekdays = Json.GetString(d, "weekdays", "");
            return t;
        }

        public Dictionary<string, object> ToJson()
        {
            Dictionary<string, object> d = new Dictionary<string, object>();
            d["id"] = Id; d["title"] = Title; d["start_time"] = StartTime; d["end_time"] = EndTime;
            d["date_mode"] = DateMode; d["date_start"] = DateStart; d["date_end"] = DateEnd;
            d["weekdays"] = Weekdays;
            return d;
        }

        /// <summary>该任务在某天是否生效</summary>
        public bool AppliesOn(DateTime date)
        {
            string ds = date.ToString("yyyy-MM-dd");
            switch (DateMode)
            {
                case "once":
                    return DateStart == ds;
                case "range":
                    return DateStart.Length > 0 && DateEnd.Length > 0 &&
                           String.CompareOrdinal(DateStart, ds) <= 0 && String.CompareOrdinal(ds, DateEnd) <= 0;
                case "weekly":
                    int dow = (int)date.DayOfWeek; if (dow == 0) dow = 7;
                    foreach (string part in Weekdays.Split(','))
                    {
                        int n;
                        if (int.TryParse(part.Trim(), out n) && n == dow) return true;
                    }
                    return false;
                case "daily":
                default:
                    return true;
            }
        }

        /// <summary>当天的开始时刻(固定格式解析,不受系统区域设置影响)</summary>
        public DateTime StartOn(DateTime date)
        {
            return DateTime.ParseExact(date.ToString("yyyy-MM-dd") + " " + StartTime,
                "yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture);
        }

        public bool IsRange { get { return EndTime.Length > 0; } }

        /// <summary>此刻是否处于该任务进行中(时间段任务在区间内;时间点任务开始后 5 分钟内)</summary>
        public bool IsActiveNow(DateTime now)
        {
            DateTime start = StartOn(now.Date);
            if (IsRange)
            {
                DateTime end = DateTime.ParseExact(now.Date.ToString("yyyy-MM-dd") + " " + EndTime,
                    "yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture);
                return now >= start && now < end;
            }
            return now >= start && now < start.AddMinutes(5);
        }

        public string TimeText
        {
            get { return IsRange ? StartTime + " - " + EndTime : StartTime; }
        }
    }

    internal class Settings
    {
        public int PopupSeconds = 10;
        public int Volume = 50;
        public string OverlayPosition = "right";
        public double FontScale = 1.0;

        public static Settings FromJson(IDictionary<string, object> d)
        {
            Settings s = new Settings();
            if (d == null) return s;
            s.PopupSeconds = Clamp(Json.GetInt(d, "popup_seconds", 10), 3, 120);
            s.Volume = Clamp(Json.GetInt(d, "volume", 50), 0, 100);
            string pos = Json.GetString(d, "overlay_position", "right");
            s.OverlayPosition = (pos == "left" || pos == "top") ? pos : "right";
            double fs = Json.GetDouble(d, "font_scale", 1.0);
            s.FontScale = fs < 0.6 ? 0.6 : (fs > 2 ? 2 : fs);
            return s;
        }

        public Dictionary<string, object> ToJson()
        {
            Dictionary<string, object> d = new Dictionary<string, object>();
            d["popup_seconds"] = PopupSeconds; d["volume"] = Volume;
            d["overlay_position"] = OverlayPosition; d["font_scale"] = FontScale;
            return d;
        }

        private static int Clamp(int v, int min, int max)
        {
            return v < min ? min : (v > max ? max : v);
        }
    }

    internal class Notice
    {
        public string Text = "";
        public bool Enabled = false;

        public static Notice FromJson(IDictionary<string, object> d)
        {
            Notice n = new Notice();
            if (d == null) return n;
            n.Text = Json.GetString(d, "text", "");
            n.Enabled = Json.GetBool(d, "enabled", false);
            return n;
        }

        public Dictionary<string, object> ToJson()
        {
            Dictionary<string, object> d = new Dictionary<string, object>();
            d["text"] = Text; d["enabled"] = Enabled;
            return d;
        }
    }

    internal class CallInfo
    {
        public int Id;
        public string Numbers = "";
        public string Destination = "办公室";
        public string Reason = "";

        public static CallInfo FromJson(IDictionary<string, object> d)
        {
            CallInfo c = new CallInfo();
            c.Id = Json.GetInt(d, "id", 0);
            c.Numbers = Json.GetString(d, "numbers", "");
            c.Destination = Json.GetString(d, "destination", "办公室");
            c.Reason = Json.GetString(d, "reason", "");
            return c;
        }
    }
}
