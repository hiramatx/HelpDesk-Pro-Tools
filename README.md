# HelpDesk Pro Tools

IT help-desk toolkit for running admin tasks against domain PCs.
Avalonia UI 12 · .NET 10 · MVVM (CommunityToolkit.Mvvm) · Designed by Hiram Martinez.

## Features

The main window opens as tall as the monitor's usable area (screen minus taskbar); on short screens the cards scroll.

| Card | What it does |
|---|---|
| **Header** | Light / dark theme toggle (neutral grey), current user with an indicator (green = `-admin` account, orange = standard) |
| **Remote PC** | Editable PC name box with saved history (last 30, stored in `%AppData%\HelpDeskProTools\settings.json`). **Get PC Details** (or Enter) opens the details window |
| **Remote Tools** | SFC Scan, Remote PS, Cleanup Temp Folders, Download Log Files, Ping, C Share, Remote Assist, Remote Admin (RDP), Computer Management, Reboot (with confirmation), Send Message. Every tool checks that a valid PC name is entered first |
| **Local Tools** | AD Users & Computers, Windows Terminal |
| **Scripts** | Tabs (Testing, Tools, Fixes, GAD, Utilities, Installs) built from `scripts.json`; each entry becomes a button that opens the `.ps1` in `pwsh` |

### How each remote tool runs

| Tool | Implementation |
|---|---|
| SFC Scan | `Invoke-Command` → `DISM /RestoreHealth` + `sfc /scannow`, output streamed live into a pop-up (the only one using PowerShell because DISM and SFC have no managed API) |
| Cleanup Temp Folders | C# over `\\PC\c$`: Windows Temp, each user's Temp, Recycle Bin, Edge and Chrome caches, with a freed-space report |
| Download Log Files | C# `EventLogSession` exports System/Application/Setup `.evtx` on the PC, then copies them plus CBS, DISM, Panther and CCM logs to `Desktop\RemoteLogs\PC_timestamp` |
| Ping PC | C# `Ping` + DNS lookup |
| Reboot PC | C# WMI `Win32_OperatingSystem.Win32Shutdown` (forced restart), then watches the PC go offline and come back |
| Send Message | `msg.exe * /server:PC` |
| Remote PS | `pwsh` → `Enter-PSSession` |
| C Share | Explorer at `\\PC\c$` and `\\PC\c$\Users\<you without -admin>` |
| Remote Assist / Remote Admin / Computer Mgmt | `msra /offerra`, `mstsc /admin`, `compmgmt.msc /computer:` |

**PC Details** uses WMI (`System.Management`) and AD (`System.DirectoryServices`): PC and user OU, liquid-fill gauges for CPU / RAM / C: usage (green up to 50%, yellow 51-85%, red 86-100%), logged-in user (falls back to the owner of `explorer.exe` for RDP sessions), video cards, monitors (name and native resolution) and Device Manager errors.

## Requirements on the admin PC / targets

- Domain-joined workstation, run as your `-admin` account
- RSAT: Active Directory tools (for **AD Console**)
- PowerShell 7 (`pwsh`) recommended; Windows PowerShell 5.1 is used as a fallback
- Targets: WinRM enabled (SFC Scan, Remote PS), admin share `c$`, WMI/RPC reachable through the firewall
- **Send Message**: the target needs `HKLM\SYSTEM\CurrentControlSet\Control\Terminal Server\AllowRemoteRPC = 1`

## scripts.json

Lives next to the exe (copied from the project on build). Edit it from the app (**Edit scripts.json**) and press **Reload**.

```jsonc
{
  "Tools": [
    {
      "name": "Get Installed Apps",
      "path": "Scripts\\Tools\\Get-Apps.ps1",   // absolute, UNC or relative to scripts.json
      "arguments": "-ComputerName {PC}",           // optional; {PC} = current Remote PC
      "description": "Tooltip text"                // optional
    }
  ]
}
```

A category name that isn't one of the six default tabs becomes an extra tab.

## Project layout

```
Controls/     LiquidFillGauge (custom-drawn animated gauge)
Models/       AppSettings, PcDetails, ScriptEntry
Services/     RemoteOperations, PcInfoService, ActiveDirectoryService, ProcessLauncher,
              DialogService, SettingsService, ScriptCatalogService, ThemeService, WmiHelper
ViewModels/   MainViewModel, PcDetailsViewModel, OutputViewModel, SendMessageViewModel, ScriptCategoryViewModel
Views/        MainWindow, PcDetailsWindow, OutputWindow, SendMessageWindow, MessageDialog
Themes/       AppStyles.axaml (cards, flat buttons, tabs); theme colours are in App.axaml
Scripts/      Sample .ps1 files referenced by scripts.json
```
