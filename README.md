# HelpDesk Pro Tools

IT help-desk toolkit for running admin tasks against domain PCs.
Avalonia UI 12 · .NET 10 · MVVM (CommunityToolkit.Mvvm) · Designed by Hiram Martinez.

## Download

**[Download the latest release](https://github.com/hiramatx/HelpDesk-Pro-Tools/releases/latest)** (Windows x64 zip)

Extract the zip and run `HelpDeskProTools.exe`, ideally as your `-admin` account. The release is self-contained (.NET is included), so nothing needs to be installed, and it can run straight from a file share.

## Features

The main window opens as tall as the monitor's usable area (screen minus taskbar); on short screens the cards scroll.

| Card | What it does |
|---|---|
| **Header** | Light / dark theme toggle (neutral grey), current user with an indicator (green = `-admin` account, orange = standard) |
| **Remote PC** | Editable PC name box with saved history (last 30, stored in `%AppData%\HelpDeskProTools\settings.json`). **Get PC Details** (or Enter) opens the details window |
| **Remote Tools** | SFC Scan, Remote PS, Cleanup Temp Folders, Download Log Files, Ping, C Share, Remote Assist, Remote Admin (RDP), Computer Management, Reboot (with confirmation), Send Message. Every tool checks that a valid PC name is entered first |
| **Local Tools** | AD Users & Computers, Windows Terminal |
| **Scripts** | Tabs (Testing, Tools, Fixes, GAD, Utilities, Installs) built from `scripts.json`; each entry becomes a button that opens the `.ps1` in Windows PowerShell (`powershell.exe`) |

### How each remote tool runs

| Tool | Implementation |
|---|---|
| SFC Scan | `Invoke-Command` → `DISM /RestoreHealth` + `sfc /scannow`, output streamed live into a pop-up (the only one using PowerShell because DISM and SFC have no managed API) |
| Cleanup Temp Folders | C# over `\\PC\c$`: Windows Temp, each user's Temp, Recycle Bin, Edge and Chrome caches, with a freed-space report |
| Download Log Files | C# `EventLogSession` exports System/Application/Setup `.evtx` on the PC, then copies them plus CBS, DISM, Panther and CCM logs to `Desktop\RemoteLogs\PC_timestamp` |
| Ping PC | C# `Ping` + DNS lookup, continuous (like `ping -t`) with timestamps until **Stop**, then shows statistics |
| Reboot PC | C# WMI `Win32_OperatingSystem.Win32Shutdown` (forced restart), then watches the PC go offline and come back |
| Send Message | `msg.exe * /server:PC` |
| Remote PS | `pwsh` → `Enter-PSSession` |
| C Share | Two file windows for moving files between the tech's PC and the target: the tech's own profile on this PC (`C:\Users\<you without -admin>`) and `\\PC\c$`. Both use the account the app runs as. When the app is started with "Run as different user", they open in an in-app file window, because Windows always runs File Explorer as the logged-on desktop user |
| Remote Assist / Remote Admin / Computer Mgmt | `msra /offerra`, `mstsc /admin`, `compmgmt.msc /computer:` |

The main window fills the right half of its screen and **PC Details** opens filling the left half of the same screen (taskbar excluded). Data comes from WMI via `System.Management`, remote registry via the Remote Registry service when it is reachable (otherwise WMI `StdRegProv`, which works without the service), the `c$` share and AD via `System.DirectoryServices`. The sections are read in parallel and each card fills in as soon as its data arrives; the header shows how long the whole load took (hover it for the time of each section):

| Section | Fields |
|---|---|
| Utilization | Liquid-fill gauges for CPU / RAM / C: (green up to 50%, yellow 51-85%, red 86-100%) |
| PC | Name, model, OS with version and build (e.g. 25H2), serial, UAC/LUA, domain controller, IP (orange on 105.195.x.x, green otherwise), network speed in Gbits/sec, last boot, uptime (green up to 48 h, yellow over 48 h, red over 96 h), OU |
| Users | Logged-in user (no domain) and OU, console or remote (RDP), members of local Administrators, Remote Desktop Users and "Direct Access Users" (local group, or the AD group if there is no local one), minus anyone in `excluded_users.json`. Names are shown without the domain; local admins are red, Remote Desktop and Direct Access members orange |
| Hardware | Processor, total RAM, RAM sticks per slot, MAC, each video card (minus those in `excluded_video_cards.json`), monitor (name, resolution) and fixed drive (total / used / free) |
| Software | Programs listed in `software.json`, coloured against `baselines.json` (green = up to date, red = outdated or required but missing, orange = couldn't check; hover for details), plus GPO system and user dates: when Group Policy last applied to the PC and the logged-in user (green within the last 7 days, today included; red otherwise) |
| Device Manager | Devices with errors, if any |

## Requirements on the admin PC / targets

- Domain-joined workstation, run as your `-admin` account
- RSAT: Active Directory tools (for **AD Console**)
- PowerShell 7 (`pwsh`) recommended; Windows PowerShell 5.1 is used as a fallback
- Targets: WinRM enabled (SFC Scan, Remote PS), admin share `c$`, WMI/RPC reachable through the firewall
- **Send Message**: the target needs `HKLM\SYSTEM\CurrentControlSet\Control\Terminal Server\AllowRemoteRPC = 1`

## Config folder

All settings files live in `Config\` next to `HelpDeskProTools.exe`. When the app runs from a file share, everyone uses the same files. Each file explains its options in comments at the top.

| File | Controls |
|---|---|
| `scripts.json` | Script buttons in the Scripts tabs. Edit it from the app (**Edit scripts.json**) and press **Reload** |
| `software.json` | Programs in the PC Details Software card, and where to find each version |
| `baselines.json` | Minimum version per program (green / red colouring) |
| `excluded_users.json` | Members hidden from the Local Admin / Remote Desktop / Direct Access lists |
| `excluded_video_cards.json` | Video cards hidden from the Hardware card (e.g. virtual display adapters) |

Changes to `software.json`, `baselines.json` and the two `excluded_*.json` files apply the next time PC Details is opened or refreshed. PC-name history and the theme choice stay per user in `%AppData%\HelpDeskProTools\settings.json`.

### scripts.json

```jsonc
{
  "Tools": [
    {
      "name": "Get Installed Apps",
      "path": "Scripts\\Tools\\Get-Apps.ps1",   // absolute, UNC or relative to the exe's folder
      "arguments": "-ComputerName {PC}",           // optional; {PC} = current Remote PC
      "description": "Tooltip text"                // optional
    }
  ]
}
```

A category name that isn't one of the six default tabs becomes an extra tab.

### software.json

Each program is checked in this order, and the first source that finds a version wins:

| Option | Finds the version from |
|---|---|
| `builtIn: "Office"` | Microsoft 365 / Click-to-Run or MSI Office (product, version and channel) |
| `files` | The version of an exe on the PC (`C:\...` is read through `\\PC\c$`) |
| `registry` | An HKLM value: `{ "key": "SOFTWARE\\...", "value": "pv" }` |
| `content` | Text inside a file: `{ "path": "C:\\...\\version.txt", "pattern": "Version=([0-9.]+)" }`; the part in `( )` is the version |
| `uninstall` | The start of the Programs & Features name, e.g. `"Zoom"` |

`"required": true` shows "Not installed" in red instead of normal text.

Two more options:
- `"label"` shows that text exactly, instead of "<name> Version".
- `"process": "nschill.exe"` checks for a running process instead of a version. If it's running, it shows **Running / user** in green (several users are comma-separated). If not, it shows **NA / NA** in red.

```jsonc
{ "name": "NSCHILL", "label": "NSCHILL status/user", "process": "nschill.exe" }
```

```jsonc
{ "software": [
  { "name": "Chrome", "files": [ "C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe" ] },
  { "name": "MS Office", "builtIn": "Office" },
  { "name": "Zoom", "uninstall": "Zoom Workplace" },
  { "name": "Vendor Agent", "required": true,
    "content": { "path": "C:\\ProgramData\\Vendor\\Agent\\version.txt", "pattern": "Version\\s*=\\s*([0-9.]+)" } }
] }
```

### baselines.json

```jsonc
{ "Chrome": "150.0.0.0", "MS Office": "16.0.19000.0", "Vendor Agent": "4.10.0" }
```

Keys are the `name` values from `software.json`. Versions compare number by number (4.10.2 is newer than 4.9.8).

### excluded_users.json

```jsonc
{
  "allGroups": [ "Domain Admins", "S-1-5-21-*" ],   // hidden from every list
  "localAdmins": [ "Administrator" ],
  "remoteDesktopUsers": [],
  "directAccessUsers": []
}
```

Not case-sensitive. `Domain Admins` also matches `CORP\Domain Admins`, and `*` is a wildcard.

### excluded_video_cards.json

```jsonc
{ "excluded": [ "Microsoft Basic Display Adapter", "*Remote Display*", "Citrix*" ] }
```

Not case-sensitive, and `*` is a wildcard.

## Project layout

```
Controls/     LiquidFillGauge (custom-drawn animated gauge)
Models/       AppSettings, PcDetails, ScriptEntry, ConfigModels (software / exclusions)
Services/     RemoteOperations, PcInfoService, SoftwareInventoryService, ActiveDirectoryService,
              LocalGroupService, UserExclusions, RemoteRegistry, ProcessLauncher, DialogService,
              SettingsService, ScriptCatalogService, ConfigFiles, ThemeService, WmiHelper
ViewModels/   MainViewModel, PcDetailsViewModel, OutputViewModel, SendMessageViewModel, ScriptCategoryViewModel
Views/        MainWindow, PcDetailsWindow, OutputWindow, SendMessageWindow, MessageDialog
Themes/       AppStyles.axaml (cards, flat buttons, tabs); theme colours are in App.axaml
Config/       scripts.json, software.json, baselines.json, excluded_users.json, excluded_video_cards.json (copied next to the exe)
Scripts/      Sample .ps1 files referenced by scripts.json
```
