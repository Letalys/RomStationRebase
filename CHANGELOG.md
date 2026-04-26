# Changelog

All notable changes to **RomStation Rebase** are documented in this file.

**[English](CHANGELOG.md) · [Français](CHANGELOG.fr.md)**

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),  
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

---

## [1.2.0] - 2026-04-26

A polish-and-robustness release introducing automatic update checks, single-instance locking, and a visual refresh of the Settings window aligned with the rest of the application.

### Added

- **Automatic update check** — on startup, the application checks GitHub in the background for a new version. When an update is available, a clickable link appears in the bottom status bar and in the Settings panel to open the latest release page directly. A "Check for updates" button in Settings allows manual checks at any time, with the date of the last check displayed
- **Single instance** — launching a second instance now brings the already-open window to the foreground instead of starting a new process. Prevents accidental duplicates and conflicts on the RomStation database
- **Wiki link** — an "Open documentation (wiki)" button in the Settings panel that opens the user documentation directly in the browser

### Fixed

- **Taskbar icon** appearing blurry or generic on high-density (HiDPI) displays — all windows now point to the multi-resolution icon for crisp rendering at every size
- **Rebase target path** not memorized when the window was closed without starting the rebase — the selected folder is now saved on close, regardless of whether the rebase was started
- **Settings window** was not resizable and showed a stray border — now aligned with the visual pattern of the other application windows, with working resize and a visible resize grip
- **"Update available" banner** could be shown incorrectly in some cases where the running application version had moved past the version persisted by a previous check — version comparison is now consistent between live network checks and reloads from the persisted state

### Changed

- **Visual refresh of the "Update available" link**, with consistent hover/pressed affordances between the Settings panel and the bottom status bar. Identical wording in both locations
- **Strengthened protection of user preferences** — in case of a transient read error on the preferences file (antivirus lock, I/O issue), existing preferences are no longer overwritten by a blank file

---

## [1.1.0] - 2026-04-24

Major UI refresh bringing dark theme support, a dedicated game detail window, and a cleaner sidebar. Focused on polish and daily-use ergonomics based on real library usage.

### Added

- **Dark theme** with live switching from Settings (no restart needed). All windows, dialogs, and controls adapt to the chosen theme
- **Game detail window**: dedicated view for each game showing cover art, system, year, developer, publisher, players, genres, available languages (with country flags), and full description. Opens via a new eye affordance on hover in grid and list views, or by double-clicking a tile or row. Includes shortcut buttons to open the game folder in Explorer and to view the game on RomStation's website
- **System icons** in the filter sidebar, next to each system name, for quicker visual identification
- **Alphabetical navigation rail** on the right edge of the main window to jump to games starting with a given letter
- **Thumbnail size** selector (Normal / Large) in the toolbar
- **Global sort** by Title or System in grid view, with preference preserved across sessions
- **Sync confirmation dialog** before reloading the RomStation database, with a reminder to close RomStation first (database is single-connection)
- **Hide empty systems** toggle in the sidebar (enabled by default) to declutter the filter panel when the library only covers a few systems
- **Tooltip** on truncated titles in grid view, showing the full title on hover
- **Fallback system icons** for systems without a usable RomStation icon (Windows and MacOS, whose default icons were white-on-transparent and invisible on light theme)

### Fixed

- Crash on startup when the RomStation Derby database was not yet initialized
- Crash when opening the rebase window with an invalid target drive still selected from a previous session
- "Open folder" button silently doing nothing in some edge cases
- DataGrid text in the rebase window was unreadable in dark theme (black on dark background) due to a system color fallback
- Secondary button borders were barely visible on the light theme background

### Changed

- "Issues only" filter moved from the sidebar bottom to the main filter row, next to the "All games" counter. The toggle is now automatically hidden when the library has no issues, avoiding a dead option
- Both "Issues only" and "Hide empty systems" toggles are now persisted across sessions
- Sort by Files (total file count) removed — not actionable from a user perspective, replaced by the global sort
- Minor polish pass on sidebar icon rendering and grid view spacing

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