// Install / uninstall for the current user only (no admin rights):
//  - copies the .exe to %LOCALAPPDATA%\Programs\MouseBattery
//  - autostart via HKCU\...\Run
//  - entry in Settings → Apps (HKCU\...\Uninstall), so it can be removed like any other app

using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace MouseBattery
{
    static class Installer
    {
        public static string Version => typeof(Installer).Assembly.GetName().Version?.ToString(3) ?? "?";

        static readonly string InstallDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "MouseBattery");
        static readonly string InstalledExe = Path.Combine(InstallDir, "MouseBattery.exe");
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string UninstallKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\MouseBattery";

        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int MessageBoxW(IntPtr h, string text, string caption, uint type);
        const uint MB_YESNO = 0x4, MB_OK = 0x0, MB_ICONQUESTION = 0x20, MB_ICONINFO = 0x40, MB_ICONERROR = 0x10;
        const int IDYES = 6;

        static string CurrentExe => Environment.ProcessPath;

        public static bool RunningFromInstallDir() =>
            string.Equals(Path.GetFullPath(CurrentExe), Path.GetFullPath(InstalledExe), StringComparison.OrdinalIgnoreCase);

        public static bool Install(bool silent)
        {
            if (!silent && MessageBoxW(IntPtr.Zero, T.InstallQuestion, Program.AppName, MB_YESNO | MB_ICONQUESTION) != IDYES) return false;
            try
            {
                if (!RunningFromInstallDir())
                {
                    StopOthers();
                    Directory.CreateDirectory(InstallDir);
                    File.Copy(CurrentExe, InstalledExe, true);
                }
                using (var run = Registry.CurrentUser.CreateSubKey(RunKey))
                    run.SetValue("MouseBattery", $"\"{InstalledExe}\" --background");

                using (var u = Registry.CurrentUser.CreateSubKey(UninstallKey))
                {
                    u.SetValue("DisplayName", Program.AppName);
                    u.SetValue("DisplayVersion", Version);
                    u.SetValue("Publisher", "Mouse Battery (open source)");
                    u.SetValue("DisplayIcon", InstalledExe);
                    u.SetValue("InstallLocation", InstallDir);
                    u.SetValue("UninstallString", $"\"{InstalledExe}\" --uninstall");
                    u.SetValue("QuietUninstallString", $"\"{InstalledExe}\" --uninstall --silent");
                    u.SetValue("NoModify", 1, RegistryValueKind.DWord);
                    u.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                    u.SetValue("EstimatedSize", (int)(new FileInfo(InstalledExe).Length / 1024), RegistryValueKind.DWord);
                }

                Program.Log("Installed to " + InstallDir);
                if (!RunningFromInstallDir() || silent)
                    Process.Start(new ProcessStartInfo(InstalledExe, "--background") { UseShellExecute = false, WorkingDirectory = InstallDir });
                if (!silent) MessageBoxW(IntPtr.Zero, T.Installed, Program.AppName, MB_OK | MB_ICONINFO);
                return true;
            }
            catch (Exception e)
            {
                Program.Log("Install failed: " + e);
                if (!silent) MessageBoxW(IntPtr.Zero, T.Failed(e.Message), Program.AppName, MB_OK | MB_ICONERROR);
                return false;
            }
        }

        public static bool Uninstall(bool silent)
        {
            if (!silent && MessageBoxW(IntPtr.Zero, T.UninstallQuestion, Program.AppName, MB_YESNO | MB_ICONQUESTION) != IDYES) return false;
            try
            {
                StopOthers();
                // Remove its notifications from the notification center too: nothing is left behind.
                try { Windows.UI.Notifications.ToastNotificationManager.History.Clear(Program.AppId); } catch { }
                using (var run = Registry.CurrentUser.OpenSubKey(RunKey, true)) run?.DeleteValue("MouseBattery", false);
                Registry.CurrentUser.DeleteSubKeyTree(UninstallKey, false);
                Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\AppUserModelId\" + Program.AppId, false);
                Program.UnregisterClickScheme();

                // The running .exe cannot delete itself: let cmd remove the folders a moment after we exit.
                string cmd = $"/c timeout /t 2 /nobreak >nul & rmdir /s /q \"{InstallDir}\" & rmdir /s /q \"{Program.DataDir}\"";
                Process.Start(new ProcessStartInfo("cmd.exe", cmd) { CreateNoWindow = true, UseShellExecute = false, WorkingDirectory = Path.GetTempPath() });
                if (!silent) MessageBoxW(IntPtr.Zero, T.Uninstalled, Program.AppName, MB_OK | MB_ICONINFO);
                return true;
            }
            catch (Exception e)
            {
                if (!silent) MessageBoxW(IntPtr.Zero, T.Failed(e.Message), Program.AppName, MB_OK | MB_ICONERROR);
                return false;
            }
        }

        static void StopOthers()
        {
            int me = Environment.ProcessId;
            foreach (var p in Process.GetProcessesByName("MouseBattery"))
            {
                try { if (p.Id != me) { p.Kill(); p.WaitForExit(3000); } } catch { }
            }
        }
    }
}
