using AFMediaBar.Classes.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

/// <summary>
/// 任务管理器"已禁用"审批记录的还原与判定（StartupApproved 修复）。
/// Restore and detection of Task Manager's "disabled" approval record (StartupApproved fix).
/// </summary>
[TestClass]
public sealed class StartupApprovedPolicyTests
{
    /// <summary>
    /// 任务管理器的"已禁用"记录在用户从程序内打开开关时必须还原成启用，时间戳一并清零。
    /// A Task Manager "disabled" record has to be restored to enabled when the user turns the switch on, timestamp zeroed too.
    /// </summary>
    [TestMethod]
    public void DisabledApprovalIsRestoredToEnabledOnRequest()
    {
        // 禁用态有两种：3 允许用户再启用，9 是灰显（用户不能启用），分别还原成 2 与 8。
        // Two disabled forms: 3 can be re-enabled by the user, 9 is greyed out; they map to 2 and 8.
        foreach (var (disabled, enabled) in new[] { (0x03, 0x02), (0x09, 0x08) })
        {
            var record = new byte[12];
            BitConverter.TryWriteBytes(record.AsSpan(0, 4), disabled);
            for (var i = 4; i < 12; i++)
                record[i] = 0xAB; // 非零时间戳，确认还原时它被清零。/ Non-zero, to prove enabling clears it.

            Assert.IsTrue(StartupRegistrationPolicy.TryBuildEnabledApproval(record, out var restored));
            Assert.AreEqual(enabled, BitConverter.ToInt32(restored, 0));
            for (var i = 4; i < 12; i++)
                Assert.AreEqual(0, restored[i], $"byte {i} should be cleared");
        }
    }

    /// <summary>
    /// 已经被用户改过或从未被改过的记录都不动：程序不凭空造记录，也不改看不懂的记录。
    /// Records that are already enabled, never touched, or unrecognised stay untouched: the program neither invents a record
    /// nor rewrites one it cannot read.
    /// </summary>
    [TestMethod]
    public void ApprovalIsLeftAloneUnlessItIsARecognisedDisabledRecord()
    {
        byte[] Approval(int state)
        {
            var record = new byte[12];
            BitConverter.TryWriteBytes(record.AsSpan(0, 4), state);
            return record;
        }

        // 从未动过、已是启用态、格式不认识、长度不足：都不动。写坏记录比不禁用更难排查。
        // Never touched, already enabled, unrecognised, too short: all stay untouched, since a corrupted record is harder to
        // diagnose than a missing one.
        foreach (var state in new[] { 0x02, 0x08, 0x00, 0x01, 0x06, 0xFF })
            Assert.IsFalse(StartupRegistrationPolicy.TryBuildEnabledApproval(Approval(state), out _));

        Assert.IsFalse(StartupRegistrationPolicy.TryBuildEnabledApproval(null, out _));
        Assert.IsFalse(StartupRegistrationPolicy.TryBuildEnabledApproval([], out _));
        Assert.IsFalse(StartupRegistrationPolicy.TryBuildEnabledApproval([0x03, 0x00, 0x00, 0x00], out _));
    }

    /// <summary>
    /// 只有 3 与 9算「被禁用」，它认出的取值必须与还原时一致——两处判断若分叉，开关会显示与实际相反的状态。
    /// Only 3 and 9 count as disabled, and they must be exactly the states enabling restores: if the two disagreed, the
    /// switch would display the opposite of reality.
    /// </summary>
    [TestMethod]
    public void OnlyTheRestorableStatesCountAsDisabled()
    {
        byte[] Approval(int state)
        {
            var record = new byte[12];
            BitConverter.TryWriteBytes(record.AsSpan(0, 4), state);
            return record;
        }

        foreach (var disabled in new[] { 0x03, 0x09 })
            Assert.IsTrue(StartupRegistrationPolicy.IsDisabled(Approval(disabled)));

        // 启用、未知、缺失、太短：都不是禁用。
        // Enabled, unknown, absent, too short: none of them is disabled.
        foreach (var state in new[] { 0x02, 0x08, 0x00, 0xFF })
            Assert.IsFalse(StartupRegistrationPolicy.IsDisabled(Approval(state)));

        Assert.IsFalse(StartupRegistrationPolicy.IsDisabled(null));
        Assert.IsFalse(StartupRegistrationPolicy.IsDisabled([]));
        Assert.IsFalse(StartupRegistrationPolicy.IsDisabled([0x03, 0x00, 0x00, 0x00]));
    }
}
