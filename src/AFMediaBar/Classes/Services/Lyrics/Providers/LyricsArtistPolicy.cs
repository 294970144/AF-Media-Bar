// Normalizes artist lists for lyric matching and queries; never changes the displayed player metadata.
// Only QQ Music's slash-delimited metadata is split, preserving names such as AC/DC from other players.
using AFMediaBar.Classes.Models;

namespace AFMediaBar.Classes.Services.Lyrics;

/// <summary>按当前播放器的元数据约定解析参与歌词匹配的歌手列表。</summary>
internal static class LyricsArtistPolicy
{
    internal static string[] Split(LyricsRequest request) => Split(request.Artist, request.PlaybackSourceId);

    internal static string[] Split(string? artist, string? playbackSourceId)
    {
        if (!LyricsSourcePolicy.IsCurrentPlaybackSource(playbackSourceId, LyricsSourceCatalog.QQMusic))
            return [artist ?? string.Empty];

        var artists = (artist ?? string.Empty).Split('/', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return artists.Length == 0 ? [string.Empty] : artists;
    }
}
