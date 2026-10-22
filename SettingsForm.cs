using System;
using System.Drawing;
using System.Drawing.Text;
using System.Globalization;
using System.Windows.Forms;

namespace TinyDesktopClock
{
    internal sealed class SettingsForm : Form
    {
        private readonly AppSettings _original;
        private CheckBox _hour24;
        private CheckBox _seconds;
        private CheckBox _date;
        private ComboBox _dateFormat;
        private ComboBox _font;
        private NumericUpDown _fontSize;
        private Button _textColor;
        private Button _backgroundColor;
        private Button _accentColor;
        private NumericUpDown _opacity;
        private ComboBox _mode;
        private CheckBox _taskbar;
        private CheckBox _lock;
        private CheckBox _through;
        private CheckBox _startup;
        private NumericUpDown _width;
        private NumericUpDown _height;
        private bool _resetPosition;

        public AppSettings Result { get; private set; }

        public SettingsForm(AppSettings original)
        {
            _original = original.Copy();
            Result = original.Copy();
            Text = "ミニデスクトップ時計 - 設定";
            ClientSize = new Size(520, 475);
            Font = new Font("Yu Gothic UI", 9.5f, FontStyle.Regular, GraphicsUnit.Point);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(245, 247, 252);

            TabControl tabs = new TabControl();
            tabs.Dock = DockStyle.Fill;
            tabs.Padding = new Point(14, 7);
            tabs.TabPages.Add(BuildTimeTab());
            tabs.TabPages.Add(BuildAppearanceTab());
            tabs.TabPages.Add(BuildWindowTab());
            tabs.TabPages.Add(BuildStartupTab());

            Panel footer = new Panel();
            footer.Dock = DockStyle.Bottom;
            footer.Height = 61;
            footer.Padding = new Padding(8);
            footer.BackColor = Color.White;
            Button ok = new Button();
            ok.Text = "OK（保存）";
            ok.Size = new Size(117, 35);
            ok.Location = new Point(260, 12);
            ok.BackColor = Color.FromArgb(23, 73, 89);
            ok.ForeColor = Color.White;
            ok.FlatStyle = FlatStyle.Flat;
            ok.Click += SaveClick;
            Button cancel = new Button();
            cancel.Text = "キャンセル";
            cancel.Size = new Size(117, 35);
            cancel.Location = new Point(388, 12);
            cancel.DialogResult = DialogResult.Cancel;
            footer.Controls.Add(ok);
            footer.Controls.Add(cancel);
            Controls.Add(tabs);
            Controls.Add(footer);
            AcceptButton = ok;
            CancelButton = cancel;
        }

        private TabPage NewPage(string name, out FlowLayoutPanel flow)
        {
            TabPage page = new TabPage(name);
            page.BackColor = Color.White;
            flow = new FlowLayoutPanel();
            flow.Dock = DockStyle.Fill;
            flow.FlowDirection = FlowDirection.TopDown;
            flow.WrapContents = false;
            flow.AutoScroll = true;
            flow.Padding = new Padding(12, 15, 8, 8);
            page.Controls.Add(flow);
            return page;
        }

        private static Panel Row(string title, Control control)
        {
            Panel row = new Panel();
            row.Size = new Size(445, 43);
            row.Margin = new Padding(0, 0, 0, 2);
            Label label = new Label();
            label.Text = title;
            label.AutoSize = false;
            label.Location = new Point(1, 9);
            label.Size = new Size(162, 27);
            label.TextAlign = ContentAlignment.MiddleLeft;
            control.Location = new Point(170, 7);
            control.Width = Math.Min(control.Width, 258);
            row.Controls.Add(label);
            row.Controls.Add(control);
            return row;
        }

        private static CheckBox Check(string title, bool isChecked)
        {
            CheckBox box = new CheckBox();
            box.Text = title;
            box.Checked = isChecked;
            box.AutoSize = false;
            box.Height = 28;
            box.Width = 418;
            box.Margin = new Padding(7, 1, 0, 8);
            return box;
        }

        private static NumericUpDown Number(int min, int max, int value)
        {
            NumericUpDown number = new NumericUpDown();
            number.Minimum = min;
            number.Maximum = max;
            number.Value = Math.Max(min, Math.Min(max, value));
            number.Width = 142;
            number.Height = 27;
            number.TextAlign = HorizontalAlignment.Right;
            return number;
        }

        private static Label Note(string text, int height)
        {
            Label note = new Label();
            note.Text = text;
            note.ForeColor = Color.FromArgb(83, 95, 113);
            note.Width = 427;
            note.Height = height;
            note.Margin = new Padding(7, 9, 4, 10);
            return note;
        }

        private TabPage BuildTimeTab()
        {
            FlowLayoutPanel flow;
            TabPage page = NewPage("時刻・日付", out flow);
            _hour24 = Check("24時間形式で表示（オフ：AM/PM形式）", _original.Use24Hour);
            _seconds = Check("秒まで表示する", _original.ShowSeconds);
            _date = Check("日付も表示する", _original.ShowDate);
            flow.Controls.Add(_hour24);
            flow.Controls.Add(_seconds);
            flow.Controls.Add(_date);

            _dateFormat = new ComboBox();
            _dateFormat.DropDownStyle = ComboBoxStyle.DropDown;
            _dateFormat.Width = 251;
            _dateFormat.Items.AddRange(new object[] {
                "yyyy/MM/dd (ddd)", "yyyy年M月d日 (ddd)", "MM/dd (ddd)",
                "yyyy-MM-dd", "dddd, MMM d, yyyy"
            });
            _dateFormat.Text = _original.DateFormat;
            flow.Controls.Add(Row("日付の形式", _dateFormat));
            flow.Controls.Add(Note("日付の形式は自由に入力できます。例：yyyy/MM/dd (ddd)\n時計本体をダブルクリック、または右クリックして設定を開けます。", 56));
            return page;
        }

        private TabPage BuildAppearanceTab()
        {
            FlowLayoutPanel flow;
            TabPage page = NewPage("外観・色", out flow);
            _font = new ComboBox();
            _font.DropDownStyle = ComboBoxStyle.DropDownList;
            _font.Width = 251;
            try
            {
                using (InstalledFontCollection fonts = new InstalledFontCollection())
                {
                    foreach (FontFamily family in fonts.Families)
                        _font.Items.Add(family.Name);
                }
            }
            catch { _font.Items.AddRange(new object[] { "Consolas", "Yu Gothic UI", "Meiryo UI", "Arial" }); }
            if (!_font.Items.Contains(_original.FontName)) _font.Items.Add(_original.FontName);
            _font.SelectedItem = _original.FontName;
            flow.Controls.Add(Row("時計のフォント", _font));

            _fontSize = Number(18, 100, _original.FontSize);
            flow.Controls.Add(Row("時刻の文字サイズ", _fontSize));
            _textColor = ColorButton(_original.ForegroundColor);
            _backgroundColor = ColorButton(_original.BackgroundColor);
            _accentColor = ColorButton(_original.AccentColor);
            flow.Controls.Add(Row("文字色", _textColor));
            flow.Controls.Add(Row("背景色", _backgroundColor));
            flow.Controls.Add(Row("アクセント色", _accentColor));
            _opacity = Number(30, 100, _original.OpacityPercent);
            _opacity.Increment = 5;
            flow.Controls.Add(Row("不透明度（%）", _opacity));
            flow.Controls.Add(Note("背景や文字は好みの色に変更できます。透明度を下げると、ウィンドウ全体が透けます。", 57));
            return page;
        }

        private static Button ColorButton(string initial)
        {
            Button button = new Button();
            button.Width = 200;
            button.Height = 29;
            button.FlatStyle = FlatStyle.Flat;
            SetButtonColor(button, ColorTools.Parse(initial, "#FFFFFF"));
            button.Click += delegate
            {
                using (ColorDialog dialog = new ColorDialog())
                {
                    dialog.FullOpen = true;
                    dialog.Color = button.BackColor;
                    if (dialog.ShowDialog() == DialogResult.OK) SetButtonColor(button, dialog.Color);
                }
            };
            return button;
        }

        private static void SetButtonColor(Button button, Color color)
        {
            button.BackColor = color;
            button.ForeColor = color.GetBrightness() > 0.52f ? Color.Black : Color.White;
            button.Text = ColorTranslator.ToHtml(color);
        }

        private TabPage BuildWindowTab()
        {
            FlowLayoutPanel flow;
            TabPage page = NewPage("ウィンドウ", out flow);
            _mode = new ComboBox();
            _mode.DropDownStyle = ComboBoxStyle.DropDownList;
            _mode.Width = 247;
            _mode.Items.AddRange(new object[] { "常に最前面に表示", "通常の表示", "背面に表示" });
            _mode.SelectedIndex = _original.WindowMode == "top" ? 0 : _original.WindowMode == "bottom" ? 2 : 1;
            flow.Controls.Add(Row("表示レベル", _mode));
            _taskbar = Check("タスクバーに時計を表示する", _original.ShowInTaskbar);
            _lock = Check("位置とサイズを固定する", _original.LockPosition);
            _through = Check("マウス操作を透過する", _original.ClickThrough);
            flow.Controls.Add(_taskbar);
            flow.Controls.Add(_lock);
            flow.Controls.Add(_through);
            _width = Number(210, 1400, _original.Width);
            _height = Number(96, 450, _original.Height);
            flow.Controls.Add(Row("横幅（px）", _width));
            flow.Controls.Add(Row("高さ（px）", _height));
            flow.Controls.Add(Note("背面：通常のウィンドウより後ろに配置します。\nマウス透過中も通知領域の時計アイコンから設定を変更できます。", 59));
            return page;
        }

        private TabPage BuildStartupTab()
        {
            FlowLayoutPanel flow;
            TabPage page = NewPage("起動・操作", out flow);
            _startup = Check("Windowsへのサインイン時に自動起動する", _original.StartWithWindows);
            flow.Controls.Add(_startup);
            flow.Controls.Add(Note("設定と時計の位置は自動保存されます。保存先：\n%APPDATA%\\TinyDesktopClockn\\settings.json", 66));
            Button reset = new Button();
            reset.Text = "画面右上の初期位置に戻す";
            reset.Width = 270;
            reset.Height = 35;
            reset.Margin = new Padding(8, 5, 0, 7);
            reset.Click += delegate
            {
                _resetPosition = true;
                reset.Text = "適用時に右上に移動します ✓";
            };
            flow.Controls.Add(reset);
            flow.Controls.Add(Note("時計の中央をドラッグして移動できます。端をドラッグするとサイズを変更できます。\n右クリック：各種切り替え・設定・終了\nダブルクリック／F2：設定を開く\n通知領域のアイコンをダブルクリック：前面へ復帰・透過を解除", 122));
            return page;
        }

        private void SaveClick(object sender, EventArgs e)
        {
            AppSettings result = _original.Copy();
            result.Use24Hour = _hour24.Checked;
            result.ShowSeconds = _seconds.Checked;
            result.ShowDate = _date.Checked;
            result.DateFormat = _dateFormat.Text;
            result.FontName = _font.Text;
            result.FontSize = (int)_fontSize.Value;
            result.ForegroundColor = ColorTranslator.ToHtml(_textColor.BackColor);
            result.BackgroundColor = ColorTranslator.ToHtml(_backgroundColor.BackColor);
            result.AccentColor = ColorTranslator.ToHtml(_accentColor.BackColor);
            result.OpacityPercent = (int)_opacity.Value;
            result.WindowMode = _mode.SelectedIndex == 0 ? "top" : _mode.SelectedIndex == 2 ? "bottom" : "normal";
            result.ShowInTaskbar = _taskbar.Checked;
            result.LockPosition = _lock.Checked;
            result.ClickThrough = _through.Checked;
            result.StartWithWindows = _startup.Checked;
            result.Width = (int)_width.Value;
            result.Height = (int)_height.Value;
            if (_resetPosition)
            {
                Rectangle area = Screen.PrimaryScreen.WorkingArea;
                result.X = area.Right - result.Width - 18;
                result.Y = area.Top + 22;
            }
            result.Normalize();
            Result = result;
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
