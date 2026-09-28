namespace AFMediaBar.Classes.Models;

/// <summary>
/// 歌词结果：一个来源命中的歌词文档及其来源标识。
/// Lyrics result: the lyric document one source matched, together with that source's identifier.
///
/// 解析在取词侧完成，因此这里携带的是已解析的 <see cref="LyricDocument"/>，而不是原始文本；呈现层只做按位置选行，
/// 不再在 UI 线程上解析歌词文本。
/// Parsing happens on the retrieval side, so this carries an already parsed <see cref="LyricDocument"/> instead of raw text,
/// and the presentation layer only selects the active line instead of parsing lyric text on the UI thread.
///
/// ⚠️ 注意 Note:
/// 这是一个不可变记录类型（record），每次修改都会创建新实例。
/// This is an immutable record type; modifications create new instances.
/// </summary>
/// <param name="Source">歌词来源标识（如 "Netease"、"LRCLIB"），只用于诊断 / Source identifier such as "Netease" or "LRCLIB"; diagnostics only.</param>
/// <param name="Document">该来源命中的歌词文档 / The lyric document this source matched.</param>
public sealed record LyricsResult(string Source, LyricDocument Document)
{
    /// <summary>获取侧计算的匹配分（0–100），用于备用结果排序。/ Retrieval-side metadata score for fallback ranking.</summary>
    public int MatchScore { get; init; }
}
