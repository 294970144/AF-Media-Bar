using System;
using AFMediaBar.Classes.Models.Updates;
using AFMediaBar.Classes.Services.Updates;
using AFMediaBar.Classes.Settings;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

/// <summary>
/// 自动检查排期、静默安装参数与更新文案的纯逻辑测试。
/// Pure-logic tests for automatic check scheduling, silent install arguments, and update wording.
/// </summary>
[TestClass]
public sealed class UpdateScheduleAndInstallPolicyTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 16, 12, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public void ShouldCheck_IsDrivenByRecordedTimeNotByProcessLifetime()
    {
        // 反复重启程序不能让程序一天检查十几次。
        // Restarting the application repeatedly must not make it check a dozen times a day.
        var never = UpdateSettings.Default;
        Assert.IsTrue(UpdateCheckSchedulePolicy.ShouldCheck(never, Now));

        var checkedAnHourAgo = never with { LastCheckUtc = Now.AddHours(-1), LastCheckSucceeded = true };
        Assert.IsFalse(UpdateCheckSchedulePolicy.ShouldCheck(checkedAnHourAgo, Now));

        var checkedYesterday = never with { LastCheckUtc = Now.AddHours(-25), LastCheckSucceeded = true };
        Assert.IsTrue(UpdateCheckSchedulePolicy.ShouldCheck(checkedYesterday, Now));
    }

    [TestMethod]
    public void ShouldCheck_RetriesSoonerAfterAFailure()
    {
        var failed = UpdateSettings.Default with { LastCheckUtc = Now.AddMinutes(-30), LastCheckSucceeded = false };
        Assert.IsFalse(UpdateCheckSchedulePolicy.ShouldCheck(failed, Now));

        var retryDue = failed with { LastCheckUtc = Now.AddHours(-2) };
        Assert.IsTrue(UpdateCheckSchedulePolicy.ShouldCheck(retryDue, Now));

        var unknownResult = UpdateSettings.Default with { LastCheckUtc = Now.AddHours(-2), LastCheckSucceeded = null };
        Assert.IsFalse(
            UpdateCheckSchedulePolicy.ShouldCheck(unknownResult, Now),
            "结果未知时按成功间隔处理，避免反复重试。");
    }

    [TestMethod]
    public void ShouldCheck_DisabledNeverChecksAndSurvivesAClockThatMovedBackwards()
    {
        var disabled = UpdateSettings.Default with { AutoCheckEnabled = false, LastCheckUtc = Now.AddDays(-30) };
        Assert.IsFalse(UpdateCheckSchedulePolicy.ShouldCheck(disabled, Now));
        Assert.IsNull(UpdateCheckSchedulePolicy.ResolveNextCheckUtc(disabled, Now));

        var future = UpdateSettings.Default with { LastCheckUtc = Now.AddDays(3), LastCheckSucceeded = true };
        Assert.IsTrue(
            UpdateCheckSchedulePolicy.ShouldCheck(future, Now),
            "系统时间被回拨时放行一次检查，新的时间戳会立刻纠正它。");
    }

    [TestMethod]
    public void ResolveNextCheckUtc_AddsTheInitialDelayAfterStartup()
    {
        Assert.AreEqual(Now + UpdateCheckSchedulePolicy.InitialDelay, UpdateCheckSchedulePolicy.ResolveNextCheckUtc(UpdateSettings.Default, Now));
        Assert.AreEqual(
            Now.AddHours(23),
            UpdateCheckSchedulePolicy.ResolveNextCheckUtc(
                UpdateSettings.Default with { LastCheckUtc = Now.AddHours(-1), LastCheckSucceeded = true },
                Now));
    }

    [TestMethod]
    public void IsSkipped_ComparesVersionsSemantically()
    {
        Assert.IsTrue(UpdateCheckSchedulePolicy.IsSkipped(UpdateSettings.Default with { SkippedVersion = "1.2.0" }, "v1.2"));
        Assert.IsFalse(UpdateCheckSchedulePolicy.IsSkipped(UpdateSettings.Default with { SkippedVersion = "1.2.0" }, "1.2.1"));
        Assert.IsFalse(UpdateCheckSchedulePolicy.IsSkipped(UpdateSettings.Default, "1.2.0"));
        Assert.IsFalse(
            UpdateCheckSchedulePolicy.IsSkipped(UpdateSettings.Default with { SkippedVersion = "留下的话" }, "1.2.0"),
            "无法解析的遗留文本永远不匹配任何版本。");
    }

    [TestMethod]
    public void TrayAction_TakesTheUserToTheHighlightsBeforeDownloadingAnything()
    {
        Assert.AreEqual(UpdateTrayAction.Check, UpdatePresentationPolicy.ResolveTrayAction(State(UpdatePhase.Idle)));
        Assert.AreEqual(UpdateTrayAction.Check, UpdatePresentationPolicy.ResolveTrayAction(State(UpdatePhase.Failed)));
        Assert.AreEqual(UpdateTrayAction.None, UpdatePresentationPolicy.ResolveTrayAction(State(UpdatePhase.Checking)));
        Assert.AreEqual(UpdateTrayAction.Cancel, UpdatePresentationPolicy.ResolveTrayAction(State(UpdatePhase.Downloading)));
        Assert.AreEqual(UpdateTrayAction.Cancel, UpdatePresentationPolicy.ResolveTrayAction(State(UpdatePhase.Verifying)));

        // 托盘只有一行，点击"发现新版本"必须先打开亮点，而不是直接开始 70 MB 下载。
        // The tray has one line, so clicking "a newer version exists" must open the highlights rather than start a
        // 70 MB download.
        Assert.AreEqual(UpdateTrayAction.OpenUpdatePage, UpdatePresentationPolicy.ResolveTrayAction(State(UpdatePhase.Available)));
        Assert.AreEqual(UpdateTrayAction.OpenUpdatePage, UpdatePresentationPolicy.ResolveTrayAction(State(UpdatePhase.Skipped)));
        Assert.AreEqual(UpdateTrayAction.OpenUpdatePage, UpdatePresentationPolicy.ResolveTrayAction(State(UpdatePhase.ManualOnly)));

        Assert.AreEqual(UpdateTrayAction.InstallAndRestart, UpdatePresentationPolicy.ResolveTrayAction(State(UpdatePhase.Ready)));
        Assert.AreEqual(
            UpdateTrayAction.OpenUpdatePage,
            UpdatePresentationPolicy.ResolveTrayAction(
                State(UpdatePhase.Ready, installBlockedReason: UpdateInstallPlanPolicy.PortableBlockedReason)),
            "便携版无法自动安装时，托盘入口只能把人带到下载页。");

        // 托盘标题与动作必须来自同一个状态，否则会出现"写着点击重启、点下去却在检查更新"。
        // Title and action come from the same state, otherwise the entry could say "click to restart" while the
        // click actually checks for updates.
        Assert.AreEqual("检查更新", UpdatePresentationPolicy.ResolveTrayHeader(State(UpdatePhase.Idle)));
        Assert.AreEqual("发现新版本 v1.2.0（点击查看）", UpdatePresentationPolicy.ResolveTrayHeader(State(UpdatePhase.Available)));
        Assert.AreEqual("更新已就绪（v1.2.0），点击查看", UpdatePresentationPolicy.ResolveTrayHeader(State(UpdatePhase.Ready)));
    }

    [TestMethod]
    public void TaskbarUpdateNoticeAppearsOnlyWhenAReallyNewVersionIsKnown()
    {
        foreach (var phase in new[] { UpdatePhase.Idle, UpdatePhase.Checking, UpdatePhase.UpToDate, UpdatePhase.Skipped })
            Assert.IsFalse(UpdatePresentationPolicy.ShouldShowTrayNotice(State(phase)));

        foreach (var phase in new[] { UpdatePhase.Available, UpdatePhase.Downloading, UpdatePhase.Verifying,
                     UpdatePhase.Ready, UpdatePhase.ManualOnly })
            Assert.IsTrue(UpdatePresentationPolicy.ShouldShowTrayNotice(State(phase)), phase.ToString());

        Assert.IsTrue(UpdatePresentationPolicy.ShouldShowTrayNotice(State(UpdatePhase.Failed)));
        Assert.IsFalse(UpdatePresentationPolicy.ShouldShowTrayNotice(State(UpdatePhase.Failed) with { Manifest = null }));
        Assert.IsFalse(UpdatePresentationPolicy.ShouldShowTrayNotice(State(UpdatePhase.Failed) with
        {
            Manifest = UpdateVersionPolicyTests.CreateManifest("1.1.1")
        }));
        Assert.IsFalse(UpdatePresentationPolicy.ShouldShowTrayNotice(State(UpdatePhase.Available) with
        {
            Manifest = UpdateVersionPolicyTests.CreateManifest("1.1.1")
        }));
    }

    [TestMethod]
    public void UpdateSettingsNormalize_TrimsAndBoundsTheSkippedVersion()
    {
        Assert.IsNull((UpdateSettings.Default with { SkippedVersion = "   " }).Normalize().SkippedVersion);
        Assert.AreEqual("1.2.0", (UpdateSettings.Default with { SkippedVersion = "  1.2.0 " }).Normalize().SkippedVersion);
        Assert.AreEqual(
            UpdateSettings.MaximumSkippedVersionLength,
            (UpdateSettings.Default with { SkippedVersion = new string('9', 80) }).Normalize().SkippedVersion!.Length);
    }

    [TestMethod]
    public void BuildArguments_MatchesTheInstallerContract()
    {
        var arguments = UpdateInstallPlanPolicy.BuildArguments(relaunchAfterInstall: true, @"C:\Users\u\AppData\Local\AFMediaBar\updates\install-1.2.0.log");

        StringAssert.Contains(arguments, "/SILENT");
        StringAssert.Contains(arguments, "/SUPPRESSMSGBOXES");
        StringAssert.Contains(arguments, "/NORESTART");
        StringAssert.Contains(arguments, "/CLOSEAPPLICATIONS");
        StringAssert.Contains(arguments, "/AUTORELAUNCH=1");
        StringAssert.Contains(arguments, "/LOG=\"C:\\Users\\u\\AppData\\Local\\AFMediaBar\\updates\\install-1.2.0.log\"");
        Assert.IsFalse(
            arguments.Contains("/VERYSILENT", StringComparison.OrdinalIgnoreCase),
            "必须保留安装进度窗口，因此不能用 /VERYSILENT。");

        StringAssert.Contains(UpdateInstallPlanPolicy.BuildArguments(relaunchAfterInstall: false, "x.log"), "/AUTORELAUNCH=0");
    }

    [TestMethod]
    public void Decide_BlocksPortableCopiesAndOtherInstances()
    {
        var portable = UpdateInstallPlanPolicy.Decide(isInstalledCopy: false, isMachineWide: false, isElevated: false, otherInstanceCount: 0);
        Assert.IsFalse(portable.CanInstall);
        Assert.AreEqual(UpdateInstallPlanPolicy.PortableBlockedReason, portable.BlockedReason);

        var secondInstance = UpdateInstallPlanPolicy.Decide(true, false, false, otherInstanceCount: 1);
        Assert.IsFalse(secondInstance.CanInstall);
        Assert.AreEqual(UpdateInstallPlanPolicy.OtherInstancesBlockedReason, secondInstance.BlockedReason);
    }

    [TestMethod]
    public void Decide_ElevatesOnlyForMachineWideInstallsThatNeedIt()
    {
        var perUser = UpdateInstallPlanPolicy.Decide(true, isMachineWide: false, isElevated: false, otherInstanceCount: 0);
        Assert.IsTrue(perUser.CanInstall);
        Assert.IsFalse(perUser.UseRunAs);

        var machineWide = UpdateInstallPlanPolicy.Decide(true, isMachineWide: true, isElevated: false, otherInstanceCount: 0);
        Assert.IsTrue(machineWide.CanInstall);
        Assert.IsTrue(machineWide.UseRunAs, "Program Files 需要一次 UAC。");

        var alreadyElevated = UpdateInstallPlanPolicy.Decide(true, true, isElevated: true, otherInstanceCount: 0);
        Assert.IsFalse(alreadyElevated.UseRunAs);
    }

    [TestMethod]
    public void SkipAndInstallAvailability_RespectMandatoryAndBlockedStates()
    {
        Assert.IsTrue(UpdatePresentationPolicy.CanSkipVersion(State(UpdatePhase.Available)));
        Assert.IsFalse(
            UpdatePresentationPolicy.CanSkipVersion(State(UpdatePhase.Available, mandatory: true)),
            "必须更新时不允许跳过，否则用户会停在不再受支持的版本上。");
        Assert.IsFalse(UpdatePresentationPolicy.CanSkipVersion(State(UpdatePhase.Ready)));

        Assert.IsTrue(UpdatePresentationPolicy.CanInstallNow(State(UpdatePhase.Ready)));
        Assert.IsFalse(
            UpdatePresentationPolicy.CanInstallNow(
                State(UpdatePhase.Ready, installBlockedReason: UpdateInstallPlanPolicy.OtherInstancesBlockedReason)));
        Assert.IsFalse(UpdatePresentationPolicy.CanInstallNow(State(UpdatePhase.Available)));

        Assert.IsTrue(UpdatePresentationPolicy.IsProgressVisible(State(UpdatePhase.Downloading)));
        Assert.IsTrue(UpdatePresentationPolicy.IsProgressVisible(State(UpdatePhase.Verifying)));
        Assert.IsFalse(UpdatePresentationPolicy.IsProgressVisible(State(UpdatePhase.Ready)));
    }

    private static UpdateState State(
        UpdatePhase phase,
        double progress = 0d,
        bool mandatory = false,
        string? failureReason = null,
        string? installBlockedReason = null,
        UpdateDownloadSource? activeSource = null) =>
        new(
            phase,
            "1.1.1",
            phase is UpdatePhase.Idle or UpdatePhase.Checking or UpdatePhase.UpToDate
                ? null
                : UpdateVersionPolicyTests.CreateManifest("1.2.0"),
            progress,
            failureReason,
            activeSource,
            mandatory,
            phase == UpdatePhase.Skipped,
            installBlockedReason);
}
