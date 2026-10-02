# AF Media Bar 1.3.1 — 性能优化与稳定性修复

这次更新主要优化 1.3.0 的性能问题，减少后台开销，改进任务栏显隐，并修复几项小问题。原有设置继续保留。

## ⚡ 性能优化

频谱隐藏时停止采样，播放进度更新不再反复定位媒体栏；文字、歌词样式和频谱动画也减少了重复处理。
歌词按需加载，关闭或持续无歌词时释放资源；关屏或锁屏时尝试挂起，减少后台负担。

注:安装目录下的 AFMediaBar.exe.WebView2，文件已不再需要，可手动删除。

## ✨ 任务栏显隐动画

媒体栏随自动隐藏任务栏一起滑入、滑出，歌词画面保持连续。移动期间暂停点击、滚轮和拖动，展开稳定后恢复交互。

## 🔧 其他更新

- 修复安装目录不可写时的 WebView2 数据目录错误。
- 修复「显示模式」恢复默认时未还原封面开关的问题。
- 修复空间音效关闭后仍显示已启用的问题。
- 再次检查更新会保留已就绪的安装包；安装前重新校验，拒绝异常路径或被修改的文件。

## 下载与安装

系统要求与安装方式见 [README](https://github.com/Fervent-Tempo/AF-Media-Bar#readme)。安装包和便携版均自带 .NET 运行时。

- `AFMediaBar-Setup-v1.3.1-win-x64.exe`：安装版。
- `AFMediaBar-v1.3.1-win-x64.zip`：便携版，解压即用。

请使用同一 Release 的 `SHA256SUMS.txt` 核对下载文件，不要使用 GitHub 自动生成的 Source code 压缩包。

## 已知限制

- 目前仅发布 `win-x64`，支持横向任务栏；竖向任务栏与悬浮模式尚未实现。
- 隐私说明与卸载方式见 [README](https://github.com/Fervent-Tempo/AF-Media-Bar#readme)。

## 贡献者

感谢 @Gorden-86、@x1a0Y4NGren 和 @294970144 为本次修复做出贡献。

## 首次贡献

- @Gorden-86：首次贡献见 [#125](https://github.com/Fervent-Tempo/AF-Media-Bar/pull/125)。
- @294970144：首次贡献见 [#133](https://github.com/Fervent-Tempo/AF-Media-Bar/pull/133)。
