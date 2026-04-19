# RomStation Rebase

[![Version](https://img.shields.io/badge/version-1.0.0-blueviolet)](https://github.com/Letalys/RomStationRebase/releases/latest)
[![License](https://img.shields.io/badge/license-MIT-green)](LICENSE)
[![Platform](https://img.shields.io/badge/platform-Windows%2010%2F11-blue)](https://github.com/Letalys/RomStationRebase/releases/latest)

**[English](README.md) · [Français](README.fr.md)**

Windows tool to copy your ROMs from RomStation to conventional folder structures compatible with RetroArch, Lakka, and Anbernic handheld devices.

---

## Why this tool?

[RomStation](https://www.romstation.fr/) centralizes your retro emulators and ROMs library into a single interface. But when you want to use your ROMs with **RetroArch**, on an **Anbernic handheld console**, or any other frontend, you face a problem: these targets expect a **conventional folder structure** (`/roms/psx`, `/roms/snes`, `/roms/gba`, etc.) that is very different from the one used by RomStation.

**RomStation Rebase** automates this copy while respecting the conventions expected by each target — without ever modifying your RomStation installation.

> ⚠️ RomStation Rebase always works **in copy mode**. Your RomStation installation remains intact and fully functional.

---

## Installation

Two versions are available on the [Releases](https://github.com/Letalys/RomStationRebase/releases) page — pick the one you prefer:

- **MSI Installer** (recommended): proper Windows installation with Start menu shortcut and uninstaller
- **Portable ZIP**: extract anywhere and run `RomStationRebase.exe`, no installation required

The most recent version is always available on the [latest release](https://github.com/Letalys/RomStationRebase/releases/latest) page.

---

## Requirements

- **Windows 10 or 11** (64-bit)
- **RomStation** installed (setup.exe version **or** portable ZIP — both are supported)
- RomStation must have been launched **at least once** for its database to be initialized

The application embeds the **.NET 10** runtime — no additional installation required.

---

## Usage

1. **Launch RomStation Rebase**
2. **Select** the games you want to copy (individual checkboxes, or "Select all" per system)
3. Click **Rebase to...**
4. **Choose** the target folder (SD card, USB drive, external disk...)
5. **Select the target architecture**: RetroArch / Lakka / ArkOS / ...
6. Click **Start**

ROMs are copied using the folder structure expected by your target device.

---

## Main features

- **Two display modes**: grid (covers) or detailed list
- **Filter by system**: only show the platforms you care about
- **Smart RomStation detection**: automatic via Windows registry, or manual selection for portable ZIP installations
- **Parallel copying**: multiple files copied simultaneously, optimized speed
- **Duplicate handling**: configurable policy (skip or overwrite)
- **Automatic retries** on transient failures (network drives, unstable USB...)
- **Preferences memory**: last target folder, target architecture and settings are restored on each launch
- **Localized interface**: French and English (automatic detection based on system language)
- **Execution report**: real-time tracking with per-game status (done, skipped, failed) and log export

---

## Technical architecture

- **C# / WPF** — native Windows interface with MVVM pattern
- **.NET 10** — self-contained, no external dependency required
- **IKVM** — Java ↔ .NET bridge to access the RomStation **Apache Derby** database
- **Apache Derby** — read-only access on a local copy of the database

The RomStation database is always worked on **via a copy**. The original remains untouched.

---

## Changelog

See [CHANGELOG.md](CHANGELOG.md) for the full version history.

---

## License

Distributed under the **MIT License**. See [LICENSE](LICENSE) for more details.

---

© 2026 [Letalys](https://github.com/Letalys)