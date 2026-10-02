using AFMediaBar.Classes.Services.Media.Smtc;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

/// <summary>
/// 会话去向诊断的纯判定与排版：把"系统有会话但读不到"拆成字典缺失、<c>ControlSession</c> 被清空、被来源过滤、未选中四种。
/// The pure verdict and layout of the session diagnostics: it breaks "the OS has sessions but nothing reads" into a missing dictionary
/// entry, a cleared <c>ControlSession</c>, a filtered source, and an unselected source.
/// </summary>
[TestClass]
public sealed class MediaSessionDiagnosticsPolicyTests
{
    private static MediaSessionCatalogState State(
        string[] os,
        string[] dict,
        string[] unusable) => new(os, dict, unusable);

    [TestMethod]
    public void ClassifyNamesTheStageThatDroppedTheSession()
    {
        // 四种去向必须彼此不同：它们对应完全不同的修法，混在一起就等于没诊断。
        // The four verdicts must differ from each other: they need four different fixes, and collapsing them is no diagnosis at all.
        Assert.AreEqual(MediaSessionLossReason.NotUsable, MediaSessionDiagnosticsPolicy.Classify(
            isUsable: false, isAllowedByFilter: true, isSelected: true));
        Assert.AreEqual(MediaSessionLossReason.FilteredOut, MediaSessionDiagnosticsPolicy.Classify(
            isUsable: true, isAllowedByFilter: false, isSelected: true));
        Assert.AreEqual(MediaSessionLossReason.NotSelected, MediaSessionDiagnosticsPolicy.Classify(
            isUsable: true, isAllowedByFilter: true, isSelected: false));
        Assert.AreEqual(MediaSessionLossReason.None, MediaSessionDiagnosticsPolicy.Classify(
            isUsable: true, isAllowedByFilter: true, isSelected: true));
    }

    [TestMethod]
    public void ValidityIsCheckedBeforeTheFilter()
    {
        // 一个既读不出又被过滤的会话必须报"读不出"：读不出是更早的环节，混淆它会让诊断指向错误的修法。
    // A session that is both unreadable and filtered must report unreadable: that is the earlier stage, and conflating them would point
    // the diagnosis at the wrong fix.
        Assert.AreEqual(MediaSessionLossReason.NotUsable, MediaSessionDiagnosticsPolicy.Classify(
            isUsable: false, isAllowedByFilter: false, isSelected: false));
    }

    [TestMethod]
    public void ASessionTheOsPublishesButTheDictionaryLacksIsReportedAsMissing()
    {
        // 这正是要区分的核心情形：系统说 Edge 在播，字典里没有它。
        // This is the central case to tell apart: the OS says Edge is playing while the dictionary does not hold it.
        var state = State(["msedge"], [], []);
        var line = MediaSessionDiagnosticsPolicy.Format(
            "test", state, allowedSourceIds: ["msedge"], selectedKey: null, isFallbackActive: false);

        StringAssert.Contains(line, "msedge=missing-in-dict");
        StringAssert.Contains(line, "os=1");
        StringAssert.Contains(line, "dict=0");
    }

    [TestMethod]
    public void ADictionaryEntryWhoseControlSessionWasClearedIsReportedAsUnreadable()
    {
        // 字典里有条目、但 ControlSession 被清空：这是与"字典缺条目"完全不同的另一种病。
        // The dictionary holds an entry whose ControlSession was cleared: a different disease from a missing entry.
        var state = State(["msedge"], ["msedge"], ["msedge"]);
        var line = MediaSessionDiagnosticsPolicy.Format(
            "test", state, allowedSourceIds: ["msedge"], selectedKey: null, isFallbackActive: false);

        StringAssert.Contains(line, "msedge=no-control");
        StringAssert.Contains(line, "unreadable=1");
        Assert.IsFalse(
            line.Contains("missing-in-dict"),
            "读不出不等于字典缺条目，两者必须能分开。/ Unreadable is not a missing dictionary entry; the two must be distinguishable.");
    }

    [TestMethod]
    public void FormatReportsCountsAndVerdictsTogether()
    {
        // 计数与逐项去向同时出现：会话多时也能一眼看出数量差，而不必逐项数。
        // Counts and per-source verdicts appear together so a quantity gap is readable at a glance without counting entries.
        var state = State(["a", "b", "c"], ["a"], []);
        var line = MediaSessionDiagnosticsPolicy.Format(
            "test", state, allowedSourceIds: ["a", "b", "c"], selectedKey: "a", isFallbackActive: true);

        StringAssert.Contains(line, "os=3");
        StringAssert.Contains(line, "dict=1");
        StringAssert.Contains(line, "fallback=on");
        StringAssert.Contains(line, "a=ok");
        StringAssert.Contains(line, "b=missing-in-dict");
    }

    [TestMethod]
    public void FormatDistinguishesFilteredFromUnselected()
    {
        // 被过滤与没被选中都表现为"读不到媒体"，但修法不同：前者要查设置，后者要查选择器。
        // Filtered and not-selected both present as "no media", yet the fixes differ: settings versus selector.
        var state = State(["allowed", "blocked", "idle"], ["allowed", "blocked", "idle"], []);
        var line = MediaSessionDiagnosticsPolicy.Format(
            "test", state, allowedSourceIds: ["allowed", "idle"], selectedKey: "allowed", isFallbackActive: false);

        StringAssert.Contains(line, "blocked=filtered");
        StringAssert.Contains(line, "idle=not-selected");
        StringAssert.Contains(line, "allowed=ok");
    }

    [TestMethod]
    public void FormatCoversSessionsTheDictionaryNeverHeldAndSkipsBlankIds()
    {
        // 系统侧的会话可能字典里没有（missing），字典侧独有、系统没报的也必须列出；
        // 空标识不能变成一个匿名条目（它只会把行搅乱，掩盖真正要看的那一项）。
        // A session the OS reports may be absent from the dictionary (missing), a dictionary entry the OS did not report must still be
        // listed, and a blank identifier must not become an anonymous entry that muddies the line and hides the entry under study.
        var state = State(["a", "", "os-only"], ["", "dict-only"], []);
        var line = MediaSessionDiagnosticsPolicy.Format(
            "test", state, allowedSourceIds: ["a", "os-only", "dict-only"], selectedKey: null, isFallbackActive: false);

        StringAssert.Contains(line, "a=missing-in-dict");
        StringAssert.Contains(line, "os-only=missing-in-dict");

        // 字典里有、系统没报的那一项不算 missing：它在字典里，只是没被选中。两种"系统与库不一致"必须能分开。
        // An entry the dictionary holds while the OS did not report it is not missing: it is in the dictionary, merely not selected. The
        // two kinds of disagreement between system and library must stay distinguishable.
        StringAssert.Contains(line, "dict-only=not-selected");

        var verdicts = line[(line.IndexOf('|') + 1)..]
            .Split(", ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Assert.AreEqual(3, verdicts.Length, $"空标识不应产生条目，实际诊断行为：{line}");
    }

    [TestMethod]
    public void DescribeUsesStableGreppableWords()
    {
        // 诊断词必须稳定，日志才能被 grep 与统计。
        // The verdict words must be stable so the log can be grepped and counted.
        Assert.AreEqual("missing-in-dict", MediaSessionDiagnosticsPolicy.Describe(MediaSessionLossReason.MissingFromDictionary));
        Assert.AreEqual("no-control", MediaSessionDiagnosticsPolicy.Describe(MediaSessionLossReason.NotUsable));
        Assert.AreEqual("filtered", MediaSessionDiagnosticsPolicy.Describe(MediaSessionLossReason.FilteredOut));
        Assert.AreEqual("not-selected", MediaSessionDiagnosticsPolicy.Describe(MediaSessionLossReason.NotSelected));
        Assert.AreEqual("ok", MediaSessionDiagnosticsPolicy.Describe(MediaSessionLossReason.None));
    }
}
