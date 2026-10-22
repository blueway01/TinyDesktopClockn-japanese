using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace TinyDesktopClock
{
    internal static class Program
    {
        [DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();

        [STAThread]
        private static int Main(string[] args)
        {
            if (args.Length > 0 && args[0] == "--self-test")
                return SelfTest.Run();

            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] != "--settings-file") continue;
                if (++i >= args.Length || String.IsNullOrWhiteSpace(args[i]))
                {
                    MessageBox.Show("--settings-file にはファイルパスが必要です。", "ミニデスクトップ時計");
                    return 2;
                }
                SettingsStore.CustomFilePath = Path.GetFullPath(args[i]);
            }

            try { SetProcessDPIAware(); } catch { }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            bool createdNew;
            using (Mutex mutex = new Mutex(true, @"Local\TinyDesktopClockn-6B7ED347", out createdNew))
            {
                if (!createdNew)
                {
                    if (Array.IndexOf(args, "--smoke-test") >= 0) return 3;
                    if (args.Length == 0 || args[0] != "--startup")
                        MessageBox.Show("時計は既に起動しています。画面または通知領域（トレイ）の時計アイコンを確認してください。", "ミニデスクトップ時計", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return 0;
                }

                Application.ThreadException += delegate(object sender, System.Threading.ThreadExceptionEventArgs e)
                {
                    Log.Write(e.Exception);
                    MessageBox.Show("予期しない問題が発生しました。詳細はエラーログに保存しました。\n" + e.Exception.Message,
                        "ミニデスクトップ時計", MessageBoxButtons.OK, MessageBoxIcon.Error);
                };
                AppDomain.CurrentDomain.UnhandledException += delegate(object sender, UnhandledExceptionEventArgs e)
                {
                    Log.Write(e.ExceptionObject as Exception);
                };

                try
                {
                    AppSettings settings = SettingsStore.Load();
                    bool smokeTest = Array.IndexOf(args, "--smoke-test") >= 0;
                    using (ClockForm form = new ClockForm(settings, smokeTest))
                    {
                        if (smokeTest)
                        {
                            System.Windows.Forms.Timer smokeTimer = new System.Windows.Forms.Timer();
                            smokeTimer.Interval = 800;
                            int stage = 0;
                            smokeTimer.Tick += delegate
                            {
                                try
                                {
                                    if (stage == 0)
                                    {
                                        SavePreview(form, "preview-clock.png");
                                        using (SettingsForm previewDialog = new SettingsForm(settings.Copy()))
                                        {
                                            previewDialog.Show();
                                            Application.DoEvents();
                                            SavePreview(previewDialog, "preview-settings.png");
                                            previewDialog.Close();
                                        }
                                        AppSettings temporary = settings.Copy();
                                        temporary.WindowMode = "bottom";
                                        temporary.ShowInTaskbar = true;
                                        temporary.ClickThrough = false;
                                        form.ApplySettings(temporary, false);
                                    }
                                    else if (stage == 1)
                                    {
                                        AppSettings temporary = settings.Copy();
                                        temporary.WindowMode = "top";
                                        temporary.ShowInTaskbar = false;
                                        temporary.ClickThrough = true;
                                        form.ApplySettings(temporary, false);
                                    }
                                    else
                                    {
                                        smokeTimer.Stop();
                                        smokeTimer.Dispose();
                                        form.Close();
                                    }
                                    stage++;
                                }
                                catch (Exception ex)
                                {
                                    Log.Write(ex);
                                    Environment.ExitCode = 1;
                                    smokeTimer.Stop();
                                    form.Close();
                                }
                            };
                            form.Shown += delegate { smokeTimer.Start(); };
                        }
                        Application.Run(form);
                    }
                    return Environment.ExitCode;
                }
                catch (Exception ex)
                {
                    Log.Write(ex);
                    MessageBox.Show("時計を起動できませんでした。\n" + ex.Message, "ミニデスクトップ時計",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return 1;
                }
            }
        }

        private static void SavePreview(Form form, string filename)
        {
            using (Bitmap bitmap = new Bitmap(form.Width, form.Height))
            {
                form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                // Screenshots for diagnostics are best-effort. Read-only install
                // folders must not break the clock's ordinary operation.
                try { bitmap.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, filename), ImageFormat.Png); }
                catch (UnauthorizedAccessException) { }
                catch (IOException) { }
            }
        }
    }

    [DataContract]
    internal sealed class AppSettings
    {
        [DataMember] public bool Use24Hour { get; set; }
        [DataMember] public bool ShowSeconds { get; set; }
        [DataMember] public bool ShowDate { get; set; }
        [DataMember] public string DateFormat { get; set; }
        [DataMember] public string FontName { get; set; }
        [DataMember] public int FontSize { get; set; }
        [DataMember] public string ForegroundColor { get; set; }
        [DataMember] public string BackgroundColor { get; set; }
        [DataMember] public string AccentColor { get; set; }
        [DataMember] public int OpacityPercent { get; set; }
        [DataMember] public string WindowMode { get; set; }
        [DataMember] public bool ShowInTaskbar { get; set; }
        [DataMember] public bool LockPosition { get; set; }
        [DataMember] public bool ClickThrough { get; set; }
        [DataMember] public bool StartWithWindows { get; set; }
        [DataMember] public int X { get; set; }
        [DataMember] public int Y { get; set; }
        [DataMember] public int Width { get; set; }
        [DataMember] public int Height { get; set; }

        // Legacy or partially written JSON can omit fields. Initialize absent
        // members with genuine application defaults before reading the JSON.
        [OnDeserializing]
        private void InitializeMissingMembers(StreamingContext context)
        {
            AppSettings defaults = Default();
            Use24Hour = defaults.Use24Hour;
            ShowSeconds = defaults.ShowSeconds;
            ShowDate = defaults.ShowDate;
            DateFormat = defaults.DateFormat;
            FontName = defaults.FontName;
            FontSize = defaults.FontSize;
            ForegroundColor = defaults.ForegroundColor;
            BackgroundColor = defaults.BackgroundColor;
            AccentColor = defaults.AccentColor;
            OpacityPercent = defaults.OpacityPercent;
            WindowMode = defaults.WindowMode;
            ShowInTaskbar = defaults.ShowInTaskbar;
            LockPosition = defaults.LockPosition;
            ClickThrough = defaults.ClickThrough;
            StartWithWindows = defaults.StartWithWindows;
            X = defaults.X;
            Y = defaults.Y;
            Width = defaults.Width;
            Height = defaults.Height;
        }

        public static AppSettings Default()
        {
            return new AppSettings
            {
                Use24Hour = true,
                ShowSeconds = true,
                ShowDate = true,
                DateFormat = "yyyy/MM/dd (ddd)",
                FontName = "Consolas",
                FontSize = 40,
                ForegroundColor = "#EAF3FF",
                BackgroundColor = "#172133",
                AccentColor = "#65E1C1",
                OpacityPercent = 94,
                WindowMode = "top",
                ShowInTaskbar = false,
                LockPosition = false,
                ClickThrough = false,
                StartWithWindows = false,
                X = int.MinValue,
                Y = int.MinValue,
                Width = 250,
                Height = 112
            };
        }

        public AppSettings Copy()
        {
            return (AppSettings)MemberwiseClone();
        }

        public void Normalize()
        {
            FontSize = Math.Max(18, Math.Min(100, FontSize));
            Width = Math.Max(210, Math.Min(1400, Width));
            Height = Math.Max(96, Math.Min(450, Height));
            OpacityPercent = Math.Max(30, Math.Min(100, OpacityPercent));
            if (String.IsNullOrWhiteSpace(FontName)) FontName = "Consolas";
            if (String.IsNullOrWhiteSpace(DateFormat) || DateFormat.Length > 70) DateFormat = "yyyy/MM/dd (ddd)";
            if (WindowMode != "top" && WindowMode != "normal" && WindowMode != "bottom") WindowMode = "normal";
            ForegroundColor = ColorTools.Normalize(ForegroundColor, "#EAF3FF");
            BackgroundColor = ColorTools.Normalize(BackgroundColor, "#172133");
            AccentColor = ColorTools.Normalize(AccentColor, "#65E1C1");
        }
    }

    internal static class SettingsStore
    {
        public static string DirectoryPath { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TinyDesktopClockn"); } }
        public static string CustomFilePath { get; set; }
        public static string FilePath { get { return String.IsNullOrEmpty(CustomFilePath) ? Path.Combine(DirectoryPath, "settings.json") : CustomFilePath; } }

        public static AppSettings Load()
        {
            try
            {
                if (!File.Exists(FilePath))
                {
                    if (File.Exists(FilePath + ".bak")) return LoadFrom(FilePath + ".bak");
                    return AppSettings.Default();
                }
                return LoadFrom(FilePath);
            }
            catch (Exception ex)
            {
                Log.Write(ex);
                try
                {
                    string backup = FilePath + ".bak";
                    if (File.Exists(backup)) return LoadFrom(backup);
                }
                catch (Exception backupError) { Log.Write(backupError); }
                return AppSettings.Default();
            }
        }

        public static AppSettings LoadFrom(string path)
        {
            using (FileStream stream = File.OpenRead(path))
            {
                // DataContractJsonSerializer on .NET Framework expects JSON at
                // byte zero; skip a UTF-8 BOM from manually edited files.
                if (stream.Length >= 3)
                {
                    int b1 = stream.ReadByte(), b2 = stream.ReadByte(), b3 = stream.ReadByte();
                    if (b1 != 0xEF || b2 != 0xBB || b3 != 0xBF) stream.Position = 0;
                }
                DataContractJsonSerializer json = new DataContractJsonSerializer(typeof(AppSettings));
                AppSettings settings = (AppSettings)json.ReadObject(stream);
                if (settings == null) throw new InvalidDataException("設定ファイルが空です。");
                settings.Normalize();
                return settings;
            }
        }

        public static void Save(AppSettings settings)
        {
            SaveTo(FilePath, settings);
        }

        public static void SaveTo(string path, AppSettings settings)
        {
            settings.Normalize();
            string directory = Path.GetDirectoryName(Path.GetFullPath(path));
            Directory.CreateDirectory(directory);
            string temp = path + ".tmp";
            using (FileStream stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                DataContractJsonSerializer json = new DataContractJsonSerializer(typeof(AppSettings));
                json.WriteObject(stream, settings);
                stream.Flush(true);
            }
            if (File.Exists(path))
            {
                try { File.Replace(temp, path, path + ".bak", true); }
                catch (PlatformNotSupportedException) { File.Copy(temp, path, true); File.Delete(temp); }
                catch (IOException) { File.Copy(temp, path, true); File.Delete(temp); }
            }
            else File.Move(temp, path);
        }
    }

    internal static class ClockFormatter
    {
        public static string Time(DateTime now, AppSettings settings)
        {
            string format = settings.Use24Hour ? (settings.ShowSeconds ? "HH:mm:ss" : "HH:mm") :
                (settings.ShowSeconds ? "hh:mm:ss tt" : "hh:mm tt");
            return now.ToString(format, CultureInfo.InvariantCulture);
        }

        public static string Date(DateTime now, AppSettings settings)
        {
            try { return now.ToString(settings.DateFormat, CultureInfo.CurrentCulture); }
            catch (FormatException) { return now.ToString("yyyy/MM/dd (ddd)", CultureInfo.CurrentCulture); }
        }
    }

    internal static class ColorTools
    {
        public static string Normalize(string html, string fallback)
        {
            try
            {
                if (String.IsNullOrWhiteSpace(html)) return fallback;
                Color color = ColorTranslator.FromHtml(html);
                return ColorTranslator.ToHtml(color);
            }
            catch { return fallback; }
        }

        public static Color Parse(string html, string fallback)
        {
            return ColorTranslator.FromHtml(Normalize(html, fallback));
        }
    }

    internal static class StartupManager
    {
        private const string RegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string EntryName = "TinyDesktopClockn";

        public static void EnsureEnabled()
        {
            string expected = "\"" + Application.ExecutablePath + "\" --startup";
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RegistryPath, false))
            {
                if (key != null && String.Equals(key.GetValue(EntryName) as string, expected, StringComparison.OrdinalIgnoreCase))
                    return;
            }
            SetEnabled(true);
        }

        public static void SetEnabled(bool enabled)
        {
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RegistryPath, true))
            {
                if (key == null) throw new InvalidOperationException("自動起動の設定を開けませんでした。");
                if (enabled)
                {
                    string command = "\"" + Application.ExecutablePath + "\" --startup";
                    key.SetValue(EntryName, command, RegistryValueKind.String);
                }
                else key.DeleteValue(EntryName, false);
            }
        }
    }

    internal static class Log
    {
        public static void Write(Exception error)
        {
            try
            {
                Directory.CreateDirectory(SettingsStore.DirectoryPath);
                File.AppendAllText(Path.Combine(SettingsStore.DirectoryPath, "error.log"),
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + "\r\n" +
                    (error == null ? "Unknown error" : error.ToString()) + "\r\n\r\n");
            }
            catch { }
        }
    }

    internal static class SelfTest
    {
        private static void Check(bool condition, string description)
        {
            if (!condition) throw new Exception("Failed: " + description);
        }

        public static int Run()
        {
            string root = Path.Combine(Path.GetTempPath(), "TinyClock-試験 space-" + Guid.NewGuid().ToString("N"));
            try
            {
                AppSettings settings = AppSettings.Default();
                DateTime sample = new DateTime(2026, 10, 11, 23, 59, 58);
                Check(ClockFormatter.Time(sample, settings) == "23:59:58", "24-hour format");
                settings.Use24Hour = false;
                Check(ClockFormatter.Time(sample, settings) == "11:59:58 PM", "12-hour format");
                settings.ShowSeconds = false;
                Check(ClockFormatter.Time(sample, settings) == "11:59 PM", "hide seconds");
                settings.WindowMode = "bottom";
                settings.ShowInTaskbar = true;
                settings.ClickThrough = true;
                settings.FontName = "Yu Gothic UI";
                settings.Width = 333;
                string file = Path.Combine(root, "日本語 設定.json");
                SettingsStore.SaveTo(file, settings);
                AppSettings loaded = SettingsStore.LoadFrom(file);
                Check(loaded.WindowMode == "bottom" && loaded.ShowInTaskbar && loaded.ClickThrough, "persist flags");
                Check(loaded.Width == 333 && loaded.FontName == "Yu Gothic UI", "persist text and size");
                Check(loaded.ShowSeconds == false && loaded.Use24Hour == false, "persist time switches");
                loaded.Width = -1;
                loaded.OpacityPercent = 999;
                loaded.ForegroundColor = "bad-color!!!";
                loaded.Normalize();
                Check(loaded.Width == 210 && loaded.OpacityPercent == 100, "clamp ranges");
                Check(loaded.ForegroundColor == "#EAF3FF", "invalid color fallback");
                string partial = Path.Combine(root, "古い設定.json");
                File.WriteAllText(partial, "{\"ShowSeconds\":false}", System.Text.Encoding.UTF8);
                AppSettings migrated = SettingsStore.LoadFrom(partial);
                Check(!migrated.ShowSeconds && migrated.Use24Hour && migrated.ShowDate &&
                    migrated.Width == 250 && migrated.OpacityPercent == 94 &&
                    migrated.X == int.MinValue, "missing JSON members keep defaults");
                string previousPath = SettingsStore.CustomFilePath;
                try
                {
                    SettingsStore.CustomFilePath = Path.Combine(root, "バックアップ.json");
                    SettingsStore.Save(settings);
                    File.Copy(SettingsStore.FilePath, SettingsStore.FilePath + ".bak");
                    File.Delete(SettingsStore.FilePath);
                    Check(SettingsStore.Load().WindowMode == "bottom", "recover missing primary from backup");
                }
                finally { SettingsStore.CustomFilePath = previousPath; }
                Console.WriteLine("SELF-TEST OK (formats, Japanese paths, JSON roundtrip, settings validation)");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.ToString());
                Log.Write(ex);
                return 1;
            }
            finally
            {
                try { if (Directory.Exists(root)) Directory.Delete(root, true); } catch { }
            }
        }
    }
}
