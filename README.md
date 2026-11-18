<picture>
  <source media="(prefers-color-scheme: dark)" srcset="docs/images/header-dark.png" />
  <img src="docs/images/header-light.png" alt="BatTray — Your devices. One glance." width="296" />
</picture>

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
- **Usage-based estimates** for devices that never report their battery (many budget headsets and mice):
  BatTray counts how long you use them and shows an estimate such as `~62%`.
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
| Sino Wealth "Aurora" | Glorious Model O Wireless, Lamzu, Attack Shark R5 Ultra |
| MonsGeek / Akko (RongYuan) | MonsGeek and Akko keyboards on their 2.4 GHz dongle (3151:5037/5038) |
| Keychron Ultra-Link | Keychron M2 / M5 / M6 mice |
| Rapoo | Rapoo VT3 PRO receiver |
| Ajazz | Ajazz AJ179 |
| Darmoshark | Darmoshark mice (1915:0723 receiver) |
| MCHOSE (Realtek) | MCHOSE L7 / M7 / A7 V2 / K7 |
| GetPower (Compx/Areson/Nordic) | Redragon (25A7:FA07/FA08), ATK, VXE, VGN, Dareu, Zaopin, Scyrox, G-Wolves |
| Standard HID battery (0x06/0x20, 0x85) | Any receiver that follows the HID specification |

**Safety:** only read-only queries are sent, and only to receivers a protocol positively recognises.
Commands that change settings or switch dongle modes are never used. Unrecognised receivers are never
written to — use **Save diagnostics report…** to capture their HID
layout so support can be added.

> The Razer, SteelSeries, Corsair, HyperX, PlayStation, Aurora, MonsGeek/Akko, Keychron, Rapoo, Ajazz,
> Darmoshark and MCHOSE protocols are implemented from public
> documentation and open-source drivers and covered by unit tests, but not yet verified on real hardware.

## Settings
Everything is set from the tray menu (right-click the BatTray icon or the widget):
- **Each device** is listed with its level: untick it to hide it from the taskbar.
- **Start with Windows**.
- **Low battery alerts**: on or off.
- **Save diagnostics report…** and **Open log folder**: useful when asking for support.
- **Customize devices…**: rename any device and pick its icon from the built-in set (mouse, keyboard, headset,
  earbuds, speaker, microphone, gamepad, trackball, pen, phone, tablet, watch, battery, Bluetooth) or upload your
  own image (PNG, JPG, BMP, GIF or ICO; it is resized to 64×64 and stored in BatTray's settings folder).
- **About BatTray**: version, website and license.

### Devices without battery data
Some devices do not send their battery level to the PC at all, so nothing can read it. They are listed under
**Devices without battery data** in the menu, with two options:

- **Set level manually**: pick 100 / 75 / 50 / 25 % or *Other…*. The level stays fixed until you change it.
- **Estimate from usage**: enter the battery life (hours of use on a full charge, from the manual) and the
  current charge. From then on:
  - A headset counts as in use while sound plays through it, and for 10 minutes after.
  - A mouse or keyboard counts as in use while you use it, and for 10 minutes after.

Both are shown with a `~` (for example `~62%`). Each device's submenu has **Set current level**,
**I just charged it**, a switch between the two modes and **Remove**.

An estimate is an approximation: the real battery depends on volume, lighting and the battery's age.
Estimates are stored in `%AppData%\KoratSolutions\BatTray\estimates.json`.

The level turns amber below 30% and red below 15%, which is also when the alert is shown.

## License
[MIT](LICENSE) © 2026 Korat Solutions.
