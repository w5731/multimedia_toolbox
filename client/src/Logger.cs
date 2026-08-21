using System;
using System.IO;

namespace MultimediaClient
{
    /// <summary>本地日志:写入程序目录 logs/,按日期分文件,异常不抛出</summary>
    internal static class Logger
    {
        private static string _logFile;
        private static readonly object _lock = new object();

        public static void Init()
        {
            try
            {
                string dir = Path.Combine(Config.AppDir, "logs");
                Directory.CreateDirectory(dir);
                _logFile = Path.Combine(dir, "client-" + DateTime.Now.ToString("yyyyMMdd") + ".log");
            }
            catch { }
        }

        public static void Info(string msg)
        {
            Write("INFO", msg, null);
        }

        public static void Error(string msg, Exception ex)
        {
            Write("ERROR", msg, ex);
        }

        private static void Write(string level, string msg, Exception ex)
        {
            try
            {
                if (_logFile == null) return;
                string line = "[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "] [" + level + "] " + msg;
                if (ex != null) line += Environment.NewLine + ex;
                lock (_lock)
                {
                    // 单文件超过 2MB 则截断重写,防止老机器磁盘被日志占满
                    if (File.Exists(_logFile) && new FileInfo(_logFile).Length > 2 * 1024 * 1024)
                    {
                        File.WriteAllText(_logFile, line + Environment.NewLine);
                    }
                    else
                    {
                        File.AppendAllText(_logFile, line + Environment.NewLine);
                    }
                }
            }
            catch { }
        }
    }
}
