> English release notes follow the Chinese section.

# AF Media Bar 1.2.2

本次以稳定性和使用体验修复为主，并补充少量设置选项。

## 新增

- 歌词改用 WebView2 显示，呈现更美观；可分别调整行距、字距和显示区域长度。
- 新增滚轮操作提示开关、交互按钮大小调节和独立的组件设置页。

## 修复

- 媒体出现或消失时，音符、封面、歌词和信息区的切换更流畅；调整媒体栏宽度时减少组件抖动、裁切和位置跳动。
- 关闭浏览器媒体后更及时地收起旧画面；修复无媒体时的空白提示，以及任务栏图标减少后固定宽度上限不更新的问题。
- 改进媒体会话恢复、歌词进度和封面匹配；修复低音量时频谱不动，以及托盘提示未及时反映输出设备或音量变化的问题。
- 优化歌词查找，能匹配到更多歌曲

## 下载与安装

系统要求与安装方式见 [README](https://github.com/Fervent-Tempo/AF-Media-Bar#readme)。安装包和便携版均自带 .NET 运行时。

- `AFMediaBar-Setup-v1.2.2-win-x64.exe`：安装版。
- `AFMediaBar-v1.2.2-win-x64.zip`：便携版，解压即用。

请使用同一 Release 的 `SHA256SUMS.txt` 核对下载文件，不要使用 GitHub 自动生成的 Source code 压缩包。

## 已知限制

- 目前仅发布 `win-x64`；竖向任务栏与悬浮模式尚未实现。
- 排列方向目前仅支持横向；托盘组合滚轮设置暂时禁用。
- 1.2.1 → 1.2.2 保留现有设置。1.1.1 及更早版本的旧 schema 设置文件仍会改名留档，设置回到默认值。
- 隐私说明与卸载方式见 [README](https://github.com/Fervent-Tempo/AF-Media-Bar#readme)。

---

# AF Media Bar 1.2.2

This release focuses on reliability and everyday polish, with a few new settings.

## Added

- Lyrics now use WebView2 for a more polished display. Adjust line spacing, character spacing, and display width;
- Control wheel-gesture hints and interaction button size, with a separate page for component settings.

## Fixed

- Smoother transitions between the idle note, artwork, lyrics, and playback details, with less jitter, clipping, and jumping when resizing the bar.
- Browser media clears sooner after closing; the empty tooltip while idle is gone, and the fixed-width limit updates when taskbar icons leave.
- More reliable media-session recovery, lyric timing, and artwork matching; the spectrum responds at low volume, and the tray tooltip reflects current output device and volume.
- Optimize lyrics search to match more songs

## Download and install

See the [English README](https://github.com/Fervent-Tempo/AF-Media-Bar/blob/main/README.en-US.md#readme) for requirements and installation. Both packages include the .NET runtime.

- `AFMediaBar-Setup-v1.2.2-win-x64.exe` — installer.
- `AFMediaBar-v1.2.2-win-x64.zip` — portable build.

Verify downloads against `SHA256SUMS.txt` from the same Release. Do not use GitHub's generated source archives.

## Known limitations

- Only `win-x64` is published; vertical taskbars and floating modes are not implemented yet.
- Layout orientation is currently horizontal only; the tray modifier-wheel setting is temporarily disabled.
- Settings are retained when upgrading from 1.2.1 to 1.2.2. Files from 1.1.1 or earlier use an older schema and are archived while settings return to defaults.
- See the [English README](https://github.com/Fervent-Tempo/AF-Media-Bar/blob/main/README.en-US.md#readme) for privacy and uninstalling.
