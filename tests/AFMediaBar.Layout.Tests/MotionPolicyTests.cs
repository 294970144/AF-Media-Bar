using AFMediaBar.Classes.Services;
using System.Windows.Media.Animation;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

[TestClass]
public sealed class MotionPolicyTests
{
    [TestMethod]
    public void Resolve_DisabledClientAnimations_IsInstant()
    {
        var profile = MotionPolicy.Resolve(clientAreaAnimation: false, highContrast: false, lowPerformance: false);

        Assert.AreEqual(MotionMode.Instant, profile.Mode);
        Assert.IsFalse(profile.UseTransitions);
        Assert.IsFalse(profile.UseDecorativeEffects);
        Assert.IsFalse(profile.UseContinuousMotion);
        Assert.AreEqual(TimeSpan.Zero, profile.PositionDuration);
        Assert.AreEqual(TimeSpan.Zero, profile.RestConnectionDuration);
    }

    [TestMethod]
    public void Resolve_HighContrast_UsesReducedMotion()
    {
        var profile = MotionPolicy.Resolve(clientAreaAnimation: true, highContrast: true, lowPerformance: false);

        Assert.AreEqual(MotionMode.Reduced, profile.Mode);
        Assert.IsTrue(profile.UseTransitions);
        Assert.IsFalse(profile.UseDecorativeEffects);
        Assert.IsFalse(profile.UseContinuousMotion);
        Assert.AreEqual(160, profile.PositionDuration.TotalMilliseconds);
        Assert.AreEqual(160, profile.RestConnectionDuration.TotalMilliseconds);
    }

    [TestMethod]
    public void Resolve_LowPerformance_UsesReducedMotion()
    {
        var profile = MotionPolicy.Resolve(clientAreaAnimation: true, highContrast: false, lowPerformance: true);

        Assert.AreEqual(MotionMode.Reduced, profile.Mode);
        Assert.IsFalse(profile.UseDecorativeEffects);
        Assert.IsFalse(profile.UseContinuousMotion);
    }

    [TestMethod]
    public void Resolve_NormalDesktop_UsesFullMotion()
    {
        var profile = MotionPolicy.Resolve(clientAreaAnimation: true, highContrast: false, lowPerformance: false);

        Assert.AreEqual(MotionMode.Full, profile.Mode);
        Assert.AreEqual(180, profile.StandardDuration.TotalMilliseconds);
        Assert.AreEqual(180, profile.PanelDuration.TotalMilliseconds);
        Assert.AreEqual(220, profile.PositionDuration.TotalMilliseconds);
        Assert.AreEqual(450, profile.RestConnectionDuration.TotalMilliseconds);
        Assert.AreEqual(120, profile.ExitDuration.TotalMilliseconds);
        Assert.IsTrue(profile.UseDecorativeEffects);
        Assert.IsTrue(profile.UseContinuousMotion);
    }

    [TestMethod]
    public void RestConnectionEaseUsesSmoothSymmetricProgress()
    {
        var ease = MotionPolicy.CreateRestConnectionEase();
        Assert.AreEqual(EasingMode.EaseInOut, ease.EasingMode);
        Assert.AreEqual(0, ease.Ease(0), 0.0001);
        Assert.AreEqual(0.5, ease.Ease(0.5), 0.0001);
        Assert.AreEqual(1, ease.Ease(1), 0.0001);
        Assert.IsTrue(ease.Ease(0.25) < 0.25);
        Assert.AreEqual(1, ease.Ease(0.25) + ease.Ease(0.75), 0.0001);
    }

    [TestMethod]
    public void Resolve_AllProfilesStayWithinUiMotionBudget()
    {
        var profiles = new[]
        {
            MotionPolicy.Resolve(clientAreaAnimation: true, highContrast: false, lowPerformance: false),
            MotionPolicy.Resolve(clientAreaAnimation: true, highContrast: true, lowPerformance: false),
            MotionPolicy.Resolve(clientAreaAnimation: false, highContrast: false, lowPerformance: false)
        };

        foreach (var profile in profiles)
        {
            Assert.IsTrue(profile.FastDuration >= TimeSpan.Zero && profile.FastDuration <= TimeSpan.FromMilliseconds(300));
            Assert.IsTrue(profile.StandardDuration >= TimeSpan.Zero && profile.StandardDuration <= TimeSpan.FromMilliseconds(300));
            Assert.IsTrue(profile.PanelDuration >= TimeSpan.Zero && profile.PanelDuration <= TimeSpan.FromMilliseconds(300));
            Assert.IsTrue(profile.PositionDuration >= TimeSpan.Zero && profile.PositionDuration <= TimeSpan.FromMilliseconds(300));
            Assert.IsTrue(profile.ExitDuration >= TimeSpan.Zero && profile.ExitDuration <= TimeSpan.FromMilliseconds(300));
        }
    }
}
