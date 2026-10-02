// Diagnostics for where a media session is lost: on the catalog side (which side of the library dictionary the session is on) and on
// the build side (which silent branch dropped it).
using System.Globalization;
using System.Text;
using AFMediaBar.Classes.Services;
using static WindowsMediaController.MediaManager;

namespace AFMediaBar.Classes.Services.Media.Smtc;

/// <summary>
/// 快照构建丢弃一个会话的确切原因。
/// The exact reason a snapshot build dropped a session.
/// </summary>
public enum MediaSessionBuildDropReason
{
    /// <summary>库已清空 <c>ControlSession</c>，读不出任何内容。/ The library cleared <c>ControlSession</c>, so nothing can be read.</summary>
    NoControlSession = 0,

    /// <summary><c>TryGetMediaPropertiesAsync</c> 返回了空：会话还在，但系统不给媒体属性。/ <c>TryGetMediaPropertiesAsync</c> returned null: the session is still there but the OS gives no media properties.</summary>
    NoMediaProperties = 1,

    /// <summary>读属性时抛出 COM/无效操作/对象已释放异常。/ Reading the properties threw a COM, invalid-operation, or object-disposed exception.</summary>
    ReadThrew = 2
}

/// <summary>
/// 构建侧的丢失记账：把 <see cref="MediaSnapshotBuilder"/> 里的静默失败变成可读日志。
///
/// 这些分支原本一个都不记日志，而"系统有会话却读不出媒体"完全可以由它们造成——不记账就无法区分
/// "库字典里没有"与"字典里有但读不出"。真机日志里出现过一个 22 秒的窗口：看门狗反复报告
/// "系统有会话但目录读不到"，而同一窗口内一条快照都没发出来——那次连快照都没进到发布环节，
/// 因此只在动作触发点记日志的诊断覆盖不到它。
///
/// 记账刻意做成"按来源累计 + 达到阈值才输出"：正常换歌时 <c>ControlSession</c> 被清空是常事，
/// 逐次记录会把日志刷满，而故障态需要的是"同一个来源反复丢"这个事实。
/// Loss accounting for the build side: it turns the silent branches in <see cref="MediaSnapshotBuilder"/> into readable logs.
///
/// None of those branches logged anything, yet "the OS has sessions while nothing readable comes out" can be caused by exactly them —
/// without accounting there is no way to tell "absent from the library dictionary" from "present but unreadable". A real log held a
/// 22-second window where the watchdog kept reporting "the OS has sessions but the catalog reads nothing" while not a single snapshot
/// was published in that window, so diagnostics recorded only at action points miss it entirely.
///
/// Accounting is deliberately "count per source, print once a threshold is reached": a cleared <c>ControlSession</c> is routine during a
/// track change, and logging every occurrence would flood the file, whereas the broken state is characterized by the same source dropping
/// repeatedly.
/// </summary>
public static class MediaSessionDiagnostics
{
    /// <summary>同一来源累计到该次数就输出一次记账行。/ A source prints one accounting line once it has dropped this many times.</summary>
    private const int ReportThreshold = 3;

    private static readonly Lock Gate = new();
    private static readonly Dictionary<string, int> Counts = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> Reported = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 记一次构建侧丢弃。达到 <see cref="ReportThreshold"/> 次时输出该来源的累计行，之后同一来源不再输出。
    /// Records one build-side drop. Once <see cref="ReportThreshold"/> is reached it prints the running total for that source and stays quiet
    /// about that source afterwards.
    /// </summary>
    /// <param name="session">被丢弃的会话。/ The dropped session.</param>
    /// <param name="reason">丢弃原因。/ Why it was dropped.</param>
    /// <param name="detail">补充信息，通常是异常类型名。/ Extra detail, usually the exception type name.</param>
    public static void ReportBuildDrop(MediaSession? session, MediaSessionBuildDropReason reason, string? detail = null)
    {
        if (session is null)
        {
            return;
        }

        var source = ReadSourceId(session) is { Length: > 0 } id ? id : "(unknown)";

        int count;
        bool shouldReport;
        lock (Gate)
        {
            Counts.TryGetValue(source, out count);
            count++;
            Counts[source] = count;

            shouldReport = count >= ReportThreshold && Reported.Add(source);
        }

        if (!shouldReport)
        {
            return;
        }

        var line = new StringBuilder()
            .Append("构建丢会话/build-drop source=").Append(source)
            .Append(" 原因/reason=").Append(Describe(reason))
            .Append(" 累计/count=").Append(count.ToString(CultureInfo.InvariantCulture));

        if (!string.IsNullOrWhiteSpace(detail))
        {
            line.Append(" 明细/detail=").Append(detail);
        }

        AppLogService.Current?.Warn("Media", line.ToString());
    }

    /// <summary>清空记账状态：会话成功恢复后调用，避免旧计数污染下一次故障。/ Clears the accounting state; called after a session recovers so old counts do not pollute the next failure.</summary>
    public static void ResetBuildDrops()
    {
        lock (Gate)
        {
            Counts.Clear();
            Reported.Clear();
        }

        lock (RefreshGate)
        {
            RefreshesWithSessions = 0;
            RefreshReported = false;
        }

        lock (SelectorGate)
        {
            SelectorDrops = 0;
            SelectorReported = false;
        }
    }

    private static int RefreshesWithSessions;
    private static int SelectorDrops;
    private static bool SelectorReported;
    private static bool RefreshReported;
    private static readonly Lock RefreshGate = new();
    private static readonly Lock SelectorGate = new();

    /// <summary>
    /// 记一次"读到了会话的刷新"。用来判断刷新链路本身是否还活着：故障窗口内如果这个计数在涨而快照一条都没发，
    /// 丢点就在刷新入口（目录不可用、选择器抛异常一类），而不在会话内容本身。
    ///
    /// 与构建侧同理只报一次：空闲时每秒都会刷新，逐次输出没有意义，要看的是"刷新还在跑"这个事实本身。
    /// Records a refresh that did read sessions, which tells whether the refresh chain is alive at all: if this count climbs during a
    /// failure window while no snapshot is published, the loss sits at the refresh entry (an unavailable catalog, a throwing selector)
    /// rather than in the session content.
    ///
    /// Like the build side it reports once only: while idle a refresh runs every second, and what matters is the fact that the refresh
    /// chain is still running, not each occurrence.
    /// </summary>
    /// <param name="isConnected">当前已发布快照是否连接。只有断连窗口才记账：正常播放时"读到会话"是常态而非异常。
    /// Whether the published snapshot is currently connected. Only a disconnected window is counted: while playing normally, "read the
    /// sessions" is the normal case rather than an anomaly.</param>
    public static void NoteRefreshWithSessions(bool isConnected)
    {
        if (isConnected)
        {
            return;
        }

        int count;
        bool shouldReport;
        lock (RefreshGate)
        {
            RefreshesWithSessions++;
            count = RefreshesWithSessions;
            shouldReport = count >= ReportThreshold && !RefreshReported;
            RefreshReported = RefreshReported || shouldReport;
        }

        if (!shouldReport)
        {
            return;
        }

        AppLogService.Current?.Warn(
            "Media",
            $"刷新读到会话但未产出快照/refresh-read-no-snapshot 累计/count={count.ToString(CultureInfo.InvariantCulture)}");
    }

    /// <summary>
    /// 记一次"会话读到了但一个都没被选中"。这层丢弃与字典无关，只有计数到达阈值才输出，否则空闲时每次刷新都会记一次。
    /// Records a round that read sessions but selected none. This layer's loss is independent of the dictionary, and it only prints
    /// once the count reaches its threshold, otherwise every refresh while idle would be recorded.
    /// </summary>
    /// <param name="candidateCount">进入选择器的候选会话数。/ How many candidates reached the selector.</param>
    /// <param name="selectedKey">选择器当前选中的键；未选中任何会话时为 <see langword="null"/>。/ The selector's current key, or <see langword="null"/> when nothing is selected.</param>
    /// <param name="isCatalogStarted">目录是否已就绪。/ Whether the catalog is ready.</param>
    public static void ReportSelectorDrop(int candidateCount, string? selectedKey, bool isCatalogStarted)
    {
        int count;
        bool shouldReport;
        lock (SelectorGate)
        {
            SelectorDrops++;
            count = SelectorDrops;
            shouldReport = count >= ReportThreshold && !SelectorReported;
            SelectorReported = SelectorReported || shouldReport;
        }

        if (!shouldReport)
        {
            return;
        }

        AppLogService.Current?.Warn(
            "Media",
            $"选择器丢会话/selector-drop 候选/candidates={candidateCount.ToString(CultureInfo.InvariantCulture)} " +
            $"已选/selected={selectedKey ?? "(null)"} 目录就绪/catalog-started={isCatalogStarted} " +
            $"累计/count={count.ToString(CultureInfo.InvariantCulture)}");
    }

    /// <summary>把丢弃原因映射成稳定短词，便于在日志里 grep 统计。/ Maps a drop reason to a stable short word so it can be grepped and counted.</summary>
    /// <param name="reason">丢弃原因。/ Why the session was dropped.</param>
    public static string Describe(MediaSessionBuildDropReason reason) => reason switch
    {
        MediaSessionBuildDropReason.NoControlSession => "no-control",
        MediaSessionBuildDropReason.NoMediaProperties => "no-media-properties",
        MediaSessionBuildDropReason.ReadThrew => "read-threw",
        _ => "unknown"
    };

    private static string? ReadSourceId(MediaSession session)
    {
        try
        {
            return session.ControlSession?.SourceAppUserModelId;
        }
        catch (Exception exception) when (exception is InvalidOperationException or ObjectDisposedException)
        {
            // 读会话标识本身也可能失败（它已被库关闭）；标识缺失不该让记账反过来抛出异常。
            // Reading the session identifier can fail too (the library has closed it); a missing identifier must not make the accounting
            // throw in turn.
            return null;
        }
    }
}
