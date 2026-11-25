<div align="center">

<img src="assets/icon.png" width="84" alt="Mouse Battery">

# Mouse Battery

**Know your G-Wolves battery level – without opening any software.**

A tiny, free Windows background app – no window, no tray icon, no account, no tracking.

[**⬇ Download**](../../releases/latest) · [Install in 1 minute](#install) · [🇩🇪 Deutsch](German-Version/README.md)

</div>

---

## ✨ What it does

### 🔋 A notification instead of a configurator
When you turn on your PC, a quiet Windows notification tells you the level: **„HSK Pro V2: 82 %“**. After that, every 3 hours. That's it – no web page, no extra program window.

> 📸 *Screenshot coming soon.*

### 🎮 Never in the middle of a game
Low-battery warnings **wait until you switch windows** (Windows key, Alt+Tab, clicking another window). So a warning never pops up while you're aiming – you see it the next time you leave the game anyway.

| Battery | What happens |
|---|---|
| **30 %** | silent notification |
| **15 %** | notification with sound |
| **10 %** | with sound, stays until you click **OK** |
| **below 10 %** | like 10 %, repeated every minute |
| **5 % and below** | **immediately**, no matter what you're doing, every minute |

Warnings reset as soon as you charge.

### 🖱️ All G-Wolves wireless mice, recognized automatically
Plug in the receiver (or the cable) – the app finds the mouse and uses its real name. Several mice at the same time work too; each gets its own notification.

---

## 🧰 Everything else
- Only **reads** the battery level. It never sends anything that changes your mouse settings.
- Uses very little memory and practically 0 % CPU.
- English and German – follows your Windows language.
- Double-click the app while it's running → shows the current level right away.
- Writes a small history (`history.csv`) so you can see how long a charge lasts.

## Install
1. [**Download**](../../releases/latest) `MouseBattery.exe`.
2. Double-click it and answer **Yes**.
3. Done – it now runs in the background and starts with Windows.

No admin rights and no .NET installation needed. Windows 10 or 11 (64-bit).

> **Windows SmartScreen** may warn you because the file is new and not signed. Click **More info** → **Run anyway**. The full source code is in this repository, and the `.exe` is built by GitHub Actions from it.

**Uninstall:** *Settings → Apps → Installed apps → Mouse Battery → Uninstall*.

## 🖱️ Supported mice

All models the official G-Wolves web configurator knows (as of November 2026):

| Protocol | Models |
|---|---|
| Newer | HSK Pro V2, HTM Plus, HTXU, Fenrir Pro |
| Older | HSK Pro, HSK Plus, HSK Lite, HTS Plus, HTS Ultra, HTX, HTX Mini, HTR, HTR Pro, HT-S2, HT-S2 Pro, Fenrir, Fenrir Max, VUK, HSK Pro ACE, HSK Plus ACE, HTS Plus ACE, HTX ACE |
| „Receiver RS“ | Fenrir Pro, Fenrir Asym, Fenrir Max Asym, HTS Plus, HTS Plus Pro, HTXU, HTX Mini, HTX Mini Asym, Lycan, WARG, VUK Mini |

**Tested so far:** HSK Pro V2 and Fenrir Pro (Receiver RS). The others use the same protocols and should work – if yours doesn't (or works!), please [tell us](../../issues/new/choose). A new mouse that isn't listed is often only one line of code.

## ⚙️ Settings
Settings live in `%LOCALAPPDATA%\MouseBattery\config.json` (paste that into the Explorer address bar). Change a value, then restart the app (or sign out and back in).

| Setting | Default | Meaning |
|---|---|---|
| `Language` | `auto` | `auto`, `en` or `de` |
| `InfoEveryHours` | `3` | how often the normal level notification comes |
| `FirstWarningPercent` | `30` | silent warning |
| `SecondWarningPercent` | `15` | warning with sound |
| `ThirdWarningPercent` | `10` | warning that stays until **OK** |
| `ImmediatePercent` | `5` | warning right away, even in a game |
| `RepeatSeconds` | `60` | repeat interval below 10 % |
| `CheckIntervalSeconds` | `10` | how often the battery is read |
| `MouseNames` | `{}` | own names, e.g. `{ "5817": "My HSK" }` (product ID from `log.txt`) |

## ❓ FAQ

**I don't get any notification.**
Check that notifications are on for *Mouse Battery* in *Settings → System → Notifications*, and that *Do not disturb* is off. The log is in `%LOCALAPPDATA%\MouseBattery\log.txt`.

**It says the mouse is asleep.**
G-Wolves mice sleep after a short idle time and can't answer then. Move the mouse – the level comes as soon as it wakes up.

**Can it break my mouse or change my settings?**
No. It only sends the same *read battery* request the official web configurator sends. No settings, no firmware.

**Does it work together with the official configurator?**
Yes. If both talk to the mouse at the very same moment, one reading may simply be skipped.

**Is it an anti-cheat risk?**
It doesn't read keys, doesn't inject anything into games and doesn't hook input. It only notices *that* the foreground window changed (a normal Windows event).

**Why is the .exe 11 MB?**
It contains the .NET runtime, so you don't need to install anything.

## 🙏 Credits
The battery protocols were worked out by reading these projects – thanks! No code was copied.
- [quenTiago/windows-taskbar-battery](https://github.com/quenTiago/windows-taskbar-battery)
- [maximelonguepe/gwolves-battery-overlay](https://github.com/maximelonguepe/gwolves-battery-overlay) (protocol documentation)
- The model list comes from the device list the public G-Wolves web configurator loads in the browser.

## ⚠️ Disclaimer
This is an **unofficial** community project. It is **not made, endorsed or supported by G-Wolves**. „G-Wolves“ and the model names are trademarks of their owner and are only used to say which mice are supported. Use at your own risk.

## 💬 Feedback & ideas
Bug, new mouse, or an idea? [Open an issue](../../issues/new/choose) – suggestions are very welcome! Pull requests too, see [CONTRIBUTING](CONTRIBUTING.md). If it helps you, a ⭐ is appreciated.

## How it's made
Planned and tested by its maintainer and written with the help of **Claude**, Anthropic's AI assistant.

## License
MIT – see [LICENSE](LICENSE).
