// LRCLIB 只负责候选和正文获取；使用统一评分，不把试听时长作为服务器检索条件。
using AFMediaBar.Classes.Abstractions;
using AFMediaBar.Classes.Models;
using Lyricify.Lyrics.Providers.Web.LRCLIB;

namespace AFMediaBar.Classes.Services.Lyrics;

/// <summary>从 LRCLIB 搜索候选并按统一匹配分选择可解析的同步歌词。/ Scored LRCLIB candidate lookup.</summary>
public sealed class LrclibLyricsProvider : ILyricsProvider
{
    private readonly Api _api = new();
    public string SourceName => LyricsSourceCatalog.Lrclib;

    public async Task<LyricsResult?> GetLyricsAsync(LyricsRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Title)) return null;
        var title = LyricsSearchQueryPolicy.WithoutTranslation(request.Title);
        var artist = LyricsSearchQueryPolicy.WithoutTranslation(request.Artist);
        var candidates = await _api.Search(title, artist).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        var ranked = (candidates ?? []).Where(candidate => !candidate.Instrumental && !string.IsNullOrWhiteSpace(candidate.SyncedLyrics))
            .Select(candidate => (Candidate: candidate, Score: LyricsMetadataScore.Calculate(request,
                candidate.TrackName, candidate.ArtistName, candidate.AlbumName, candidate.Duration)))
            .Where(item => item.Score >= LyricsMetadataScore.MinimumScore).OrderByDescending(item => item.Score);
        foreach (var item in ranked)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var document = LyricsTextParser.Parse(item.Candidate.SyncedLyrics, request: request,
                durationSeconds: request.DurationSeconds, filterInfoLines: request.FilterInfoLines);
            if (document.Lines.Count > 0) return new LyricsResult(SourceName, document) { MatchScore = item.Score };
        }
        return null;
    }
}
