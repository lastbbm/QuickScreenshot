using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using WinFormsTimer = System.Windows.Forms.Timer;

namespace QuickScreenshot
{
    internal static class Program
    {
        private const string MutexName = @"Local\QuickScreenshot.SingleInstance.7B4E1E2A";

        [STAThread]
        private static void Main(string[] args)
        {
            Native.TryEnablePerMonitorDpiAwareness();

            bool autoStart = false;
            if (args != null)
            {
                for (int i = 0; i < args.Length; i++)
                {
                    if (string.Equals(args[i], "--autostart", StringComparison.OrdinalIgnoreCase))
                    {
                        autoStart = true;
                    }
                }
            }

            bool createdNew;
            using (Mutex mutex = new Mutex(true, MutexName, out createdNew))
            {
                if (!createdNew)
                {
                    ActivateExistingInstance(autoStart);
                    return;
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
                Application.ThreadException += OnThreadException;
                AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;

                try
                {
                    using (TrayApp app = new TrayApp(autoStart))
                    {
                        Application.Run(app);
                    }
                }
                catch (Exception ex)
                {
                    AppLog.WriteException("Fatal", ex);
                    MessageBox.Show(
                        "QuickScreenshot 无法启动：\n\n" + ex.Message,
                        "QuickScreenshot",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                }

                GC.KeepAlive(mutex);
            }
        }

        private static void ActivateExistingInstance(bool quiet)
        {
            for (int attempt = 0; attempt < 12; attempt++)
            {
                IntPtr handle = Native.FindWindow(null, TrayApp.HiddenWindowTitle);
                if (handle != IntPtr.Zero)
                {
                    if (!quiet)
                    {
                        Native.PostMessage(handle, Native.WM_APP_ACTIVATE, IntPtr.Zero, IntPtr.Zero);
                    }

                    return;
                }

                Thread.Sleep(80);
            }

            if (!quiet)
            {
                MessageBox.Show(
                    "QuickScreenshot 已经在运行。\n\n请在系统托盘中找到蓝色截图图标，或按 Ctrl+Alt+A。",
                    "QuickScreenshot",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
        }

        private static void OnThreadException(object sender, ThreadExceptionEventArgs e)
        {
            AppLog.WriteException("UI thread", e == null ? null : e.Exception);
            try
            {
                MessageBox.Show(
                    "QuickScreenshot 遇到了一个错误：\n\n" +
                    (e == null || e.Exception == null ? "未知错误" : e.Exception.Message) +
                    "\n\n你可以继续使用截图功能。",
                    "QuickScreenshot",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
            catch
            {
            }
        }

        private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            AppLog.WriteException("Unhandled", e == null ? null : e.ExceptionObject as Exception);
        }
    }

    internal static class ScreenCapture
    {
        public static Rectangle GetVirtualBounds()
        {
            return Native.GetVirtualScreenBounds();
        }

        public static Bitmap CaptureVirtualScreen()
        {
            Rectangle bounds = Native.GetVirtualScreenBounds();
            if (bounds.Width <= 0 || bounds.Height <= 0)
            {
                throw new InvalidOperationException("无法获取屏幕尺寸。");
            }

            Bitmap bitmap = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
            try
            {
                using (Graphics graphics = Graphics.FromImage(bitmap))
                {
                    graphics.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
                    graphics.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, bounds.Size, CopyPixelOperation.SourceCopy);
                }

                return bitmap;
            }
            catch
            {
                bitmap.Dispose();
                throw;
            }
        }
    }

    internal sealed class HotkeyWindow : NativeWindow, IDisposable
    {
        private const int HotkeyId = 0x4450;
        private bool _registered;
        private bool _disposed;

        public event EventHandler HotkeyPressed;
        public event EventHandler ActivateRequested;

        public HotkeyWindow()
        {
            CreateParams createParams = new CreateParams();
            createParams.Caption = TrayApp.HiddenWindowTitle;
            createParams.X = -32000;
            createParams.Y = -32000;
            createParams.Width = 0;
            createParams.Height = 0;
            createParams.Style = 0;
            createParams.ExStyle = 0x00000080; // WS_EX_TOOLWINDOW
            CreateHandle(createParams);
        }

        public bool IsRegistered
        {
            get { return _registered; }
        }

        public bool TryRegister()
        {
            if (_disposed || Handle == IntPtr.Zero)
            {
                return false;
            }

            if (_registered)
            {
                Native.UnregisterHotKey(Handle, HotkeyId);
                _registered = false;
            }

            _registered = Native.RegisterHotKey(
                Handle,
                HotkeyId,
                Native.MOD_CONTROL | Native.MOD_ALT | Native.MOD_NOREPEAT,
                0x41); // A

            if (_registered)
            {
                AppLog.Write("Global hotkey Ctrl+Alt+A registered.");
            }
            else
            {
                int error = Marshal.GetLastWin32Error();
                AppLog.Write("Global hotkey registration failed. Win32Error=" +
                    error.ToString(CultureInfo.InvariantCulture));
            }

            return _registered;
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Native.WM_HOTKEY && m.WParam.ToInt32() == HotkeyId)
            {
                EventHandler handler = HotkeyPressed;
                if (handler != null)
                {
                    try
                    {
                        handler(this, EventArgs.Empty);
                    }
                    catch (Exception ex)
                    {
                        AppLog.WriteException("Hotkey handler", ex);
                    }
                }

                return;
            }

            if (m.Msg == Native.WM_APP_ACTIVATE)
            {
                EventHandler handler = ActivateRequested;
                if (handler != null)
                {
                    try
                    {
                        handler(this, EventArgs.Empty);
                    }
                    catch (Exception ex)
                    {
                        AppLog.WriteException("Activate handler", ex);
                    }
                }

                return;
            }

            base.WndProc(ref m);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            try
            {
                if (_registered && Handle != IntPtr.Zero)
                {
                    Native.UnregisterHotKey(Handle, HotkeyId);
                }
            }
            catch
            {
            }

            _registered = false;
            if (Handle != IntPtr.Zero)
            {
                DestroyHandle();
            }

            GC.SuppressFinalize(this);
        }
    }

    internal sealed class TrayApp : ApplicationContext
    {
        internal const string HiddenWindowTitle = "QuickScreenshot::Hidden::Window::7B4E1E2A";

        private readonly bool _autoStart;
        private readonly AppSettings _settings;
        private readonly NotifyIcon _notifyIcon;
        private readonly ContextMenuStrip _menu;
        private readonly ToolStripMenuItem _startupItem;
        private readonly ToolStripMenuItem _installOcrItem;
        private readonly ToolStripMenuItem _enhanceOcrItem;
        private readonly ToolStripMenuItem _retryHotkeyItem;
        private readonly HotkeyWindow _hotkeyWindow;
        private readonly Icon _trayIcon;
        private readonly WinFormsTimer _captureTimer;
        private readonly WinFormsTimer _firstRunTimer;
        private readonly WinFormsTimer _ocrInstallTimer;
        private readonly List<PinForm> _pins = new List<PinForm>();
        private CaptureForm _captureForm;
        private RecordingForm _recordingForm;
        private string _ocrInstallStatusPath;
        private string _ocrInstallScriptPath;
        private DateTime _ocrInstallStarted;
        private bool _ocrInstallInProgress;
        private bool _ocrInstallSucceeded;
        private bool _exitRequested;
        private bool _disposed;

        public TrayApp(bool autoStart)
        {
            _autoStart = autoStart;
            _settings = new AppSettings();
            _trayIcon = AppIcon.CreateTrayIcon();
            _menu = new ContextMenuStrip();
            _menu.ShowImageMargin = false;
            _menu.ShowCheckMargin = true;
            _menu.Font = new Font("Microsoft YaHei UI", 9.2f, FontStyle.Regular, GraphicsUnit.Point);
            _menu.ShowItemToolTips = true;

            ToolStripMenuItem captureItem = new ToolStripMenuItem("截图\tCtrl+Alt+A");
            captureItem.Font = new Font(_menu.Font, FontStyle.Bold);
            captureItem.Click += delegate { BeginCapture(true, 170); };
            _menu.Items.Add(captureItem);

            _menu.Items.Add(new ToolStripSeparator());

            _startupItem = new ToolStripMenuItem("开机启动");
            _startupItem.CheckOnClick = true;
            _startupItem.Checked = StartupManager.IsEnabled();
            _startupItem.Click += OnStartupClicked;
            _menu.Items.Add(_startupItem);

            _installOcrItem = new ToolStripMenuItem("安装文字识别 OCR");
            _installOcrItem.Visible = !WindowsOcr.IsRecognizerInstalled("en");
            _installOcrItem.Click += OnInstallOcrClicked;
            _menu.Items.Add(_installOcrItem);

            _enhanceOcrItem = new ToolStripMenuItem("下载增强 OCR（PaddleOCR）");
            _enhanceOcrItem.Visible = !PaddleOcr.IsInstalled();
            _enhanceOcrItem.Click += OnEnhanceOcrClicked;
            _menu.Items.Add(_enhanceOcrItem);

            _retryHotkeyItem = new ToolStripMenuItem("重新注册热键");
            _retryHotkeyItem.Visible = false;
            _retryHotkeyItem.Click += OnRetryHotkeyClicked;
            _menu.Items.Add(_retryHotkeyItem);

            ToolStripMenuItem aboutItem = new ToolStripMenuItem("关于 QuickScreenshot");
            aboutItem.Click += delegate { ShowAbout(); };
            _menu.Items.Add(aboutItem);

            _menu.Items.Add(new ToolStripSeparator());

            ToolStripMenuItem exitItem = new ToolStripMenuItem("退出");
            exitItem.Click += delegate { ExitApp(); };
            _menu.Items.Add(exitItem);

            _menu.Opening += delegate
            {
                // Keep the check mark in sync with the actual HKCU Run entry.
                _startupItem.Checked = StartupManager.IsEnabled();
                _installOcrItem.Visible = !_ocrInstallSucceeded &&
                    !WindowsOcr.IsRecognizerInstalled("en");
                _enhanceOcrItem.Visible = !PaddleOcr.IsInstalled();
                _retryHotkeyItem.Visible = !_hotkeyWindow.IsRegistered;
            };

            _notifyIcon = new NotifyIcon();
            _notifyIcon.Icon = _trayIcon;
            _notifyIcon.Text = "QuickScreenshot 截图  ·  Ctrl+Alt+A";
            _notifyIcon.Visible = true;
            _notifyIcon.ContextMenuStrip = _menu;
            _notifyIcon.DoubleClick += delegate { BeginCapture(false, 40); };
            _notifyIcon.BalloonTipClicked += delegate { BeginCapture(false, 40); };

            _hotkeyWindow = new HotkeyWindow();
            _hotkeyWindow.HotkeyPressed += delegate { BeginCapture(false, 55); };
            _hotkeyWindow.ActivateRequested += delegate
            {
                if (_captureForm != null && !_captureForm.IsDisposed)
                {
                    _captureForm.BringToFront();
                    _captureForm.Activate();
                }
                else
                {
                    BeginCapture(false, 40);
                }
            };

            bool hotkeyOk = _hotkeyWindow.TryRegister();
            _retryHotkeyItem.Visible = !hotkeyOk;

            _captureTimer = new WinFormsTimer();
            _captureTimer.Tick += OnCaptureTimerTick;

            _ocrInstallTimer = new WinFormsTimer();
            _ocrInstallTimer.Interval = 2000;
            _ocrInstallTimer.Tick += OnOcrInstallTimerTick;

            _firstRunTimer = new WinFormsTimer();
            _firstRunTimer.Interval = 800;
            _firstRunTimer.Tick += OnFirstRunTimerTick;
            _firstRunTimer.Start();

            AppLog.Write("QuickScreenshot started. AutoStart=" + autoStart);
        }

        private void OnFirstRunTimerTick(object sender, EventArgs e)
        {
            _firstRunTimer.Stop();
            _firstRunTimer.Dispose();

            if (_exitRequested)
            {
                return;
            }

            if (!_hotkeyWindow.IsRegistered)
            {
                ShowBalloon(
                    "热键注册失败",
                    "Ctrl+Alt+A 可能已被 QQ、微信或其他截图工具占用。\n右键托盘图标可手动截图或重新注册。",
                    ToolTipIcon.Warning,
                    7000);
            }
            else if (!_autoStart)
            {
                ShowBalloon(
                    "QuickScreenshot 已就绪",
                    "按 Ctrl+Alt+A 框选截图。右键托盘图标可设置开机启动或退出。",
                    ToolTipIcon.Info,
                    5000);
            }
        }

        private void BeginCapture(bool fromMenu, int delayMilliseconds)
        {
            if (_exitRequested)
            {
                return;
            }

            if (_captureForm != null && !_captureForm.IsDisposed)
            {
                _captureForm.BringToFront();
                _captureForm.Activate();
                return;
            }

            if (_recordingForm != null && !_recordingForm.IsDisposed)
            {
                _recordingForm.BringToFront();
                _recordingForm.Activate();
                ShowBalloon("正在录制", "请先停止当前录制，再开始新的截图。", ToolTipIcon.Info, 2000);
                return;
            }

            if (fromMenu)
            {
                _menu.Close();
            }

            _captureTimer.Stop();
            _captureTimer.Interval = Math.Max(0, delayMilliseconds);
            _captureTimer.Start();
        }

        private void OnCaptureTimerTick(object sender, EventArgs e)
        {
            _captureTimer.Stop();
            DoCapture();
        }

        private void DoCapture()
        {
            if (_exitRequested ||
                (_recordingForm != null && !_recordingForm.IsDisposed))
            {
                return;
            }

            Bitmap screen = null;
            try
            {
                screen = ScreenCapture.CaptureVirtualScreen();
                Rectangle bounds = ScreenCapture.GetVirtualBounds();
                CaptureForm form = new CaptureForm(screen, bounds, _settings, this);
                screen = null;
                _captureForm = form;
                form.FormClosed += delegate
                {
                    if (object.ReferenceEquals(_captureForm, form))
                    {
                        _captureForm = null;
                    }
                };
                form.Show();
                form.Activate();
                Native.SetForegroundWindow(form.Handle);
            }
            catch (Exception ex)
            {
                AppLog.WriteException("Capture", ex);
                if (screen != null)
                {
                    screen.Dispose();
                }

                MessageBox.Show(
                    "无法截取屏幕：\n\n" + ex.Message +
                    "\n\n如果正在播放受保护视频或使用安全桌面，系统会阻止截图。",
                    "QuickScreenshot",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
        }

        internal void StartRecording(Rectangle captureRect, RecordingFormat format)
        {
            if (_exitRequested)
            {
                return;
            }

            if (_recordingForm != null && !_recordingForm.IsDisposed)
            {
                _recordingForm.BringToFront();
                _recordingForm.Activate();
                return;
            }

            RecordingForm form = null;
            try
            {
                form = new RecordingForm(captureRect, format, _settings, this);
                _recordingForm = form;
                form.FormClosed += delegate
                {
                    if (object.ReferenceEquals(_recordingForm, form))
                    {
                        _recordingForm = null;
                    }
                };
                form.Show();
                Native.SetForegroundWindow(form.Handle);
            }
            catch (Exception ex)
            {
                AppLog.WriteException("StartRecording", ex);
                _recordingForm = null;
                if (form != null)
                {
                    try
                    {
                        form.Abort();
                    }
                    catch
                    {
                    }
                }

                MessageBox.Show(
                    "无法开始录制：\n\n" + ex.Message,
                    "QuickScreenshot",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
        }

        internal void ShowToast(string title, string text)
        {
            ShowBalloon(title, text, ToolTipIcon.None, 2400);
        }

        private void ShowBalloon(string title, string text, ToolTipIcon icon, int timeout)
        {
            try
            {
                _notifyIcon.BalloonTipTitle = title;
                _notifyIcon.BalloonTipText = text;
                _notifyIcon.BalloonTipIcon = icon;
                _notifyIcon.ShowBalloonTip(timeout);
            }
            catch
            {
            }
        }

        private void OnStartupClicked(object sender, EventArgs e)
        {
            bool wantEnabled = _startupItem.Checked;
            try
            {
                StartupManager.SetEnabled(wantEnabled);
                _startupItem.Checked = StartupManager.IsEnabled();
                ShowBalloon(
                    "开机启动",
                    _startupItem.Checked
                        ? "已开启。登录 Windows 后 QuickScreenshot 会自动在托盘运行。"
                        : "已关闭开机启动。",
                    ToolTipIcon.None,
                    2500);
            }
            catch (Exception ex)
            {
                AppLog.WriteException("Startup", ex);
                _startupItem.Checked = StartupManager.IsEnabled();
                MessageBox.Show(
                    "无法修改开机启动设置：\n\n" + ex.Message,
                    "QuickScreenshot",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
        }

        private void OnEnhanceOcrClicked(object sender, EventArgs e)
        {
            if (PaddleOcr.IsInstalled())
            {
                _enhanceOcrItem.Visible = false;
                return;
            }

            DialogResult answer = MessageBox.Show(
                "增强 OCR 使用 PaddleOCR 本地模型，截图不会上传。\n\n" +
                "需要下载约 170 MB，安装后占用约 350 MB。\n" +
                "仅在需要更高识别精度时使用。\n\n" +
                "现在下载并安装吗？",
                "下载增强 OCR",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);
            if (answer != DialogResult.Yes)
            {
                return;
            }

            using (OcrPackForm form = new OcrPackForm())
            {
                form.ShowDialog();
                if (form.Success)
                {
                    _enhanceOcrItem.Visible = false;
                    ShowBalloon(
                        "增强 OCR 安装完成",
                        "PaddleOCR 已启用，文字识别会自动使用更高精度结果。",
                        ToolTipIcon.Info,
                        6000);
                }
                else if (!string.IsNullOrEmpty(form.Error))
                {
                    MessageBox.Show(
                        "增强 OCR 安装失败：\n\n" + form.Error,
                        "QuickScreenshot",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                }
            }
        }

        private void OnInstallOcrClicked(object sender, EventArgs e)
        {
            if (_ocrInstallInProgress)
            {
                ShowBalloon(
                    "正在安装文字识别 OCR",
                    "请等待当前安装结束。",
                    ToolTipIcon.Info,
                    3000);
                return;
            }

            if (WindowsOcr.IsRecognizerInstalled("en"))
            {
                _ocrInstallSucceeded = true;
                _installOcrItem.Visible = false;
                ShowBalloon("文字识别 OCR", "英语 OCR 已经安装。", ToolTipIcon.Info, 3000);
                return;
            }

            string statusPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "QuickScreenshot",
                "ocr-install.status");
            string scriptPath = Path.Combine(
                Path.GetTempPath(),
                "QuickScreenshot-install-ocr.ps1");

            try
            {
                File.WriteAllText(
                    scriptPath,
                    BuildOcrInstallScript(statusPath),
                    new System.Text.UTF8Encoding(true));

                ProcessStartInfo startInfo = new ProcessStartInfo();
                startInfo.FileName = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.System),
                    @"WindowsPowerShell\v1.0\powershell.exe");
                startInfo.Arguments =
                    "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"" +
                    scriptPath + "\"";
                startInfo.UseShellExecute = true;
                startInfo.Verb = "runas";
                startInfo.WindowStyle = ProcessWindowStyle.Hidden;
                Process.Start(startInfo);

                _ocrInstallStatusPath = statusPath;
                _ocrInstallScriptPath = scriptPath;
                _ocrInstallStarted = DateTime.UtcNow;
                _ocrInstallInProgress = true;
                _installOcrItem.Text = "正在安装文字识别 OCR...";
                _installOcrItem.Enabled = false;
                _ocrInstallTimer.Start();
                ShowBalloon(
                    "安装文字识别 OCR",
                    "请在 UAC 提示中点击“是”。安装会自动从 Windows Update 下载并完成。",
                    ToolTipIcon.Info,
                    6000);
            }
            catch (Win32Exception ex)
            {
                TryDeleteFile(scriptPath);
                ResetOcrInstallUi();
                if (ex.NativeErrorCode == 1223)
                {
                    ShowBalloon(
                        "已取消安装",
                        "没有安装文字识别 OCR。",
                        ToolTipIcon.Info,
                        3000);
                }
                else
                {
                    ShowOcrInstallFailure(ex.Message);
                }
            }
            catch (Exception ex)
            {
                TryDeleteFile(scriptPath);
                ResetOcrInstallUi();
                AppLog.WriteException("Install OCR", ex);
                ShowOcrInstallFailure(ex.Message);
            }
        }

        private static string BuildOcrInstallScript(string statusPath)
        {
            string safeStatus = (statusPath ?? string.Empty).Replace("'", "''");
            return
                "$ErrorActionPreference = 'Stop'\r\n" +
                "$status = '" + safeStatus + "'\r\n" +
                "New-Item -ItemType Directory -Force -Path (Split-Path -Parent $status) | Out-Null\r\n" +
                "try {\r\n" +
                "  'INSTALLING' | Set-Content -Path $status -Encoding UTF8\r\n" +
                "  Add-WindowsCapability -Online -Name 'Language.Basic~~~en-US~0.0.1.0' " +
                "-ErrorAction Stop | Out-Null\r\n" +
                "  Add-WindowsCapability -Online -Name 'Language.OCR~~~en-US~0.0.1.0' " +
                "-ErrorAction Stop | Out-Null\r\n" +
                "  'OK' | Set-Content -Path $status -Encoding UTF8\r\n" +
                "} catch {\r\n" +
                "  ('ERROR ' + $_.Exception.Message) | Set-Content -Path $status -Encoding UTF8\r\n" +
                "  exit 1\r\n" +
                "}\r\n";
        }

        private void OnOcrInstallTimerTick(object sender, EventArgs e)
        {
            if (!_ocrInstallInProgress)
            {
                _ocrInstallTimer.Stop();
                return;
            }

            // Add-WindowsCapability can finish without writing OK (for example
            // when the elevated host is closed). The recognizer itself is the
            // reliable completion signal.
            if (WindowsOcr.IsRecognizerInstalled("en"))
            {
                CompleteOcrInstall(true, string.Empty);
                return;
            }

            if ((DateTime.UtcNow - _ocrInstallStarted).TotalMinutes > 20)
            {
                CompleteOcrInstall(false, "安装超时。请检查网络和 Windows Update 后重试。");
                return;
            }

            try
            {
                if (string.IsNullOrEmpty(_ocrInstallStatusPath) ||
                    !File.Exists(_ocrInstallStatusPath))
                {
                    return;
                }

                if (File.GetLastWriteTimeUtc(_ocrInstallStatusPath) <
                    _ocrInstallStarted.AddSeconds(-3))
                {
                    return;
                }

                string status = File.ReadAllText(_ocrInstallStatusPath).Trim();
                if (status.StartsWith("OK", StringComparison.OrdinalIgnoreCase))
                {
                    CompleteOcrInstall(true, string.Empty);
                }
                else if (status.StartsWith("ERROR", StringComparison.OrdinalIgnoreCase))
                {
                    string detail = status.Length > 5 ? status.Substring(5).Trim() : string.Empty;
                    CompleteOcrInstall(false, detail);
                }
            }
            catch
            {
            }
        }

        private void CompleteOcrInstall(bool success, string detail)
        {
            _ocrInstallTimer.Stop();
            _ocrInstallInProgress = false;
            _ocrInstallSucceeded = success;
            _installOcrItem.Text = "安装文字识别 OCR";
            _installOcrItem.Enabled = true;
            _installOcrItem.Visible = !success && !WindowsOcr.IsRecognizerInstalled("en");
            TryDeleteFile(_ocrInstallScriptPath);
            _ocrInstallScriptPath = null;

            if (success)
            {
                bool installed = WindowsOcr.IsRecognizerInstalled("en");
                ShowBalloon(
                    "文字识别 OCR 安装完成",
                    installed
                        ? "英语 OCR 已经可用。"
                        : "安装完成，请重启 QuickScreenshot 后生效。",
                    ToolTipIcon.Info,
                    6000);
            }
            else
            {
                ShowOcrInstallFailure(detail);
            }
        }

        private void ShowOcrInstallFailure(string detail)
        {
            string text =
                "自动安装文字识别 OCR 失败。\n\n" +
                "请手动打开：\n" +
                "设置 → 时间和语言 → 语言和区域 → 英语 → 语言选项 → 可选语言功能 → " +
                "光学字符识别。\n\n" +
                "也可以稍后重试。";
            if (!string.IsNullOrEmpty(detail))
            {
                string shortDetail = detail.Length > 400 ? detail.Substring(0, 400) : detail;
                text += "\n\n错误信息：" + shortDetail;
            }

            MessageBox.Show(
                text,
                "QuickScreenshot",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }

        private void ResetOcrInstallUi()
        {
            _ocrInstallTimer.Stop();
            _ocrInstallInProgress = false;
            _installOcrItem.Text = "安装文字识别 OCR";
            _installOcrItem.Enabled = true;
            _installOcrItem.Visible = !_ocrInstallSucceeded &&
                !WindowsOcr.IsRecognizerInstalled("en");
        }

        private static void TryDeleteFile(string path)
        {
            try
            {
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
            }
        }

        private void OnRetryHotkeyClicked(object sender, EventArgs e)
        {
            bool ok = _hotkeyWindow.TryRegister();
            _retryHotkeyItem.Visible = !ok;
            if (ok)
            {
                ShowBalloon("热键已启用", "现在可以使用 Ctrl+Alt+A 截图。", ToolTipIcon.Info, 2500);
            }
            else
            {
                MessageBox.Show(
                    "仍然无法注册 Ctrl+Alt+A。\n\n" +
                    "请关闭可能占用该快捷键的程序（例如 QQ、微信、其他截图工具），然后重试。",
                    "QuickScreenshot",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
        }

        private void ShowAbout()
        {
            Version version = typeof(TrayApp).Assembly.GetName().Version;
            MessageBox.Show(
                "QuickScreenshot  " + version.ToString(3) + "\n\n" +
                "轻量的 Windows 截图、标注、文字识别与 GIF/MP4 录制工具。\n\n" +
                "全局热键：Ctrl+Alt+A\n" +
                "录屏：框选后点击工具栏 GIF 或 MP4，停止后保存（MP4 含系统声音）\n" +
                "钉图：点击工具栏图钉按钮，把选区截图钉在桌面\n" +
                "开机启动：右键托盘图标设置\n\n" +
                "文字识别、系统声音采集与视频编码均使用 Windows 本机组件，不会上传内容。\n\n" +
                "单文件绿色版 · 数据仅保存在本机",
                "关于 QuickScreenshot",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }

        internal void RegisterPin(PinForm form)
        {
            if (form == null)
            {
                return;
            }

            _pins.Add(form);
            form.FormClosed += delegate
            {
                _pins.Remove(form);
            };
        }

        private void ClosePins()
        {
            PinForm[] pins = _pins.ToArray();
            _pins.Clear();
            for (int i = 0; i < pins.Length; i++)
            {
                try
                {
                    pins[i].Close();
                }
                catch
                {
                }
            }
        }

        private void ExitApp()
        {
            if (_exitRequested)
            {
                return;
            }

            _exitRequested = true;
            _captureTimer.Stop();
            _firstRunTimer.Stop();
            _ocrInstallTimer.Stop();

            if (_captureForm != null && !_captureForm.IsDisposed)
            {
                try
                {
                    _captureForm.Close();
                }
                catch
                {
                }
            }

            if (_recordingForm != null && !_recordingForm.IsDisposed)
            {
                try
                {
                    _recordingForm.Abort();
                }
                catch
                {
                }
            }

            ClosePins();

            try
            {
                _notifyIcon.Visible = false;
            }
            catch
            {
            }

            _hotkeyWindow.Dispose();
            AppLog.Write("QuickScreenshot exited.");
            ExitThread();
        }

        protected override void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                _disposed = true;
                if (disposing)
                {
                    if (!_exitRequested)
                    {
                        _exitRequested = true;
                        _hotkeyWindow.Dispose();
                    }

                    if (_recordingForm != null && !_recordingForm.IsDisposed)
                    {
                        try
                        {
                            _recordingForm.Abort();
                        }
                        catch
                        {
                        }
                    }

                    ClosePins();
                    _notifyIcon.Visible = false;
                    _notifyIcon.Dispose();
                    _menu.Dispose();
                    _trayIcon.Dispose();
                    _captureTimer.Dispose();
                    _firstRunTimer.Dispose();
                    _ocrInstallTimer.Dispose();
                }
            }

            base.Dispose(disposing);
        }
    }
}




