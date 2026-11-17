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

## Using BatTray
Right-click the widget, or click the BatTray icon in the notification area (Windows 11 places new icons
under the `^` overflow; drag it onto the taskbar, or enable it in *Settings › Personalization › Taskbar ›
Other system tray icons*). The menu lets you:

- **Choose which devices are shown** on the taskbar (tick / untick each one).
- **Start with Windows** (per-user, no admin rights).
- **Save diagnostics report…** — a text file listing every device found, including unsupported 2.4 GHz receivers.
- **Open log folder** and **Exit BatTray**.

When the taskbar has no room for every device, the ones with the most battery are hidden first.

### Settings
Stored in `%AppData%\KoratSolutions\BatTray\settings.json`:

| Key | Default | Meaning |
|---|---|---|
| `hiddenDeviceIds` | `[]` | Devices not shown on the taskbar (managed from the menu) |
| `lowThreshold` | `30` | Below this %, the level turns amber |
| `criticalThreshold` | `15` | Below this %, the level turns red |

Logs are written to `%LocalAppData%\KoratSolutions\BatTray\logs`.

## Supported battery sources
| Source | Devices | How |
|---|---|---|
| Bluetooth LE | Modern mice, keyboards, headsets | GATT Battery Service (0x180F) |
| Bluetooth (Windows) | Classic headsets and any device Windows reports | PnP property `DEVPKEY_Bluetooth_Battery`, the same value shown in Settings |
| USB receiver (2.4 GHz) | Dongles with a known protocol (see below) | Pluggable HID protocols |

### 2.4 GHz receivers
Unlike Bluetooth, 2.4 GHz dongles have no common battery standard: every chip vendor uses its own
protocol. BatTray therefore uses a catalogue of **pluggable protocols** (`IHidBatteryProtocol`):

| Protocol | Devices |
|---|---|
| Logitech HID++ 2.0 (features 0x1004, 0x1000, 0x1001) | Unifying, Bolt and Lightspeed receivers (up to 6 devices each) and Logitech devices on USB |
| Standard HID battery (Battery Strength 0x06/0x20, Battery System 0x85) | Any receiver that follows the HID specification |
| GetPower (Compx/Areson/Nordic) | Redragon (25A7:FA07/FA08), ATK, VXE, VGN, Dareu, Zaopin, Scyrox, G-Wolves… |

Only read-only queries are sent, and only to receivers a protocol positively recognises. Unrecognised
receivers are never written to; run `BatTray.exe --list-devices` to log their HID layout so support can be added.

## Status
🚧 In development — phase 3: tray icon, settings and start with Windows.
