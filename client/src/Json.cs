using System;
using System.Collections;
using System.Collections.Generic;
using System.Web.Script.Serialization;

namespace MultimediaClient
{
    /// <summary>JSON 解析辅助(基于内置 JavaScriptSerializer,无第三方依赖)</summary>
    internal static class Json
    {
        private static readonly JavaScriptSerializer _ser = CreateSerializer();

        private static JavaScriptSerializer CreateSerializer()
        {
            JavaScriptSerializer s = new JavaScriptSerializer();
            s.MaxJsonLength = 8 * 1024 * 1024;
            s.RecursionLimit = 64;
            return s;
        }

        public static string Serialize(object obj)
        {
            return _ser.Serialize(obj);
        }

        public static Dictionary<string, object> ParseObject(string json)
        {
            object o = _ser.DeserializeObject(json);
            return o as Dictionary<string, object>;
        }

        public static string GetString(IDictionary<string, object> d, string key, string def)
        {
            object v;
            if (d != null && d.TryGetValue(key, out v) && v != null) return Convert.ToString(v);
            return def;
        }

        public static int GetInt(IDictionary<string, object> d, string key, int def)
        {
            object v;
            if (d != null && d.TryGetValue(key, out v) && v != null)
            {
                int n;
                if (int.TryParse(Convert.ToString(v), out n)) return n;
            }
            return def;
        }

        public static double GetDouble(IDictionary<string, object> d, string key, double def)
        {
            object v;
            if (d != null && d.TryGetValue(key, out v) && v != null)
            {
                double n;
                if (double.TryParse(Convert.ToString(v), out n)) return n;
            }
            return def;
        }

        public static bool GetBool(IDictionary<string, object> d, string key, bool def)
        {
            object v;
            if (d != null && d.TryGetValue(key, out v) && v != null)
            {
                if (v is bool) return (bool)v;
                int n;
                if (int.TryParse(Convert.ToString(v), out n)) return n != 0;
            }
            return def;
        }

        public static Dictionary<string, object> GetObject(IDictionary<string, object> d, string key)
        {
            object v;
            if (d != null && d.TryGetValue(key, out v)) return v as Dictionary<string, object>;
            return null;
        }

        public static List<object> GetArray(IDictionary<string, object> d, string key)
        {
            List<object> list = new List<object>();
            object v;
            if (d != null && d.TryGetValue(key, out v) && v != null)
            {
                // JavaScriptSerializer 把数组反序列化为 object[],而非 ArrayList
                IEnumerable en = v as IEnumerable;
                if (en != null && !(v is string) && !(v is IDictionary<string, object>))
                {
                    foreach (object item in en) list.Add(item);
                }
            }
            return list;
        }
    }
}
