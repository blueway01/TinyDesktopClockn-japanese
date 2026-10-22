// Standalone Windows GUI verification harness. Compile with Program.cs,
// ClockForm.cs and SettingsForm.cs, selecting this type as the entry point.
using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace TinyDesktopClock
{
    internal static class IntegrationTests
    {
        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hwnd, int index);
        [DllImport("user32.dll")]
        private static extern IntPtr GetShellWindow();
        [DllImport("user32.dll")]
        private static extern IntPtr GetWindow(IntPtr hwnd, uint command);
        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TRANSPARENT = 0x20;

        private static void Require(bool value, string message)
        {
            if (!value) throw new Exception("ASSERT FAILED: " + message);
            Console.WriteLine("PASS: " + message);
        }

        [STAThread]
        public static int Main(string[] args)
        {
            if (args.Length != 2)
            {
                Console.Error.WriteLine("Usage: ClockIntegrationTests.exe <phase1|phase2> <settings path>");
                return 2;
            }
            try
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                SettingsStore.CustomFilePath = Path.GetFullPath(args[1]);
                TestPeakTrafficRecord();
                if (args[0] == "phase1") Phase1();
                else if (args[0] == "phase2") Phase2();
                else throw new ArgumentException("Unknown phase");
                Console.WriteLine("INTEGRATION " + args[0] + " OK");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex);
                return 1;
            }
        }

        private static void Pump() { Application.DoEvents(); }

        private static void TestPeakTrafficRecord()
        {
            PeakTrafficRecord peak = new PeakTrafficRecord();
            peak.Update(new TrafficReading { Status = "測定準備中", DownloadMbps = 99, UploadMbps = 88 });
            Require(peak.DownloadMbps == 0 && peak.UploadMbps == 0, "preparing sample is excluded from peak values");

            peak.Update(new TrafficReading { Status = "接続中", DownloadMbps = 1, UploadMbps = 4 });
            peak.Update(new TrafficReading { Status = "接続中", DownloadMbps = 5, UploadMbps = 2 });
            peak.Update(new TrafficReading { Status = "接続中", DownloadMbps = 3, UploadMbps = 8 });
            Require(peak.DownloadMbps == 5 && peak.UploadMbps == 8, "download/upload peak values update independently");

            peak.Update(new TrafficReading { Status = "Wi-Fi 未接続", DownloadMbps = 100, UploadMbps = 100 });
            peak.Update(new TrafficReading { Status = "接続中", DownloadMbps = double.NaN, UploadMbps = double.PositiveInfinity });
            Require(peak.DownloadMbps == 5 && peak.UploadMbps == 8, "invalid and disconnected samples preserve peak values");
        }

        private static void CheckTray(ClockForm form)
        {
            FieldInfo field = typeof(ClockForm).GetField("_tray", BindingFlags.NonPublic | BindingFlags.Instance);
            NotifyIcon tray = (NotifyIcon)field.GetValue(form);
            Require(tray.Visible, "notification area icon remains enabled");
            Require(tray.ContextMenuStrip != null, "notification area context menu is available");
        }

        private static void Restore(ClockForm form)
        {
            MethodInfo method = typeof(ClockForm).GetMethod("RestoreToForeground", BindingFlags.NonPublic | BindingFlags.Instance);
            method.Invoke(form, null);
            Pump();
        }

        private static void EnsureOnScreen(ClockForm form)
        {
            Rectangle area = Screen.PrimaryScreen.WorkingArea;
            Rectangle overlap = Rectangle.Intersect(area, form.Bounds);
            Require(overlap.Width >= 60 && overlap.Height >= 30, "clock visible on a connected monitor");
        }

        private static void TestAutostart()
        {
            const string path = @"Software\Microsoft\Windows\CurrentVersion\Run";
            const string name = "TinyDesktopClockn";
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(path, true))
            {
                Require(key != null, "startup registry is accessible");
                object original = key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                RegistryValueKind kind = original == null ? RegistryValueKind.String : key.GetValueKind(name);
                try
                {
                    StartupManager.SetEnabled(true);
                    string command = key.GetValue(name) as string;
                    Require(command != null && command.StartsWith("\"" + Application.ExecutablePath + "\"", StringComparison.OrdinalIgnoreCase),
                        "autostart command correctly quotes Japanese/space path");
                    StartupManager.SetEnabled(false);
                    Require(key.GetValue(name) == null, "autostart can be disabled");
                }
                finally
                {
                    if (original == null) key.DeleteValue(name, false);
                    else key.SetValue(name, original, kind);
                }
            }
        }

        private static void Phase1()
        {
            AppSettings seed = AppSettings.Default();
            seed.WindowMode = "top";
            seed.X = 100000;
            seed.Y = 100000;
            seed.FontName = "Yu Gothic UI";
            seed.DateFormat = "yyyy年M月d日 (ddd)";
            SettingsStore.Save(seed);

            using (ClockForm form = new ClockForm(SettingsStore.Load(), false))
            {
                form.Show();
                Pump();
                EnsureOnScreen(form);
                Require(form.TopMost, "always-on-top applied");
                Require(!form.ShowInTaskbar, "taskbar hidden mode applied");
                CheckTray(form);

                AppSettings changed = SettingsStore.Load();
                changed.X = form.Left;
                changed.Y = form.Top;
                changed.WindowMode = "normal";
                changed.ShowInTaskbar = true;
                changed.Width = 355;
                changed.Height = 118;
                changed.OpacityPercent = 75;
                changed.ForegroundColor = "#FFCC88";
                changed.BackgroundColor = "#193048";
                changed.AccentColor = "#55AA66";
                changed.Use24Hour = false;
                changed.ShowSeconds = false;
                changed.ShowDate = false;
                changed.LockPosition = true;
                form.ApplySettings(changed, true);
                Pump();
                Require(!form.TopMost && form.ShowInTaskbar, "normal z-order and taskbar on");
                Require(Math.Abs(form.Opacity - 0.75) < 0.02, "opacity is applied");
                Require(form.Width == 355 && form.Height == 118, "window dimensions are applied");

                changed.WindowMode = "bottom";
                changed.ClickThrough = true;
                form.ApplySettings(changed, true);
                Pump();
                Require(!form.TopMost, "back mode not topmost");
                Require((GetWindowLong(form.Handle, GWL_EXSTYLE) & WS_EX_TRANSPARENT) != 0,
                    "click-through window style enabled");
                CheckTray(form);
                IntPtr shell = GetShellWindow();
                if (shell != IntPtr.Zero)
                    Require(GetWindow(form.Handle, 2) == shell, "background clock stays directly above desktop shell");

                Restore(form);
                Require(form.TopMost, "tray recovery puts clock on top");
                Require((GetWindowLong(form.Handle, GWL_EXSTYLE) & WS_EX_TRANSPARENT) == 0,
                    "tray recovery disables click-through");

                changed.WindowMode = "bottom";
                changed.ClickThrough = true;
                form.ApplySettings(changed, true);
                Pump();
                form.Close();
            }

            AppSettings loaded = SettingsStore.Load();
            Require(loaded.WindowMode == "bottom" && loaded.ClickThrough && loaded.ShowInTaskbar,
                "background/click-through/taskbar settings saved at app shutdown");
            Require(!loaded.ShowDate && !loaded.ShowSeconds && !loaded.Use24Hour,
                "date/time controls persisted");
            Require(loaded.Width == 355 && loaded.Height == 118 && loaded.OpacityPercent == 75,
                "size and transparency persisted");
            Require(loaded.LockPosition && loaded.ForegroundColor == "#FFCC88" && loaded.BackgroundColor == "#193048",
                "position lock and colors persisted");
            TestAutostart();
        }

        private static void Phase2()
        {
            AppSettings loaded = SettingsStore.Load();
            Require(loaded.WindowMode == "bottom" && loaded.ClickThrough && loaded.ShowInTaskbar,
                "new process restored saved behavior");
            Require(loaded.FontName == "Yu Gothic UI" && loaded.DateFormat == "yyyy年M月d日 (ddd)",
                "new process restored Japanese settings values");
            using (ClockForm form = new ClockForm(loaded, false))
            {
                form.Show();
                Pump();
                CheckTray(form);
                Require((GetWindowLong(form.Handle, GWL_EXSTYLE) & WS_EX_TRANSPARENT) != 0,
                    "click-through effective immediately after relaunch");
                EnsureOnScreen(form);
                Restore(form);
                Require(form.TopMost, "new process can recover clock from tray");
                form.Close();
            }
            Require(SettingsStore.Load().WindowMode == "top" && !SettingsStore.Load().ClickThrough,
                "recovery state saved on exit");
        }
    }
}
