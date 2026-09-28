using AFMediaBar.Classes.Abstractions;
using AFMediaBar.Classes.Settings;

namespace AFMediaBar.Classes.Services.Lyrics;

/// <summary>
/// 解析启用来源；null 表示全部、空数组表示全部关闭，未知来源忽略。
/// 保留序列化列表顺序，但运行时阶段与同分顺序由 LyricsRetrievalPolicy/目录拥有。
/// </summary>
public static class LyricsSourcePolicy
{
    /// <summary>
    /// 解析本次取词要用的提供器。
    /// Resolves the providers one retrieval uses.
    /// </summary>
    /// <param name="providers">全部提供器（组合根构造的默认顺序）/ Every provider in the default order built at the composition root.</param>
    /// <param name="settings">用户的来源设置 / The user's source settings.</param>
    /// <returns>启用的提供器，按用户顺序；用户关掉全部来源时为空 / The enabled providers in the user's order, empty when every source is off.</returns>
    public static IReadOnlyList<ILyricsProvider> ResolveActive(
        IReadOnlyList<ILyricsProvider> providers,
        LyricsSourceSettings settings)
    {
        ArgumentNullException.ThrowIfNull(providers);
        if (providers.Count == 0)
        {
            return [];
        }

        var enabledIds = settings.Normalize().EnabledSourceIds;
        if (enabledIds is null)
        {
            return providers;
        }

        if (enabledIds.Count == 0)
        {
            return [];
        }

        var byId = new Dictionary<string, ILyricsProvider>(StringComparer.Ordinal);
        foreach (var provider in providers)
        {
            byId.TryAdd(provider.SourceName, provider);
        }

        var active = new List<ILyricsProvider>(enabledIds.Count);
        foreach (var id in enabledIds)
        {
            if (byId.TryGetValue(id, out var provider) && !active.Contains(provider))
            {
                active.Add(provider);
            }
        }

        return active;
    }
}
