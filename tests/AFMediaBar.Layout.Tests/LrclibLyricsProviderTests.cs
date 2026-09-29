// LRCLIB 候选身份优先的回归；纯音乐和空歌词不能被低分有词候选覆盖。
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Services.Lyrics;
using Lyricify.Lyrics.Providers.Web.LRCLIB;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

[TestClass]
public sealed class LrclibLyricsProviderTests
{
    [DataTestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void HighestScoringInstrumentalOrEmptyLyricsWins(bool instrumental)
    {
        var correct = new SearchResultItem
        {
            TrackName = "Song", ArtistName = "Artist", AlbumName = "Album", Duration = 200,
            Instrumental = instrumental
        };
        var wrong = new SearchResultItem
        {
            TrackName = "Song", ArtistName = "Artist", AlbumName = "Other", Duration = 20,
            SyncedLyrics = "[00:01.00]Wrong lyrics"
        };
        var result = LrclibLyricsProvider.Resolve(new("Song", "Artist", "Album", 200, null),
            [wrong, correct], CancellationToken.None);
        Assert.AreEqual(100, result!.MatchScore);
        Assert.AreEqual(LyricsResultStatus.NoLyrics, result.Status);
        Assert.AreEqual(0, result.Document.Lines.Count);
    }
}
