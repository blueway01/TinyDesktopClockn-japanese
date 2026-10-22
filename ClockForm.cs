using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace TinyDesktopClock
{
    internal sealed class ClockForm : Form
    {
        private const int WM_NCHITTEST = 0x0084;
        private const int WM_NCLBUTTONDOWN = 0x00A1;
        private const int WM_NCLBUTTONDBLCLK = 0x00A3;
        private const int WM_EXITSIZEMOVE = 0x0232;
        private const int HTCAPTION = 2;
        private const int HTLEFT = 10;
        private const int HTRIGHT = 11;
        private const int HTTOP = 12;
        private const int HTTOPLEFT = 13;
        private const int HTTOPRIGHT = 14;
        private const int HTBOTTOM = 15;
        private const int HTBOTTOMLEFT = 16;
        private const int HTBOTTOMRIGHT = 17;
        private const int WS_EX_TRANSPARENT = 0x00000020;
        private const int WS_EX_LAYERED = 0x00080000;

        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();
        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int width, int height, uint flags);
        [DllImport("user32.dll")]
        private static extern bool DestroyIcon(IntPtr icon);
        [DllImport("user32.dll")]
        private static extern IntPtr GetShellWindow();

        private static readonly IntPtr HwndBottom = new IntPtr(1);
        private const uint KeepSizeLocationAndFocus = 0x0001 | 0x0002 | 0x0010;

        private AppSettings _settings;
        private readonly bool _smokeTest;
        private readonly Timer _tick;
        private readonly NotifyIcon _tray;
        private readonly ContextMenuStrip _menu;
        private readonly Icon _clockIcon;
        private readonly WifiTrafficMeter _traffic = new WifiTrafficMeter();
        private readonly PeakTrafficRecord _peakTraffic = new PeakTrafficRecord();
        private TrafficReading _reading;
        private DateTime _nextTrafficSample = DateTime.MinValue;
        private double _meterScale = 5;
        private string _lastTime = "";
        private string _lastDate = "";
        private int _bottomTicks;
        private bool _reallyClosing;

        public ClockForm(AppSettings settings, bool smokeTest)
        {
            _settings = settings.Copy();
            _settings.Normalize();
            _smokeTest = smokeTest;
            Text = "ミニデスクトップ時計";
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            MinimumSize = new Size(210, 96);
            DoubleBuffered = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            KeyPreview = true;
            ShowInTaskbar = _settings.ShowInTaskbar;
            Rectangle initialBounds = InitialBounds(_settings);
            Bounds = initialBounds;
            // Keep the recovered coordinates as the active settings, otherwise
            // ApplySettings would move the clock back to an unplugged monitor.
            _settings.X = initialBounds.X;
            _settings.Y = initialBounds.Y;
            _settings.Width = initialBounds.Width;
            _settings.Height = initialBounds.Height;
            _clockIcon = MakeClockIcon();
            Icon = _clockIcon;

            _menu = new ContextMenuStrip();
            _menu.ShowImageMargin = false;
            _menu.Opening += MenuOpening;
            ContextMenuStrip = _menu;

            _tray = new NotifyIcon();
            _tray.Icon = _clockIcon;
            _tray.Text = "ミニデスクトップ時計（右クリックで設定）";
            _tray.ContextMenuStrip = _menu;
            _tray.DoubleClick += delegate { RestoreToForeground(); };
            _tray.Visible = !_smokeTest;

            if (_settings.StartWithWindows && !_smokeTest)
            {
                try { StartupManager.EnsureEnabled(); }
                catch (Exception ex) { Log.Write(ex); }
            }

            _tick = new Timer();
            _tick.Interval = 250;
            _tick.Tick += delegate
            {
                RefreshText();
                if (DateTime.UtcNow >= _nextTrafficSample)
                {
                    _reading = _traffic.Sample();
                    _peakTraffic.Update(_reading);
                    _nextTrafficSample = DateTime.UtcNow.AddSeconds(1);
                    UpdateMeterScale();
                    Invalidate();
                }
                if (_settings.WindowMode == "bottom" && ++_bottomTicks >= 6)
                {
                    _bottomTicks = 0;
                    PutAtBottom();
                }
            };
            UpdateCornerRegion();
            ApplySettings(_settings, false);
            RefreshText();
            _reading = _traffic.Sample();
            _peakTraffic.Update(_reading);
            _tick.Start();
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                if (_settings != null && _settings.ClickThrough)
                    cp.ExStyle |= WS_EX_TRANSPARENT | WS_EX_LAYERED;
                return cp;
            }
        }

        private static Rectangle InitialBounds(AppSettings settings)
        {
            Rectangle desktop = Screen.PrimaryScreen.WorkingArea;
            int width = Math.Min(settings.Width, Math.Max(210, desktop.Width - 24));
            int height = Math.Min(settings.Height, Math.Max(96, desktop.Height - 24));
            if (settings.X == int.MinValue || settings.Y == int.MinValue)
                return new Rectangle(desktop.Right - width - 18, desktop.Top + 22, width, height);

            Rectangle candidate = new Rectangle(settings.X, settings.Y, settings.Width, settings.Height);
            foreach (Screen screen in Screen.AllScreens)
            {
                Rectangle overlap = Rectangle.Intersect(candidate, screen.WorkingArea);
                if (overlap.Width >= 60 && overlap.Height >= 30) return candidate;
            }
            return new Rectangle(desktop.Right - width - 18, desktop.Top + 22, width, height);
        }

        private void UpdateCornerRegion()
        {
            if (Width < 1 || Height < 1) return;
            using (GraphicsPath path = RoundedRectangle(new Rectangle(0, 0, Width, Height), 17))
            {
                Region old = Region;
                Region = new Region(path);
                if (old != null) old.Dispose();
            }
        }

        private static GraphicsPath RoundedRectangle(Rectangle rect, int radius)
        {
            int diameter = Math.Min(radius * 2, Math.Min(rect.Width, rect.Height));
            GraphicsPath path = new GraphicsPath();
            if (diameter < 2)
            {
                path.AddRectangle(rect);
                return path;
            }
            Rectangle arc = new Rectangle(rect.Left, rect.Top, diameter, diameter);
            path.AddArc(arc, 180, 90);
            arc.X = rect.Right - diameter;
            path.AddArc(arc, 270, 90);
            arc.Y = rect.Bottom - diameter;
            path.AddArc(arc, 0, 90);
            arc.X = rect.Left;
            path.AddArc(arc, 90, 90);
            path.CloseFigure();
            return path;
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            UpdateCornerRegion();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if (_settings.WindowMode == "bottom") PutAtBottom();
        }

        protected override void OnActivated(EventArgs e)
        {
            base.OnActivated(e);
            if (_settings != null && _settings.WindowMode == "bottom" && IsHandleCreated)
                BeginInvoke((MethodInvoker)delegate { PutAtBottom(); });
        }

        private void RefreshText()
        {
            DateTime now = DateTime.Now;
            string time = ClockFormatter.Time(now, _settings);
            string date = _settings.ShowDate ? ClockFormatter.Date(now, _settings) : "";
            if (_lastTime == time && _lastDate == date) return;
            _lastTime = time;
            _lastDate = date;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            Color back = ColorTools.Parse(_settings.BackgroundColor, "#172133");
            Color text = ColorTools.Parse(_settings.ForegroundColor, "#EAF3FF");
            Color accent = ColorTools.Parse(_settings.AccentColor, "#65E1C1");

            Rectangle card = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath bgPath = RoundedRectangle(card, 17))
            using (LinearGradientBrush fill = new LinearGradientBrush(card, ControlPaint.Light(back, 0.08f), back, 90f))
            using (Pen outline = new Pen(Color.FromArgb(100, accent), 1f))
            {
                g.FillPath(fill, bgPath);
                g.DrawPath(outline, bgPath);
            }

            using (SolidBrush accentBrush = new SolidBrush(accent))
                g.FillRectangle(accentBrush, 18, 12, 26, 3);

            float meterTop = Height - 37;
            float dateHeight = _settings.ShowDate ? 15f : 0;
            RectangleF timeArea = new RectangleF(9, 9, Width - 18, meterTop - 11 - dateHeight);
            float fontSize = Math.Min(_settings.FontSize, Math.Max(18f, timeArea.Height * 0.83f));
            Font clockFont = null;
            try
            {
                using (StringFormat format = new StringFormat(StringFormat.GenericTypographic))
                {
                    format.Alignment = StringAlignment.Center;
                    format.LineAlignment = StringAlignment.Center;
                    format.FormatFlags = StringFormatFlags.NoWrap;
                    while (fontSize >= 12f)
                    {
                        if (clockFont != null) clockFont.Dispose();
                        clockFont = CreateFont(fontSize, FontStyle.Bold);
                        SizeF measured = g.MeasureString(_lastTime, clockFont, int.MaxValue, format);
                        if (measured.Width <= timeArea.Width - 18 && measured.Height <= timeArea.Height) break;
                        fontSize -= 1f;
                    }
                    using (SolidBrush textBrush = new SolidBrush(text))
                        g.DrawString(_lastTime, clockFont, textBrush, timeArea, format);
                }
            }
            finally { if (clockFont != null) clockFont.Dispose(); }

            if (_settings.ShowDate)
            {
                using (Font dateFont = CreateDateFont(Math.Max(10f, Math.Min(12.5f, Height * 0.13f))))
                using (SolidBrush dateBrush = new SolidBrush(Color.FromArgb(200, text)))
                using (StringFormat format = new StringFormat())
                {
                    format.Alignment = StringAlignment.Center;
                    format.LineAlignment = StringAlignment.Center;
                    format.Trimming = StringTrimming.EllipsisCharacter;
                    RectangleF dateArea = new RectangleF(12, meterTop - dateHeight - 2, Width - 24, dateHeight);
                    g.DrawString(_lastDate, dateFont, dateBrush, dateArea, format);
                }
            }
            DrawTraffic(g, meterTop);
        }

        private void UpdateMeterScale()
        {
            double peak = Math.Max(_reading.DownloadMbps, _reading.UploadMbps) * 1.15;
            double[] levels = { 1, 2, 5, 10, 20, 50, 100, 200, 500, 1000, 2500, 5000, 10000 };
            double target = levels[levels.Length - 1];
            foreach (double level in levels)
                if (peak <= level) { target = level; break; }
            _meterScale = Math.Max(target, _meterScale * 0.92);
        }

        private void DrawTraffic(Graphics g, float top)
        {
            using (Font font = new Font("Yu Gothic UI", 8.2f, FontStyle.Bold))
            using (SolidBrush label = new SolidBrush(Color.FromArgb(225, 233, 245)))
            {
                if (_reading.Status != "接続中")
                {
                    g.DrawString(_reading.Status ?? "測定準備中", font, label, 13, top + 8);
                    return;
                }
                DrawMeterRow(g, font, label, top + 2, "↓", _reading.DownloadMbps, _peakTraffic.DownloadMbps);
                DrawMeterRow(g, font, label, top + 18, "↑", _reading.UploadMbps, _peakTraffic.UploadMbps);
            }
        }

        private void DrawMeterRow(Graphics g, Font font, Brush label, float y, string arrow, double speed, double peakSpeed)
        {
            string value = FormatSpeed(speed);
            string peak = FormatSpeed(peakSpeed);
            string display = arrow + value + " 最高" + peak + " Mbps";
            g.DrawString(display, font, label, 11, y - 2);
            int labelWidth = (int)Math.Ceiling(g.MeasureString(display, font).Width);
            int x = Math.Max(126, Math.Min(labelWidth + 15, Width - 36));
            int available = Math.Max(24, Width - x - 12);
            int segments = Math.Max(8, Math.Min(30, available / 5));
            int active = (int)Math.Round(Math.Min(speed / Math.Max(_meterScale, 1), 1) * segments);
            for (int i = 0; i < segments; i++)
            {
                int left = x + i * available / segments;
                int right = x + (i + 1) * available / segments - 2;
                Color color = i < active ? MeterColor(i, segments) : Color.FromArgb(45, 62, 80);
                using (SolidBrush brush = new SolidBrush(color))
                    g.FillRectangle(brush, left, y, Math.Max(2, right - left), 8);
            }
        }

        private static string FormatSpeed(double speed)
        {
            return speed < 10 ? speed.ToString("F2", CultureInfo.InvariantCulture) :
                speed.ToString("F1", CultureInfo.InvariantCulture);
        }

        private static Color MeterColor(int index, int total)
        {
            double ratio = index / (double)total;
            if (ratio < 0.45) return Color.FromArgb(52, 211, 111);
            if (ratio < 0.70) return Color.FromArgb(241, 210, 57);
            if (ratio < 0.87) return Color.FromArgb(246, 150, 48);
            return Color.FromArgb(239, 82, 80);
        }

        private Font CreateFont(float size, FontStyle style)
        {
            try { return new Font(_settings.FontName, size, style, GraphicsUnit.Point); }
            catch { return new Font(FontFamily.GenericMonospace, size, style, GraphicsUnit.Point); }
        }

        private static Font CreateDateFont(float size)
        {
            return new Font("Yu Gothic UI", size, FontStyle.Regular, GraphicsUnit.Point);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_NCLBUTTONDBLCLK && _settings != null && !_settings.ClickThrough)
            {
                BeginInvoke((MethodInvoker)delegate { OpenSettings(); });
                return;
            }
            if (m.Msg == WM_NCHITTEST && _settings != null && !_settings.LockPosition && !_settings.ClickThrough)
            {
                base.WndProc(ref m);
                Point point = PointToClient(new Point(unchecked((short)((long)m.LParam & 0xFFFF)),
                    unchecked((short)(((long)m.LParam >> 16) & 0xFFFF))));
                int grip = 8;
                bool left = point.X < grip;
                bool right = point.X >= Width - grip;
                bool top = point.Y < grip;
                bool bottom = point.Y >= Height - grip;
                if (top && left) m.Result = (IntPtr)HTTOPLEFT;
                else if (top && right) m.Result = (IntPtr)HTTOPRIGHT;
                else if (bottom && left) m.Result = (IntPtr)HTBOTTOMLEFT;
                else if (bottom && right) m.Result = (IntPtr)HTBOTTOMRIGHT;
                else if (left) m.Result = (IntPtr)HTLEFT;
                else if (right) m.Result = (IntPtr)HTRIGHT;
                else if (top) m.Result = (IntPtr)HTTOP;
                else if (bottom) m.Result = (IntPtr)HTBOTTOM;
                return;
            }
            base.WndProc(ref m);
            if (m.Msg == WM_EXITSIZEMOVE) SavePositionAndSize();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left && e.Clicks == 1 && !_settings.LockPosition && !_settings.ClickThrough)
            {
                ReleaseCapture();
                SendMessage(Handle, WM_NCLBUTTONDOWN, (IntPtr)HTCAPTION, IntPtr.Zero);
            }
        }

        protected override void OnDoubleClick(EventArgs e)
        {
            base.OnDoubleClick(e);
            OpenSettings();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.F2)
            {
                OpenSettings();
                e.Handled = true;
            }
            base.OnKeyDown(e);
        }

        private void SavePositionAndSize()
        {
            if (_smokeTest || _reallyClosing || WindowState != FormWindowState.Normal) return;
            _settings.X = Left;
            _settings.Y = Top;
            _settings.Width = Width;
            _settings.Height = Height;
            SaveSafely();
        }

        private void SaveSafely()
        {
            if (_smokeTest) return;
            try { SettingsStore.Save(_settings); }
            catch (Exception ex)
            {
                Log.Write(ex);
                MessageBox.Show("設定の保存に失敗しました。\n" + ex.Message, "ミニデスクトップ時計",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void PutAtBottom()
        {
            if (!IsHandleCreated || !Visible || _settings.WindowMode != "bottom") return;
            // The shell owns the desktop background. HWND_BOTTOM alone can put
            // the clock *behind* the wallpaper, making it impossible to see.
            // Place it immediately above the shell, below ordinary windows.
            IntPtr desktop = GetShellWindow();
            SetWindowPos(Handle, desktop != IntPtr.Zero && desktop != Handle ? desktop : HwndBottom,
                0, 0, 0, 0, KeepSizeLocationAndFocus);
        }

        public void ApplySettings(AppSettings settings, bool persist)
        {
            settings = settings.Copy();
            settings.Normalize();
            if (settings.StartWithWindows != _settings.StartWithWindows && !_smokeTest)
            {
                try { StartupManager.SetEnabled(settings.StartWithWindows); }
                catch (Exception ex)
                {
                    Log.Write(ex);
                    settings.StartWithWindows = _settings.StartWithWindows;
                    MessageBox.Show("Windows起動時の自動起動を変更できませんでした。\n" + ex.Message,
                        "ミニデスクトップ時計", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }

            bool clickChanged = _settings.ClickThrough != settings.ClickThrough;
            _settings = settings;
            if (clickChanged && IsHandleCreated) RecreateHandle();
            ShowInTaskbar = settings.ShowInTaskbar;
            TopMost = settings.WindowMode == "top";
            Opacity = settings.OpacityPercent / 100.0;
            if (Width != settings.Width || Height != settings.Height)
                Size = new Size(settings.Width, settings.Height);
            if (settings.X != int.MinValue && settings.Y != int.MinValue &&
                (Left != settings.X || Top != settings.Y))
                Location = new Point(settings.X, settings.Y);
            _lastTime = "";
            RefreshText();
            Invalidate();
            if (settings.WindowMode == "bottom") PutAtBottom();
            if (persist)
            {
                _settings.X = Left;
                _settings.Y = Top;
                SaveSafely();
            }
        }

        private void MenuOpening(object sender, CancelEventArgs e)
        {
            _menu.Items.Clear();
            _menu.Items.Add("時計を前面に戻す（操作可能にする）", null, delegate { RestoreToForeground(); });
            _menu.Items.Add("設定を開く...", null, delegate { OpenSettings(); });
            _menu.Items.Add(new ToolStripSeparator());

            ToolStripMenuItem modeMenu = new ToolStripMenuItem("表示位置");
            AddMode(modeMenu, "常に最前面", "top");
            AddMode(modeMenu, "通常のウィンドウ", "normal");
            AddMode(modeMenu, "背面に表示", "bottom");
            _menu.Items.Add(modeMenu);

            AddToggle("タスクバーに表示", _settings.ShowInTaskbar, delegate(AppSettings s) { s.ShowInTaskbar = !s.ShowInTaskbar; });
            AddToggle("秒を表示", _settings.ShowSeconds, delegate(AppSettings s) { s.ShowSeconds = !s.ShowSeconds; });
            AddToggle("日付を表示", _settings.ShowDate, delegate(AppSettings s) { s.ShowDate = !s.ShowDate; });
            AddToggle("位置・サイズを固定", _settings.LockPosition, delegate(AppSettings s) { s.LockPosition = !s.LockPosition; });
            AddToggle("マウス操作を透過（トレイから解除）", _settings.ClickThrough, delegate(AppSettings s) { s.ClickThrough = !s.ClickThrough; });

            _menu.Items.Add(new ToolStripSeparator());
            _menu.Items.Add("画面右上に戻す", null, delegate
            {
                AppSettings s = _settings.Copy();
                s.X = int.MinValue;
                s.Y = int.MinValue;
                Rectangle rect = InitialBounds(s);
                s.X = rect.X;
                s.Y = rect.Y;
                ApplySettings(s, true);
            });
            _menu.Items.Add("終了", null, delegate { Close(); });
        }

        private void AddMode(ToolStripMenuItem parent, string label, string mode)
        {
            ToolStripMenuItem item = new ToolStripMenuItem(label);
            item.Checked = _settings.WindowMode == mode;
            item.Click += delegate
            {
                AppSettings s = _settings.Copy();
                s.WindowMode = mode;
                ApplySettings(s, true);
            };
            parent.DropDownItems.Add(item);
        }

        private void AddToggle(string text, bool checkedValue, Action<AppSettings> change)
        {
            ToolStripMenuItem item = new ToolStripMenuItem(text);
            item.Checked = checkedValue;
            item.Click += delegate
            {
                AppSettings s = _settings.Copy();
                change(s);
                ApplySettings(s, true);
            };
            _menu.Items.Add(item);
        }

        private void OpenSettings()
        {
            using (SettingsForm dialog = new SettingsForm(_settings.Copy()))
            {
                dialog.TopMost = _settings.WindowMode == "top";
                if (dialog.ShowDialog(this) == DialogResult.OK)
                    ApplySettings(dialog.Result, true);
            }
        }

        private void RestoreToForeground()
        {
            if (WindowState == FormWindowState.Minimized)
                WindowState = FormWindowState.Normal;
            AppSettings settings = _settings.Copy();
            settings.WindowMode = "top";
            settings.ClickThrough = false;
            ApplySettings(settings, true);
            Show();
            BringToFront();
            Activate();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            _reallyClosing = true;
            if (!_smokeTest)
            {
                Rectangle normalBounds = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
                if (normalBounds.Width >= MinimumSize.Width && normalBounds.Height >= MinimumSize.Height)
                {
                    _settings.X = normalBounds.Left;
                    _settings.Y = normalBounds.Top;
                    _settings.Width = normalBounds.Width;
                    _settings.Height = normalBounds.Height;
                }
                SaveSafely();
            }
            _tick.Stop();
            _tray.Visible = false;
            base.OnFormClosing(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _tick.Dispose();
                _tray.Dispose();
                _menu.Dispose();
                _clockIcon.Dispose();
            }
            base.Dispose(disposing);
        }

        private static Icon MakeClockIcon()
        {
            using (Bitmap bmp = new Bitmap(32, 32))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                using (Pen ring = new Pen(Color.FromArgb(101, 225, 193), 3))
                using (Pen hand = new Pen(Color.White, 2.6f))
                using (SolidBrush background = new SolidBrush(Color.FromArgb(23, 33, 51)))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.Clear(Color.Transparent);
                    g.FillEllipse(background, 1, 1, 30, 30);
                    g.DrawEllipse(ring, 3, 3, 26, 26);
                    g.DrawLine(hand, 16, 16, 16, 8);
                    g.DrawLine(hand, 16, 16, 22, 19);
                }
                IntPtr handle = bmp.GetHicon();
                try { return (Icon)Icon.FromHandle(handle).Clone(); }
                finally { DestroyIcon(handle); }
            }
        }
    }
}
