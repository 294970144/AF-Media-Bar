namespace AFMediaBar.Classes.Services;

/// <summary>
/// 开机自动启动的纯策略：命令行拼装与"这条记录是否属于当前程序"的判定。
/// Pure policy for run-at-startup: command-line assembly and deciding whether an existing entry belongs to this application.
/// </summary>
public static class StartupRegistrationPolicy
{
    /// <summary>
    /// 拼装启动项命令行。路径一律加引号：程序目录可能含空格，不加引号时 Windows 会把第一个空格当作参数分隔符，
    /// 于是启动的是另一个路径，用户看到的只是"开机后没启动"。
    /// Builds the startup entry's command line. The path is always quoted: the installation directory can contain spaces, and
    /// without quotes Windows splits at the first space, launching a different path while the user only sees that nothing started.
    /// </summary>
    /// <param name="executablePath">可执行文件的完整路径。/ Full path of the executable.</param>
    public static string BuildCommandLine(string executablePath) => $"\"{executablePath.Trim().Trim('"')}\"";

    /// <summary>
    /// 判断启动项记录是否指向当前程序。比较时忽略外层引号与大小写，并允许记录里带参数。
    /// Decides whether a startup entry points at this application, ignoring surrounding quotes and case, and tolerating arguments.
    /// </summary>
    /// <param name="registeredCommand">注册表里的命令行；缺失时传 null。/ Command line read from the registry, or null when absent.</param>
    /// <param name="executablePath">当前可执行文件的完整路径。/ Full path of the current executable.</param>
    public static bool Matches(string? registeredCommand, string executablePath)
    {
        if (string.IsNullOrWhiteSpace(registeredCommand) || string.IsNullOrWhiteSpace(executablePath))
            return false;

        var registered = ExtractExecutable(registeredCommand);
        return registered is not null &&
               string.Equals(registered, executablePath.Trim().Trim('"'), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 判断审批记录是否表示"被禁用"。认出与 <see cref="TryBuildEnabledApproval"/> 相同的一组取值，两处分叉会让开关显示与实际相反。
    /// Decides whether an approval record says the item is disabled, recognizing the same states as
    /// <see cref="TryBuildEnabledApproval"/>: if the two disagreed, the switch would display the opposite of reality.
    /// </summary>
    /// <param name="approval">注册表里的审批记录；该项不存在时传 null。/ Approval record read from the registry, or null when absent.</param>
    public static bool IsDisabled(byte[]? approval) =>
        approval is { Length: >= 12 } && BitConverter.ToInt32(approval, 0) is 0x03 or 0x09;

    /// <summary>
    /// 把任务管理器留下的"已禁用"记录还原成启用，产出要写回的审批记录。
    ///
    /// Windows 用两处表达启动项：Run 键存命令，Explorer\StartupApproved 下存启用/禁用的二进制标记。任务管理器禁用时
    /// **不删 Run 项**、只改后者，所以程序内重新打开开关时 Run 项回来了而禁用标记仍在——界面显示"开启"而实际不启动。
    /// 只认2/8（启用）与 3/9（禁用）这四个值并按固定映射还原：状态字节是位对，算出来的值可能得到格式里不存在的
    /// 取值，而写坏审批记录比不禁用更难排查。
    /// Produces the approval record that puts a Task Manager-disabled entry back to enabled.
    ///
    /// Windows expresses a startup item in two places: the Run key holds the command, while a binary record under
    /// Explorer\StartupApproved holds the state. Disabling does **not** delete the Run entry, it only rewrites the approval —
    /// so re-enabling from inside the program brings the command back while the disabled mark stays, and the switch reads on
    /// while nothing starts. Only the four states 2/8 (enabled) and 3/9 (disabled) are restored through a fixed mapping,
    /// because the state is a bit pair and a computed value can fall outside the format.
    /// </summary>
    /// <param name="existing">注册表里现有的审批记录；该项不存在时传 null。/ Approval record read from the registry, or null when absent.</param>
    /// <param name="enabled">要写回的启用记录；返回 false 时无值。/ The enabled record to write back; no value when this returns false.</param>
    /// <returns>是否需要写回。/ Whether a record has to be written back.</returns>
    public static bool TryBuildEnabledApproval(byte[]? existing, out byte[] enabled)
    {
        enabled = [];

        // 记录不存在 = 用户从未在任务管理器里动过这一项，Windows 默认视为启用，程序不该凭空造一条。
        // An absent record means the user never touched the item and Windows treats it as enabled, so do not invent one.
        if (existing is null || existing.Length < 12)
            return false;

        var restored = BitConverter.ToInt32(existing, 0) switch
        {
            0x03 => 0x02,
            0x09 => 0x08,
            _ => 0,
        };

        if (restored == 0)
            return false;

        // 时间戳一并清零，与任务管理器启用时写的一样。
        // The timestamp is zeroed as well, exactly as Task Manager does when enabling.
        enabled = new byte[12];
        BitConverter.TryWriteBytes(enabled.AsSpan(0, 4), restored);
        return true;
    }

    private static string? ExtractExecutable(string command)
    {
        var trimmed = command.Trim();
        if (trimmed.Length == 0)
            return null;

        if (trimmed[0] == '"')
        {
            var closing = trimmed.IndexOf('"', 1);
            return closing > 1 ? trimmed[1..closing] : null;
        }

        var separator = trimmed.IndexOf(' ');
        return separator < 0 ? trimmed : trimmed[..separator];
    }
}
