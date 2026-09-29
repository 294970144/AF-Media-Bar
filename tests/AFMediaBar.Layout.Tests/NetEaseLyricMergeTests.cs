using AFMediaBar.Classes.Models;
using AFMediaBar.Classes.Services.Lyrics;
using Lyricify.Lyrics.Providers.Web.Netease;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

/// <summary>
/// 网易云两个取词端点的合并测试。
/// Tests for merging NetEase's two retrieval endpoints.
///
/// 夹具直接来自对真实接口的实测（2026-09-17）：新端点的 <c>Yrc</c> 是"署名 JSON 行 + YRC 逐字行"的混合文本，<c>Lrc</c>
/// 只有署名 JSON，<c>Ytlrc</c> 是逐字中文译文。这条回归曾经让网易云歌词整体丢失并回落到其他来源，因此必须有夹具守住。
/// The fixtures come from calls against the real endpoints (2026-09-17): the new endpoint's <c>Yrc</c> mixes credit JSON lines with
/// YRC syllable lines, its <c>Lrc</c> carries only credit JSON, and <c>Ytlrc</c> carries the syllable Chinese translation. This
/// regression once lost NetEase lyrics entirely and fell back to other sources, so a fixture has to guard it.
/// </summary>
[TestClass]
public sealed class NetEaseLyricMergeTests
{
    /// <summary>新端点的逐字字段（实测样本裁剪）：前两行署名 JSON，之后是逐字行。
    /// The new endpoint's syllable field (trimmed real sample): two credit JSON lines followed by syllable lines.</summary>
    private const string WordLevelYrc =
        "{\"t\":0,\"c\":[{\"tx\":\"作词: \"},{\"tx\":\"Taylor Swift\"}]}\n" +
        "{\"t\":30,\"c\":[{\"tx\":\"作曲: \"},{\"tx\":\"Taylor Swift\"}]}\n" +
        "[120,2910](120,120,0)I (240,420,0)promise (660,150,0)that\n" +
        "[3450,2520](3450,120,0)I (3570,210,0)know (3780,210,0)that";

    private const string LegacyLrc =
        "[00:00.12]I promise that\n[00:03.45]I know that";

    private const string WordLevelTranslation =
        "[00:00.120]我保证\n[00:03.450]我知道";

    private static LyricsRequest Request() => new("ME!", "Taylor Swift", "Lover", 200, NetEaseSongId: null);

    private static LyricResult Payload(
        string? lrc = null,
        string? tlyric = null,
        string? romalrc = null,
        string? yrc = null,
        string? ytlrc = null,
        bool nolyric = false) =>
        new()
        {
            Code = 200,
            Nolyric = nolyric,
            Lrc = lrc is null ? null : new Lyrics { Lyric = lrc },
            Tlyric = tlyric is null ? null : new Lyrics { Lyric = tlyric },
            Romalrc = romalrc is null ? null : new Lyrics { Lyric = romalrc },
            Yrc = yrc is null ? null : new Lyrics { Lyric = yrc },
            Ytlrc = ytlrc is null ? null : new Lyrics { Lyric = ytlrc }
        };

    [TestMethod]
    public void HybridWordLevelPayloadWinsAndKeepsTranslation()
    {
        var result = NetEaseLyricMerge.Resolve(
            "Netease",
            Payload(lrc: LegacyLrc, tlyric: "[00:00.12]旧译文"),
            Payload(lrc: "{\"t\":0,\"c\":[{\"tx\":\"作词: \"}]}", yrc: WordLevelYrc, ytlrc: WordLevelTranslation),
            Request());

        Assert.IsNotNull(result);
        var lines = result!.Document.Lines;

        // 署名行被信息行过滤丢弃，逐字行保留音节时间轴。
        // Credit lines are dropped by the info-line filter while the syllable lines keep their timeline.
        Assert.AreEqual(2, lines.Count);
        Assert.AreEqual("I promise that", lines[0].Text);
        Assert.AreEqual(3, lines[0].Words.Count);
        Assert.AreEqual("我保证", lines[0].Translation);
        Assert.AreEqual("我知道", lines[1].Translation);
        // 该载荷本身混了署名行与逐字行，库因此判定为混合同步；署名行被过滤后剩下的行仍带音节时间轴。
        // The payload itself mixes credit lines with syllable lines, which is why the library reports mixed sync; the lines that
        // survive the credit filter still carry their syllable timeline.
        Assert.AreEqual(LyricsSyncType.MixedSynced, result.Document.SyncType);
    }

    [TestMethod]
    public void UnrecognizableWordLevelFallsBackToTheLegacyLineLevelBody()
    {
        var result = NetEaseLyricMerge.Resolve(
            "Netease",
            Payload(lrc: LegacyLrc, tlyric: "[00:00.12]行级译文"),
            Payload(lrc: "{\"t\":0,\"c\":[{\"tx\":\"作词: \"}]}", yrc: "{\"unrelated\":true}"),
            Request());

        Assert.IsNotNull(result);
        Assert.AreEqual(2, result!.Document.Lines.Count);
        Assert.AreEqual("I promise that", result.Document.Lines[0].Text);
        Assert.AreEqual("行级译文", result.Document.Lines[0].Translation);
        Assert.AreEqual(0, result.Document.Lines[0].Words.Count);
    }

    [TestMethod]
    public void WordLevelPayloadAloneIsEnough()
    {
        var result = NetEaseLyricMerge.Resolve(
            "Netease",
            legacy: null,
            Payload(yrc: WordLevelYrc, ytlrc: WordLevelTranslation),
            Request());

        Assert.IsNotNull(result);
        Assert.AreEqual(2, result!.Document.Lines.Count);
        Assert.AreEqual(3, result.Document.Lines[0].Words.Count);
        Assert.AreEqual("我保证", result.Document.Lines[0].Translation);
    }

    [TestMethod]
    public void TranslationFallsBackToTheLegacyPayload()
    {
        var result = NetEaseLyricMerge.Resolve(
            "Netease",
            Payload(lrc: LegacyLrc, tlyric: "[00:00.12]行级译文"),
            Payload(yrc: WordLevelYrc),
            Request());

        Assert.IsNotNull(result);
        Assert.AreEqual("行级译文", result!.Document.Lines[0].Translation);
    }

    [TestMethod]
    public void MissingOrUnparseablePayloadsRemainFailuresWhileExplicitNoLyricsIsValid()
    {
        Assert.IsNull(NetEaseLyricMerge.Resolve("Netease", null, null, Request()));
        Assert.IsNull(NetEaseLyricMerge.Resolve("Netease", Payload(lrc: "   "), Payload(yrc: "   "), Request()));
        var result = NetEaseLyricMerge.Resolve("Netease", Payload(lrc: LegacyLrc, nolyric: true), null, Request());
        Assert.AreEqual(LyricsResultStatus.NoLyrics, result!.Status);
        Assert.AreEqual(0, result.Document.Lines.Count);
        var error = Payload(nolyric: true);
        error.Code = 500;
        Assert.IsNull(NetEaseLyricMerge.Resolve("Netease", error, null, Request()));
    }

    [TestMethod]
    public void RomanizationIsKeptFromEitherEndpoint()
    {
        var legacy = NetEaseLyricMerge.Resolve(
            "Netease",
            Payload(lrc: LegacyLrc, romalrc: "[00:00.12]ay promis dhat"),
            Payload(yrc: WordLevelYrc),
            Request());

        Assert.IsNotNull(legacy);
        Assert.AreEqual("ay promis dhat", legacy!.Document.Lines[0].Romanization);
    }
}
