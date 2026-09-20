![RomStation Rebase](docs/images/banner.png)

# RomStation Rebase

[![Version](https://img.shields.io/badge/version-1.3.0-blueviolet)](https://github.com/Letalys/RomStationRebase/releases/latest)
[![License](https://img.shields.io/badge/license-MIT-green)](LICENSE)
[![Platform](https://img.shields.io/badge/platform-Windows%2010%2F11-blue)](https://github.com/Letalys/RomStationRebase/releases/latest)

**[English](README.md) · [Français](README.fr.md)**

Windows tool that copies your games from RomStation to an SD card or a folder, arranged and named the way your handheld or your frontend expects them: ArkOS and dArkOS, RetroArch, Batocera, EmulationStation, ES-DE, Onion, Cocoon, stock Anbernic firmware.

---

## Why this tool?

[RomStation](https://www.romstation.fr/) stores its games its own way. An Anbernic handheld, **RetroArch** or **EmulationStation** expect something else: one folder per system, readable names, games sometimes extracted from their archive, covers and metadata in the right place.

**RomStation Rebase** does that work for the games you check, for the target you choose.

> ⚠️ RomStation Rebase always works **on copies**. Your RomStation installation is never modified.

> ℹ️ Saves made in RomStation are not transferred, and neither BIOS files nor emulators are copied. See [Limitations](https://github.com/Letalys/RomStationRebase/wiki/Limitations).

---

## Installation

On the [Releases](https://github.com/Letalys/RomStationRebase/releases/latest) page:

- **MSI installer** (recommended)
- **Portable ZIP**: extract it anywhere, then run `RomStationRebase.exe`

### Requirements

- **Windows 10 or 11** (64-bit)
- **RomStation** installed and initialized, meaning it has been run once
- For conversions, which are optional: the emulators that contain the tools (MAME, Dolphin…), installed by RomStation or any other way

---

## Usage

1. **Check** the games to copy in the library
2. Click **Rebase to…**
3. Choose the **destination** and the **preconfigured target architecture** of your device
4. Click **Start**

---

## Features

- Eight ready-made targets: ArkOS / dArkOS, RetroArch, Batocera, EmulationStation, ES-DE, Cocoon, Onion, stock Anbernic
- Games stored in the right folders, extracted when needed, with their M3U playlists
- Covers and game information copied along
- File conversion during the copy (CHD, CSO, RVZ)
- Presets to get your games and settings back

The full list is on the [Features](https://github.com/Letalys/RomStationRebase/wiki/Features) page of the wiki.

---

## Documentation

The [wiki](https://github.com/Letalys/RomStationRebase/wiki/Home-English) is in English and in [French](https://github.com/Letalys/RomStationRebase/wiki). To report a problem or give feedback on your device, open an [issue](https://github.com/Letalys/RomStationRebase/issues/new/choose): every error message carries an `RSR-nnnn` [code](https://github.com/Letalys/RomStationRebase/wiki/Error-codes) to quote there.

---

## Development and testing

- Version **1.3.0** was co-written with an AI: [Claude Code](https://claude.com/claude-code), Anthropic's Claude Fable 5.1 model
- Device testing was done on an **Anbernic RG353V running dArkOS**
- The [technical architecture](https://github.com/Letalys/RomStationRebase/wiki/Technical-architecture) is described in the wiki

---

## Changelog

The version history is in [CHANGELOG.md](CHANGELOG.md).

---

## License

Distributed under the **MIT License**. See [LICENSE](LICENSE).

---

© 2026 [Letalys](https://github.com/Letalys)
