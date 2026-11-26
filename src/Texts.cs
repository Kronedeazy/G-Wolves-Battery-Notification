// All user-visible texts (English only).

namespace MouseBattery
{
    static class T
    {
        public const string Charging = "Charging.";
        public const string FullyCharged = "Fully charged.";

        // One-time first start
        public const string YourMouse = "Your mouse";
        public const string Demo1 = "Demo 1/3 · This is how you see your battery: at PC start and every hour. While charging every 10 %.";
        public const string Demo2 = "Demo 2/3 · Example warning. Warnings wait until you leave your game (Windows key, Alt+Tab).";
        public const string Demo3Title = "Demo 3/3 · Save your battery?";
        public const string Demo3Text = "Keeps the battery between about 25 and 80 % so it lasts more years. Asked only now; to change it, reinstall.";
        public const string Yes = "Yes";
        public const string No = "No";
        public const string SaverOnTitle = "Battery saver is on";
        public const string SaverOnText = "While charging: notifications up to 80 %, then \"Unplug now\". Warnings at 40 / 30 / 25 %, right away at 15 %.";
        public const string CableTip = "Tip: charge with a separate USB-C cable. The receiver cable stays plugged in – no loose contact at the dock.";
        public const string DriverTitle = "Which page should open?";
        public const string DriverText = "Official G-Wolves web drivers. Your choice opens on every click from now on. To pick again: hold Ctrl and click a notification.";
        public const string DriverTextAgain = "Official G-Wolves web drivers. Your choice opens on every click from now on.";
        public const string More = "More…";
        public const string Back = "Back";

        public const string Unplug = "Unplug now – saves the battery.";
        public const string GettingLow = "Getting low.";
        public const string ChargeSoon = "Charge soon.";
        public static string AlmostEmpty(string name, int p) => $"{name} almost empty: {p} %";
        public const string ChargeNow = "Please charge now.";
        public const string Asleep = "Mouse is asleep – move it to get the value.";
        public const string NoMouse = "No G-Wolves mouse found.";
        public const string NoMouseHint = "Plug in the receiver or cable.";

        public const string InstallQuestion = "Install Mouse Battery?\n\nIt runs invisibly in the background, starts with Windows and tells you the battery level of your G-Wolves mouse.\n\nNo admin rights needed. Remove it any time via Settings → Apps.";
        public const string Installed = "Installed – now running in the background.";
        public const string UninstallQuestion = "Remove Mouse Battery?\n\nAutostart, program and settings will be deleted.";
        public const string Uninstalled = "Mouse Battery has been removed.";
        public static string Failed(string msg) => "That did not work:\n" + msg;
    }
}
