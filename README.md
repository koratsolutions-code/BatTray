# BatTray
**Your devices. One glance.** — by Korat Solutions

🌐 **English** · [Español](README.es.md)

BatTray shows the battery level of your peripherals (mouse, keyboard, headset, gamepad…) right on the
Windows taskbar: the device icon next to its percentage.

```
[🖱 50%]  [🎧 80%⚡]  [⌨ 12%]   ⌃  🔊  🔋97%   13:45
```

## Features
- **Taskbar widget** with each device's icon and percentage (green / amber / red), on every monitor's taskbar.
  When there is no room, the devices with the most battery are hidden first — app buttons are never covered.
- **Detail panel** (left-click the widget or the tray icon): every device with a level bar, charging state,
  last reading and source.
- **Low battery alerts**: one Windows notification per discharge when a device drops to the critical level.
- **Tray icon and menu** (right-click): choose which devices to show, start with Windows, alerts on/off,
  diagnostics report, log folder.
- Light and dark taskbar themes, any DPI scaling, auto-hide taskbar, full-screen apps, Explorer restarts.

## Install
Download and run `BatTray-Setup-<version>.msi`. It installs **per user** (no administrator rights) to
`%LocalAppData%\Programs\BatTray`, adds a Start menu entry and starts BatTray. Uninstall it from
*Settings › Apps*; the uninstaller also removes the start-with-Windows entry.

## Supported devices
| Source | Devices | How |
|---|---|---|
| Bluetooth LE | Modern mice, keyboards, headsets, controllers | GATT Battery Service (0x180F) |
| Bluetooth (Windows) | Classic headsets and any device Windows reports | PnP property `DEVPKEY_Bluetooth_Battery`, the value shown in Settings |
| Game controllers | Xbox controllers (USB, Xbox Wireless Adapter, Bluetooth) and others Windows reports | `Windows.Gaming.Input` battery report |
| PlayStation controllers | DualShock 4, DualSense, DualSense Edge (USB and Bluetooth) | Battery field of the regular input report |
| USB receivers (2.4 GHz) | See below | Pluggable HID protocols |

### 2.4 GHz receivers
Unlike Bluetooth, 2.4 GHz dongles have no common battery standard: every vendor uses its own protocol.
BatTray uses a catalogue of **pluggable protocols** (`IHidBatteryProtocol`):

| Protocol | Devices |
|---|---|
| Logitech HID++ 2.0 (0x1004, 0x1000, 0x1001) | Unifying, Bolt, Lightspeed receivers (up to 6 devices each) and Logitech devices on USB |
| Razer (90-byte feature report) | Wireless mice and keyboards (receivers and cable) |
| SteelSeries | Arctis 1/7/7+/Pro/7X/7P, Arctis Nova 3P/5/7, Aerox 3/5/9, Prime, Rival 3 Wireless, Rival 650 |
| Corsair | Legacy Void/HS headsets and Bragi devices (Dark Core, Ironclaw, Harpoon, M65/M75, K57/K70, Slipstream, Virtuoso, HS80…) |
| HyperX | Cloud Flight, Cloud Alpha Wireless, Cloud II Wireless, Cloud Alpha 2, Pulsefire Haste 2 |
| GetPower (Compx/Areson/Nordic) | Redragon (25A7:FA07/FA08), ATK, VXE, VGN, Dareu, Zaopin, Scyrox, G-Wolves |
| Standard HID battery (0x06/0x20, 0x85) | Any receiver that follows the HID specification |

**Safety:** only read-only queries are sent, and only to receivers a protocol positively recognises.
Commands that change settings or switch dongle modes are never used. Unrecognised receivers are never
written to — use **Save diagnostics report…** (or `BatTray.exe --list-devices`) to capture their HID
layout so support can be added.

> The Razer, SteelSeries, Corsair, HyperX and PlayStation protocols are implemented from public
> documentation and open-source drivers and covered by unit tests, but not yet verified on real hardware.

## Settings
Managed from the menu and stored in `%AppData%\KoratSolutions\BatTray\settings.json`:

| Key | Default | Meaning |
|---|---|---|
| `hiddenDeviceIds` | `[]` | Devices not shown on the taskbar |
| `lowThreshold` | `30` | Below this %, the level turns amber |
| `criticalThreshold` | `15` | Below this %, the level turns red and an alert is shown |
| `lowBatteryAlerts` | `true` | Windows notification at the critical level |
| `showOnAllTaskbars` | `true` | Widget on every monitor's taskbar (restart to apply) |

Logs: `%LocalAppData%\KoratSolutions\BatTray\logs`.

## Development
Requirements: Windows 11 (10 2004+), [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0).

| Path | Contents |
|---|---|
| `src/BatTray.Core` | Models, providers (Bluetooth, controllers, HID receivers), HID protocols, monitor, settings |
| `src/BatTray.App` | WPF app: taskbar widgets, detail panel, tray icon, menus |
| `tests/BatTray.Tests` | Unit tests (xUnit) |
| `installer/` | WiX 5 per-user MSI |
| `tools/` | `generate-icon.ps1` (brand icon), `build-installer.ps1` |

```bash
dotnet build
dotnet test
dotnet run --project src/BatTray.App
```

Build the installer (publishes a self-contained build, then the MSI into `artifacts/`):
```bash
pwsh tools/build-installer.ps1
```

### Command-line options
| Option | Description |
|---|---|
| `--mode child` | *(default)* The widget is a child window of the taskbar. |
| `--mode overlay` | Popup placed on top of the taskbar (fallback strategy). |
| `--demo` | Simulated devices instead of real readings. |
| `--list-devices` | Log one reading of every provider (and every receiver's HID layout), then exit. |
| `--uninstall-cleanup` | Used by the uninstaller: disable start with Windows and close BatTray. |
