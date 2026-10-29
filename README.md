# QuickScreenshot

轻量的 Windows 截图工具，单个 exe，双击即用，常驻托盘。

## 功能

- `Ctrl + Alt + A` 全局截图，支持框选、调整选区、矩形 / 椭圆 / 画笔 / 箭头标注、颜色和线宽、撤销重做。
- 本地 OCR 文字识别，不上传截图。
- GIF / MP4 选区录制，带停止按钮；MP4 支持系统声音。
- 钉图到桌面：置顶、可拖动，右键支持复制、另存为和关闭。
- 托盘常驻、开机启动、高 DPI / 多显示器适配。

## 使用

1. 运行 `QuickScreenshot.exe`。
2. 按 `Ctrl + Alt + A` 框选区域。
3. 使用工具栏标注、识别、录制、保存、钉图或完成复制。
4. 右键托盘图标可以截图、设置开机启动或退出。

## 构建

需要 Windows 10 / 11 x64 和 .NET Framework 4.8。

```powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1
```

输出：`QuickScreenshot.exe`。

## 说明

- OCR 使用 Windows 本机语言组件；英文或代码内容建议安装英语 OCR。
- MP4 使用 Windows 本机 H.264 编码，无外部 ffmpeg 依赖。
- 不访问网络，不收集数据。

当前版本：`1.0.0`
