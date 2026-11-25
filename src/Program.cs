// Mouse Battery: shows the battery level of G-Wolves mice as Windows notifications.
// Several mice at once are fine; each one gets its own notification with its name.
//
// Rules (per mouse):
//  - First answer after Windows starts: "HSK Pro V2: 68 %" – immediately, even in fullscreen, silent.
//  - Then every 3 hours:
//      mouse awake, nothing fullscreen   -> immediately (silent)
//      mouse awake, something fullscreen -> waits for the next window switch (Windows key / Alt+Tab)
//      mouse asleep                      -> the moment it is woken up, immediately with sound, even in fullscreen
//    The new notification replaces the old one of the same mouse.
//  - Warnings wait for the next window switch (Windows key / Alt+Tab / clicking another window),
//    so they never pop up in the middle of a game. Only the newest one is shown:
//      30 %: silent.  15 %: with sound.  10 %: with sound, stays until "OK".
//      below 10 %: like 10 %, repeated every minute.
//      5 % and below: immediately, no matter what, every minute.
// No window, no tray icon. Settings: %LOCALAPPDATA%\MouseBattery\config.json

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using Microsoft.Win32;
using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;

namespace MouseBattery
{
    public class Settings
    {
        public string Language { get; set; } = "auto";
        public int CheckIntervalSeconds { get; set; } = 10;
        public double InfoEveryHours { get; set; } = 3;
        public int FirstWarningPercent { get; set; } = 30;
        public int SecondWarningPercent { get; set; } = 15;
        public int ThirdWarningPercent { get; set; } = 10;
        public int ImmediatePercent { get; set; } = 5;
        public int RepeatSeconds { get; set; } = 60;
        // Optional own sounds (.wav). If set, the notification itself stays silent and the app plays
        // this file instead – so it still sounds when all Windows notification sounds are muted.
        // Empty = use the normal Windows notification sound.
        public string WarningSound { get; set; } = "";
        public string UrgentSound { get; set; } = "";
        // Own names, by product ID (hex) as shown in log.txt: { "5817": "My HSK" }
        public Dictionary<string, string> MouseNames { get; set; } = new();
    }

    [JsonSourceGenerationOptions(WriteIndented = true)]
    [JsonSerializable(typeof(Settings))]
    partial class SettingsJson : JsonSerializerContext { }

    class MouseStatus
    {
        public string Name;
        public bool StartShown;
        public MouseState LastState = MouseState.NotFound;
        public DateTime LastInfo = DateTime.MinValue;
        // Levels: 0 none, 1 = 30 %, 2 = 15 %, 3 = 10 %, 4 = below 10 % (repeated), 5 = immediate (repeated)
        public int ShownLevel;
        public int PendingLevel;       // warning waiting for the next window switch
        public int PendingPercent;
        public DateTime LastRepeat = DateTime.MinValue;
        public int LastHistoryValue = -1;
        public bool PendingInfo;       // 3-hour info waiting for a window switch (something was fullscreen)
        public int PendingInfoPercent;
        public bool PendingInfoCharging;
    }

    static class Program
    {
        public const string AppName = "Mouse Battery";
        public const string AppId = "MouseBattery";
        public static readonly string DataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MouseBattery");
        static readonly string LogFile = Path.Combine(DataDir, "log.txt");
        static readonly string HistoryFile = Path.Combine(DataDir, "history.csv");
        static readonly string ConfigFile = Path.Combine(DataDir, "config.json");

        static Settings cfg = new Settings();
        static readonly Dictionary<string, MouseStatus> mice = new(StringComparer.OrdinalIgnoreCase);

        // ---------- Win32 ----------
        delegate void WinEventProc(IntPtr hook, uint evt, IntPtr hwnd, int obj, int child, uint thread, uint time);
        [DllImport("user32.dll")] static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr mod, WinEventProc proc, uint pid, uint tid, uint flags);
        [DllImport("user32.dll")] static extern IntPtr SetTimer(IntPtr hwnd, IntPtr id, uint ms, IntPtr func);
        [DllImport("user32.dll")] static extern int GetMessage(out MSG m, IntPtr hwnd, uint min, uint max);
        [DllImport("user32.dll")] static extern IntPtr DispatchMessage(ref MSG m);
        [StructLayout(LayoutKind.Sequential)] struct MSG { public IntPtr hwnd; public uint message; public IntPtr wParam; public IntPtr lParam; public uint time; public int x, y; }
        const uint EVENT_SYSTEM_FOREGROUND = 0x0003, EVENT_SYSTEM_SWITCHSTART = 0x0014, WINEVENT_OUTOFCONTEXT = 0, WM_TIMER = 0x0113;
        static WinEventProc windowSwitchProc; // keep a reference so the GC does not collect the callback

        [STAThread]
        static int Main(string[] args)
        {
            Directory.CreateDirectory(DataDir);
            LoadSettings();
            T.Init(cfg.Language);
            string arg = args.Length > 0 ? args[0].ToLowerInvariant() : "";

            if (arg == "--install") return Installer.Install(silent: args.Contains("--silent")) ? 0 : 1;
            if (arg == "--uninstall") return Installer.Uninstall(silent: args.Contains("--silent")) ? 0 : 1;
            if (arg == "--test") { RegisterSender(); ShowNow(test: true); return 0; }
            if (arg == "--show" && args.Length > 1) { ShowType(args); return 0; }

            // Started from somewhere else (e.g. Downloads): offer to install.
            if (arg != "--background" && !Installer.RunningFromInstallDir())
                return Installer.Install(silent: false) ? 0 : 1;

            using var single = new Mutex(true, @"Local\MouseBattery", out bool first);
            if (!first)
            {
                // Already running (e.g. double-clicked again): just show the current level.
                RegisterSender(); ShowNow(test: false);
                return 0;
            }

            RegisterSender();
            Log("Started " + Installer.Version);

            // Notice window switches (Windows key opens Start, Alt+Tab). Does not read any keys.
            windowSwitchProc = WindowSwitched;
            SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, IntPtr.Zero, windowSwitchProc, 0, 0, WINEVENT_OUTOFCONTEXT);
            SetWinEventHook(EVENT_SYSTEM_SWITCHSTART, EVENT_SYSTEM_SWITCHSTART, IntPtr.Zero, windowSwitchProc, 0, 0, WINEVENT_OUTOFCONTEXT);

            SetTimer(IntPtr.Zero, IntPtr.Zero, (uint)Math.Max(2, cfg.CheckIntervalSeconds) * 1000, IntPtr.Zero);
            Check();
            while (GetMessage(out MSG m, IntPtr.Zero, 0, 0) > 0)
            {
                if (m.message == WM_TIMER) Check();
                else DispatchMessage(ref m);
            }
            return 0;
        }

        // One query of all mice, one notification each.
        static void ShowNow(bool test)
        {
            var all = GWolvesHid.ReadAll(cfg.MouseNames);
            string suffix = test ? " (Test)" : "";
            if (all.Count == 0) { Log("No G-Wolves mouse found"); Show("none", T.NoMouse, T.NoMouseHint + suffix, Kind.Info); }
            foreach (var s in all)
            {
                Log((test ? "Test: " : "Now: ") + s);
                if (s.State == MouseState.Awake) Show(s.Name, $"{s.Name}: {s.Percent} %", (s.Charging ? T.Charging : "") + suffix, Kind.Info);
                else if (s.State == MouseState.Asleep) Show(s.Name, $"{s.Name}: ?", T.Asleep + suffix, Kind.Info);
                else Show(s.Name, $"{s.Name}: ?", s.Info + suffix, Kind.Info);
            }
        }

        // Shows one notification type exactly as in normal use (for screenshots / docs).
        // Usage: --show start|wake|charging|low30|low15|low10|low5 [mouse name] [--de]
        static void ShowType(string[] args)
        {
            T.De = args.Contains("--de");
            RegisterSender();
            var r = GWolvesHid.ReadAll(cfg.MouseNames).FirstOrDefault(x => x.State == MouseState.Awake);
            string name = args.Length > 2 && !args[2].StartsWith("--") ? args[2] : r?.Name ?? "HSK Pro V2";
            var m = new MouseStatus { Name = name };
            int p = r?.Percent ?? 80;
            switch (args[1].ToLowerInvariant())
            {
                case "start": Show(m.Name, $"{m.Name}: {p} %", "", Kind.Info); break;
                case "wake": Show(m.Name, $"{m.Name}: {p} %", "", Kind.Warning); break;
                case "charging": Show(m.Name, $"{m.Name}: {p} %", T.Charging, Kind.Info); break;
                case "low30": ShowWarning(m, 1, 28); break;
                case "low15": ShowWarning(m, 2, 14); break;
                case "low10": ShowWarning(m, 3, 10); break;
                case "low5": ShowWarning(m, 5, 5); break;
            }
            Thread.Sleep(3000); // let an own sound finish before the process ends
        }

        static void Check()
        {
            List<BatteryReading> all;
            try { all = GWolvesHid.ReadAll(cfg.MouseNames); }
            catch (Exception e) { Log("ERROR while reading: " + e.Message); return; }
            foreach (var s in all)
            {
                try { CheckMouse(s); }
                catch (Exception e) { Log($"ERROR ({s.Name}): " + e.Message); }
            }
        }

        static void CheckMouse(BatteryReading s)
        {
            if (!mice.TryGetValue(s.Name, out var m))
            {
                m = new MouseStatus { Name = s.Name };
                mice[s.Name] = m;
                Log($"New mouse: {s.Name} ({s.Product})");
            }

            if (s.State == MouseState.Awake)
            {
                int p = s.Percent;
                if (p != m.LastHistoryValue) { WriteHistory(s); m.LastHistoryValue = p; }

                // Re-arm warnings when charging or clearly above the level again (5 % gap against flicker)
                if (s.Charging || p > cfg.FirstWarningPercent + 5) { m.ShownLevel = 0; m.PendingLevel = 0; }
                else if (p > cfg.SecondWarningPercent + 5) m.ShownLevel = Math.Min(m.ShownLevel, 1);
                else if (p > cfg.ThirdWarningPercent + 5) m.ShownLevel = Math.Min(m.ShownLevel, 2);

                int level = LevelFor(p, s.Charging);
                bool repeatDue = (DateTime.Now - m.LastRepeat).TotalSeconds >= cfg.RepeatSeconds - 1;
                bool infoDue = !m.StartShown || (DateTime.Now - m.LastInfo).TotalHours >= cfg.InfoEveryHours;
                bool justWokeUp = m.LastState == MouseState.Asleep;
                string chargingText = s.Charging ? T.Charging : "";

                if (level == 5 && m.StartShown)
                {
                    // 5 % and below: immediately, no matter what (also replaces a due info)
                    if (repeatDue) { ShowWarning(m, level, p); m.LastInfo = DateTime.Now; m.PendingInfo = false; }
                }
                else if (infoDue && (!m.StartShown || justWokeUp || !IsFullscreen()))
                {
                    // PC start: immediately. Woken up: immediately with sound. Otherwise only when nothing is fullscreen.
                    // The info shows the level anyway; if it is low, the warning counts as shown.
                    Kind kind = KindFor(level);
                    if (m.StartShown && justWokeUp && kind == Kind.Info) kind = Kind.Warning;
                    Show(m.Name, $"{m.Name}: {p} %", chargingText, kind);
                    m.StartShown = true;
                    m.LastInfo = DateTime.Now;
                    m.PendingInfo = false;
                    MarkShown(m, level);
                }
                else if (infoDue)
                {
                    // Something is fullscreen: wait for Windows key / Alt+Tab
                    if (!m.PendingInfo) Log($"{m.Name}: info {p} % waits for window switch (fullscreen)");
                    m.PendingInfo = true; m.PendingInfoPercent = p; m.PendingInfoCharging = s.Charging;
                    if (level > m.ShownLevel) { m.PendingLevel = Math.Max(m.PendingLevel, level); m.PendingPercent = p; }
                }
                else if (level == 4 ? repeatDue : level > m.ShownLevel)
                {
                    if (m.PendingLevel == 0) Log($"{m.Name}: warning {p} % waits for window switch");
                    m.PendingLevel = Math.Max(m.PendingLevel, level); m.PendingPercent = p;
                }
                else if (m.PendingLevel > 0) m.PendingPercent = p;
            }
            else if (s.State == MouseState.Asleep)
            {
                if (m.LastState != MouseState.Asleep) Log($"{m.Name} asleep");
            }
            else if (s.State != m.LastState) Log("Mouse: " + s);

            m.LastState = s.State;
        }

        static void WindowSwitched(IntPtr hook, uint evt, IntPtr hwnd, int obj, int child, uint thread, uint time)
        {
            foreach (var m in mice.Values.Where(x => x.PendingLevel > 0 || x.PendingInfo).ToList())
            {
                // A warning wins over the plain info (only the newest notification is shown)
                if (m.PendingLevel > 0) ShowWarning(m, m.PendingLevel, m.PendingPercent);
                else Show(m.Name, $"{m.Name}: {m.PendingInfoPercent} %", m.PendingInfoCharging ? T.Charging : "", Kind.Info);
                if (m.PendingInfo) { m.PendingInfo = false; m.LastInfo = DateTime.Now; }
            }
        }

        [DllImport("shell32.dll")] static extern int SHQueryUserNotificationState(out int state);

        // True while something runs fullscreen (game, fullscreen video, presentation).
        // 2 = busy/fullscreen, 3 = Direct3D fullscreen, 4 = presentation mode
        static bool IsFullscreen()
        {
            if (SHQueryUserNotificationState(out int s) != 0) return false;
            return s == 2 || s == 3 || s == 4;
        }

        static int LevelFor(int p, bool charging)
        {
            if (charging) return 0;
            if (p <= cfg.ImmediatePercent) return 5;
            if (p < cfg.ThirdWarningPercent) return 4;
            if (p <= cfg.ThirdWarningPercent) return 3;
            if (p <= cfg.SecondWarningPercent) return 2;
            if (p <= cfg.FirstWarningPercent) return 1;
            return 0;
        }

        static Kind KindFor(int level) => level >= 3 ? Kind.Urgent : level == 2 ? Kind.Warning : Kind.Info;

        static void ShowWarning(MouseStatus m, int level, int p)
        {
            if (level >= 3) Show(m.Name, T.AlmostEmpty(m.Name, p), T.ChargeNow, Kind.Urgent);
            else if (level == 2) Show(m.Name, $"{m.Name}: {p} %", T.ChargeSoon, Kind.Warning);
            else Show(m.Name, $"{m.Name}: {p} %", T.GettingLow, Kind.Info);
            MarkShown(m, level);
        }

        static void MarkShown(MouseStatus m, int level)
        {
            m.ShownLevel = Math.Max(m.ShownLevel, level);
            m.PendingLevel = 0;
            if (level >= 4) m.LastRepeat = DateTime.Now;
        }

        // ---------- Notifications ----------
        // Info = silent, Warning = with sound, Urgent = with sound and stays until "OK"
        enum Kind { Info, Warning, Urgent }

        [DllImport("winmm.dll", CharSet = CharSet.Unicode)] static extern bool PlaySound(string file, IntPtr mod, uint flags);
        const uint SND_ASYNC = 0x0001, SND_NODEFAULT = 0x0002, SND_FILENAME = 0x00020000;

        // Own sound file for this kind, or null (= silent for Info, Windows sound otherwise)
        static string OwnSoundFor(Kind kind)
        {
            string f = kind == Kind.Urgent ? cfg.UrgentSound : kind == Kind.Warning ? cfg.WarningSound : null;
            return !string.IsNullOrWhiteSpace(f) && File.Exists(f) ? f : null;
        }

        static void Show(string mouse, string title, string text, Kind kind)
        {
            try
            {
                string ownSound = OwnSoundFor(kind);
                string mute = ownSound != null ? "<audio silent=\"true\"/>" : "";
                string line2 = string.IsNullOrEmpty(text) ? "" : $"<text>{SecurityElement.Escape(text)}</text>";
                string head = $"<visual><binding template=\"ToastGeneric\"><text>{SecurityElement.Escape(title)}</text>{line2}</binding></visual>";
                string xml = kind switch
                {
                    Kind.Info => $"<toast>{head}<audio silent=\"true\"/></toast>",
                    Kind.Warning => $"<toast>{head}{mute}</toast>",
                    _ => $"<toast scenario=\"reminder\">{head}<actions><action content=\"OK\" arguments=\"dismiss\" activationType=\"system\"/></actions>{mute}</toast>",
                };
                var doc = new XmlDocument();
                doc.LoadXml(xml);

                // Same tag per mouse: the new notification replaces the old one of the same mouse.
                string tag = Tag(mouse);
                try { ToastNotificationManager.History.Remove(tag, "mouse", AppId); } catch { }
                var toast = new ToastNotification(doc) { Tag = tag, Group = "mouse" };
                ToastNotificationManager.CreateToastNotifier(AppId).Show(toast);
                if (ownSound != null) PlaySound(ownSound, IntPtr.Zero, SND_FILENAME | SND_ASYNC | SND_NODEFAULT);
                string sound = kind == Kind.Info ? "silent" : ownSound ?? "Windows sound";
                Log($"NOTIFICATION ({kind}, {sound}): {title} {text}");
            }
            catch (Exception e) { Log("Notification failed: " + e.Message); }
        }

        // Tag may have at most 64 characters
        static string Tag(string name)
        {
            string k = new string(name.Where(char.IsLetterOrDigit).ToArray());
            return k.Length == 0 ? "mouse" : k.Length > 60 ? k[..60] : k;
        }

        // Name and icon Windows shows at the top of the notification (per-user registry key, no admin needed).
        public static void RegisterSender()
        {
            try
            {
                string icon = Path.Combine(DataDir, "icon.png");
                using (var res = typeof(Program).Assembly.GetManifestResourceStream("MouseBattery.icon.png"))
                    if (res != null && (!File.Exists(icon) || new FileInfo(icon).Length != res.Length))
                        using (var f = File.Create(icon)) res.CopyTo(f);
                using var k = Registry.CurrentUser.CreateSubKey(@"Software\Classes\AppUserModelId\" + AppId);
                k.SetValue("DisplayName", AppName);
                if (File.Exists(icon)) k.SetValue("IconUri", icon);
            }
            catch (Exception e) { Log("Register sender failed: " + e.Message); }
        }

        // ---------- Helpers ----------
        static void LoadSettings()
        {
            try
            {
                if (File.Exists(ConfigFile))
                    cfg = JsonSerializer.Deserialize(File.ReadAllText(ConfigFile), SettingsJson.Default.Settings) ?? cfg;
                else
                    File.WriteAllText(ConfigFile, JsonSerializer.Serialize(cfg, SettingsJson.Default.Settings));
                cfg.MouseNames ??= new();
            }
            catch (Exception e) { Log("config.json invalid, using defaults: " + e.Message); }
        }

        public static void Log(string text)
        {
            try
            {
                File.AppendAllText(LogFile, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {text}{Environment.NewLine}");
                if (new FileInfo(LogFile).Length > 500_000)
                {
                    var lines = File.ReadAllLines(LogFile);
                    File.WriteAllLines(LogFile, lines[(lines.Length / 2)..]);
                }
            }
            catch { }
        }

        static void WriteHistory(BatteryReading s)
        {
            try
            {
                if (!File.Exists(HistoryFile)) File.WriteAllText(HistoryFile, "Time;Mouse;Percent;Charging" + Environment.NewLine);
                File.AppendAllText(HistoryFile, $"{DateTime.Now:yyyy-MM-dd HH:mm};{s.Name};{s.Percent};{(s.Charging ? 1 : 0)}{Environment.NewLine}");
            }
            catch { }
        }
    }
}
