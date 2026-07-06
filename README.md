# 语音日志 (VoiceRecoder)

Windows 11 托盘语音日志应用，支持 Windows 内置语音识别与 Qwen DashScope API 两种方案可切换，按日期本地存储文字记录。

## 技术栈

- .NET 10 + WinUI 3 (Windows App SDK)
- H.NotifyIcon.WinUI（无窗口托盘常驻）
- 本地 JSON 按日期分片存储

## 项目结构

```
VoiceRecoder.slnx
src/VoiceRecoder/          # WinUI 3 主项目
```

## 环境要求

- Windows 10 19041+ / Windows 11
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- [Windows App SDK](https://learn.microsoft.com/windows/apps/windows-app-sdk/)
- 开发者模式（WinUI 打包应用需要）

```powershell
winget install Microsoft.DotNet.SDK.10
winget install Microsoft.WinAppCLI
dotnet new install Microsoft.WindowsAppSDK.WinUI.CSharp.Templates
```

## 构建与运行

```powershell
dotnet build src/VoiceRecoder/VoiceRecoder.csproj
dotnet run --project src/VoiceRecoder/VoiceRecoder.csproj
```

启动后应用以托盘图标常驻，无可见主窗口。右键托盘图标可打开上下文菜单。


Get-Process -Name "VoiceRecoder" -ErrorAction SilentlyContinue | Stop-Process -Force
dotnet run --project src/VoiceRecoder/VoiceRecoder.csproj