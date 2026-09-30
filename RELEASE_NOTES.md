> English release notes follow the Chinese section.

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

---

# AF Media Bar 1.3.0 — Lyrics and Taskbar Polish

This release improves lyric display, playback transitions, and bar resizing, along with fixes for media and audio behavior.

## 🎵 More polished lyrics

Lyrics now use WebView2 for consistent syllable highlighting, translations, and romanization. Adjust line spacing, character spacing, and display width; improved lyric matching finds more songs.

## ✨ Smoother transitions

The idle note gives way to artwork, lyrics, and playback details in one continuous motion, then reverses when playback ends. Rapid width adjustments also cause less jitter, clipping, and jumping.

## 🧩 Easier settings

Control wheel-gesture hints and interaction button size, with a separate page for component settings. Artwork can now be hidden in the rest layer without changing the idle note setting.

## 🔧 Other updates

- Browser media clears sooner after closing, and the idle note no longer shows an empty tooltip.
- The fixed-width limit follows available taskbar space; notification artwork keeps its original aspect ratio.
- More reliable media-session recovery, lyric timing, and artwork matching. The spectrum responds at low volume, and the tray tooltip reflects the current output device and volume.
- The media bar and tray context menus now have a Quick Launch submenu for bound apps. Their update entry appears only when a newer version is known and opens the update page.

## Download and install

See the [English README](https://github.com/Fervent-Tempo/AF-Media-Bar/blob/main/README.en-US.md#readme) for requirements and installation. Both packages include the .NET runtime.

- `AFMediaBar-Setup-v1.3.0-win-x64.exe` — installer.
- `AFMediaBar-v1.3.0-win-x64.zip` — portable build.

Verify downloads against `SHA256SUMS.txt` from the same Release. Do not use GitHub's generated source archives.

## Known limitations

- Only `win-x64` is published; vertical taskbars and floating modes are not implemented yet.
- Layout orientation is currently horizontal only; the tray modifier-wheel setting is temporarily disabled.
- See the [English README](https://github.com/Fervent-Tempo/AF-Media-Bar/blob/main/README.en-US.md#readme) for privacy and uninstalling.
