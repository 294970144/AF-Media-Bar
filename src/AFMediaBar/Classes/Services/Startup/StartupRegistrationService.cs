using AFMediaBar.Resources;
using System.Diagnostics;
using Microsoft.Win32;

namespace AFMediaBar.Classes.Services;

/// <summary>
/// 开机自动启动的注册表登记项：只管理当前用户的 Run 键，不申请提权，也不改动 Windows 自己的启动项审批状态。
/// The run-at-startup registration: it manages the current user's Run key only, asks for no elevation, and never touches Windows'
/// own per-entry approval state.
/// </summary>
public sealed class StartupRegistrationService
{
    /// <summary>注册表值名；固定值使重复写入始终覆盖同一条记录。 / Registry value name; a fixed name keeps repeated writes on one single entry.</summary>
    public const string ValueName = "AFMediaBar";

    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    /// <summary>最近一次 <see cref="Apply"/> 的失败原因；成功时为 null，取走后即为空。/ The failure from the most recent <see cref="Apply"/>, or null on success; cleared once taken.</summary>
    public string? TakeLastFailure()
    {
        var failure = LastFailure;
        LastFailure = null;
        return failure;
    }

    private string? LastFailure;

    /// <summary>
    /// 登记或取消登记开机自动启动。
    /// Registers or unregisters run-at-startup.
    /// </summary>
    /// <param name="enabled">是否随登录启动。/ Whether the application should start with the session.</param>
    /// <returns>失败原因；成功时为 null。/ The failure reason, or null on success.</returns>
    public string? Apply(bool enabled)
    {
        // 失败先留在这里再返回：启动时的核对发生在托盘图标建立之前，那一刻没有界面能承载提示，
        // 而开机自启动没登记上、用户不问就永远不会知道。由图标就绪后的那一步取走。
        // The failure is kept before returning: the startup reconciliation runs before the tray icon exists, when no surface
        // can carry a message, yet an unregistered item would go unnoticed. The ready tray takes it from here.
        LastFailure = ApplyCore(enabled);
        return LastFailure;
    }

    private string? ApplyCore(bool enabled)
    {
        var executable = ResolveExecutablePath();
        if (executable is null)
            return Translations.Get("Service.Startup.ExecutablePathUnavailable");

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
                            ?? Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
            if (key is null)
                return Translations.Get("Service.Startup.RegistryKeyUnavailable");

            if (enabled)
            {
                var command = StartupRegistrationPolicy.BuildCommandLine(executable);
                if (key.GetValue(ValueName) as string == command)
                    return null;

                key.SetValue(ValueName, command, RegistryValueKind.String);
            }
            else if (key.GetValue(ValueName) is not null)
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }

            return null;
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"[StartupRegistrationService] Apply failed: {exception}");
            return exception.Message;
        }
    }

    /// <summary>
    /// 读取当前是否已登记开机自动启动。注册表不可读时返回 null，调用方据此保留设置里的意图而不谎报状态。
    /// Reads whether run-at-startup is currently registered. An unreadable registry returns null so callers can keep the stored
    /// intent instead of reporting a state they could not verify.
    /// </summary>
    public bool? IsRegistered()
    {
        var executable = ResolveExecutablePath();
        if (executable is null)
            return null;

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            var command = key?.GetValue(ValueName) as string;
            return StartupRegistrationPolicy.Matches(command, executable);
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"[StartupRegistrationService] Read failed: {exception}");
            return null;
        }
    }

    private static string? ResolveExecutablePath()
    {
        var path = Environment.ProcessPath;
        return string.IsNullOrWhiteSpace(path) ? null : path;
    }
}
