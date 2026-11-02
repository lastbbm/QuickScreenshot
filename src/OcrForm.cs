using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using WinFormsTimer = System.Windows.Forms.Timer;

namespace QuickScreenshot
{
    internal enum OcrChannel
    {
        Windows = 0,
        Paddle = 1
    }

    internal sealed class OcrForm : Form
    {
        private const int WM_NCLBUTTONDOWN = 0xA1;
        private const int HTCAPTION = 2;

        private readonly float _scale;
        private readonly Rectangle _sourceSelection;
        private readonly Bitmap _sourceBitmap;
        private readonly TextBox _textBox;
        private readonly WinFormsTimer _spinnerTimer;
        private readonly Font _fontTitle;
        private readonly Font _fontSubtitle;
        private readonly Font _fontStatus;
        private readonly Font _fontButton;
        private readonly Font _fontClose;

        private Rectangle _bodyBounds;
        private Rectangle _copyBounds;
        private Rectangle _closeBounds;
        private Rectangle _xBounds;
        private Rectangle _headerBounds;
        private readonly Rectangle[] _channelBounds = new Rectangle[2];
        private Rectangle _channelGroupBounds;

        private OcrChannel _channel;
        private int _hoverChannel = -1;
        private int _requestVersion;
        private bool _loading;
        private bool _hasText;
        private bool _hoverCopy;
        private bool _hoverClose;
        private bool _hoverX;
        private string _textValue;
        private string _statusText;
        private string _subtitle;
        private Color _statusColor;
        private bool _copied;
        private int _spinnerAngle;

        public OcrForm(Rectangle sourceSelection, float baseScale, Bitmap sourceBitmap)
        {
            _sourceSelection = sourceSelection;
            _scale = CalculateScale(sourceSelection, baseScale);
            _sourceBitmap = sourceBitmap == null ? null : new Bitmap(sourceBitmap);
            _channel = PaddleOcr.IsInstalled() ? OcrChannel.Paddle : OcrChannel.Windows;
            _loading = true;
            _subtitle = _channel == OcrChannel.Paddle
                ? "PaddleOCR · 正在识别"
                : "Windows 本机 OCR · 正在识别";
            _statusText = "正在识别文字";
            _statusColor = AppPalette.SubInk;

            AutoScaleMode = AutoScaleMode.None;
            BackColor = AppPalette.Surface;
            ClientSize = new Size(
                (int)Math.Round(616f * _scale),
                (int)Math.Round(428f * _scale));
            DoubleBuffered = true;
            FormBorderStyle = FormBorderStyle.None;
            KeyPreview = true;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            Text = "识别文字 - QuickScreenshot";
            TopMost = true;

            SetStyle(ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint |
                     ControlStyles.ResizeRedraw, true);

            _fontTitle = DrawUtil.CreateFont(19f * _scale, FontStyle.Bold);
            _fontSubtitle = DrawUtil.CreateFont(11.4f * _scale, FontStyle.Regular);
            _fontStatus = DrawUtil.CreateFont(11.5f * _scale, FontStyle.Regular);
            _fontButton = DrawUtil.CreateFont(12.3f * _scale, FontStyle.Bold);
            _fontClose = DrawUtil.CreateFont(12.3f * _scale, FontStyle.Regular);

            _textBox = new TextBox();
            _textBox.BackColor = Color.White;
            _textBox.BorderStyle = BorderStyle.None;
            _textBox.Font = DrawUtil.CreateFont(14.2f * _scale, FontStyle.Regular);
            _textBox.ForeColor = AppPalette.SubInk;
            _textBox.Multiline = true;
            _textBox.ReadOnly = true;
            _textBox.ScrollBars = ScrollBars.Vertical;
            _textBox.ShortcutsEnabled = true;
            _textBox.WordWrap = true;
            _textBox.Text = "正在从截图中提取文字…";
            Controls.Add(_textBox);

            _spinnerTimer = new WinFormsTimer();
            _spinnerTimer.Interval = 65;
            _spinnerTimer.Tick += delegate
            {
                _spinnerAngle = (_spinnerAngle + 26) % 360;
                if (_loading)
                {
                    Invalidate(new Rectangle(0, ClientSize.Height - (int)(84f * _scale),
                        ClientSize.Width, (int)(84f * _scale)));
                }
            };
            _spinnerTimer.Start();
            LayoutChildren();

            Rectangle work = Native.GetMonitorWorkArea(new Point(
                sourceSelection.Left + sourceSelection.Width / 2,
                sourceSelection.Top + sourceSelection.Height / 2));
            int x = work.Left + (work.Width - Width) / 2;
            int y = work.Top + (work.Height - Height) / 2;
            Location = new Point(Math.Max(work.Left + 8, x), Math.Max(work.Top + 8, y));

            Region old = Region;
            Region = DrawUtil.RoundedRegion(new Rectangle(0, 0, Width, Height), (int)Math.Round(10f * _scale));
            if (old != null)
            {
                old.Dispose();
            }
        }

        private static float CalculateScale(Rectangle source, float baseScale)
        {
            float scale = baseScale;
            if (scale < 0.75f) scale = 0.75f;
            if (scale > 2.2f) scale = 2.2f;

            try
            {
                Rectangle work = Native.GetMonitorWorkArea(new Point(
                    source.Left + source.Width / 2, source.Top + source.Height / 2));
                float fitWidth = (work.Width * 0.92f) / 616f;
                float fitHeight = (work.Height * 0.88f) / 428f;
                float fit = Math.Min(fitWidth, fitHeight);
                if (fit < scale)
                {
                    scale = Math.Max(0.68f, fit);
                }
            }
            catch
            {
            }

            return scale;
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ClassStyle |= 0x00020000; // CS_DROPSHADOW
                return cp;
            }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            LayoutChildren();
            Native.SetForegroundWindow(Handle);
            Activate();
        }

        private void LayoutChildren()
        {
            float headerHeight = 73f * _scale;
            float footerHeight = 73f * _scale;
            float sidePadding = 22f * _scale;
            float bottomGap = 13f * _scale;

            _headerBounds = new Rectangle(0, 0, ClientSize.Width, (int)Math.Round(headerHeight));
            int bodyTop = (int)Math.Round(headerHeight + 15f * _scale);
            int footerTop = ClientSize.Height - (int)Math.Round(footerHeight);
            _bodyBounds = new Rectangle(
                (int)Math.Round(sidePadding),
                bodyTop,
                ClientSize.Width - (int)Math.Round(sidePadding * 2f),
                footerTop - bodyTop - (int)Math.Round(bottomGap));
            if (_bodyBounds.Height < 20)
            {
                _bodyBounds.Height = 20;
            }

            int xSize = (int)Math.Round(32f * _scale);
            _xBounds = new Rectangle(
                ClientSize.Width - (int)Math.Round(sidePadding) - xSize,
                (int)Math.Round((headerHeight - xSize) / 2f),
                xSize, xSize);

            int groupHeight = (int)Math.Round(30f * _scale);
            int groupWidth = (int)Math.Round(ClientSize.Width * 0.30f);
            int groupLeft = (int)Math.Round(ClientSize.Width * 0.42f);
            int maxRight = _xBounds.Left - (int)Math.Round(14f * _scale);
            if (groupLeft + groupWidth > maxRight)
            {
                groupWidth = Math.Max((int)Math.Round(150f * _scale),
                    maxRight - groupLeft);
            }

            _channelGroupBounds = new Rectangle(
                groupLeft,
                (int)Math.Round((headerHeight - groupHeight) / 2f),
                groupWidth,
                groupHeight);
            int inner = Math.Max(1, (int)Math.Round(2f * _scale));
            int gap = Math.Max(1, (int)Math.Round(2f * _scale));
            int usable = groupWidth - inner * 2 - gap;
            int windowsWidth = (int)Math.Round(usable * 0.45f);
            int paddleWidth = usable - windowsWidth;
            _channelBounds[0] = new Rectangle(groupLeft + inner, _channelGroupBounds.Top + inner,
                windowsWidth, groupHeight - inner * 2);
            _channelBounds[1] = new Rectangle(_channelBounds[0].Right + gap,
                _channelGroupBounds.Top + inner, paddleWidth, groupHeight - inner * 2);

            float buttonHeight = 34f * _scale;
            float copyWidth = 108f * _scale;
            float closeWidth = 76f * _scale;
            float buttonGap = 9f * _scale;
            int footerY = footerTop + (int)Math.Round((footerHeight - buttonHeight) / 2f);
            _closeBounds = new Rectangle(
                ClientSize.Width - (int)Math.Round(sidePadding) - (int)Math.Round(closeWidth),
                footerY,
                (int)Math.Round(closeWidth),
                (int)Math.Round(buttonHeight));
            _copyBounds = new Rectangle(
                _closeBounds.Left - (int)Math.Round(buttonGap) - (int)Math.Round(copyWidth),
                footerY,
                (int)Math.Round(copyWidth),
                (int)Math.Round(buttonHeight));

            int inset = (int)Math.Round(13f * _scale);
            _textBox.SetBounds(
                _bodyBounds.Left + inset,
                _bodyBounds.Top + inset,
                Math.Max(10, _bodyBounds.Width - inset * 2),
                Math.Max(10, _bodyBounds.Height - inset * 2));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

            using (SolidBrush background = new SolidBrush(AppPalette.Surface))
            {
                g.FillRectangle(background, ClientRectangle);
            }

            using (SolidBrush header = new SolidBrush(Color.White))
            {
                g.FillRectangle(header, _headerBounds);
            }

            using (Pen divider = new Pen(Color.FromArgb(235, 238, 244), 1f))
            {
                g.DrawLine(divider, 0, _headerBounds.Bottom - 1, ClientSize.Width, _headerBounds.Bottom - 1);
                g.DrawLine(divider, 0, ClientSize.Height - (int)Math.Round(73f * _scale),
                    ClientSize.Width, ClientSize.Height - (int)Math.Round(73f * _scale));
            }

            using (SolidBrush body = new SolidBrush(Color.White))
            {
                g.FillRectangle(body, _bodyBounds);
            }

            DrawUtil.DrawRounded(g, Color.FromArgb(221, 226, 235), 1f,
                new RectangleF(_bodyBounds.X, _bodyBounds.Y, _bodyBounds.Width - 1, _bodyBounds.Height - 1),
                8f * _scale);

            float left = 23f * _scale;
            float titleWidth = Math.Max(90f * _scale, _channelGroupBounds.Left - left - 12f * _scale);
            DrawUtil.DrawText(g, "识别文字", _fontTitle, AppPalette.Ink,
                new RectangleF(left, 15f * _scale, titleWidth, 27f * _scale),
                StringAlignment.Near, StringAlignment.Center, true);
            DrawUtil.DrawText(g, _subtitle, _fontSubtitle, AppPalette.SubInk,
                new RectangleF(left, 42f * _scale, titleWidth, 20f * _scale),
                StringAlignment.Near, StringAlignment.Center, true);

            DrawChannelSelector(g);

            if (_hoverX)
            {
                DrawUtil.FillRounded(g, Color.FromArgb(239, 242, 247), _xBounds, 7f * _scale);
            }

            using (Pen closePen = new Pen(Color.FromArgb(87, 96, 113), Math.Max(1.2f, 1.55f * _scale)))
            {
                closePen.StartCap = LineCap.Round;
                closePen.EndCap = LineCap.Round;
                float inset = 10f * _scale;
                g.DrawLine(closePen,
                    _xBounds.Left + inset, _xBounds.Top + inset,
                    _xBounds.Right - inset, _xBounds.Bottom - inset);
                g.DrawLine(closePen,
                    _xBounds.Right - inset, _xBounds.Top + inset,
                    _xBounds.Left + inset, _xBounds.Bottom - inset);
            }

            DrawFooter(g);
            DrawBorder(g);
        }

        private void DrawChannelSelector(Graphics g)
        {
            DrawUtil.FillRounded(g, Color.FromArgb(244, 246, 251),
                new RectangleF(_channelGroupBounds.X, _channelGroupBounds.Y,
                    _channelGroupBounds.Width, _channelGroupBounds.Height), 8f * _scale);
            DrawUtil.DrawRounded(g, Color.FromArgb(220, 226, 236), 1f,
                new RectangleF(_channelGroupBounds.X, _channelGroupBounds.Y,
                    _channelGroupBounds.Width - 1, _channelGroupBounds.Height - 1), 8f * _scale);

            string[] labels = new string[] { "Windows", "PaddleOCR" };
            for (int i = 0; i < 2; i++)
            {
                bool selected = (int)_channel == i;
                bool available = i != 1 || PaddleOcr.IsInstalled();
                if (selected)
                {
                    DrawUtil.FillRounded(g, AppPalette.Accent,
                        new RectangleF(_channelBounds[i].X, _channelBounds[i].Y,
                            _channelBounds[i].Width, _channelBounds[i].Height), 6f * _scale);
                }
                else if (_hoverChannel == i && available)
                {
                    DrawUtil.FillRounded(g, Color.FromArgb(232, 237, 247),
                        new RectangleF(_channelBounds[i].X, _channelBounds[i].Y,
                            _channelBounds[i].Width, _channelBounds[i].Height), 6f * _scale);
                }

                Color textColor = selected
                    ? Color.White
                    : (available ? AppPalette.Ink : Color.FromArgb(168, 176, 190));
                DrawUtil.DrawText(g, labels[i], _fontSubtitle, textColor,
                    new RectangleF(_channelBounds[i].X, _channelBounds[i].Y,
                        _channelBounds[i].Width, _channelBounds[i].Height),
                    StringAlignment.Center, StringAlignment.Center, true);
            }
        }

        private void DrawFooter(Graphics g)
        {
            float footerHeight = 73f * _scale;
            int footerTop = ClientSize.Height - (int)Math.Round(footerHeight);
            float statusX = 23f * _scale;
            float statusWidth = _copyBounds.Left - statusX - 14f * _scale;
            if (statusWidth < 40f * _scale)
            {
                statusWidth = 40f * _scale;
            }

            if (_loading)
            {
                RectangleF spinner = new RectangleF(
                    statusX, footerTop + (footerHeight - 15f * _scale) / 2f,
                    15f * _scale, 15f * _scale);
                using (Pen arc = new Pen(AppPalette.Accent, Math.Max(1.5f, 2f * _scale)))
                {
                    arc.StartCap = LineCap.Round;
                    arc.EndCap = LineCap.Round;
                    g.DrawArc(arc, spinner, _spinnerAngle, 250f);
                }
            }
            else
            {
                Color dotColor = _hasText ? Color.FromArgb(52, 168, 105) : AppPalette.Danger;
                float dot = 6.5f * _scale;
                using (SolidBrush dotBrush = new SolidBrush(dotColor))
                {
                    g.FillEllipse(dotBrush, statusX + 1f * _scale,
                        footerTop + (footerHeight - dot) / 2f, dot, dot);
                }
            }

            float textOffset = _loading ? 24f * _scale : 16f * _scale;
            RectangleF statusRect = new RectangleF(
                statusX + textOffset,
                footerTop,
                statusWidth - textOffset,
                footerHeight);
            DrawUtil.DrawText(g, _statusText, _fontStatus, _statusColor, statusRect,
                StringAlignment.Near, StringAlignment.Center, true);

            DrawButton(g, _copyBounds, _copied ? "已复制" : "复制文本",
                _hoverCopy, _hasText, true, _copied);
            DrawButton(g, _closeBounds, "关闭", _hoverClose, true, false, false);
        }

        private void DrawButton(Graphics g, Rectangle bounds, string label, bool hover,
            bool enabled, bool primary, bool copied)
        {
            RectangleF rect = new RectangleF(bounds.X, bounds.Y, bounds.Width, bounds.Height);
            Color fill;
            Color textColor;
            Color borderColor;

            if (primary)
            {
                if (!enabled)
                {
                    fill = Color.FromArgb(226, 233, 246);
                    textColor = Color.FromArgb(152, 167, 196);
                    borderColor = Color.Transparent;
                }
                else
                {
                    fill = copied ? Color.FromArgb(47, 158, 97) : (hover ? AppPalette.AccentHover : AppPalette.Accent);
                    textColor = Color.White;
                    borderColor = Color.Transparent;
                }
            }
            else
            {
                fill = hover ? Color.FromArgb(240, 243, 248) : Color.Transparent;
                textColor = enabled ? AppPalette.Ink : AppPalette.SubInk;
                borderColor = Color.FromArgb(218, 224, 234);
            }

            if (fill.A > 0)
            {
                DrawUtil.FillRounded(g, fill, rect, 7f * _scale);
            }

            if (borderColor.A > 0)
            {
                DrawUtil.DrawRounded(g, borderColor, 1f, rect, 7f * _scale);
            }

            DrawUtil.DrawText(g, label, primary ? _fontButton : _fontClose, textColor, rect,
                StringAlignment.Center, StringAlignment.Center, true);
        }

        private void DrawBorder(Graphics g)
        {
            using (Pen border = new Pen(Color.FromArgb(206, 213, 225), 1f))
            {
                g.DrawRectangle(border, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
            }
        }

        private int ChannelAt(Point point)
        {
            for (int i = 0; i < _channelBounds.Length; i++)
            {
                if (_channelBounds[i].Contains(point))
                {
                    return i;
                }
            }

            return -1;
        }

        private string GetChannelLabel()
        {
            return _channel == OcrChannel.Paddle ? "PaddleOCR" : "Windows 本机 OCR";
        }

        private void SwitchChannel(OcrChannel channel)
        {
            if (channel == _channel)
            {
                return;
            }

            if (channel == OcrChannel.Paddle && !PaddleOcr.IsInstalled())
            {
                _statusText = "PaddleOCR 未安装，请先从托盘菜单下载增强 OCR";
                _statusColor = AppPalette.Danger;
                Invalidate();
                return;
            }

            _channel = channel;
            _loading = true;
            _copied = false;
            _statusText = "正在使用" + GetChannelLabel() + "重新识别";
            _statusColor = AppPalette.SubInk;
            _subtitle = GetChannelLabel() + " · 正在识别";
            _spinnerTimer.Start();
            Invalidate();

            int version = ++_requestVersion;
            OcrChannel requested = channel;
            ThreadPool.QueueUserWorkItem(delegate(object state)
            {
                OcrOutcome outcome;
                try
                {
                    outcome = RecognizeForChannel(requested);
                }
                catch (Exception ex)
                {
                    AppLog.WriteException("OCR channel", ex);
                    outcome = new OcrOutcome();
                    outcome.Error = "文字识别失败：\n\n" + ex.Message;
                }

                if (IsDisposed || Disposing)
                {
                    return;
                }

                try
                {
                    BeginInvoke(new Action(delegate
                    {
                        if (version != _requestVersion || IsDisposed || Disposing)
                        {
                            return;
                        }

                        ApplyOutcome(outcome);
                    }));
                }
                catch (ObjectDisposedException)
                {
                }
                catch (InvalidOperationException)
                {
                }
            });
        }

        private OcrOutcome RecognizeForChannel(OcrChannel channel)
        {
            if (_sourceBitmap == null)
            {
                return ChannelError("没有可重新识别的截图。");
            }

            string culture = CultureInfo.CurrentUICulture.Name;
            if (channel == OcrChannel.Paddle)
            {
                if (!PaddleOcr.IsInstalled())
                {
                    return ChannelError("PaddleOCR 未安装，请先从托盘菜单下载增强 OCR。");
                }

                return FromPaddleResult(PaddleOcr.Recognize(_sourceBitmap));
            }

            return WindowsOcr.Recognize(_sourceBitmap, culture);
        }

        private static OcrOutcome FromPaddleResult(PaddleOcrResult result)
        {
            OcrOutcome outcome = new OcrOutcome();
            if (result == null || !result.Success)
            {
                outcome.Error = result == null
                    ? "PaddleOCR 没有返回结果。"
                    : (string.IsNullOrEmpty(result.Error)
                        ? "PaddleOCR 没有识别到文字。"
                        : result.Error);
                outcome.LanguageName = "PaddleOCR";
                return outcome;
            }

            outcome.Success = true;
            outcome.Text = result.Text;
            outcome.LineCount = result.LineCount;
            outcome.CharCount = result.CharCount;
            outcome.LanguageName = "PaddleOCR（本地增强）";
            outcome.SuggestEnglishOcr = false;
            return outcome;
        }

        private static OcrOutcome ChannelError(string message)
        {
            OcrOutcome outcome = new OcrOutcome();
            outcome.Error = message;
            return outcome;
        }

        internal void SetOutcome(OcrOutcome outcome)
        {
            if (_requestVersion != 0)
            {
                return;
            }

            if (outcome != null && outcome.Success && !string.IsNullOrEmpty(outcome.LanguageName))
            {
                _channel = outcome.LanguageName.IndexOf(
                    "PaddleOCR", StringComparison.OrdinalIgnoreCase) >= 0
                    ? OcrChannel.Paddle
                    : OcrChannel.Windows;
            }

            ApplyOutcome(outcome);
        }

        private void ApplyOutcome(OcrOutcome outcome)
        {
            if (IsDisposed || Disposing)
            {
                return;
            }

            _loading = false;
            _spinnerTimer.Stop();
            _copied = false;

            if (outcome == null)
            {
                _hasText = false;
                _statusText = "识别失败";
                _statusColor = AppPalette.Danger;
                _subtitle = GetChannelLabel() + " · 无法处理这张图片";
                _textBox.ForeColor = AppPalette.SubInk;
                _textBox.Text = "文字识别没有返回结果，请稍后重试。";
            }
            else if (!outcome.Success)
            {
                _hasText = false;
                _statusText = "未识别到文字";
                _statusColor = AppPalette.Danger;
                _subtitle = GetChannelLabel() + " · 无法处理这张图片";
                _textBox.ForeColor = Color.FromArgb(139, 63, 63);
                _textBox.Text = outcome.Error ?? "没有识别到文字。请重新框选后再试。";
            }
            else
            {
                _hasText = true;
                _textValue = outcome.Text ?? string.Empty;
                _statusText = string.Format("{0} 行 · {1} 字符", outcome.LineCount, outcome.CharCount);
                if (outcome.SuggestEnglishOcr)
                {
                    _statusText += " · 英文建议安装英语 OCR";
                }

                _statusColor = AppPalette.SubInk;
                _subtitle = BuildSubtitle(outcome);
                _textBox.ForeColor = AppPalette.Ink;
                _textBox.Text = _textValue;
                try
                {
                    _textBox.SelectionStart = 0;
                    _textBox.SelectionLength = 0;
                    _textBox.Focus();
                }
                catch
                {
                }
            }

            Invalidate();
        }

        private string BuildSubtitle(OcrOutcome outcome)
        {
            string language = outcome == null ? string.Empty : (outcome.LanguageName ?? string.Empty);
            if (_channel == OcrChannel.Paddle)
            {
                return "PaddleOCR · 本地增强";
            }

            return "Windows 本机 OCR" +
                (language.Length == 0 ? string.Empty : " · " + language);
        }

        private void CopyText()
        {
            if (!_hasText || string.IsNullOrEmpty(_textValue))
            {
                return;
            }

            Exception last = null;
            for (int attempt = 0; attempt < 4; attempt++)
            {
                try
                {
                    Clipboard.SetText(_textValue);
                    _copied = true;
                    _statusText = "已复制 " + _textValue.Length.ToString() + " 个字符到剪贴板";
                    _statusColor = Color.FromArgb(45, 140, 90);
                    Invalidate();
                    return;
                }
                catch (Exception ex)
                {
                    last = ex;
                    Thread.Sleep(50);
                    Application.DoEvents();
                }
            }

            _statusText = "复制失败，请在文本框中全选后复制";
            _statusColor = AppPalette.Danger;
            AppLog.WriteException("OCR clipboard", last);
            Invalidate();
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape)
            {
                Close();
                return true;
            }

            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            bool newHoverCopy = _hasText && _copyBounds.Contains(e.Location);
            bool newHoverClose = _closeBounds.Contains(e.Location);
            bool newHoverX = _xBounds.Contains(e.Location);
            int newHoverChannel = ChannelAt(e.Location);

            if (newHoverCopy || newHoverClose || newHoverX || newHoverChannel >= 0)
            {
                Cursor = Cursors.Hand;
            }
            else if (_headerBounds.Contains(e.Location))
            {
                Cursor = Cursors.SizeAll;
            }
            else
            {
                Cursor = Cursors.Default;
            }

            if (newHoverCopy != _hoverCopy || newHoverClose != _hoverClose ||
                newHoverX != _hoverX || newHoverChannel != _hoverChannel)
            {
                _hoverCopy = newHoverCopy;
                _hoverClose = newHoverClose;
                _hoverX = newHoverX;
                _hoverChannel = newHoverChannel;
                Invalidate();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _hoverCopy = false;
            _hoverClose = false;
            _hoverX = false;
            _hoverChannel = -1;
            Cursor = Cursors.Default;
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left)
            {
                return;
            }

            if (_xBounds.Contains(e.Location))
            {
                Close();
                return;
            }

            int channel = ChannelAt(e.Location);
            if (channel >= 0)
            {
                if (channel == (int)OcrChannel.Paddle && !PaddleOcr.IsInstalled())
                {
                    _statusText = "PaddleOCR 未安装，请先从托盘菜单下载增强 OCR";
                    _statusColor = AppPalette.Danger;
                    Invalidate();
                    return;
                }

                SwitchChannel((OcrChannel)channel);
                return;
            }

            if (_copyBounds.Contains(e.Location))
            {
                CopyText();
                return;
            }

            if (_closeBounds.Contains(e.Location))
            {
                Close();
                return;
            }

            if (_headerBounds.Contains(e.Location))
            {
                Native.ReleaseCapture();
                Native.SendMessage(Handle, WM_NCLBUTTONDOWN, new IntPtr(HTCAPTION), IntPtr.Zero);
            }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _spinnerTimer.Stop();
            _spinnerTimer.Dispose();
            if (_sourceBitmap != null)
            {
                _sourceBitmap.Dispose();
            }

            _fontTitle.Dispose();
            _fontSubtitle.Dispose();
            _fontStatus.Dispose();
            _fontButton.Dispose();
            _fontClose.Dispose();
            if (_textBox != null)
            {
                _textBox.Font.Dispose();
            }
            base.OnFormClosed(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_spinnerTimer != null)
                {
                    _spinnerTimer.Dispose();
                }
            }

            base.Dispose(disposing);
        }
    }
}



