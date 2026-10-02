// Compares what Windows publishes with what the third-party dictionary holds and names the step that dropped each session.
namespace AFMediaBar.Classes.Services.Media.Smtc;

/// <summary>
/// 会话在「系统 → 库字典 → 有效性检查 → 来源过滤」这条链路上的去向。
/// Where a session ended up on the path from the OS through the library dictionary and the validity check to the source filter.
/// </summary>
public enum MediaSessionLossReason
{
    /// <summary>全程保留，并被选为当前来源。/ Kept all the way and selected as the current source.</summary>
    None = 0,

    /// <summary>系统发布了它，但第三方库的字典里没有。/ The OS published it while the library dictionary does not hold it.</summary>
    MissingFromDictionary,

    /// <summary>字典里有它，但 <c>ControlSession</c> 已被库清空，读不出内容。/ The dictionary holds it but the library cleared its <c>ControlSession</c>, so nothing can be read.</summary>
    NotUsable,

    /// <summary>本身读得出，但被来源允许列表挡掉。/ Readable on its own, but excluded by the source allow-list.</summary>
    FilteredOut,

    /// <summary>读得出且未被过滤，但没有被选为当前来源。/ Readable and unfiltered, yet not selected as the current source.</summary>
    NotSelected
}

/// <summary>
/// 某一时刻目录两侧的对照：Windows 报告了哪些会话、库字典里有哪些、其中哪些已经读不出内容。
///
/// 这是纯快照，不含任何恢复动作：它的用途是把「系统说有会话」拆成逐个来源的去向，
/// 从而回答会话究竟丢在字典缺失、有效性检查还是来源过滤上。
/// A snapshot of both sides of the catalog at one instant: which sessions Windows reports, which the library dictionary holds, and
/// which of those can no longer be read.
///
/// This is a pure snapshot with no recovery action: it exists to break "the OS says there are sessions" down into a per-source verdict,
/// answering whether a session was lost to a missing dictionary entry, the validity check, or the source filter.
/// </summary>
/// <param name="OsSourceIds">Windows 报告的会话来源标识。/ Source identifiers Windows reports.</param>
/// <param name="LibrarySourceIds">库字典里存在的会话标识。/ Session identifiers present in the library dictionary.</param>
/// <param name="UnusableSourceIds">字典里存在但 <c>ControlSession</c> 已被清空、读不出内容的会话标识。/ Identifiers the dictionary holds whose <c>ControlSession</c> was cleared and that therefore read nothing.</param>
public sealed record MediaSessionCatalogState(
    IReadOnlyList<string> OsSourceIds,
    IReadOnlyList<string> LibrarySourceIds,
    IReadOnlyList<string> UnusableSourceIds)
{
    /// <summary>目录不可用（已释放或正在替换）时的空快照。/ An empty snapshot for when the catalog is unavailable (disposed or being replaced).</summary>
    public static MediaSessionCatalogState Unavailable { get; } = new([], [], []);
}

/// <summary>
/// 会话去向的纯判定与日志行格式化：把两侧快照变成一行可直接比对、可用固定关键字 grep 的诊断文本。
/// 独立于目录与来源注册表，因此判定与排版都能单独单测。
/// Pure verdict and log-line formatting for where sessions went: it turns two-sided snapshots into one directly comparable diagnostic
/// line that can be grepped by fixed keywords. It is independent of the catalog and the source registry, so both the verdict and the
/// layout are unit-testable on their own.
/// </summary>
public static class MediaSessionDiagnosticsPolicy
{
    /// <summary>
    /// 判定单个会话的去向。前置条件是：不在字典里时后两个判定无意义，调用方应直接得出
    /// <see cref="MediaSessionLossReason.MissingFromDictionary"/>。
    /// Decides where one session ended up. A session outside the dictionary makes the later checks meaningless, so callers must settle on
    /// <see cref="MediaSessionLossReason.MissingFromDictionary"/> first.
    /// </summary>
    /// <param name="isUsable">会话是否仍持有可读取的 <c>ControlSession</c>。/ Whether the session still holds a readable <c>ControlSession</c>.</param>
    /// <param name="isAllowedByFilter">来源允许列表是否放行。/ Whether the source allow-list admits it.</param>
    /// <param name="isSelected">是否被选为当前来源。/ Whether it was selected as the current source.</param>
    public static MediaSessionLossReason Classify(
        bool isUsable,
        bool isAllowedByFilter,
        bool isSelected)
    {
        if (!isUsable)
        {
            return MediaSessionLossReason.NotUsable;
        }

        if (!isAllowedByFilter)
        {
            return MediaSessionLossReason.FilteredOut;
        }

        return isSelected ? MediaSessionLossReason.None : MediaSessionLossReason.NotSelected;
    }

    /// <summary>
    /// 排出一行诊断：阶段标签、两侧计数、逐个来源的去向，以及是否处在兜底读模式。
    ///
    /// 计数与逐项去向分开写，是为了在会话很多时也能一眼看出「系统报了 5 个、字典里只有 1 个」这类数量差，
    /// 而不必逐项数过去。
    /// Lays out one diagnostic line: the stage label, both counts, a per-source verdict, and whether the fallback read is active.
    ///
    /// Counts and per-source verdicts are both present so a quantity gap such as "the OS reported five, the dictionary holds one" is
    /// readable at a glance without counting entries.
    /// </summary>
    /// <param name="stage">阶段标签，例如看门狗升级或手动重连。/ Stage label, such as a watchdog escalation or a manual reconnect.</param>
    /// <param name="catalog">目录两侧的快照。/ The two-sided catalog snapshot.</param>
    /// <param name="allowedSourceIds">来源允许列表放行的标识。/ Identifiers the source allow-list admits.</param>
    /// <param name="selectedKey">当前选中的会话键；未选中任何会话时为 <see langword="null"/>。/ The currently selected session key, or <see langword="null"/> when nothing is selected.</param>
    /// <param name="isFallbackActive">是否正在走绕开库字典的兜底读。/ Whether the read is bypassing the library dictionary.</param>
    public static string Format(
        string stage,
        in MediaSessionCatalogState catalog,
        IReadOnlyCollection<string> allowedSourceIds,
        string? selectedKey,
        bool isFallbackActive)
    {
        var allowed = new HashSet<string>(allowedSourceIds, StringComparer.OrdinalIgnoreCase);
        var library = new HashSet<string>(catalog.LibrarySourceIds, StringComparer.OrdinalIgnoreCase);
        var unusable = new HashSet<string>(catalog.UnusableSourceIds, StringComparer.OrdinalIgnoreCase);

        var ids = catalog.OsSourceIds
            .Concat(catalog.LibrarySourceIds)
            .Where(static id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static id => id, StringComparer.OrdinalIgnoreCase);

        var verdicts = ids.Select(id =>
        {
            if (!library.Contains(id))
            {
                return $"{id}=missing-in-dict";
            }

            var reason = Classify(
                isUsable: !unusable.Contains(id),
                isAllowedByFilter: allowed.Contains(id),
                isSelected: string.Equals(id, selectedKey, StringComparison.Ordinal));
            return reason == MediaSessionLossReason.None
                ? $"{id}=ok"
                : $"{id}={Describe(reason)}";
        });

        return $"阶段/stage={stage} 系统/os={catalog.OsSourceIds.Count} 库/dict={catalog.LibrarySourceIds.Count} " +
               $"不可读/unreadable={catalog.UnusableSourceIds.Count} 兜底/fallback={(isFallbackActive ? "on" : "off")} | " +
               string.Join(", ", verdicts);
    }

    /// <summary>把去向映射成稳定短词，便于在日志里 grep 统计。/ Maps a verdict to a stable short word so it can be grepped and counted in the log.</summary>
    /// <param name="reason">会话去向。/ Where the session ended up.</param>
    public static string Describe(MediaSessionLossReason reason) => reason switch
    {
        MediaSessionLossReason.MissingFromDictionary => "missing-in-dict",
        MediaSessionLossReason.NotUsable => "no-control",
        MediaSessionLossReason.FilteredOut => "filtered",
        MediaSessionLossReason.NotSelected => "not-selected",
        _ => "ok"
    };
}
