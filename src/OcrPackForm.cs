using System;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace QuickScreenshot
{
    internal sealed class OcrPackForm : Form
    {
        private readonly ProgressBar _bar;
        private readonly Label _label;
        private readonly Button _cancelButton;
        private CancellationTokenSource _cancellation;
        private Thread _worker;
        private bool _closing;

        public bool Success;
        public string Error;

        public OcrPackForm()
        {
            Text = "下载增强 OCR（PaddleOCR）";
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(440, 142);
            Font = new Font("Microsoft YaHei UI", 9f, FontStyle.Regular, GraphicsUnit.Point);

            _label = new Label();
            _label.AutoSize = false;
            _label.SetBounds(18, 16, 404, 40);
            _label.Text = "准备下载...";
            Controls.Add(_label);

            _bar = new ProgressBar();
            _bar.SetBounds(18, 60, 404, 18);
            _bar.Minimum = 0;
            _bar.Maximum = 100;
            Controls.Add(_bar);

            _cancelButton = new Button();
            _cancelButton.Text = "取消";
            _cancelButton.SetBounds(340, 96, 82, 28);
            _cancelButton.Click += delegate
            {
                if (_cancellation != null)
                {
                    _cancellation.Cancel();
                    _cancelButton.Enabled = false;
                    _label.Text = "正在取消...";
                }
            };
            Controls.Add(_cancelButton);

            Shown += delegate
            {
                _cancellation = new CancellationTokenSource();
                _worker = new Thread(Run);
                _worker.IsBackground = true;
                _worker.Start();
            };
            FormClosing += delegate
            {
                _closing = true;
                if (_worker != null && _worker.IsAlive && _cancellation != null)
                {
                    _cancellation.Cancel();
                }
            };
        }

        private void Run()
        {
            try
            {
                PaddleOcr.Install(Report, _cancellation.Token);
                Success = true;
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                Error = ex.Message;
            }
            finally
            {
                CloseFromWorker();
            }
        }

        private void Report(int percent, string text)
        {
            if (_closing || IsDisposed || !IsHandleCreated)
            {
                return;
            }

            try
            {
                BeginInvoke(new Action(delegate
                {
                    if (_closing || IsDisposed)
                    {
                        return;
                    }

                    _bar.Value = Math.Max(0, Math.Min(100, percent));
                    _label.Text = text;
                }));
            }
            catch
            {
            }
        }

        private void CloseFromWorker()
        {
            if (IsDisposed || !IsHandleCreated)
            {
                return;
            }

            try
            {
                BeginInvoke(new Action(delegate
                {
                    if (!IsDisposed)
                    {
                        Close();
                    }
                }));
            }
            catch
            {
            }
        }
    }
}
