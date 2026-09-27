// 独立来源在没有 SMTC 时也可参与选择；策略不持有资源，读取器仍由 Provider 管理。
using AFMediaBar.Classes.Models;

namespace AFMediaBar.Classes.Abstractions;

/// <summary>
/// 提供独立来源身份及双通道仲裁规则的可选能力。注册为 IMediaSourceProvider 即可由目录自动发现。
/// Optional standalone-source capability, discovered through the existing IMediaSourceProvider registration.
/// </summary>
public interface IIndependentMediaSourceProvider : IMediaSourceProvider
{
    /// <summary>稳定且无副作用的来源策略；同一 Provider 生命周期内不得更换。</summary>
    IMediaSourcePolicy SourcePolicy { get; }
}

/// <summary>
/// 独立来源的纯策略；只认领自己的会话，不修改其他来源的候选，别名不得与其他策略重叠。
/// Pure standalone-source policy: claims only its own sessions, preserves other candidates, and uses non-overlapping aliases.
/// </summary>
public interface IMediaSourcePolicy
{
    /// <summary>用于发现和设置的稳定来源 ID。</summary>
    string SourceId { get; }
    /// <summary>跨通道重建保持不变的选择键，须在注册来源间唯一。</summary>
    string SelectionKey { get; }
    /// <summary>识别规范 ID 及 SMTC 别名。</summary>
    bool Matches(string? sourceId);
    /// <summary>合并自己的候选；保留其他来源的候选及顺序。</summary>
    IReadOnlyList<MediaSourceCandidate> Combine(IReadOnlyList<MediaSourceCandidate> sessions, MediaSnapshot? memory);
    /// <summary>合并本来源快照，拒绝跨来源数据并处理通道失效。</summary>
    MediaSnapshot Merge(MediaSnapshot? memory, MediaSnapshot smtc);
}
