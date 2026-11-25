// All user-visible texts. English by default, German when Windows is set to German (or "Language": "de").

using System.Runtime.InteropServices;

namespace MouseBattery
{
    static class T
    {
        [DllImport("kernel32.dll")] static extern ushort GetUserDefaultUILanguage();

        public static bool De;

        public static void Init(string setting)
        {
            setting = (setting ?? "auto").Trim().ToLowerInvariant();
            if (setting == "de") De = true;
            else if (setting == "en") De = false;
            else De = (GetUserDefaultUILanguage() & 0x3FF) == 0x07; // primary language German
        }

        public static string Charging => De ? "Lädt gerade." : "Charging.";
        public static string GettingLow => De ? "Wird knapp." : "Getting low.";
        public static string ChargeSoon => De ? "Bald laden." : "Charge soon.";
        public static string AlmostEmpty(string name, int p) => De ? $"{name} fast leer: {p} %" : $"{name} almost empty: {p} %";
        public static string ChargeNow => De ? "Bitte jetzt laden." : "Please charge now.";
        public static string Asleep => De ? "Maus schläft – bewegen, dann kommt der Wert." : "Mouse is asleep – move it to get the value.";
        public static string NoMouse => De ? "Keine G-Wolves-Maus gefunden." : "No G-Wolves mouse found.";
        public static string NoMouseHint => De ? "Empfänger oder Kabel einstecken." : "Plug in the receiver or cable.";

        public static string InstallQuestion => De
            ? "Mouse Battery installieren?\n\nEs läuft unsichtbar im Hintergrund, startet mit Windows und meldet den Akkustand deiner G-Wolves-Maus.\n\nKeine Admin-Rechte nötig. Entfernen über Einstellungen → Apps."
            : "Install Mouse Battery?\n\nIt runs invisibly in the background, starts with Windows and tells you the battery level of your G-Wolves mouse.\n\nNo admin rights needed. Remove it any time via Settings → Apps.";
        public static string Installed => De ? "Installiert – läuft jetzt im Hintergrund." : "Installed – now running in the background.";
        public static string UninstallQuestion => De
            ? "Mouse Battery entfernen?\n\nAutostart, Programm und Einstellungen werden gelöscht."
            : "Remove Mouse Battery?\n\nAutostart, program and settings will be deleted.";
        public static string Uninstalled => De ? "Mouse Battery wurde entfernt." : "Mouse Battery has been removed.";
        public static string Failed(string msg) => De ? "Das hat nicht geklappt:\n" + msg : "That did not work:\n" + msg;
    }
}
