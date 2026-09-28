// Coordinates taskbar rest-component transitions across media connection changes.
// The control owns and releases temporary bitmap ghosts and its completion timer.
using System.Windows;
using System.Windows.Controls;
using System.Diagnostics;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using AFMediaBar.Classes.Models.Layout;
using AFMediaBar.Classes.Services;
using AFMediaBar.Classes.Settings;

namespace AFMediaBar.Components;

/// <summary>媒体栏静置组件的身份匹配过渡。/ Identity-matched transitions for taskbar rest components.</summary>
public partial class TaskBarMediaControl
{
    private readonly List<Image> _restTransitionGhosts = [];
    private DispatcherTimer? _restTransitionCompletionTimer;
    private bool _restTransitionDefersHide;

    /// <summary>退场完成后通知宿主重算窗口显隐。/ Notifies the host to re-evaluate window visibility after an exit.</summary>
    public event EventHandler? RestTransitionFinished;

    private sealed record RestVisual(
        FrameworkElement Element,
        Point Position,
        double Width,
        double Height,
        double Opacity,
        BitmapSource? Bitmap);

    private Dictionary<TaskbarRestComponent, RestVisual>? CaptureRestVisuals(bool targetConnected)
    {
        if (!IsLoaded || _currentMode != WindowMode.Taskbar || _isVertical || !CurrentMotion.UseTransitions)
            return null;

        var experience = SettingsManager.Current.TaskbarExperience.Normalize();
        var targetVisibility = new TaskbarRestLayoutPolicy.Visibility(
            targetConnected,
            experience.IdleComponents,
            experience.SpectrumVisible,
            experience.PerformanceVisible,
            experience.OutputDeviceVisible,
            experience.VolumeVisible);
        var visuals = new Dictionary<TaskbarRestComponent, RestVisual>();
        foreach (var component in TaskbarRestLayoutPolicy.DefaultOrder)
        {
            var element = GetRestElement(component);
            if (element.Visibility != Visibility.Visible || element.ActualWidth <= 0 || element.ActualHeight <= 0)
                continue;

            if (!TryGetRestPosition(element, MainCanvas, out var point))
                continue;
            var dpi = VisualTreeHelper.GetDpi(element);
            var pixelWidth = (int)Math.Ceiling(element.ActualWidth * dpi.DpiScaleX);
            var pixelHeight = (int)Math.Ceiling(element.ActualHeight * dpi.DpiScaleY);
            BitmapSource? bitmap = null;
            var needsBitmap = component == TaskbarRestComponent.Artwork ||
                !TaskbarRestLayoutPolicy.IsVisible(component, targetVisibility);
            if (needsBitmap && pixelWidth > 0 && pixelHeight > 0 && pixelWidth <= 4096 && pixelHeight <= 1024)
            {
                try
                {
                    var render = new RenderTargetBitmap(pixelWidth, pixelHeight,
                        96 * dpi.DpiScaleX, 96 * dpi.DpiScaleY, PixelFormats.Pbgra32);
                    render.Render(element);
                    render.Freeze();
                    bitmap = render;
                }
                catch (Exception ex)
                {
                    // A temporarily detached WebView or visual can still join the live transition without a ghost.
                    Debug.WriteLine(ex);
                }
            }

            visuals[component] = new RestVisual(element, point, element.ActualWidth,
                element.ActualHeight, element.Opacity, bitmap);
        }

        return visuals;
    }

    private FrameworkElement GetRestElement(TaskbarRestComponent component) => component switch
    {
        TaskbarRestComponent.Artwork => SongImageBorder,
        TaskbarRestComponent.MediaText => SongInfoStackPanel,
        TaskbarRestComponent.Spectrum => TaskbarSpectrumHoverSurface,
        TaskbarRestComponent.Performance => TaskbarPerformanceHoverSurface,
        TaskbarRestComponent.OutputDevice => TaskbarOutputDeviceHoverSurface,
        TaskbarRestComponent.Volume => TaskbarVolumeHoverSurface,
        _ => throw new ArgumentOutOfRangeException(nameof(component))
    };

    internal static bool TryGetRestPosition(FrameworkElement element, UIElement canvas, out Point position)
    {
        position = default;
        if (element.FindCommonVisualAncestor(canvas) is null)
            return false;

        try
        {
            // The four rest widgets are siblings of MainCanvas, not descendants of it.
            // TranslatePoint uses their shared ancestor and also covers artwork/text inside the canvas.
            position = element.TranslatePoint(new Point(), canvas);
            return double.IsFinite(position.X) && double.IsFinite(position.Y);
        }
        catch (InvalidOperationException)
        {
            // A control can leave the visual tree while its media or host window is changing.
            return false;
        }
    }

    private void AnimateRestConnectionChange(Dictionary<TaskbarRestComponent, RestVisual>? before)
    {
        if (before is null)
            return;

        ClearRestTransitionGhosts();
        _restTransitionCompletionTimer?.Stop();
        var motion = CurrentMotion;
        var targetDuration = motion.PositionDuration;

        // Retarget from the visible position captured above. A previous transition's render offset
        // must not become part of the new layout destination.
        foreach (var component in TaskbarRestLayoutPolicy.DefaultOrder)
            GetRestElement(component).RenderTransform = Transform.Identity;
        InteractionSurface.UpdateLayout();

        foreach (var component in TaskbarRestLayoutPolicy.DefaultOrder)
        {
            var element = GetRestElement(component);
            var wasVisible = before.TryGetValue(component, out var previous);
            var isVisible = element.Visibility == Visibility.Visible &&
                (element.ActualWidth > 0 || (double.IsFinite(element.Width) && element.Width > 0));

            if (wasVisible && (!isVisible || component == TaskbarRestComponent.Artwork))
                AddRestTransitionGhost(previous!, component == TaskbarRestComponent.Artwork && isVisible
                    ? motion.StandardDuration : motion.ExitDuration);

            if (!isVisible)
                continue;

            // The layout lands first. Its inverse transform keeps the old screen position visible,
            // then one geometry animation lets the persistent component travel to its new position.
            if (!TryGetRestPosition(element, MainCanvas, out var target))
                continue;
            var fromX = wasVisible ? previous!.Position.X - target.X : -4;
            var fromY = wasVisible ? previous!.Position.Y - target.Y : 0;
            var transform = new TranslateTransform();
            element.RenderTransform = transform;
            if (Math.Abs(fromX) > 0.25)
                transform.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(fromX, 0, targetDuration)
                {
                    EasingFunction = CreateEaseOut(), FillBehavior = FillBehavior.Stop
                }, HandoffBehavior.SnapshotAndReplace);
            if (Math.Abs(fromY) > 0.25)
                transform.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(fromY, 0, targetDuration)
                {
                    EasingFunction = CreateEaseOut(), FillBehavior = FillBehavior.Stop
                }, HandoffBehavior.SnapshotAndReplace);

            var targetOpacity = component == TaskbarRestComponent.MediaText && _isTaskbarHoverVisible
                ? TaskbarCoveredOpacity : 1;
            element.Opacity = targetOpacity;
            element.BeginAnimation(OpacityProperty, new DoubleAnimation(
                wasVisible && component != TaskbarRestComponent.Artwork ? previous!.Opacity : 0,
                targetOpacity,
                motion.StandardDuration)
            {
                EasingFunction = CreateEaseOut(), FillBehavior = FillBehavior.Stop
            }, HandoffBehavior.SnapshotAndReplace);
        }

        _restTransitionDefersHide = !_isConnected && _isRestLayerEmpty && _restTransitionGhosts.Count > 0;
        _restTransitionCompletionTimer ??= new DispatcherTimer(DispatcherPriority.Background);
        _restTransitionCompletionTimer.Interval = targetDuration + TimeSpan.FromMilliseconds(30);
        _restTransitionCompletionTimer.Tick -= RestTransitionCompletionTimer_Tick;
        _restTransitionCompletionTimer.Tick += RestTransitionCompletionTimer_Tick;
        _restTransitionCompletionTimer.Start();
    }

    private void AddRestTransitionGhost(RestVisual previous, TimeSpan duration)
    {
        if (previous.Bitmap is null)
            return;

        var ghost = new Image
        {
            Source = previous.Bitmap,
            Width = previous.Width,
            Height = previous.Height,
            IsHitTestVisible = false,
            Opacity = 1,
            Stretch = Stretch.Fill
        };
        Canvas.SetLeft(ghost, previous.Position.X);
        Canvas.SetTop(ghost, previous.Position.Y);
        Panel.SetZIndex(ghost, 100);
        MainCanvas.Children.Add(ghost);
        _restTransitionGhosts.Add(ghost);
        ghost.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0, duration)
        {
            EasingFunction = CreateEaseOut(), FillBehavior = FillBehavior.Stop
        }, HandoffBehavior.SnapshotAndReplace);
    }

    private void RestTransitionCompletionTimer_Tick(object? sender, EventArgs e)
    {
        _restTransitionCompletionTimer?.Stop();
        FinishRestTransition();
    }

    private void FinishRestTransition()
    {
        ClearRestTransitionGhosts();
        if (!_restTransitionDefersHide)
            return;

        _restTransitionDefersHide = false;
        RestTransitionFinished?.Invoke(this, EventArgs.Empty);
    }

    private void ClearRestTransitionGhosts()
    {
        foreach (var ghost in _restTransitionGhosts)
        {
            ghost.BeginAnimation(OpacityProperty, null);
            MainCanvas.Children.Remove(ghost);
        }
        _restTransitionGhosts.Clear();
    }

    private void StopRestTransitions()
    {
        _restTransitionCompletionTimer?.Stop();
        _restTransitionDefersHide = false;
        ClearRestTransitionGhosts();
    }
}
