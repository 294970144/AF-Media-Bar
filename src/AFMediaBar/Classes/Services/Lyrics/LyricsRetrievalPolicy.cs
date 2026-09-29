// 固定取词阶段与同分选择的纯策略；扩展顺序只改此策略/目录，不改 UI 或网络协调者。
using AFMediaBar.Classes.Abstractions;
using AFMediaBar.Classes.Models;

namespace AFMediaBar.Classes.Services.Lyrics;

/// <summary>优先源与并发备用源的执行计划。/ Preferred-source and parallel-fallback plan.</summary>
internal sealed record LyricsRetrievalPlan(ILyricsProvider? Preferred, IReadOnlyList<ILyricsProvider> Fallbacks);

/// <summary>构造阶段并按最高匹配分选择备用结果。/ Plans retrieval and ranks fallback results.</summary>
internal static class LyricsRetrievalPolicy
{
    // QQ 优质歌词库优先；备用源并发后按分数采纳。阶段、权重和门槛属于实现策略，
    // 避免用户组合出互相冲突的调度规则；后续扩展在此调整，不重新添加策略 UI 设置。
    internal const string PreferredSource = LyricsSourceCatalog.QQMusic;
    // QQ 在线候选必须足够可信才能结束优先阶段；所有结果都必须经过真实元数据评分。
    internal const int PreferredMinimumScore = 85;

    public static LyricsRetrievalPlan Plan(IReadOnlyList<ILyricsProvider> providers, IReadOnlyList<ILyricsProvider> enabled)
    {
        var active = providers.Where(provider => enabled.Contains(provider)).ToArray();
        var preferred = active.FirstOrDefault(provider => provider.SourceName == PreferredSource);
        return new(preferred, active.Where(provider => !ReferenceEquals(provider, preferred)).ToArray());
    }

    public static LyricsResult? Select(IEnumerable<LyricsResult?> results) => results
        .Where(result => result is not null).OrderByDescending(result => result!.MatchScore).FirstOrDefault();
}
