using System;
using System.Reflection;
using Microsoft.Win32;

namespace MultimediaClient
{
    /// <summary>开机自启:HKCU Run 注册表项(无需管理员权限)</summary>
    internal static class AutoStart
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "MultimediaClient";

        public static bool IsEnabled()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKey, false))
                {
                    if (key == null) return false;
                    object v = key.GetValue(ValueName);
                    return v != null && v.ToString().Length > 0;
                }
            }
            catch { return false; }
        }

        public static void Set(bool enable)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKey, true))
                {
                    if (key == null) return;
                    if (enable)
                    {
                        string exe = Assembly.GetExecutingAssembly().Location;
                        key.SetValue(ValueName, "\"" + exe + "\"");
                    }
                    else
                    {
                        key.DeleteValue(ValueName, false);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error("设置开机自启失败", ex);
            }
        }
    }
}
