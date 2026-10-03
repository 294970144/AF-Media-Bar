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
    /// 判断登记与设置是否已经一致，即这次调用是否需要真的写注册表。
    /// 写入之前先问这一句，是为了让"修复"只补真正缺失或指向旧路径的那一条：
    /// 已指向本程序时不动注册表，既避免无谓的写入，也不会抹掉用户在注册表里手写的参数。
    /// Decides whether the registry already agrees with the setting, so a repair only writes what is genuinely missing or stale.
    /// Nothing is written while the entry still points at this application, which avoids pointless writes and never drops arguments
    /// the user typed into the registry by hand.
    /// </summary>
    /// <param name="enabled">设置里的意图。/ The intent stored in the settings.</param>
    /// <param name="registeredCommand">注册表里现有的命令行；项缺失时传 null。/ Command line currently in the registry, or null when the entry is absent.</param>
    /// <param name="executablePath">当前可执行文件的完整路径。/ Full path of the current executable.</param>
    public static bool ShouldWrite(bool enabled, string? registeredCommand, string executablePath)
    {
        // 关闭时只看"这一项在不在"，不看它指向谁：值名固定为本程序所有，登记项存在就是本程序的记录，
        // 即便它指向一个早已不存在的路径——那恰恰是用户要求"别再开机启动"时最该清掉的一条。
        // 指向别处的同名记录也一并清掉：留着它，开机的仍是某个程序，用户的意图没有兑现。
        // When disabling, only the presence of the entry matters, not what it points at: the value name belongs to this
        // application, so an existing entry is this application's record even when it points at a path that no longer exists
        // — which is exactly the one to remove when the user asked not to start at login. A same-named record pointing elsewhere
        // goes too: leaving it would still launch something at login, and the user's intent would not be honoured.
        if (!enabled)
            return registeredCommand is not null;

        return !Matches(registeredCommand, executablePath);
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
