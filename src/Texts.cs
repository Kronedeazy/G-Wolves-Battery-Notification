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
        public static string FullyCharged => De ? "Voll geladen." : "Fully charged.";
        // One-time first start
        public static string YourMouse => De ? "Deine Maus" : "Your mouse";
        public static string Demo1 => De
            ? "Demo 1/3 · So siehst du deinen Akku: beim PC-Start und jede Stunde. Beim Laden alle 10 %."
            : "Demo 1/3 · This is how you see your battery: at PC start and every hour. While charging every 10 %.";
        public static string Demo2 => De
            ? "Demo 2/3 · Beispiel-Warnung. Warnungen warten, bis du das Spiel verlässt (Windows-Taste, Alt+Tab)."
            : "Demo 2/3 · Example warning. Warnings wait until you leave your game (Windows key, Alt+Tab).";
        public static string Demo3Title => De ? "Demo 3/3 · Akku schonen?" : "Demo 3/3 · Save your battery?";
        public static string Demo3Text => De
            ? "Hält den Akku zwischen etwa 25 und 80 % – er hält mehr Jahre. Wird nur jetzt gefragt; ändern = neu installieren."
            : "Keeps the battery between about 25 and 80 % so it lasts more years. Asked only now; to change it, reinstall.";
        public static string Yes => De ? "Ja" : "Yes";
        public static string No => De ? "Nein" : "No";
        public static string SaverOnTitle => De ? "Akku-Schonmodus ist an" : "Battery saver is on";
        public static string SaverOnText => De
            ? "Beim Laden: Meldungen bis 80 %, dann „Jetzt abstecken“. Warnungen bei 40 / 30 / 25 %, sofort ab 15 %."
            : "While charging: notifications up to 80 %, then \"Unplug now\". Warnings at 40 / 30 / 25 %, right away at 15 %.";
        public static string CableTip => De
            ? "Tipp: Lade mit einem eigenen USB-C-Kabel. Das Empfänger-Kabel bleibt stecken – kein Wackelkontakt am Dock."
            : "Tip: charge with a separate USB-C cable. The receiver cable stays plugged in – no loose contact at the dock.";
        public static string DriverTitle => De ? "Welche Seite soll sich öffnen?" : "Which page should open?";
        public static string DriverText => De
            ? "Offizielle G-Wolves-Seiten. Deine Wahl öffnet sich ab jetzt bei jedem Klick. Neu wählen: Strg halten und Meldung anklicken."
            : "Official G-Wolves pages. Your choice opens on every click from now on. To pick again: hold Ctrl and click a notification.";
        public static string DriverTextAgain => De
            ? "Offizielle G-Wolves-Seiten. Deine Wahl öffnet sich ab jetzt bei jedem Klick."
            : "Official G-Wolves pages. Your choice opens on every click from now on.";
        public static string PageWebDriver => De ? "Webtreiber" : "Web driver";
        public static string PageDownloads => "Downloads";
        public static string PageShop => "Shop";

        public static string Unplug => De ? "Jetzt abstecken – schont den Akku." : "Unplug now – saves the battery.";
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
