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
4. 右键托盘图标可以截图、设置开机启动、安装文字识别 OCR 或退出。

## 构建

需要 Windows 10 / 11 x64 和 .NET Framework 4.8。

```powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1
```

输出：`QuickScreenshot.exe`。

## 说明

- OCR 使用 Windows 本机语言组件；未安装英语 OCR 时，托盘右键菜单会显示「安装文字识别 OCR」。
- 点击后会在 UAC 提示中请求管理员权限，并自动从 Windows Update 安装英语 Basic 与 OCR 组件。
- 安装英语 OCR 后程序会自动尝试中英文，并按行合并结果。
- 对艺术字、图标小字等 Windows OCR 难以识别的内容，可在托盘菜单选择「下载增强 OCR（PaddleOCR）」。
- 增强 OCR 是可选项，约 170 MB 下载、350 MB 磁盘占用；安装后默认使用 PaddleOCR，未安装时默认 Windows，截图不会上传。
- 增强 OCR 来自 PaddleOCRSharp / PaddleOCR，相关许可证会随组件保存。
- 识别窗口顶部可在「Windows / PaddleOCR」之间实时切换，切换后立即重新识别当前选区。
- 艺术字、带描边的图标文字可能识别不全；这类场景对 OCR 模型要求较高。
- MP4 使用 Windows 本机 H.264 编码，无外部 ffmpeg 依赖。
- 除手动下载增强 OCR 外，不访问网络，不收集数据。

当前版本：`1.3.0`
