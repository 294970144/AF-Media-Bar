using AFMediaBar.Classes.Settings;

namespace AFMediaBar.Classes.Models;

/// <summary>
/// 歌词检索请求，保留播放器原始元数据；过滤选项由协调器在取词前填入。
/// MatchStrictness 仅为旧调用兼容，不参与候选评分；门槛和权重由代码策略统一定义。
/// Retains original player metadata; filtering is supplied by the coordinator and scoring belongs to the fixed policy.
/// </summary>
/// <param name="Title">曲名 / Track title.</param>
/// <param name="Artist">歌手 / Artist.</param>
/// <param name="Album">专辑 / Album.</param>
/// <param name="DurationSeconds">曲目时长（秒），未知为 null / Track duration in seconds, null when unknown.</param>
/// <param name="NetEaseSongId">网易云歌曲 id；非空时按 id 精确取词 / NetEase song id; a non-null value retrieves by id.</param>
/// <param name="SourceAppId">正在播放的来源应用的 SMTC 标识（SourceAppUserModelId 原文），未知为 null。
/// 原文保留作为请求上下文，不决定取词顺序。
/// The raw SMTC identifier of the playing application, null when unknown; retained as context without controlling retrieval order.</param>
/// <param name="MatchStrictness">旧调用兼容字段，运行时忽略 / Legacy compatibility field, ignored during retrieval.</param>
/// <param name="FilterInfoLines">是否丢弃作者、作曲等信息行 / Whether credit lines such as writer and composer are dropped.</param>
public sealed record LyricsRequest(
    string Title,
    string Artist,
    string Album,
    double? DurationSeconds,
    string? NetEaseSongId,
    string? SourceAppId = null,
    LyricsMatchStrictness MatchStrictness = LyricsMatchStrictness.Balanced,
    bool FilterInfoLines = true);
