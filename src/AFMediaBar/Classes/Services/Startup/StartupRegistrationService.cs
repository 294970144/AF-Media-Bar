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
    private const string ApprovalKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";

    /// <summary>
    /// 登记或取消登记开机自动启动。
    /// Registers or unregisters run-at-startup.
    /// </summary>
    /// <param name="enabled">是否随登录启动。/ Whether the application should start with the session.</param>
    /// <param name="userAsked">这次登记是否源于用户主动操作。/ Whether the caller acts on an explicit user request.</param>
    /// <returns>失败原因；成功时为 null。/ The failure reason, or null on success.</returns>
    public string? Apply(bool enabled, bool userAsked = false)
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

                // 命令行已就位时不重写，但仍要往下走：那一项可能正记着任务管理器里的禁用状态，用户点了开关就得清掉。
                // The command line is already in place, so it is not rewritten, but the flow continues: the item may still carry
                // a Task Manager "disabled" mark, and a click on the switch has to clear it.
                if (key.GetValue(ValueName) as string != command)
                    key.SetValue(ValueName, command, RegistryValueKind.String);

                // 启动核对不能抹掉禁用标记：那一刻注册表的状态与用户在任务管理器里禁用过完全一样，分不出来。
                // A startup reconciliation must not clear the mark: the registry then looks exactly as it does after the user
                // disabled the item in Task Manager, and the two cannot be told apart.
                if (userAsked)
                    RestoreApproval();
            }
            else if (key.GetValue(ValueName) is not null)
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }

            // 缓存必须在这里同步：本次调用改的就是它要读的那份注册表，调用方不必各自记得刷新。
            // The cache is synced here: this call just changed the very registry it reads, so callers need not remember to.
            RefreshDisabledState();

            return null;
        }
        catch (Exception exception)
        {
            Debug.WriteLine($"[StartupRegistrationService] Apply failed: {exception}");
            return exception.Message;
        }
    }

    /// <summary>
    /// 用户主动打开开关时，抹掉任务管理器留下的"已禁用"标记。启动核对不能做这件事：那一刻注册表的状态与用户刚打开
    /// 开关完全一样，无法分辨。失败只记录不抛出——Run 项已经写成功，审批记录抹不掉是另一件事。
    /// Clears the "disabled" mark left in Task Manager, but only on an explicit user request.
    ///
    /// A startup reconciliation must not do this: the registry then looks identical whether the user disabled the item in Task
    /// Manager or just re-enabled it here. A failure is logged rather than thrown, since the Run entry is already written.
    /// </summary>
    private static void RestoreApproval()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(ApprovalKeyPath, writable: true)
                            ?? Registry.CurrentUser.CreateSubKey(ApprovalKeyPath, writable: true);
            if (key is null)
            {
                AppLogService.Current?.Warn("Startup", "审批键打不开 / Approval key unavailable");
                return;
            }

            var before = key.GetValue(ValueName) as byte[];
            if (!StartupRegistrationPolicy.TryBuildEnabledApproval(before, out var enabled))
            {
                AppLogService.Current?.Info(
                    "Startup",
                    $"无需改动审批记录 / approval record left as is: {Describe(before)}");
                return;
            }

            key.SetValue(ValueName, enabled, RegistryValueKind.Binary);

            // 回读一次确认写进去了：SetValue 返回成功不代表值就是预期的那个。
            // Read it back: a successful SetValue does not mean the value is the intended one.
            var after = key.GetValue(ValueName) as byte[];
            AppLogService.Current?.Info(
                "Startup",
                $"已还原审批记录 / approval restored: {Describe(before)} → {Describe(after)}");
        }
        catch (Exception exception)
        {
            // 这一步失败会让开关点了没反应，而调试输出不落文件：出错时必须留下证据。
            // Failing here leaves the switch dead to a click, and debug output leaves no file: keep the evidence.
            AppLogService.Current?.Error("Startup", "清除任务管理器禁用标记失败 / Failed to clear the disabled mark", exception);
        }
    }

    /// <summary>
    /// 把审批记录的首字节写成人看得懂的样子，其余字节不参与判断，一并省略。
    /// Renders an approval record as its meaningful first bytes, skipping the rest that take no part in the decision.
    /// </summary>
    private static string Describe(byte[]? record)
    {
        if (record is null)
            return "<不存在 / absent>";

        var head = Math.Min(record.Length, 4);
        return $"{record.Length}B:{string.Join(' ', record.AsSpan(0, head).ToArray().Select(b => b.ToString("X2")))}";
    }

    /// <summary>
    /// 用户是否在任务管理器里禁用了这一项；禁用标记由 <see cref="RefreshDisabledState"/> 读入缓存。
    /// Whether the user disabled this item in Task Manager; the mark itself is cached by <see cref="RefreshDisabledState"/>.
    /// </summary>
    public bool IsDisabledInTaskManager { get; private set; }

    /// <summary>
    /// 重新读一次注册表，更新缓存。用户可以在程序运行期间去任务管理器改开关，本服务无从感知那一刻，因此需要这个
    /// 明确的刷新时机；页面打开时调用它，代价是两次注册表读取，而那一页本来也不会频繁进出。
    /// Reads the registry again and updates the cached state.
    ///
    /// The user can change the item in Task Manager while the program runs and this service cannot notice that moment, so it needs
    /// an explicit refresh point. Opening the page calls it: two registry reads on a page that is not entered repeatedly is a fair
    /// price.
    /// </summary>
    public void RefreshDisabledState()
    {
        // 缓存而非每次现读：开关的绑定会在通知与布局时反复求值，读注册表既慢、又会因时机不同给出过期答案。
        // Cached rather than read on demand: the switch binding is re-evaluated on every notification and layout pass, so
        // hitting the registry each time is slow and liable to answer with a stale reading.
        IsDisabledInTaskManager = IsRegistered() is not true
            ? false
            : StartupRegistrationPolicy.IsDisabled(ReadApproval());

        AppLogService.Current?.Info(
            "Startup",
            $"禁用状态已刷新 / disabled state refreshed: {IsDisabledInTaskManager}");
    }

    private static byte[]? ReadApproval()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(ApprovalKeyPath, writable: false);
            return key?.GetValue(ValueName) as byte[];
        }
        catch (Exception exception)
        {
            // 读失败按"没有禁用"处理，但必须留痕：这个判断直接决定开关显示什么，静默兜底会让人以为读到了禁用态。
            // A read failure counts as "not disabled", but it must leave a trace: this decision drives what the switch shows,
            // and a silent fallback would suggest a disabled state had been read when it had not.
            AppLogService.Current?.Warn("Startup", "读取任务管理器禁用标记失败 / Failed to read the disabled mark", exception);
            return null;
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
