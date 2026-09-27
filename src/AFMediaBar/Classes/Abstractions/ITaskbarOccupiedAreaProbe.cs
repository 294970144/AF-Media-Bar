using AFMediaBar.Classes.Interop;
using AFMediaBar.Classes.Models.Layout;

namespace AFMediaBar.Classes.Abstractions;

/// <summary>
/// 在后台探测 Windows 任务栏占用区域的基础设施边界。
/// Infrastructure boundary that probes Windows taskbar occupancy in the background.
/// </summary>
public interface ITaskbarOccupiedAreaProbe
{
    /// <summary>
    /// 提交异步平台探测，完成或被后续请求替代时回调；探测器释放后不再回调。
    /// Schedules a probe and reports completion or supersession; disposal suppresses callbacks.
    /// </summary>
    /// <param name="taskbarHandle">任务栏窗口句柄 / Taskbar window handle.</param>
    /// <param name="taskbarRect">任务栏屏幕矩形 / Taskbar screen rectangle.</param>
    /// <param name="orientation">布局主轴方向 / Layout primary-axis orientation.</param>
    /// <param name="dpiScale">任务栏 DPI 缩放 / Taskbar DPI scale.</param>
    /// <param name="edgePaddingPixels">媒体栏边缘留白 / Media-bar edge padding.</param>
    /// <param name="succeeded">成功时接收安全主轴区间的回调 / Callback receiving safe primary-axis ranges on success.</param>
    /// <param name="failed">请求被替代、线程启动或探测失败时的回调 / Callback invoked on supersession, worker startup failure or probe failure.</param>
    void Start(
        IntPtr taskbarHandle,
        NativeMethods.RECT taskbarRect,
        LayoutOrientation orientation,
        double dpiScale,
        int edgePaddingPixels,
        Action<IReadOnlyList<TaskbarPrimaryRange>> succeeded,
        Action failed);
}
