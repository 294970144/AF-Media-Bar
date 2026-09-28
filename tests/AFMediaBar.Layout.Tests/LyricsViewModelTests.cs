using AFMediaBar.Classes.Services.Localization;
using AFMediaBar.Classes.Services.Lyrics;
using AFMediaBar.Classes.Settings;
using AFMediaBar.ViewModels.Pages;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

/// <summary>
/// 歌词页来源列表的视图模型测试：默认状态、逐个启停、排序与恢复默认。
/// View-model tests for the lyrics page's source list: the default state, per-source toggling, ordering, and resetting.
/// </summary>
[TestClass]
public sealed class LyricsViewModelTests
{
    [TestInitialize]
    public void SetUp() => SettingsManager.ResetAll();

    [TestCleanup]
    public void TearDown() => SettingsManager.ResetAll();

    [TestMethod]
    public void DefaultStateListsEverySourceEnabledInTheDefaultOrder()
    {
        var viewModel = CreateViewModel();

        CollectionAssert.AreEqual(
            LyricsSourceCatalog.DefaultOrder.ToArray(),
            viewModel.SourceEntries.Select(entry => entry.SourceId).ToArray());
        Assert.IsTrue(viewModel.SourceEntries.All(entry => entry.IsEnabled));
        Assert.IsFalse(viewModel.IsEverySourceDisabled);
    }

    [TestMethod]
    public void DisablingOneSourcePersistsTheRemainingOrder()
    {
        var viewModel = CreateViewModel();
        var disabled = viewModel.SourceEntries[1];

        disabled.IsEnabled = false;

        var stored = SettingsManager.Current.LyricsSource.EnabledSourceIds;
        Assert.IsNotNull(stored);
        CollectionAssert.DoesNotContain(stored!.ToArray(), disabled.SourceId);
        CollectionAssert.AreEqual(
            LyricsSourceCatalog.DefaultOrder.Where(id => id != disabled.SourceId).ToArray(),
            stored.ToArray());

        // 关掉的那一行仍然留在列表里，否则用户没法把它开回来。
        // The row that was turned off stays in the list, otherwise the user could never turn it back on.
        Assert.AreEqual(LyricsSourceCatalog.DefaultOrder.Count, viewModel.SourceEntries.Count);
        Assert.IsFalse(viewModel.IsEverySourceDisabled);
    }

    [TestMethod]
    public void DisablingEverySourceIsStoredAsAnExplicitlyEmptyList()
    {
        var viewModel = CreateViewModel();

        foreach (var entry in viewModel.SourceEntries)
        {
            entry.IsEnabled = false;
        }

        var stored = SettingsManager.Current.LyricsSource.EnabledSourceIds;
        Assert.IsNotNull(stored, "关闭全部来源必须存成空数组，而不是「未配置」的 null。");
        Assert.AreEqual(0, stored!.Count);
        Assert.IsTrue(viewModel.IsEverySourceDisabled);
    }

    [TestMethod]
    public void ResetOrderRestoresEverySourceAndTheUnconfiguredState()
    {
        var viewModel = CreateViewModel();
        viewModel.SourceEntries[0].IsEnabled = false;

        viewModel.ResetSourceOrderCommand.Execute(null);

        CollectionAssert.AreEqual(
            LyricsSourceCatalog.DefaultOrder.ToArray(),
            viewModel.SourceEntries.Select(entry => entry.SourceId).ToArray());
        Assert.IsTrue(viewModel.SourceEntries.All(entry => entry.IsEnabled));
        // "全部来源按默认顺序"写回"未配置"，这样以后新增的来源会自动生效。
        // "Every source in the default order" is stored as "never configured", so a source added later takes effect on its own.
        Assert.IsNull(SettingsManager.Current.LyricsSource.EnabledSourceIds);
    }

    [TestMethod]
    public void ExternalSourceChangeRebuildsTheListWithEveryRowVisible()
    {
        var viewModel = CreateViewModel();

        SettingsManager.SetLyricsSourceSettings(new LyricsSourceSettings([LyricsSourceCatalog.Kugou]));

        var entries = viewModel.SourceEntries;
        Assert.AreEqual(LyricsSourceCatalog.DefaultOrder.Count, entries.Count);
        Assert.IsTrue(entries.Single(entry => entry.SourceId == LyricsSourceCatalog.Kugou).IsEnabled);
        Assert.IsTrue(entries.Where(entry => entry.SourceId != LyricsSourceCatalog.Kugou).All(entry => !entry.IsEnabled));
        Assert.IsFalse(viewModel.IsEverySourceDisabled);
    }

    private static LyricsViewModel CreateViewModel() => new(new LocalizationService());
}
