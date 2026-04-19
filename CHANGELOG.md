# Changelog

All notable changes to **RomStation Rebase** are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),  
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

---

## [1.0.0] - 2026-04-19

Initial public release.

### Added

- Complete rebase workflow: select games from the RomStation library, choose a target folder, and copy ROMs using the conventional folder structure for RetroArch, Lakka, or Anbernic handhelds
- Two display modes: grid (covers) and detailed list, with virtualized scrolling for large libraries
- Filter by system with real-time game count
- Parallel copy engine with configurable concurrency and automatic retries on transient failures
- Duplicate handling policy (skip or overwrite)
- Smart RomStation detection: automatic via Windows registry, with manual folder selection fallback for portable ZIP installations
- Persistent user preferences: last target folder, target architecture, copy settings, window positions, display mode, and UI language
- Embedded .NET 10 runtime (self-contained MSI and portable ZIP, no external installation required)
- Localized interface (French and English with automatic detection)
- Execution report with real-time per-game status and log export
- Settings panel with language, theme (light only), folder shortcuts and project information

### Known limitations

- Only **Light** theme is available (dark theme planned for a future release)
- Automatic update check is not yet implemented