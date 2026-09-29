// 歌词库只提供原始搜索候选；候选排序和门槛完全由本项目的纯评分策略决定。
using AFMediaBar.Classes.Models;
using Lyricify.Lyrics.Searchers;
using Lyricify.Lyrics.Searchers.Helpers;

namespace AFMediaBar.Classes.Services.Lyrics;

/// <summary>独立评分的候选搜索，不使用库内元数据筛选。/ Candidate search with application-owned scoring.</summary>
internal static class LyricsSearch
{
    internal sealed record Match(ISearchResult Candidate, int Score);

    public static Task<Match?> MatchAsync(LyricsRequest request, Searchers source, CancellationToken token,
        int minimumScore = LyricsMetadataScore.MinimumScore) =>
        MatchAsync(request, source.GetSearcher(), token, minimumScore);

    internal static async Task<Match?> MatchAsync(LyricsRequest request, ISearcher searcher, CancellationToken token,
        int minimumScore = LyricsMetadataScore.MinimumScore)
    {
        Match? best = null;
        int? bestCandidateScore = null;
        foreach (var query in LyricsSearchQueryPolicy.Build(request))
        {
            token.ThrowIfCancellationRequested();
            List<ISearchResult>? candidates;
            try
            {
                // string 重载返回原始候选；ITrackMetadata 重载会偷偷应用库内评分，不能使用。
                candidates = await searcher.SearchForResults(query).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception exception)
            {
                AppLogService.Current?.Warn("Lyrics", $"歌曲搜索异常 / search failed: {searcher.Name} {exception.GetType().Name}");
                continue;
            }
            token.ThrowIfCancellationRequested();
            foreach (var candidate in candidates ?? [])
            {
                var score = LyricsMetadataScore.Calculate(request, [candidate.Title], candidate.Artists,
                    [candidate.Album], candidate.DurationMs / 1000d);
                bestCandidateScore = Math.Max(bestCandidateScore ?? 0, score);
                if (score >= minimumScore && (best is null || score > best.Score))
                    best = new Match(candidate, score);
            }
            if (best is not null) break;
        }
        AppLogService.Current?.Info("Lyrics", $"歌曲匹配 / match: source={searcher.Name} score={best?.Score.ToString() ?? "none"} " +
            $"bestCandidateScore={bestCandidateScore?.ToString() ?? "none"} minimum={minimumScore}");
        return best;
    }
}
