![RomStation Rebase](docs/images/banner.png)

# RomStation Rebase

[![Version](https://img.shields.io/badge/version-1.2.0-blueviolet)](https://github.com/Letalys/RomStationRebase/releases/latest)
[![License](https://img.shields.io/badge/license-MIT-green)](LICENSE)
[![Platform](https://img.shields.io/badge/platform-Windows%2010%2F11-blue)](https://github.com/Letalys/RomStationRebase/releases/latest)

**[English](README.md) · [Français](README.fr.md)**

Windows tool that copies your games from RomStation to an SD card or a folder, arranged and named the way your handheld or your frontend expects them: ArkOS and dArkOS, RetroArch, Batocera, EmulationStation, ES-DE, Onion, Cocoon, stock Anbernic firmware.

---

## Why this tool?

[RomStation](https://www.romstation.fr/) brings retro emulators and games together in one interface, and stores its files its own way. An Anbernic handheld, **RetroArch** or **EmulationStation** expect something else: one folder per system (`psx`, `snes`, `gba`…), readable file names, games sometimes extracted from their archive, covers and a metadata file in the right place.

**RomStation Rebase** does that work for the games you check, according to the target you choose.

> ⚠️ RomStation Rebase always works **on copies**. Your RomStation installation is never modified.

> ℹ️ Only games, their covers and their metadata are copied. **Saves made in RomStation are not transferred**, and neither BIOS files nor emulators are copied. See the [Limitations](https://github.com/Letalys/RomStationRebase/wiki/Limitations) page of the wiki.

---

## Installation

Two versions are available on the [Releases](https://github.com/Letalys/RomStationRebase/releases/latest) page:

- **MSI installer** (recommended): standard Windows installation, with shortcut and uninstaller
- **Portable ZIP**: extract it anywhere, then run `RomStationRebase.exe`

### Requirements

- **Windows 10 or 11** (64-bit)
- **RomStation** installed, from its setup program or as a portable ZIP, with downloaded games
- RomStation launched **at least once**, so that its database exists
- About **350 MB** of disk space for the application, and a destination (SD card, USB drive, folder) with room for the chosen games
- Optional, for conversions: **chdman**, **DolphinTool** or **maxcso**. The first two are already in the emulators RomStation installs

The **.NET 10** runtime ships with the application: neither .NET nor Java to install. Details are on the [Requirements](https://github.com/Letalys/RomStationRebase/wiki/Requirements) page of the wiki.

---

## Usage

1. **Check** the games to copy in the library
2. Click **Rebase to…**
3. Choose the **destination** (SD card, USB drive, folder) and the **preconfigured target architecture** that matches your device
4. Click **Start**

The table of the rebase window tells, game by game, what will be written before anything is copied. The full documentation is in the [wiki](https://github.com/Letalys/RomStationRebase/wiki/Home-English), in English and in French.

---

## Features

### Files ready for the target

- **Eight preconfigured target architectures**: ArkOS / dArkOS, RetroArch / Lakka, Batocera / Knulli, EmulationStation / RetroPie, ES-DE, Cocoon, Onion / Miyoo Mini, stock Anbernic firmware. Each one knows its folder names and its rules per system
- **Reliable naming**: arcade romsets keep their original name, the discs of a game are numbered and gathered in an **M3U** playlist, games sharing a title no longer overwrite each other
- **Archive extraction** for the systems whose emulator cannot read zip files (PSP, Playstation, GameCube, Saturn, Dreamcast…)
- **Covers** copied where the target looks for them, resized when it requires it
- **Metadata** in the file the target reads (EmulationStation or ES-DE `gamelist.xml`, `miyoogamelist.xml`, `metadata.pegasus.txt`, Logiqx `.dat`), in French or in English. An existing file is merged: favorites and play counts are kept
- **One entry per game in EmulationStation**, even for a multi-disc game (ArkOS / dArkOS)
- **Conversion by external tools** during the copy: GDI or CUE/BIN to CHD with chdman, ISO to CSO, GameCube and Wii to RVZ with DolphinTool. The emulators installed by RomStation already contain chdman and DolphinTool, RomStation Rebase suggests their location

### Comfortable to work with

- **Architecture editor**: adjust an architecture or create your own without touching a JSON file
- **Per-game rules**: romset, M3U, extraction and conversion can also be set row by row, for the current rebase
- **Presets** (`.rsrgp` files): the checked games and every rebase setting in one file, reopened with a double-click
- **Detailed log** of every rebase, followed live in its own window
- **Two display modes**, system filters, search, A-Z rail, detail sheet for each game
- **Parallel copying**, retries on failure, duplicates skipped or overwritten, pause and cancel
- **Light or dark theme**, interface in French and in English
- **Update check** at startup

---

## Technical architecture

- **C# / WPF**, MVVM pattern
- **.NET 10**, self-contained application with nothing to install
- **IKVM**, Java to .NET bridge to read the RomStation **Apache Derby** database
- The RomStation database is always read **from a copy**, the original stays untouched
- **xUnit** test project for the naming rules, the output formats and the driving of external tools

---

## Development and testing

- Version **1.3.0** was co-written with an AI: [Claude Code](https://claude.com/claude-code), Anthropic's Claude Fable 5.1 model. Letalys defined the needs, made every call and validated the result
- Device testing was done on an **Anbernic RG353V running dArkOS**. The other target architectures follow each system's documentation: a [report from your device](https://github.com/Letalys/RomStationRebase/issues/new/choose) is welcome
- Every error message carries an `RSR-nnnn` [code](https://github.com/Letalys/RomStationRebase/wiki/Error-codes), to quote in an issue

---

## Changelog

The version history is in [CHANGELOG.md](CHANGELOG.md).

---

## License

Distributed under the **MIT License**. See [LICENSE](LICENSE).

---

© 2026 [Letalys](https://github.com/Letalys)
