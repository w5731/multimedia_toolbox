@echo off
rem Build MultimediaClient.exe (single file, zero dependency, targets .NET Framework 4.x)
setlocal
set FRAMEWORK=%SystemRoot%\Microsoft.NET\Framework64\v4.0.30319
if not exist "%FRAMEWORK%\csc.exe" set FRAMEWORK=%SystemRoot%\Microsoft.NET\Framework\v4.0.30319

"%FRAMEWORK%\csc.exe" /nologo /target:winexe /platform:anycpu /utf8output /optimize+ ^
  /r:System.dll /r:System.Core.dll /r:System.Xaml.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Web.Extensions.dll ^
  /r:"%FRAMEWORK%\WPF\PresentationFramework.dll" /r:"%FRAMEWORK%\WPF\PresentationCore.dll" /r:"%FRAMEWORK%\WPF\WindowsBase.dll" ^
  /resource:assets\bell.wav,MultimediaClient.bell.wav ^
  /out:MultimediaClient.exe ^
  src\Win32.cs src\Json.cs src\Models.cs src\Config.cs src\Logger.cs src\DataStore.cs ^
  src\CacheService.cs src\ApiClient.cs src\PollService.cs src\AudioService.cs src\AutoStart.cs ^
  src\UiKit.cs src\OverlayWindow.cs src\CallPopupWindow.cs src\PairingWindow.cs ^
  src\SettingsWindow.cs src\PasswordWindow.cs src\TrayService.cs src\AppHost.cs src\Program.cs

if %errorlevel%==0 (
  echo.
  echo === BUILD OK: %~dp0MultimediaClient.exe ===
) else (
  echo.
  echo === BUILD FAILED ===
  exit /b 1
)
