# AF Media Bar 1.3.0 — 歌词与媒体栏体验更新

这次更新改进了歌词显示、播放状态切换和媒体栏尺寸调整，也修复了多项媒体与音频问题。

## 🎵 更好看的歌词

歌词改用 WebView2 显示，逐字高亮、译文和音译的排版更统一。现在可以调整行距、字距和显示区域长度；歌词查找也经过优化，能匹配更多歌曲。

## ✨ 更顺滑的状态切换

从无媒体音符切换到封面、歌词和播放信息时，内容会连贯展开；播放结束时反向收起。快速调整媒体栏宽度时，组件抖动、裁切和位置跳动也有所减少。

## 🧩 更方便的设置

新增滚轮操作提示开关、交互按钮大小调节和独立的组件设置页。静置层封面现在可以单独关闭，无媒体时的小音符仍由原来的选项控制。

## 🔧 其他更新

- 关闭浏览器媒体后更快收起旧画面；修复无媒体时音符上的空白提示。
- 固定宽度上限会随任务栏可用空间变化；通知封面保持原有宽高比。
- 改进媒体会话恢复、歌词进度和封面匹配；修复低音量时频谱不动，以及托盘提示显示旧设备或音量的问题。
- 媒体栏与托盘右键菜单新增「快速打开」子菜单，可直接启动已绑定的应用；只有发现新版本时才显示更新入口，点击后打开更新页面。

## 下载与安装

系统要求与安装方式见 [README](https://github.com/Fervent-Tempo/AF-Media-Bar#readme)。安装包和便携版均自带 .NET 运行时。

- `AFMediaBar-Setup-v1.3.0-win-x64.exe`：安装版。
- `AFMediaBar-v1.3.0-win-x64.zip`：便携版，解压即用。

请使用同一 Release 的 `SHA256SUMS.txt` 核对下载文件，不要使用 GitHub 自动生成的 Source code 压缩包。

## 已知限制

- 目前仅发布 `win-x64`；竖向任务栏与悬浮模式尚未实现。
- 排列方向目前仅支持横向；托盘组合滚轮设置暂时禁用。
- 隐私说明与卸载方式见 [README](https://github.com/Fervent-Tempo/AF-Media-Bar#readme)。

## 贡献者

感谢 @Miaoyww、@DustOn-CL、@x1a0Y4NGren、@cXp1r 和 @sonlige 为本次更新做出贡献。

## 首次贡献

- @sonlige：首次贡献见 [#55](https://github.com/Fervent-Tempo/AF-Media-Bar/pull/55)。
- @x1a0Y4NGren：首次贡献见 [#51](https://github.com/Fervent-Tempo/AF-Media-Bar/pull/51)。
- @cXp1r：首次贡献见 [#76](https://github.com/Fervent-Tempo/AF-Media-Bar/pull/76)。
