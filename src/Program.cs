// Mouse Battery: shows the battery level of G-Wolves mice as Windows notifications.
// Several mice at once are fine; each one gets its own notification with its name.
//
// Rules (per mouse):
//  - First answer after Windows starts: "HSK Pro V2: 68 %" – immediately, even in fullscreen, silent.
//  - Then every hour (InfoEveryHours):
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
        public int CheckIntervalSeconds { get; set; } = 10;
        public double InfoEveryHours { get; set; } = 1;
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
        // Clicking a fresh notification opens this page in your browser. "ask" = the first click lets you
        // pick one of the official G-Wolves pages, which is then saved here. Empty = never open a page.
        // The app itself never goes online.
        public string DriverUrl { get; set; } = "ask";
        // Set after the one-time first-start demo (and the battery saver question) has run.
        public bool SetupDone { get; set; } = false;
        // Clicking a notification younger than this opens DriverUrl. Clicking an older one
        // (e.g. later in the notification center) shows a fresh notification with the current level instead.
        public int FreshSeconds { get; set; } = 60;
        // While charging: a notification every this many percent (0 = off).
        // Starts at least one full step above the level you plugged in at: 18 % -> 30, 40, ... 100 %.
        public int ChargeStepPercent { get; set; } = 10;
        // While charging: at this level a notification says "unplug now" (stays until OK). 0 = off (charge to 100 %).
        public int ChargeLimitPercent { get; set; } = 0;
        // Battery saver: keeps the battery between about 25 and 80 %, which makes it last more years.
        // Sets the charge limit to 80 % and the warnings to 40 / 30 / 25 % (right away at 15 %).
        public bool BatterySaver { get; set; } = false;
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
        public bool PendingInfo;       // hourly info waiting for a window switch (something was fullscreen)
        public int PendingInfoPercent;
        public bool PendingInfoCharging;
        public bool WasCharging;
        public int NextChargeStep;     // next level (%) that gets a notification while charging, 0 = none
        public bool LimitShown;        // "unplug now" already shown during this charge
        public bool PendingUnplug;     // "unplug now" waiting for a window switch (something was fullscreen)
        public int PendingUnplugPercent;
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
            string arg = args.Length > 0 ? args[0].ToLowerInvariant() : "";

            if (arg == "--install") return Installer.Install(silent: args.Contains("--silent")) ? 0 : 1;
            if (arg == "--uninstall") return Installer.Uninstall(silent: args.Contains("--silent")) ? 0 : 1;
            if (arg == "--test") { RegisterSender(); ShowNow(test: true); return 0; }
            if (arg == "--show" && args.Length > 1) { ShowType(args); return 0; }
            if (arg.StartsWith(ClickScheme + ":")) { RegisterSender(); Activated(arg); return 0; }

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
            RegisterClickScheme();
            Log("Started " + Installer.Version);

            // Notice window switches (Windows key opens Start, Alt+Tab). Does not read any keys.
            windowSwitchProc = WindowSwitched;
            SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, IntPtr.Zero, windowSwitchProc, 0, 0, WINEVENT_OUTOFCONTEXT);
            SetWinEventHook(EVENT_SYSTEM_SWITCHSTART, EVENT_SYSTEM_SWITCHSTART, IntPtr.Zero, windowSwitchProc, 0, 0, WINEVENT_OUTOFCONTEXT);

            if (!cfg.SetupDone) RunFirstStartDemo(save: true);

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
        // quiet = straight into the notification center, no banner (after a click opened the web page,
        // so there is always a notification left there to click).
        static void ShowNow(bool test, bool quiet = false)
        {
            suppressPopup = quiet;
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
        // Usage: --show start|wake|charging|low30|low15|low10|low5 [mouse name]
        static void ShowType(string[] args)
        {
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
                case "demo": RunFirstStartDemo(save: false); break;
                case "saveron": ShowSaverExplanation(); break;
                case "driver": ShowDriverChoice(); break;
                case "full": Show(m.Name, $"{m.Name}: 100 %", T.FullyCharged, Kind.Info); break;
                case "unplug": ShowUnplug(m, 80); break;
                case "saver40": ShowWarning(m, 1, 40); break;
                case "saver25": ShowWarning(m, 3, 25); break;
            }
            Thread.Sleep(3000); // let an own sound finish before the process ends
        }

        static void Check()
        {
            // Pick up choices saved by a click on a notification (that runs as a separate short process).
            if (ConfigWriteTime() != configTime) { LoadSettings(); Log("Settings reloaded"); }

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

                // Charging steps: plugged in at 18 % -> notifications at 30, 40, ... 100 % (or up to the charge limit)
                bool chargeStepDue = false, unplugDue = false;
                int limit = cfg.ChargeLimitPercent > 0 && cfg.ChargeLimitPercent < 100 ? cfg.ChargeLimitPercent : 100;
                if (s.Charging)
                {
                    int step = cfg.ChargeStepPercent;
                    if (!m.WasCharging)
                    {
                        m.LimitShown = false;
                        m.NextChargeStep = p >= limit ? 0 : step > 0 ? Math.Min(limit, (p + step + step - 1) / step * step) : 0;
                        Log($"{m.Name}: charging started at {p} %" + (m.NextChargeStep > 0 ? $", next notification at {m.NextChargeStep} %" : ""));
                    }
                    else if (m.NextChargeStep > 0 && p >= m.NextChargeStep && p < limit)
                    {
                        chargeStepDue = true;
                        m.NextChargeStep = Math.Min(limit, (p / step + 1) * step);
                    }
                    else if (m.NextChargeStep > 0 && p >= limit && limit == 100)
                    {
                        chargeStepDue = true;   // "Fully charged."
                        m.NextChargeStep = 0;
                    }
                    if (limit < 100 && p >= limit && !m.LimitShown) { unplugDue = true; m.LimitShown = true; m.NextChargeStep = 0; }
                }
                else { m.NextChargeStep = 0; m.PendingUnplug = false; }
                m.WasCharging = s.Charging;

                if (unplugDue)
                {
                    if (IsFullscreen() && m.StartShown)
                    {
                        Log($"{m.Name}: charge limit {p} % waits for window switch (fullscreen)");
                        m.PendingUnplug = true; m.PendingUnplugPercent = p;
                    }
                    else { ShowUnplug(m, p); m.StartShown = true; m.LastInfo = DateTime.Now; m.PendingInfo = false; }
                }
                else if (m.PendingUnplug) m.PendingUnplugPercent = p;

                // Re-arm warnings when charging or clearly above the level again (5 % gap against flicker)
                if (s.Charging || p > cfg.FirstWarningPercent + 5) { m.ShownLevel = 0; m.PendingLevel = 0; }
                else if (p > cfg.SecondWarningPercent + 5) m.ShownLevel = Math.Min(m.ShownLevel, 1);
                else if (p > cfg.ThirdWarningPercent + 5) m.ShownLevel = Math.Min(m.ShownLevel, 2);

                int level = LevelFor(p, s.Charging);
                bool repeatDue = (DateTime.Now - m.LastRepeat).TotalSeconds >= cfg.RepeatSeconds - 1;
                bool infoDue = !m.StartShown || chargeStepDue || (DateTime.Now - m.LastInfo).TotalHours >= cfg.InfoEveryHours;
                bool justWokeUp = m.LastState == MouseState.Asleep;
                string chargingText = ChargingText(s.Charging, p);

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
            foreach (var m in mice.Values.Where(x => x.PendingUnplug).ToList())
            {
                m.PendingUnplug = false;
                m.PendingInfo = false;
                m.LastInfo = DateTime.Now;
                ShowUnplug(m, m.PendingUnplugPercent);
            }
            foreach (var m in mice.Values.Where(x => x.PendingLevel > 0 || x.PendingInfo).ToList())
            {
                // A warning wins over the plain info (only the newest notification is shown)
                if (m.PendingLevel > 0) ShowWarning(m, m.PendingLevel, m.PendingPercent);
                else Show(m.Name, $"{m.Name}: {m.PendingInfoPercent} %", ChargingText(m.PendingInfoCharging, m.PendingInfoPercent), Kind.Info);
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

        static string ChargingText(bool charging, int p) => !charging ? "" : p >= 100 ? T.FullyCharged : T.Charging;

        static Kind KindFor(int level) => level >= 3 ? Kind.Urgent : level == 2 ? Kind.Warning : Kind.Info;

        static void ShowUnplug(MouseStatus m, int p) => Show(m.Name, $"{m.Name}: {p} %", T.Unplug, Kind.Urgent);

        static void ShowWarning(MouseStatus m, int level, int p)
        {
            if (level >= 3) Show(m.Name, p <= 10 ? T.AlmostEmpty(m.Name, p) : $"{m.Name}: {p} %", T.ChargeNow, Kind.Urgent);
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

        static bool suppressPopup;

        static void Show(string mouse, string title, string text, Kind kind)
        {
            try
            {
                string ownSound = OwnSoundFor(kind);
                string mute = ownSound != null ? "<audio silent=\"true\"/>" : "";
                string line2 = string.IsNullOrEmpty(text) ? "" : $"<text>{SecurityElement.Escape(text)}</text>";
                string head = $"<visual><binding template=\"ToastGeneric\"><text>{SecurityElement.Escape(title)}</text>{line2}</binding></visual>";
                string open = ClickAttribute();
                string xml = kind switch
                {
                    Kind.Info => $"<toast{open}>{head}<audio silent=\"true\"/></toast>",
                    Kind.Warning => $"<toast{open}>{head}{mute}</toast>",
                    _ => $"<toast scenario=\"reminder\"{open}>{head}<actions><action content=\"OK\" arguments=\"dismiss\" activationType=\"system\"/></actions>{mute}</toast>",
                };
                var doc = new XmlDocument();
                doc.LoadXml(xml);

                // Same tag per mouse: the new notification replaces the old one of the same mouse.
                string tag = Tag(mouse);
                try { ToastNotificationManager.History.Remove(tag, "mouse", AppId); } catch { }
                var toast = new ToastNotification(doc) { Tag = tag, Group = "mouse", SuppressPopup = suppressPopup };
                ToastNotificationManager.CreateToastNotifier(AppId).Show(toast);
                if (ownSound != null) PlaySound(ownSound, IntPtr.Zero, SND_FILENAME | SND_ASYNC | SND_NODEFAULT);
                string sound = kind == Kind.Info ? "silent" : ownSound ?? "Windows sound";
                Log($"NOTIFICATION ({kind}, {sound}): {title} {text}");
            }
            catch (Exception e) { Log("Notification failed: " + e.Message); }
        }

        // Click on the notification body (protocol activation, no COM server needed):
        // goes back to this app with the time it was shown -> fresh: open the web page, old: show the current level.
        // Without the registered scheme (not installed) it opens the page directly.
        static string ClickAttribute()
        {
            string uri = ClickSchemeRegistered()
                ? $"{ClickScheme}:click?shown={DateTimeOffset.UtcNow.ToUnixTimeSeconds()}"
                : IsWebUrl(cfg.DriverUrl) ? cfg.DriverUrl : null;
            return uri == null ? "" : $" activationType=\"protocol\" launch=\"{SecurityElement.Escape(uri)}\"";
        }

        static bool IsWebUrl(string u) => u != null && u.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

        // Notifications of the one-time setup (demo, battery saver question, web page choice).
        // Buttons = (label, mousebattery:... uri). stays = shows an OK button and stays until it is clicked.
        static void ShowSetup(string tag, string title, string text, string text2 = null,
                              (string label, string uri)[] buttons = null, bool stays = false, bool clickable = true)
        {
            try
            {
                string Esc(string s) => SecurityElement.Escape(s);
                string lines = $"<text>{Esc(title)}</text><text>{Esc(text)}</text>" + (text2 == null ? "" : $"<text>{Esc(text2)}</text>");
                string actions = buttons != null
                    ? "<actions>" + string.Concat(buttons.Select(b => $"<action content=\"{Esc(b.label)}\" activationType=\"protocol\" arguments=\"{Esc(b.uri)}\"/>")) + "</actions>"
                    : stays ? "<actions><action content=\"OK\" arguments=\"dismiss\" activationType=\"system\"/></actions>" : "";
                string scenario = actions != "" ? " scenario=\"reminder\"" : "";
                string xml = $"<toast{scenario}{(clickable ? ClickAttribute() : "")}><visual><binding template=\"ToastGeneric\">{lines}</binding></visual>{actions}<audio silent=\"true\"/></toast>";
                var doc = new XmlDocument();
                doc.LoadXml(xml);
                RemoveToast(tag);
                ToastNotificationManager.CreateToastNotifier(AppId).Show(new ToastNotification(doc) { Tag = tag, Group = "mouse" });
                Log($"NOTIFICATION (setup): {title} {text}");
            }
            catch (Exception e) { Log("Notification failed: " + e.Message); }
        }

        static void RemoveToast(string tag)
        {
            try { ToastNotificationManager.History.Remove(tag, "mouse", AppId); } catch { }
        }

        // ---------- One-time first start ----------
        // A short demo of what the notifications look like, then the battery saver question (Yes / No).
        // Asked only once; to change the answer, uninstall and install again.
        // Each demo picture stays until "OK"; OK shows the next one (mousebattery:demo?step=2|3).
        static void RunFirstStartDemo(bool save)
        {
            Log("First start: demo");
            BatteryReading r = null;
            try { r = GWolvesHid.ReadAll(cfg.MouseNames).FirstOrDefault(x => x.State == MouseState.Awake); } catch { }
            string name = r?.Name ?? T.YourMouse;
            ShowSetup("setup", $"{name}: {r?.Percent ?? 80} %", T.Demo1,
                buttons: new[] { ("OK", $"{ClickScheme}:demo?step=2") }, clickable: false);
            // The demo already showed the level: no extra start notification right after it.
            if (save && r != null) mice[r.Name] = new MouseStatus { Name = r.Name, StartShown = true, LastInfo = DateTime.Now, LastState = MouseState.Awake };
        }

        static void ShowDemoStep(long step)
        {
            if (step == 4)
            {
                // Battery saver explanation confirmed: setup over, first real battery notification.
                RemoveToast("setup");
                ShowNow(test: false);
                return;
            }
            if (step == 2)
            {
                BatteryReading r = null;
                try { r = GWolvesHid.ReadAll(cfg.MouseNames).FirstOrDefault(x => x.State == MouseState.Awake); } catch { }
                ShowSetup("setup", $"{r?.Name ?? T.YourMouse}: 28 %", T.Demo2,
                    buttons: new[] { ("OK", $"{ClickScheme}:demo?step=3") }, clickable: false);
            }
            else if (step == 3)
            {
                ShowSetup("setup", T.Demo3Title, T.Demo3Text, buttons: new[]
                {
                    (T.Yes, ClickScheme + ":setup?saver=1"),
                    (T.No, ClickScheme + ":setup?saver=0"),
                }, clickable: false);
                // From here on the setup counts as done; without an answer the app simply runs normally.
                SaveChoice("SetupDone", true);
                SaveChoice("BatterySaver", false);
            }
        }

        // OK ends the setup (step 4): then the first real battery notification comes.
        static void ShowSaverExplanation() =>
            ShowSetup("setup", T.SaverOnTitle, T.SaverOnText, T.CableTip,
                buttons: new[] { ("OK", $"{ClickScheme}:demo?step=4") }, clickable: false);

        // Official G-Wolves pages (linked from shop.g-wolves.com). The first click on a fresh notification lets you pick one.
        static (string label, string url)[] DriverPages => new[]
        {
            (T.PageWebDriver, "https://www.mouse.xyz/"),          // "G-Wolves Mouse Web Driver (New)"
            (T.PageDownloads, "https://shop.g-wolves.com/pages/download"),
            (T.PageShop, "https://shop.g-wolves.com/"),
        };

        // First time: with the hint about Ctrl. When picked again with Ctrl: without it.
        static void ShowDriverChoice(bool firstTime = true) =>
            ShowSetup("driver", T.DriverTitle, firstTime ? T.DriverText : T.DriverTextAgain,
                buttons: DriverPages.Select((p, i) => (p.label, $"{ClickScheme}:driver?pick={i}")).ToArray(), clickable: false);

        static void OpenPage(string url)
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true }); }
            catch (Exception e) { Log("Opening the page failed: " + e.Message); }
        }

        // Writes only the chosen values into config.json (keeps whatever else is in there).
        static void SaveChoice(string key, System.Text.Json.Nodes.JsonNode value)
        {
            try
            {
                System.Text.Json.Nodes.JsonObject o = null;
                if (File.Exists(ConfigFile)) o = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(ConfigFile)) as System.Text.Json.Nodes.JsonObject;
                o ??= new System.Text.Json.Nodes.JsonObject();
                o[key] = value;
                File.WriteAllText(ConfigFile, o.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
                LoadSettings();
            }
            catch (Exception e) { Log($"Saving {key} failed: " + e.Message); }
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
                // G-Wolves logo, so you see at a glance which mouse the notification is about.
                // New file name -> Windows doesn't keep showing an older cached icon.
                string icon = Path.Combine(DataDir, "notification-icon.png");
                using (var res = typeof(Program).Assembly.GetManifestResourceStream("MouseBattery.notification.png"))
                    if (res != null && (!File.Exists(icon) || new FileInfo(icon).Length != res.Length))
                        using (var f = File.Create(icon)) res.CopyTo(f);
                using var k = Registry.CurrentUser.CreateSubKey(@"Software\Classes\AppUserModelId\" + AppId);
                k.SetValue("DisplayName", AppName);
                if (File.Exists(icon)) k.SetValue("IconUri", icon);
            }
            catch (Exception e) { Log("Register sender failed: " + e.Message); }
        }

        // ---------- Notification click ----------
        public const string ClickScheme = "mousebattery";
        const string ClickSchemeKey = @"Software\Classes\" + ClickScheme;

        [DllImport("user32.dll")] static extern short GetAsyncKeyState(int key);

        static long Number(string uri, string name)
        {
            int i = uri.IndexOf(name + "=", StringComparison.Ordinal);
            if (i < 0) return -1;
            return long.TryParse(new string(uri[(i + name.Length + 1)..].TakeWhile(char.IsDigit).ToArray()), out long n) ? n : -1;
        }

        // Windows starts "MouseBattery.exe mousebattery:..." when a notification or one of its buttons is clicked:
        //   click?shown=<unix seconds>   notification body
        //   setup?saver=1|0              battery saver question (first start)
        //   driver?pick=<n>              web page choice (first click)
        static void Activated(string uri)
        {
            if (uri.Contains(":demo?"))
            {
                ShowDemoStep(Number(uri, "step"));
                return;
            }
            if (uri.Contains(":setup?"))
            {
                bool yes = Number(uri, "saver") == 1;
                Log("Battery saver question answered: " + (yes ? "yes" : "no"));
                SaveChoice("SetupDone", true);
                SaveChoice("BatterySaver", yes);
                if (yes) ShowSaverExplanation();
                else { RemoveToast("setup"); ShowNow(test: false); }   // setup over: first real battery notification
                return;
            }
            if (uri.Contains(":driver?"))
            {
                var pages = DriverPages;
                long n = Number(uri, "pick");
                if (n < 0 || n >= pages.Length) return;
                string url = pages[n].url;
                SaveChoice("DriverUrl", url);
                Log("Web page chosen (saved): " + url);
                RemoveToast("driver");
                OpenPage(url);
                ShowNow(test: false, quiet: true);
                return;
            }
            NotificationClicked(uri);
        }

        static void NotificationClicked(string uri)
        {
            long shown = Number(uri, "shown");
            long age = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - shown;
            // Ctrl held while clicking: pick the page again (e.g. when a page was shut down).
            bool ctrl = (GetAsyncKeyState(0x11) & 0x8000) != 0;

            bool neverChosen = string.Equals(cfg.DriverUrl, "ask", StringComparison.OrdinalIgnoreCase);

            if (ctrl || (shown > 0 && age <= cfg.FreshSeconds && neverChosen))
            {
                Log($"Clicked notification ({age} s old{(ctrl ? ", Ctrl held" : "")}): asking which page to open");
                ShowDriverChoice(firstTime: neverChosen);
            }
            else if (shown > 0 && age <= cfg.FreshSeconds && IsWebUrl(cfg.DriverUrl))
            {
                Log($"Clicked fresh notification ({age} s old): opening {cfg.DriverUrl}");
                OpenPage(cfg.DriverUrl);
                ShowNow(test: false, quiet: true);   // keep one notification in the notification center
            }
            else
            {
                Log($"Clicked old notification ({age} s old): showing the current level");
                ShowNow(test: false);
            }
        }

        // Per-user URL scheme, so a click comes back to this app (no admin needed). Removed on uninstall.
        static void RegisterClickScheme()
        {
            try
            {
                using var k = Registry.CurrentUser.CreateSubKey(ClickSchemeKey);
                k.SetValue("", "URL:" + AppName);
                k.SetValue("URL Protocol", "");
                using var c = k.CreateSubKey(@"shell\open\command");
                c.SetValue("", $"\"{Environment.ProcessPath}\" \"%1\"");
            }
            catch (Exception e) { Log("Register click scheme failed: " + e.Message); }
        }

        static bool ClickSchemeRegistered()
        {
            try
            {
                using var c = Registry.CurrentUser.OpenSubKey(ClickSchemeKey + @"\shell\open\command");
                string cmd = c?.GetValue("") as string;
                if (string.IsNullOrEmpty(cmd)) return false;
                string exe = cmd.TrimStart('"');
                int q = exe.IndexOf('"');
                return File.Exists(q > 0 ? exe[..q] : exe);
            }
            catch { return false; }
        }

        public static void UnregisterClickScheme()
        {
            try { Registry.CurrentUser.DeleteSubKeyTree(ClickSchemeKey, false); } catch { }
        }

        // ---------- Helpers ----------
        static DateTime configTime;
        static DateTime ConfigWriteTime() => File.Exists(ConfigFile) ? File.GetLastWriteTimeUtc(ConfigFile) : DateTime.MinValue;

        static void LoadSettings()
        {
            try
            {
                // Minimal: config.json only holds your one-time choices (and anything you add yourself).
                configTime = ConfigWriteTime();
                cfg = File.Exists(ConfigFile)
                    ? JsonSerializer.Deserialize(File.ReadAllText(ConfigFile), SettingsJson.Default.Settings) ?? new Settings()
                    : new Settings();
                cfg.MouseNames ??= new();
                if (cfg.BatterySaver)
                {
                    cfg.FirstWarningPercent = 40;
                    cfg.SecondWarningPercent = 30;
                    cfg.ThirdWarningPercent = 25;
                    cfg.ImmediatePercent = 15;
                    if (cfg.ChargeLimitPercent <= 0) cfg.ChargeLimitPercent = 80;
                }
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
