using System;
using System.IO;
using System.Media;
using System.Reflection;
using System.Runtime.InteropServices;

namespace MultimediaClient
{
    #region CoreAudio COM 接口(Vista 及以上,Win7 原生支持)
    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    internal class MMDeviceEnumeratorCom { }

    [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMMDeviceEnumerator
    {
        int EnumAudioEndpoints(int dataFlow, int dwStateMask, out IntPtr ppDevices);
        int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice ppDevice);
        int RegisterEndpointNotificationCallback(IntPtr pClient);
        int UnregisterEndpointNotificationCallback(IntPtr pClient);
    }

    [Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMMDevice
    {
        int Activate(ref Guid iid, int dwClsCtx, IntPtr pActivationParams,
            [MarshalAs(UnmanagedType.IUnknown)] out object ppInterface);
        int OpenPropertyStore(int stgmAccess, out IntPtr ppProperties);
        int GetId([MarshalAs(UnmanagedType.LPWStr)] out string ppstrId);
        int GetState(out int pdwState);
    }

    [Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IAudioEndpointVolume
    {
        int RegisterControlChangeNotify(IntPtr pNotify);
        int UnregisterControlChangeNotify(IntPtr pNotify);
        int GetChannelCount(out int pnChannelCount);
        int SetMasterVolumeLevel(float fLevelDB, Guid pguidEventContext);
        int SetMasterVolumeLevelScalar(float fLevel, Guid pguidEventContext);
        int GetMasterVolumeLevel(out float pfLevelDB);
        int GetMasterVolumeLevelScalar(out float pfLevel);
        int SetChannelVolumeLevel(uint nChannel, float fLevelDB, Guid pguidEventContext);
        int SetChannelVolumeLevelScalar(uint nChannel, float fLevel, Guid pguidEventContext);
        int GetChannelVolumeLevel(uint nChannel, out float pfLevelDB);
        int GetChannelVolumeLevelScalar(uint nChannel, out float pfLevel);
        int SetMute([MarshalAs(UnmanagedType.Bool)] bool bMute, Guid pguidEventContext);
        int GetMute([MarshalAs(UnmanagedType.Bool)] out bool pbMute);
    }
    #endregion

    /// <summary>铃声播放与系统音量控制</summary>
    internal static class AudioService
    {
        private static SoundPlayer _player;

        /// <summary>若系统静音:取消静音并把主音量设为指定百分比(默认 50)</summary>
        public static void EnsureAudible(int volumePercent)
        {
            try
            {
                IAudioEndpointVolume vol = GetEndpointVolume();
                if (vol == null) return;
                bool muted;
                if (vol.GetMute(out muted) == 0 && muted)
                {
                    vol.SetMute(false, Guid.Empty);
                    vol.SetMasterVolumeLevelScalar(volumePercent / 100f, Guid.Empty);
                    Logger.Info("系统处于静音,已自动取消静音并调整音量到 " + volumePercent + "%");
                }
                Marshal.ReleaseComObject(vol);
            }
            catch (Exception ex)
            {
                Logger.Error("调整系统音量失败", ex);
            }
        }

        public static void StartBell()
        {
            try
            {
                if (_player == null)
                {
                    string path = Path.Combine(Config.AppDir, "bell.wav");
                    if (!File.Exists(path))
                    {
                        // 从嵌入资源释放到程序目录
                        using (Stream src = Assembly.GetExecutingAssembly()
                            .GetManifestResourceStream("MultimediaClient.bell.wav"))
                        {
                            if (src == null) return;
                            using (FileStream dst = File.Create(path)) src.CopyTo(dst);
                        }
                    }
                    _player = new SoundPlayer(path);
                    _player.Load();
                }
                _player.PlayLooping();
            }
            catch (Exception ex)
            {
                Logger.Error("播放铃声失败", ex);
            }
        }

        public static void StopBell()
        {
            try
            {
                if (_player != null) _player.Stop();
            }
            catch { }
        }

        private static IAudioEndpointVolume GetEndpointVolume()
        {
            IMMDeviceEnumerator enumerator = null;
            IMMDevice device = null;
            try
            {
                enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorCom();
                if (enumerator.GetDefaultAudioEndpoint(0 /*eRender*/, 1 /*eMultimedia*/, out device) != 0)
                    return null;
                Guid iid = typeof(IAudioEndpointVolume).GUID;
                object o;
                if (device.Activate(ref iid, 1, IntPtr.Zero, out o) != 0) return null;
                return (IAudioEndpointVolume)o;
            }
            finally
            {
                if (device != null) Marshal.ReleaseComObject(device);
                if (enumerator != null) Marshal.ReleaseComObject(enumerator);
            }
        }
    }
}
