# Contributing

Thanks for wanting to help! Ideas, bug reports and pull requests are all welcome – English or German.

## Ideas and bugs
Use the [issue forms](../../issues/new/choose):
- **Bug report** – something doesn't work. Please attach the last lines of `%LOCALAPPDATA%\MouseBattery\log.txt`.
- **New / untested mouse** – your mouse isn't recognized, or you can confirm that one works.
- **Feature request** – anything you'd like to see.

## Building
You need the [.NET 10 SDK](https://dotnet.microsoft.com/download) on Windows.

```powershell
dotnet build                                   # debug build
dotnet run -- --test                           # read all mice once and show a notification
dotnet publish -c Release -o publish           # single self-contained MouseBattery.exe
```

Command-line options: `--test`, `--install`, `--uninstall` (both accept `--silent`), `--background`.

## Code overview
| File | What's in it |
|---|---|
| `src/Devices.cs` | Model table: product IDs, protocol, names |
| `src/GWolvesHid.cs` | Reading the battery over HID (three protocols) |
| `src/Program.cs` | Rules, warning levels, notifications |
| `src/Installer.cs` | Per-user install / uninstall |
| `src/Texts.cs` | English and German texts |
| `tools/make-icon.ps1` | Draws `assets/icon.png` / `icon.ico` |

## Adding a mouse
Most mice only need one line in `src/Devices.cs`: the wired and wireless product ID (shown in `log.txt` and in Device Manager as `VID_33E4&PID_xxxx`) and the protocol.

## Rules
- **Read-only.** Never send a command that changes mouse settings, firmware or pairing. A pull request that does will not be merged.
- Keep it small: no window, no tray icon, minimal memory.
- No G-Wolves logos or other trademarked artwork in the repository.
