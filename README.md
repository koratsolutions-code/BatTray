# BatTray
**Your devices. One glance.** — by Korat Solutions

🌐 **English** · [Español](README.es.md)

BatTray shows the battery level of your peripherals (mouse, keyboard, headset, gamepad…) right on the
Windows taskbar: the device icon next to its percentage.

```
[🖱 50%]  [🎧 80%]  [⌨ 12%]   ⌃  🔊  🔋97%   13:45
```

## Requirements
- Windows 11 (Windows 10 2004+ at minimum)
- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)

## Layout
| Path | Contents |
|---|---|
| `src/BatTray.Core` | Models, battery providers (Bluetooth LE / classic), device monitor, settings |
| `src/BatTray.App` | WPF app: taskbar widget, tray icon |
| `tests/BatTray.Tests` | Unit tests (xUnit) for `BatTray.Core` |

## Build and run
```bash
dotnet build
dotnet test
dotnet run --project src/BatTray.App
```

### Command-line options
| Option | Description |
|---|---|
| `--mode child` | *(default)* The widget is a child window of the taskbar. |
| `--mode overlay` | Popup placed on top of the taskbar (fallback strategy). |
| `--demo` | Show simulated devices instead of reading real batteries. |

To quit: right-click the widget → **Exit BatTray**. Logs are written to `%LocalAppData%\KoratSolutions\BatTray\logs`.

## Supported battery sources
| Source | Devices | How |
|---|---|---|
| Bluetooth LE | Modern mice, keyboards, headsets | GATT Battery Service (0x180F) |
| Bluetooth (Windows) | Classic headsets and any device Windows reports | PnP property `DEVPKEY_Bluetooth_Battery`, the same value shown in Settings |

## Status
🚧 In development — phase 2: real Bluetooth battery readings.
