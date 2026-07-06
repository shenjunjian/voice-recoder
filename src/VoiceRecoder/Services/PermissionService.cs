using Windows.Media.Capture;
using Windows.Security.Authorization.AppCapabilityAccess;

namespace VoiceRecoder.Services;

public sealed class PermissionService
{
    public const string FirstRunGuideText =
        "语音日志需要访问麦克风以进行语音识别。\n\n" +
        "• Windows 内置方案：请确保已在「设置 → 时间和语言 → 语音」中安装中文语音包，并在「设置 → 隐私 → 语音」中开启在线语音识别。\n" +
        "• Qwen API 方案：需要网络连接，API Key 仅保存在本机，不会上传到代码仓库。\n\n" +
        "首次录制时系统可能会弹出麦克风权限请求，请选择「允许」。";

    public const string MicrophoneDeniedMessage =
        "需要麦克风权限才能录制。请在「设置 → 隐私和安全性 → 麦克风」中允许「语音日志」访问麦克风。";

    public async Task<bool> EnsureMicrophoneAccessAsync()
    {
        try
        {
            var capability = AppCapability.Create("microphone");
            var accessStatus = capability.CheckAccess();

            if (accessStatus == AppCapabilityAccessStatus.UserPromptRequired)
            {
                accessStatus = await capability.RequestAccessAsync();
            }

            if (accessStatus == AppCapabilityAccessStatus.Allowed)
            {
                return true;
            }
        }
        catch
        {
            // Fall back to MediaCapture initialization below.
        }

        try
        {
            var capture = new MediaCapture();
            var settings = new MediaCaptureInitializationSettings
            {
                StreamingCaptureMode = StreamingCaptureMode.Audio,
                MediaCategory = MediaCategory.Speech
            };

            await capture.InitializeAsync(settings);
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
        catch (Exception ex) when (ex.HResult == unchecked((int)0x80070005))
        {
            return false;
        }
    }
}
