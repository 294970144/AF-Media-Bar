using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Settings;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

/// <summary>
/// 滚轮提示文案、组合键判定与开机启动项命令行。
/// Wheel tooltip text, chord-key resolution, and the run-at-startup command line.
/// </summary>
[TestClass]
public sealed class WheelTooltipAndStartupPolicyTests
{
    /// <summary>
    /// 悬停时普通与组合滚轮各说各的动作，组合那一行必须带上当前按键的名字，否则用户得自己记住绑定。
    /// While hovering, the plain and chord wheels each state their own action, and the chord line has to name the current key or the
    /// user would have to remember the binding.
    /// </summary>
    [TestMethod]
    public void HintsNameTheSlotAndTheCurrentModifier()
    {
        Assert.AreEqual(
            "普通滚轮：下一首 / 上一首",
            WheelTooltipPolicy.BuildHint(WheelGestureSlot.Primary, InteractionModifier.Shift, "下一首 / 上一首"));
        Assert.AreEqual(
            "Shift 键 + 滚轮：切换播放器",
            WheelTooltipPolicy.BuildHint(WheelGestureSlot.Chord, InteractionModifier.Shift, "切换播放器"));
        Assert.AreEqual(
            "鼠标右键 + 滚轮：切换播放器",
            WheelTooltipPolicy.BuildHint(WheelGestureSlot.Chord, InteractionModifier.RightMouseButton, "切换播放器"));
        Assert.AreEqual("鼠标左键", WheelTooltipPolicy.BuildModifierName(InteractionModifier.LeftMouseButton));

        Assert.AreEqual("上一首 / 下一首", WheelTooltipPolicy.BuildActionName(WheelAction.PreviousNext));
        Assert.AreEqual("调节当前媒体音量", WheelTooltipPolicy.BuildActionName(TrayWheelBehavior.AdjustVolume));
        Assert.AreEqual("未绑定", WheelTooltipPolicy.BuildActionName(TrayWheelBehavior.Disabled));
        Assert.AreEqual("未绑定", WheelTooltipPolicy.BuildActionName((WheelAction)99));
    }

    /// <summary>
    /// 滚动之后提示要说结果，并且结果缺失时不能留下一个悬空的冒号。切歌方向按滚轮方向取名。
    /// After a scroll the tooltip states the result, and a missing result must not leave a dangling colon. The skip action is named
    /// after the wheel direction.
    /// </summary>
    [TestMethod]
    public void ResultsCarryTheOutcomeAndFallBackToTheActionName()
    {
        Assert.AreEqual("下一首：夜航", WheelTooltipPolicy.BuildResult("下一首", "夜航"));
        Assert.AreEqual("播放源：Spotify", WheelTooltipPolicy.BuildResult("播放源", "Spotify"));
        Assert.AreEqual("下一首", WheelTooltipPolicy.BuildResult("下一首", null));
        Assert.AreEqual("下一首", WheelTooltipPolicy.BuildResult("下一首", "   "));
        Assert.AreEqual("上一首", WheelTooltipPolicy.BuildSkipActionName(120));
        Assert.AreEqual("下一首", WheelTooltipPolicy.BuildSkipActionName(-120));

        Assert.AreEqual(
            "普通滚轮：切换输出设备（当前：扬声器）",
            WheelTooltipPolicy.BuildHintWithValue("普通滚轮：切换输出设备", "扬声器"));
        Assert.AreEqual(
            "普通滚轮：切换输出设备",
            WheelTooltipPolicy.BuildHintWithValue("普通滚轮：切换输出设备", null));
    }

    /// <summary>
    /// 无媒体时在音符上滚轮挑播放器，提示要显示当前候选的名字；没有名字时退回"快速启动"本身，
    /// 而不是显示一个后面什么都没有的冒号或空字符串。
    /// Wheeling over the note to pick a player while there is no media shows the current candidate's name; with no name it falls back to
    /// "quick launch" itself instead of a dangling colon or an empty string.
    /// </summary>
    [TestMethod]
    public void QuickLaunchPreviewNamesTheCandidate()
    {
        Assert.AreEqual("快速启动：网易云音乐", WheelTooltipPolicy.BuildQuickLaunchPreview("网易云音乐"));
        Assert.AreEqual("快速启动：网易云音乐", WheelTooltipPolicy.BuildQuickLaunchPreview("  网易云音乐  "));
        Assert.AreEqual("快速启动", WheelTooltipPolicy.BuildQuickLaunchPreview(null));
        Assert.AreEqual("快速启动", WheelTooltipPolicy.BuildQuickLaunchPreview("   "));
    }

    /// <summary>
    /// 组合键判定只有一处：媒体栏与托盘各自提供输入状态，但必须得到同一个结论。
    /// The chord rule lives in one place: the media bar and the tray supply their own input state and must reach the same conclusion.
    /// </summary>
    [TestMethod]
    public void ChordHeldFollowsTheSharedModifierOnly()
    {
        var shift = GlobalInteractionSettings.Default with { Modifier = InteractionModifier.Shift };
        var left = shift with { Modifier = InteractionModifier.LeftMouseButton };
        var right = shift with { Modifier = InteractionModifier.RightMouseButton };

        Assert.IsTrue(GlobalWheelGesturePolicy.IsChordHeld(shift, true, false, false));
        Assert.IsFalse(GlobalWheelGesturePolicy.IsChordHeld(shift, false, true, true));
        Assert.IsTrue(GlobalWheelGesturePolicy.IsChordHeld(left, false, true, false));
        Assert.IsFalse(GlobalWheelGesturePolicy.IsChordHeld(left, true, false, true));
        Assert.IsTrue(GlobalWheelGesturePolicy.IsChordHeld(right, false, false, true));
        Assert.IsFalse(GlobalWheelGesturePolicy.IsChordHeld(right, true, true, false));

        // 组合键按住时解析到组合动作，松开时回到普通动作。
        // Holding the chord key resolves to the chord action and releasing it falls back to the plain one.
        Assert.AreEqual(
            shift.ChordWheelAction,
            GlobalWheelGesturePolicy.Resolve(shift, isShiftDown: true, false, false));
        Assert.AreEqual(
            shift.PrimaryWheelAction,
            GlobalWheelGesturePolicy.Resolve(shift, isShiftDown: false, false, false));
        Assert.AreEqual(
            right.TrayChordWheelAction,
            GlobalWheelGesturePolicy.ResolveTray(right, false, false, isRightButtonDown: true));
    }

    /// <summary>
    /// 启动项命令行必须加引号：安装目录可能含空格，不加引号时 Windows 会在第一个空格处断开，于是启动的是另一个路径。
    /// The startup command line has to quote the path: the installation directory can contain spaces, and without quotes Windows
    /// splits at the first space, which launches a different path.
    /// </summary>
    [TestMethod]
    public void StartupCommandLineQuotesThePathAndMatchingIgnoresDecorations()
    {
        Assert.AreEqual("\"C:\\Apps\\AF Media Bar\\AFMediaBar.exe\"",
            StartupRegistrationPolicy.BuildCommandLine("C:\\Apps\\AF Media Bar\\AFMediaBar.exe"));
        // 已经带引号的路径不会变成双重引号。
        // An already quoted path does not become double quoted.
        Assert.AreEqual("\"C:\\Apps\\AFMediaBar.exe\"",
            StartupRegistrationPolicy.BuildCommandLine("\"C:\\Apps\\AFMediaBar.exe\""));

        Assert.IsTrue(StartupRegistrationPolicy.Matches(
            "\"C:\\Apps\\AFMediaBar.exe\"",
            "C:\\Apps\\AFMediaBar.exe"));
        Assert.IsTrue(StartupRegistrationPolicy.Matches(
            "\"c:\\apps\\afmediabar.exe\"",
            "C:\\Apps\\AFMediaBar.exe"));
        Assert.IsTrue(StartupRegistrationPolicy.Matches(
            "\"C:\\Apps\\AFMediaBar.exe\" --minimized",
            "C:\\Apps\\AFMediaBar.exe"));
        // 未加引号的记录也能识别，但只比较第一个空格前的部分。
        // An unquoted entry is still recognized, comparing only the part before the first space.
        Assert.IsTrue(StartupRegistrationPolicy.Matches(
            "C:\\Apps\\AFMediaBar.exe --minimized",
            "C:\\Apps\\AFMediaBar.exe"));

        Assert.IsFalse(StartupRegistrationPolicy.Matches(null, "C:\\Apps\\AFMediaBar.exe"));
        Assert.IsFalse(StartupRegistrationPolicy.Matches("  ", "C:\\Apps\\AFMediaBar.exe"));
        Assert.IsFalse(StartupRegistrationPolicy.Matches("\"C:\\Other\\AFMediaBar.exe\"", "C:\\Apps\\AFMediaBar.exe"));
        Assert.IsFalse(StartupRegistrationPolicy.Matches("\"C:\\Apps\\AFMediaBar.exe\"", "  "));
    }

    /// <summary>
    /// 开启时只有缺失或指向旧路径才写：已指向当前可执行文件的记录（含用户手写的参数）保持不动。
    /// Writes while enabling only when the entry is missing or stale; an entry already pointing at the current executable,
    /// including arguments the user added by hand, is left alone.
    /// </summary>
    [TestMethod]
    public void StartupWriteIsSkippedWhenTheEntryAlreadyPointsAtThisExecutable()
    {
        const string executable = "C:\\Apps\\AFMediaBar.exe";

        // 缺失与指向旧路径都要补写，这两种正是"修复"要处理的情形。
        // A missing entry and a stale one both get written; those are exactly the states a repair has to handle.
        Assert.IsTrue(StartupRegistrationPolicy.ShouldWrite(true, null, executable));
        Assert.IsTrue(StartupRegistrationPolicy.ShouldWrite(true, "\"C:\\Old\\AFMediaBar.exe\"", executable));
        Assert.IsTrue(StartupRegistrationPolicy.ShouldWrite(true, "  ", executable));

        // 已指向当前程序时不写，用户手写的参数因此得以保留。
        // Nothing is written while it already points here, so arguments the user added survive.
        Assert.IsFalse(StartupRegistrationPolicy.ShouldWrite(
            true, "\"C:\\Apps\\AFMediaBar.exe\"", executable));
        Assert.IsFalse(StartupRegistrationPolicy.ShouldWrite(
            true, "\"c:\\apps\\afmediabar.exe\"", executable));
        Assert.IsFalse(StartupRegistrationPolicy.ShouldWrite(
            true, "\"C:\\Apps\\AFMediaBar.exe\" --minimized", executable));
    }

    /// <summary>
    /// 关闭时只要那一项在就删：值名属于本程序，即便它指向已不存在的路径，也是用户要求"别再开机启动"时最该清掉的一条。
    /// When disabling, an existing entry is removed: the value name belongs to this application, so even one pointing at a path
    /// that no longer exists is the record a user asking not to start at login wants gone.
    /// </summary>
    [TestMethod]
    public void StartupWriteRemovesAnyEntryWhenDisabling()
    {
        const string executable = "C:\\Apps\\AFMediaBar.exe";

        // 已经没有了就是目标状态，不必再删一次。
        // An absent entry already is the target state, so there is nothing to delete.
        Assert.IsFalse(StartupRegistrationPolicy.ShouldWrite(false, null, executable));

        // 指向本程序、指向旧路径、指向别处，只要这一项在都删。
        // Pointing here, pointing at an old path, or pointing elsewhere: an existing entry goes in every case.
        Assert.IsTrue(StartupRegistrationPolicy.ShouldWrite(false, "\"C:\\Apps\\AFMediaBar.exe\"", executable));
        Assert.IsTrue(StartupRegistrationPolicy.ShouldWrite(false, "\"C:\\Old\\AFMediaBar.exe\"", executable));
    }
}
