// Verifies the media bar can measure positions for both canvas children and sibling rest widgets.
// The STA test owns only in-memory WPF visuals and does not start a taskbar host.
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using AFMediaBar.Components;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AFMediaBar.Layout.Tests;

[TestClass]
public sealed class RestVisualCoordinateTests
{
    [TestMethod]
    public void SiblingRestWidgetCanTranslateToArtworkCanvas()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var root = new Grid { Width = 300, Height = 50 };
                var canvas = new Canvas { Width = 300, Height = 50 };
                var artwork = new Border { Width = 36, Height = 36 };
                Canvas.SetLeft(artwork, 20);
                Canvas.SetTop(artwork, 4);
                canvas.Children.Add(artwork);
                root.Children.Add(canvas);

                var spectrum = new Border
                {
                    Width = 36,
                    Height = 32,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(80, 5, 0, 0)
                };
                root.Children.Add(spectrum);
                root.Measure(new Size(300, 50));
                root.Arrange(new Rect(0, 0, 300, 50));

                Assert.IsTrue(TaskBarMediaControl.TryGetRestPosition(artwork, canvas, out var artworkPoint));
                Assert.AreEqual(20, artworkPoint.X, 0.01);
                Assert.AreEqual(4, artworkPoint.Y, 0.01);
                Assert.IsTrue(TaskBarMediaControl.TryGetRestPosition(spectrum, canvas, out var spectrumPoint));
                Assert.AreEqual(80, spectrumPoint.X, 0.01);
                Assert.AreEqual(5, spectrumPoint.Y, 0.01);

                root.Children.Remove(spectrum);
                Assert.IsFalse(TaskBarMediaControl.TryGetRestPosition(spectrum, canvas, out _));
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        Assert.IsNull(failure, failure?.ToString());
    }
}
