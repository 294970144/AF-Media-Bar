// 候选匹配回归：译名、试听时长和原始候选排序不得被库内等级否决。
using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Services.Lyrics;
using Lyricify.Lyrics.Searchers;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

[TestClass]
public sealed class LyricsSearchTests
{
    [TestMethod]
    public async Task TranslatedMetadataAndPreviewDurationStillSelectTheOriginalTrack()
    {
        var request = new LyricsRequest("ノーチラス（鹦鹉螺）", "ヨルシカ（夜鹿）", "", 30, null);
        var candidate = Candidate("ノーチラス", ["ヨルシカ"], "エルマ", 240000);
        var searcher = new FixtureSearcher(candidate);
        var match = await LyricsSearch.MatchAsync(request, searcher, CancellationToken.None);
        Assert.IsNotNull(match);
        Assert.AreSame(candidate, match.Candidate);
        Assert.IsTrue(match.Score >= LyricsMetadataScore.MinimumScore);
        Assert.IsNull(candidate.MatchType, "Raw candidates must not be graded by Lyricify.");
        Assert.AreEqual("ノーチラス ヨルシカ", searcher.Queries[0]);
        Assert.AreEqual("ノーチラス（鹦鹉螺）", request.Title);
    }

    [TestMethod]
    public async Task HighestScoringCandidateWinsRegardlessOfLibraryOrder()
    {
        var request = new LyricsRequest("Song", "Artist", "Album", 200, null);
        var wrong = Candidate("Song", ["Stranger"], "Elsewhere", 200000);
        var correct = Candidate("Song", ["Artist"], "Album", 200000);
        var match = await LyricsSearch.MatchAsync(request, new FixtureSearcher(wrong, correct), CancellationToken.None);
        Assert.AreSame(correct, match!.Candidate);
        Assert.AreEqual(100, match.Score);
    }

    [TestMethod]
    public async Task MissingAlbumDoesNotRejectACorrectMultiArtistCandidate()
    {
        var request = new LyricsRequest("远航星的告别", "鸣潮先约电台", "", null, null);
        var candidate = Candidate(request.Title, [request.Artist, "jixwang", "Emi Evans"], "星轨消逝之夜", 225000);
        var match = await LyricsSearch.MatchAsync(request, new FixtureSearcher(candidate), CancellationToken.None);
        Assert.AreSame(candidate, match!.Candidate);
        Assert.AreEqual(80, match.Score);
    }

    [TestMethod]
    public async Task UnrelatedCandidatesAreRejected()
    {
        var request = new LyricsRequest("远航星的告别", "鸣潮先约电台", "", null, null);
        Assert.IsNull(await LyricsSearch.MatchAsync(request,
            new FixtureSearcher(Candidate("Loafers", ["BoyWithUke"], "Faded", 215000)), CancellationToken.None));
    }

    [TestMethod]
    public void PreviewDurationHasOnlyTenPercentInfluence()
    {
        var request = new LyricsRequest("Song", "Artist", "Album", 30, null);
        Assert.AreEqual(90, LyricsMetadataScore.Calculate(request, "Song", "Artist", "Album", 240));
        Assert.AreEqual(100, LyricsMetadataScore.Calculate(request with { DurationSeconds = 240 }, "Song", "Artist", "Album", 240));
        Assert.AreEqual(95, LyricsMetadataScore.Calculate(request with { DurationSeconds = 234.5 }, "Song", "Artist", "Album", 240));
    }

    [TestMethod]
    public void VersionSuffixesAndArtistNamesArePreserved()
    {
        Assert.AreEqual("Song (Live)", LyricsSearchQueryPolicy.WithoutTranslation("Song (Live)"));
        Assert.AreEqual("Song（现场版）", LyricsSearchQueryPolicy.WithoutTranslation("Song（现场版）"));
        Assert.AreEqual("AC/DC", LyricsSearchQueryPolicy.WithoutTranslation("AC/DC"));
        Assert.AreEqual("Earth, Wind & Fire", LyricsSearchQueryPolicy.WithoutTranslation("Earth, Wind & Fire"));
    }

    [TestMethod]
    public async Task CallerCancellationAfterAnUncancellableSearchRejectsItsLateCandidate()
    {
        using var cancellation = new CancellationTokenSource();
        var pending = new TaskCompletionSource<List<ISearchResult>?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var lookup = LyricsSearch.MatchAsync(new("Song", "Artist", "", null, null), new PendingSearcher(pending.Task), cancellation.Token);
        cancellation.Cancel();
        pending.SetResult([Candidate("Song", ["Artist"], "", 200000)]);
        await Assert.ThrowsExceptionAsync<OperationCanceledException>(() => lookup);
    }

    private static QQMusicSearchResult Candidate(string title, string[] artists, string album, int duration) =>
        new(title, artists, album, null, duration, "fixture", "fixture");

    private sealed class FixtureSearcher(params ISearchResult[] candidates) : Searcher
    {
        public List<string> Queries { get; } = [];
        public override string Name => "Fixture";
        public override string DisplayName => Name;
        public override Searchers SearcherType => Searchers.QQMusic;
        public override Task<List<ISearchResult>?> SearchForResults(string searchString)
        {
            Queries.Add(searchString);
            return Task.FromResult<List<ISearchResult>?>([.. candidates]);
        }
    }

    private sealed class PendingSearcher(Task<List<ISearchResult>?> pending) : Searcher
    {
        public override string Name => "Pending";
        public override string DisplayName => Name;
        public override Searchers SearcherType => Searchers.QQMusic;
        public override Task<List<ISearchResult>?> SearchForResults(string searchString) => pending;
    }
}
