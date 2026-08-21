using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;

namespace MultimediaClient
{
    internal class ApiResult
    {
        public bool Ok;
        public int HttpStatus;
        public string Error = "";
        public Dictionary<string, object> Data;
    }

    /// <summary>
    /// HTTP 客户端:同步 HttpWebRequest(在后台线程调用),无第三方依赖。
    /// 短超时 + 调用方退避重试,保证老机器上的可靠性。
    /// </summary>
    internal class ApiClient
    {
        public string BaseUrl = "";
        public int TimeoutMs = 5000;

        public ApiResult Post(string path, Dictionary<string, object> body)
        {
            return Request("POST", path, body == null ? null : Json.Serialize(body));
        }

        public ApiResult Get(string path, Dictionary<string, string> query)
        {
            StringBuilder sb = new StringBuilder(path);
            if (query != null && query.Count > 0)
            {
                sb.Append('?');
                bool first = true;
                foreach (KeyValuePair<string, string> kv in query)
                {
                    if (!first) sb.Append('&');
                    sb.Append(Uri.EscapeDataString(kv.Key)).Append('=').Append(Uri.EscapeDataString(kv.Value));
                    first = false;
                }
            }
            return Request("GET", sb.ToString(), null);
        }

        private ApiResult Request(string method, string path, string jsonBody)
        {
            ApiResult r = new ApiResult();
            try
            {
                string url = BaseUrl.TrimEnd('/') + path;
                HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
                req.Method = method;
                req.Timeout = TimeoutMs;
                req.ReadWriteTimeout = TimeoutMs;
                req.KeepAlive = false;
                req.UserAgent = "MultimediaClient/1.0";
                // 轮询接口禁用代理自动探测,避免校园网环境下首卡几秒
                req.Proxy = null;

                if (jsonBody != null)
                {
                    byte[] bytes = Encoding.UTF8.GetBytes(jsonBody);
                    req.ContentType = "application/json; charset=utf-8";
                    req.ContentLength = bytes.Length;
                    using (Stream s = req.GetRequestStream()) s.Write(bytes, 0, bytes.Length);
                }

                using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
                {
                    r.HttpStatus = (int)resp.StatusCode;
                    using (StreamReader sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                    {
                        string text = sr.ReadToEnd();
                        r.Data = Json.ParseObject(text);
                    }
                }
                r.Ok = r.Data != null && Json.GetBool(r.Data, "ok", false);
                if (!r.Ok && r.Data != null) r.Error = Json.GetString(r.Data, "error", "");
            }
            catch (WebException wex)
            {
                HttpWebResponse resp = wex.Response as HttpWebResponse;
                if (resp != null)
                {
                    r.HttpStatus = (int)resp.StatusCode;
                    try
                    {
                        using (StreamReader sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                        {
                            r.Data = Json.ParseObject(sr.ReadToEnd());
                            if (r.Data != null) r.Error = Json.GetString(r.Data, "error", "");
                        }
                    }
                    catch { }
                    resp.Close();
                }
                else
                {
                    r.Error = "网络错误:" + wex.Message;
                }
            }
            catch (Exception ex)
            {
                r.Error = ex.Message;
            }
            return r;
        }
    }
}
