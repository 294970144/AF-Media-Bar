using AFMediaBar.Classes.Models.Updates;
using AFMediaBar.Classes.Services.Updates;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

/// <summary>
/// 已就绪更新的保留判定测试：待安装包仍有效时，后续检查不得覆盖"点击重启安装"状态。
/// Tests for retaining a ready update: while the pending installer is still valid a later check must not overwrite the
/// "click to restart and install" state.
/// </summary>
[TestClass]
public sealed class UpdateReadyRetentionPolicyTests
{
    [TestMethod]
    public void PendingInstallerIsUsableOnlyWhenAllConditionsHold()
    {
        Assert.IsTrue(UpdateReadyRetentionPolicy.IsPendingInstallerUsable(
            recordExists: true, fileExists: true, versionIsNewer: true, isSkipped: false));

        Assert.IsFalse(UpdateReadyRetentionPolicy.IsPendingInstallerUsable(
            recordExists: false, fileExists: true, versionIsNewer: true, isSkipped: false));
        Assert.IsFalse(UpdateReadyRetentionPolicy.IsPendingInstallerUsable(
            recordExists: true, fileExists: false, versionIsNewer: true, isSkipped: false));
        Assert.IsFalse(UpdateReadyRetentionPolicy.IsPendingInstallerUsable(
            recordExists: true, fileExists: true, versionIsNewer: false, isSkipped: false));
        Assert.IsFalse(UpdateReadyRetentionPolicy.IsPendingInstallerUsable(
            recordExists: true, fileExists: true, versionIsNewer: true, isSkipped: true));
    }

    [TestMethod]
    public void OnlyAReadyPhaseWithAUsableInstallerSkipsTheCheck()
    {
        // 已就绪 + 安装包仍有效：检查放过，状态保持"点击重启安装"。
        // Ready plus a usable installer: the check is skipped and the install entry stays.
        Assert.IsTrue(UpdateReadyRetentionPolicy.ShouldRetainReadyState(
            UpdatePhase.Ready, pendingInstallerUsable: true));

        // 其它阶段照常检查；就绪但安装包已失效时也要放行检查重新给出结论。
        // Other phases check as usual; a ready state whose installer went stale must also fall through so the check can
        // publish a fresh verdict.
        Assert.IsFalse(UpdateReadyRetentionPolicy.ShouldRetainReadyState(
            UpdatePhase.Ready, pendingInstallerUsable: false));
        Assert.IsFalse(UpdateReadyRetentionPolicy.ShouldRetainReadyState(
            UpdatePhase.Available, pendingInstallerUsable: true));
        Assert.IsFalse(UpdateReadyRetentionPolicy.ShouldRetainReadyState(
            UpdatePhase.Idle, pendingInstallerUsable: true));
        Assert.IsFalse(UpdateReadyRetentionPolicy.ShouldRetainReadyState(
            UpdatePhase.Downloading, pendingInstallerUsable: true));
    }
}
