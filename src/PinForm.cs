using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace QuickScreenshot
{
    internal sealed class PinForm : Form
    {
        private const int WM_NCLBUTTONDOWN = 0xA1;
        private const int HTCAPTION = 2;

        private readonly Bitmap _image;
        private readonly TrayApp _app;
        private readonly AppSettings _settings;
        private readonly float _scale;
        private readonly ContextMenuStrip _menu;
        private bool _mouseInside;
        private bool _closeHover;

        public PinForm(Bitmap image, Rectangle screenBounds, float scale, TrayApp app, AppSettings settings)
        {
            _image = image;
            _app = app;
            _settings = settings;
            _scale = Math.Max(0.75f, Math.Min(2.2f, scale));

            AutoScaleMode = AutoScaleMode.None;
            BackColor = Color.FromArgb(52, 56, 66);
            ClientSize = new Size(image.Width + 2, image.Height + 2);
            DoubleBuffered = true;
            FormBorderStyle = FormBorderStyle.None;
            KeyPreview = true;
            Location = new Point(screenBounds.Left - 1, screenBounds.Top - 1);
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            Text = "钉图 - QuickScreenshot";
            TopMost = true;
            Cursor = Cursors.SizeAll;

            SetStyle(ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint |
                     ControlStyles.ResizeRedraw, true);

            _menu = new ContextMenuStrip();
            _menu.ShowImageMargin = false;
            ToolStripMenuItem copyItem = new ToolStripMenuItem("复制钉图");
            copyItem.Click += delegate { CopyPin(); };
            ToolStripMenuItem saveItem = new ToolStripMenuItem("另存为");
            saveItem.Click += delegate { SavePin(); };
            ToolStripMenuItem closeItem = new ToolStripMenuItem("关闭钉图");
            closeItem.Click += delegate { Close(); };
            _menu.Items.Add(copyItem);
            _menu.Items.Add(saveItem);
            _menu.Items.Add(new ToolStripSeparator());
            _menu.Items.Add(closeItem);
            ContextMenuStrip = _menu;
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ClassStyle |= 0x00020000; // CS_DROPSHADOW
                cp.ExStyle |= 0x00000080;    // WS_EX_TOOLWINDOW
                return cp;
            }
        }

        private Rectangle CloseBounds
        {
            get
            {
                int size = Math.Max(14, (int)Math.Round(19f * _scale));
                int max = Math.Min(ClientSize.Width, ClientSize.Height) - 6;
                if (max > 8)
                {
                    size = Math.Min(size, max);
                }

                size = Math.Max(8, size);
                int margin = Math.Max(3, (int)Math.Round(4f * _scale));
                return new Rectangle(
                    Math.Max(1, ClientSize.Width - size - margin),
                    margin,
                    size,
                    size);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;

            using (SolidBrush border = new SolidBrush(Color.FromArgb(73, 79, 92)))
            {
                g.FillRectangle(border, ClientRectangle);
            }

            if (_image != null)
            {
                g.DrawImageUnscaled(_image, 1, 1);
            }

            if (_mouseInside)
            {
                Rectangle close = CloseBounds;
                using (SolidBrush background = new SolidBrush(Color.FromArgb(185, 20, 23, 30)))
                {
                    g.FillEllipse(background, close);
                }

                float pad = Math.Max(3f, close.Width * 0.29f);
                using (Pen pen = new Pen(Color.FromArgb(245, 255, 255, 255),
                    Math.Max(1.1f, 1.45f * _scale)))
                {
                    pen.StartCap = LineCap.Round;
                    pen.EndCap = LineCap.Round;
                    g.DrawLine(pen,
                        close.Left + pad, close.Top + pad,
                        close.Right - pad, close.Bottom - pad);
                    g.DrawLine(pen,
                        close.Right - pad, close.Top + pad,
                        close.Left + pad, close.Bottom - pad);
                }
            }
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            _mouseInside = true;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _mouseInside = false;
            _closeHover = false;
            Cursor = Cursors.SizeAll;
            Invalidate();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            _mouseInside = true;
            bool hover = CloseBounds.Contains(e.Location);
            Cursor = hover ? Cursors.Hand : Cursors.SizeAll;
            if (hover != _closeHover)
            {
                _closeHover = hover;
                Invalidate();
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left)
            {
                return;
            }

            if (CloseBounds.Contains(e.Location))
            {
                Close();
                return;
            }

            Native.ReleaseCapture();
            Native.SendMessage(Handle, WM_NCLBUTTONDOWN, (IntPtr)HTCAPTION, IntPtr.Zero);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape)
            {
                Close();
                return true;
            }

            if (keyData == (Keys.Control | Keys.C))
            {
                CopyPin();
                return true;
            }

            if (keyData == (Keys.Control | Keys.S))
            {
                SavePin();
                return true;
            }

            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void CopyPin()
        {
            if (_image == null)
            {
                return;
            }

            try
            {
                ClipboardHelper.PutImage(_image);
                if (_app != null)
                {
                    _app.ShowToast("已复制钉图", "钉图已复制到剪贴板，可直接粘贴。");
                }
            }
            catch (Exception ex)
            {
                AppLog.WriteException("Pin copy", ex);
                MessageBox.Show(this, "复制钉图失败：\n\n" + ex.Message,
                    "QuickScreenshot", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void SavePin()
        {
            if (_image == null)
            {
                return;
            }

            string initial = _settings == null ? null : _settings.LastSaveDirectory;
            if (string.IsNullOrEmpty(initial) || !System.IO.Directory.Exists(initial))
            {
                initial = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
            }

            using (SaveFileDialog dialog = new SaveFileDialog())
            {
                dialog.Title = "钉图另存为";
                dialog.InitialDirectory = initial;
                dialog.Filter = "PNG 图片 (*.png)|*.png|JPEG 图片 (*.jpg;*.jpeg)|*.jpg;*.jpeg|BMP 图片 (*.bmp)|*.bmp";
                dialog.FilterIndex = 1;
                dialog.DefaultExt = "png";
                dialog.AddExtension = true;
                dialog.OverwritePrompt = true;
                dialog.RestoreDirectory = true;
                dialog.FileName = "钉图_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");

                bool oldTopMost = TopMost;
                DialogResult result;
                TopMost = false;
                try
                {
                    result = dialog.ShowDialog(this);
                }
                finally
                {
                    TopMost = oldTopMost;
                    Activate();
                }

                if (result != DialogResult.OK)
                {
                    return;
                }

                try
                {
                    string path = dialog.FileName;
                    CaptureForm.SaveBitmap(_image, path);
                    string directory = System.IO.Path.GetDirectoryName(path);
                    if (_settings != null && !string.IsNullOrEmpty(directory))
                    {
                        _settings.LastSaveDirectory = directory;
                        _settings.Save();
                    }

                    if (_app != null)
                    {
                        _app.ShowToast("钉图已保存",
                            System.IO.Path.GetFileName(path) + "\n" + directory);
                    }
                }
                catch (Exception ex)
                {
                    AppLog.WriteException("Pin save", ex);
                    MessageBox.Show(this, "保存钉图失败：\n\n" + ex.Message,
                        "QuickScreenshot", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            if (_menu != null)
            {
                _menu.Dispose();
            }

            if (_image != null)
            {
                _image.Dispose();
            }

            base.OnFormClosed(e);
        }
    }
}
