// 固定取词阶段与同分选择的纯策略；扩展顺序只改此策略/目录，不改 UI 或网络协调者。
using AFMediaBar.Classes.Abstractions;
using AFMediaBar.Classes.Models;

namespace AFMediaBar.Classes.Services.Lyrics;

/// <summary>优先源与并发备用源的执行计划。/ Preferred-source and parallel-fallback plan.</summary>
internal sealed record LyricsRetrievalPlan(ILyricsProvider? Preferred, IReadOnlyList<ILyricsProvider> Fallbacks);

/// <summary>构造阶段并按最高匹配分选择备用结果。/ Plans retrieval and ranks fallback results.</summary>
internal static class LyricsRetrievalPolicy
{
    // QQ 优质歌词库优先；备用源并发后按分数采纳。旧查询策略、采纳模式、批次和播放器绑定
    // 仅保留序列化兼容，不再控制运行时；这些是实现策略，禁止重新添加对应 UI 设置。
    internal const string PreferredSource = LyricsSourceCatalog.QQMusic;

    public static LyricsRetrievalPlan Plan(IReadOnlyList<ILyricsProvider> providers, IReadOnlyList<ILyricsProvider> enabled)
    {
        var active = providers.Where(provider => enabled.Contains(provider)).ToArray();
        var preferred = active.FirstOrDefault(provider => provider.SourceName == PreferredSource);
        return new(preferred, active.Where(provider => !ReferenceEquals(provider, preferred)).ToArray());
    }

    public static LyricsResult? Select(IEnumerable<LyricsResult?> results) => results
        .Where(result => result is not null).OrderByDescending(result => result!.MatchScore).FirstOrDefault();
}
