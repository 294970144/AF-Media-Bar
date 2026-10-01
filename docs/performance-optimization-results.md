# 性能优化实施与测量（2026-10-02）

## 问题与改动

基线 `origin/main`（`c14930e`）在关闭歌词时仍于 XAML 构造 WebView2 歌词控件。本分支 `codex/performance-optimization` 改为：有已连接媒体和可用歌词时才创建；关闭歌词或控件卸载时立即释放；歌词暂时消失时保留 30 秒，避免频繁切歌反复启动浏览器。显示关闭或会话锁定后，已就绪的 WebView2 隐藏并尽力挂起；恢复时先唤醒，再投递当前样式和歌词帧。初始化与脚本失败有界重试，图形故障恢复仍受原预算控制。

宿主侧让频谱计时器只在频谱组件实际可见时运行。媒体进度快照不再每次请求任务栏重新定位；尺寸变化仍由 `DesiredSizeChanged` 驱动，Shell 状态仍有 1.5 秒定位检查。顺带修正一个基线音频生命周期测试：`Dispose` 与后台任务首次调度竞争时，任务取消与正常完成都表示已结束。

## 本机口径

- Windows 11，构建 26200；.NET SDK 10.0.202；Release win-x64；单次启动、单屏桌面。基线工作树 `E:\Project\AFMediaBar-perf-baseline`，优化工作树 `E:\Project\AFMediaBar-performance`。
- 暂时将 `settings.lyricsEnabled` 设为 `false`，每轮结束都用原始文件还原并核对 SHA-256。测试机当时仍有媒体与音频活动，**此场景是“关闭歌词”，不是 L1 无媒体待机**。
- `tools/measure-performance.ps1` 按根 PID、创建时刻和父子关系记录 AFMediaBar 及 WebView2 子进程；下表取各 40–45 秒短测的最后 4 个样本平均。工作集是驻留物理页；Private Bytes 是私有提交量。GPU 专用／共享显存来自该进程树 PID 对应的 Windows `GPU Process Memory` 计数器，并非整卡占用。
- 原始汇总及进程采样分别保存在本机 `%TEMP%\AFMediaBar-perf-final-baseline*.csv`、`%TEMP%\AFMediaBar-perf-final-optimized*.csv` 和 `%TEMP%\AFMediaBar-perf-geometry-optimized*.csv`。

| 场景 | WebView2 子进程 | 工作集 MiB | 私有提交 MiB | CPU（单核 %） | GPU 专用 MiB | GPU 共享 MiB |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| 基线，歌词关 | 6 | 473.01 | 633.56 | 10.88 | 31.73 | 147.48 |
| 优化，歌词关，定位改动前 | 0 | 88.75 | 311.78 | 10.87 | 9.67 | 69.81 |
| 优化，歌词关，定位改动后 | 0 | 89.90 | 314.23 | 7.42 | 9.48 | 70.06 |

这组短测显示：关闭歌词时，懒创建使 WebView2 子进程归零，私有提交量约降 322 MiB，GPU 专用显存计数约降 22 MiB。两次优化版内存读数相近。CPU 总量受媒体、Explorer 和桌面活动影响，表中差异只能作为线索；不能据此承诺固定降幅。

另一次暂时启用歌词的 35 秒启动探测中，优化版在可用歌词到达后创建了 6 个 WebView2 子进程，最终私有提交约 619 MiB；日志记录了歌词可用，未见 WebView2 初始化或脚本错误。它只证明懒创建的正向路径已启动，未验证歌词的视觉正确性。原设置同样已按 SHA-256 核对并还原。

20 秒 .NET CPU 采样里，任务栏 UIA 区域探测的归因样本在定位改动前约 286 ms，之后约 51 ms。该统计是采样估计，并非精确耗时；它支持“减少无几何变化快照引起的探测”这条因果判断。频谱不可见时停表尚未单独隔离测量。

## 参考文章后的核对

参考 [WebView2 应用资源占用优化指南](https://emohe.cn/posts/webview2%E4%BC%98%E5%8C%96/) 逐项对照代码，并以 [WebView2 官方性能建议](https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/performance) 和 [进程模型](https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/process-model) 核对 API 行为：

- **已覆盖**：按需创建和释放 WebView2、后台尽力挂起、单实例应用、本地用户数据目录、隐藏歌词时停止无限频谱动画。多个歌词控件使用相同用户数据目录和默认选项；官方进程模型指出这已能共享浏览器进程，因此没有仅为“显式共享”增加一个环境单例。
- **本轮补充**：媒体快照可能重复应用完全相同的 `LyricsWebStyle`。渲染器现在按值跳过重复样式，避免无变化时的 JSON 序列化、跨进程 `ExecuteScriptAsync` 与页面 CSS 更新。新建渲染器仍会发送完整首帧样式；此项未单独量化 CPU/GPU 收益。
- **未采用**：禁用 GPU、Edge 附加服务、限制 V8 堆、调整磁盘缓存及其他 browser flags。歌词有过渡与逐字动画；微软不建议生产应用依赖这些标志，也建议保留硬件加速。页面资源已经内嵌，未请求远程网页。单独给临时窗口创建用户数据目录也不适用于当前仅有的任务栏歌词视图。
- **后续仅在证据支持时实验**：`PostWebMessageAsJson` 或更小的进度补丁可能降低桥接开销，但现有执行队列会合并过期帧并观察脚本异常；直接换成无确认的投递可能堆积旧帧，影响快速唤醒。[官方 API 说明](https://learn.microsoft.com/en-us/dotnet/api/microsoft.web.webview2.core.corewebview2.memoryusagetargetlevel?view=webview2-dotnet-1.0.4191.47)要求 `MemoryUsageTargetLevel.Low` 与当前 `TrySuspendAsync`/`Resume` 二选一，不混用。长期内存增长若出现，再分析 JS heap、DOM 和事件订阅，而不按时间盲目刷新歌词视图。

## 验证与限制

- `dotnet restore .\src\AFMediaBar.slnx -r win-x64`：通过。
- 基线 Release build：通过，0 错误、16 个既有警告。基线测试首次 534 通过、1 失败（`AudioMonitorLifecycleTests.DisplayOffSuppressesDemandAndDisposeCancelsParkedWorker` 的取消异常），重跑 535 通过。
- 挂起改动首次 build 因遗漏 `MemoryPruneLevel` 命名空间失败；补齐引用后通过。此前同一音频用例连续两次因相同取消竞争失败，其余 537 通过；修正该测试的结束态断言后，最终 Release build 通过（0 错误、16 个既有警告），538/538 测试通过。
- 首次 `dotnet-trace` 命令使用不受当前工具支持的 profile 名称，未生成 CPU 结论；改用 `Microsoft-DotNETCore-SampleProfiler` 后，两次 20 秒跟踪成功。
- 本机多次启动了 Release 程序并采样进程树；测试脚本使用 `Stop-Process` 清理，**没有完成正常退出、可见歌词、关屏／锁屏唤醒、双屏、任务栏自动隐藏或 Explorer 重启的人工验收**。这些连同快速恢复到正确歌词帧、长期内存稳定性、Windows 10 与播放器兼容性均为**待维护者验收**。挂起 API 是尽力而为，`TrySuspendAsync` 返回 `false` 时会保持可恢复的可见性状态，不能把短测当作挂起成功证明。
