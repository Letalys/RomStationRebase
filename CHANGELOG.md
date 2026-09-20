# Changelog

All notable changes to **RomStation Rebase** are documented in this file.

**[English](CHANGELOG.md) · [Français](CHANGELOG.fr.md)**

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),  
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

---

## [1.3.0] - Unreleased

The "target-ready" release: files are named the way emulators expect them, archives can be extracted for the systems that need it, and each target receives its covers and the metadata file it knows how to read. Validated on an Anbernic RG353V running dArkOS.

This version was co-written with an AI: Claude Code, Anthropic's Claude Fable 5.1 model. Letalys defined the needs, made every call and validated the result, in the application and then on the device.

### Added

- **Archive extraction** — three modes in the rebase window: extract as the target architecture requires (default, e.g. PSP, Playstation, GameCube, Saturn, Dreamcast), never extract, or extract everything except romsets. Which systems need extraction, read M3U playlists or keep their romset name is defined once, per system, in the target architecture. A single extracted file takes the game name; a bin + cue set keeps its internal names in a folder named after the game. A disc image shipped alone as a `.bin`, which no CD emulator lists, receives the `.cue` that makes it launchable. Size estimate, progress bar and ETA are based on the decompressed size
- **Covers** — each cover is copied to the `images` folder of its system as `<ROM name>-image.png`, the EmulationStation "local art" convention that works even without a gamelist. Resized when the target requires it (Anbernic stock firmware)
- **Metadata that fits the target** — each architecture writes the file its frontend reads, in French or English, with title, description, year, developer, publisher, genres and players: EmulationStation `gamelist.xml` in each system folder (ArkOS, dArkOS, RetroPie, Batocera, Knulli), ES-DE `gamelist.xml` under `ES-DE/gamelists` with covers under `ES-DE/downloaded_media` (ES-DE ignores a gamelist placed among the ROMs; this is also what Cocoon imports), `miyoogamelist.xml` for Onion and Spruce, `metadata.pegasus.txt` for Pegasus, or a Logiqx `.dat` for Daijisho and ROM managers. An existing file is always merged: favorites, play counts, hidden games and entries written by another tool are preserved; an unreadable file is backed up before being recreated
- **One default architecture per real target** — ArkOS / dArkOS (selected by default, limited to the systems dArkOS supports), RetroArch / Lakka, Batocera / Knulli, EmulationStation / RetroPie, ES-DE, Cocoon (Android), Onion / Miyoo Mini and Anbernic stock firmware, each with its own folder names, per-system rules, metadata file and cover location
- **"Output" column** in the rebase window announcing what will be produced for each game (copy, romset, discs + M3U, versions, extraction, game folder), with the list of files to be written on hover
- **Target architecture editor** — from Settings or the rebase window, edit any architecture without touching JSON files: label, output defaults, cover folder and size (with the `{system}` marker to leave the ROM tree), metadata file, and the system table (folder, original name kept, M3U, extraction). All architectures live in one folder in your user data, where the defaults are copied on first launch: add or duplicate an architecture to create your own, delete the ones you do not use, defaults included, and bring the original default files back at any time with "Restore default architectures"
- **Remove a game from the rebase list** with the ✕ button on its row; the game is unchecked in the library at the same time
- **Selection badge** next to the library title: how many games are checked for the next rebase, including those hidden by the current filters, with a ✕ to clear the whole selection. Each system in the sidebar shows its own count of checked games
- **Per-game rules in the rebase table** — three columns, Romset, M3U and Extraction, pre-filled from the target architecture for each game's system and switchable game by game for the current rebase, without editing the architecture. Each switch shows what will really happen to the game: a rule that does not apply to it (M3U for a single-disc game, extraction of a romset, "Never extract" mode) is greyed and off, with a tooltip saying why
- **Optional backup of the existing metadata file**, for example as `gamelist.xml.yyyymmdd`, before merging
- **Presets** — a preset (`.rsrgp` file) keeps the checked games, every rebase setting and the rules switched game by game, so a working configuration comes back without re-checking everything. A "Preset" area, right of the logo, shows which one is loaded and gathers its actions: `Open…` (Ctrl+O), always visible, checks the games and keeps the settings, which "Rebase to…" will use; `Save` (Ctrl+S) is a split button, with "Save as…" (Ctrl+Shift+S) under its arrow; `Close` unchecks the games of the preset. In the rebase window, "Save this configuration" sits next to "Start", and the title bar carries the preset name, followed by a dot while changes are unsaved. The application offers to save before quitting, opening another preset or closing it, and nowhere else. While a preset is loaded its settings belong to it and no longer replace the last settings used without one. On opening, a single dialog reports the discrepancies before anything is changed, and lets you back out: games missing from the library, games recognised by title, target architecture that no longer exists, destination folder not found, file written by a newer version. `.rsrgp` files can be associated with RomStation Rebase (for the current Windows account, no administrator rights): the application offers it once, the first time a preset is saved, and the Settings button stays available. The method is the same for the MSI installer and for the ZIP version. A double-click in Explorer then opens the preset, even when the application is already running
- **One entry per game in EmulationStation** — with the "Hidden folder for multi-file games" option of an architecture, enabled for ArkOS / dArkOS, the files of a multi-disc game or of a game extracted as CUE and BIN go to a folder whose name starts with a dot, and an M3U at the root of the system launches the game. The list no longer shows the M3U next to each of its discs, nor one folder to open per game. Games already copied by a previous version stay where they were
- **Conversion by external tool** — an architecture can name, system by system, a tool that converts files during the rebase: GDI or CUE/BIN to CHD with chdman, ISO to CHD or CSO for the PSP, GameCube and Wii to RVZ with DolphinTool. RomStation Rebase drives everything itself: the archive is extracted to a work folder on the local disk, the tool runs there (one conversion at a time, while copies carry on), and only the result reaches the destination, under the name decided for the game; M3U, metadata and duplicate detection point to the converted file. The `.sbi` file of a protected Playstation game is copied next to the converted file, under the same name. The tool's progress feeds the bar and the remaining time, cancelling stops it at once and deletes the partial file, a tool that no longer answers is stopped on its own, and the RomStation library is never modified. Often there is nothing to download: the emulators RomStation installs already contain these tools (chdman in its MAME, DolphinTool in its Dolphin), RomStation Rebase finds them from the RomStation database, offers them in one click, and follows the location when RomStation updates the emulator. RomStation Rebase ships none of these programs and never runs one it found by itself: the new "External tools" window (from Settings, the architecture editor or the rebase window) is where you point to each tool's executable, test it (presence, minimum version) and describe your own tools without any script: arguments one per line, accepted extensions, progress pattern. In the rebase window, a "Convert files with external tools" master switch and a "Conversion" column: each game shows the tool that will convert it, pre-filled from the architecture and changeable game by game, among the only tools whose executable is set and that can read its content (a `.cdi` image, which chdman does not read, stays on "None"). The conversion takes care of the extraction itself; the "Output" column states the format produced; a missing executable is reported, and the games concerned are simply copied without conversion. Each tool has its own executable location, even when several share one program. No default architecture enables a conversion: it is a choice to make in the editor, "Conversion" column of the systems table
- **Detailed rebase log** — every rebase writes a `RSR_yyyymmdd_hhmmss.log` file in the `logs` folder of your user data: the whole configuration in use (destination, architecture, options, external tools and their executable), the plan file by file, then every step with its time, its duration and, on failure, the full error message. For a conversion, the log keeps the command line and what the tool printed. The "Show log" button of the rebase window opens this file in a window of its own, independent from the rebase window: lines arrive live, warnings and errors are coloured, a filter and a "Warnings and errors only" switch narrow the view, "Follow" keeps the last line on screen, and "Open…" reads an older log. The last thirty logs are kept. The file is tab-separated text, also readable in a tool such as CMTrace
- **Console icon in the architecture editor** — each row of the systems table shows the icon of its console, like the library sidebar
- **Sorting the systems table** — a click on the "RomStation system" header of the architecture editor sorts by name, a second click reverses the order, a third one returns to the file order. Sorting only changes the display, not the architecture file
- **Error codes** — every error message ends with a code such as `[RSR-3004]`, in dialogs, in the Error column of the table and in the rebase log. The first digit gives the family (startup, checks before the rebase, copy, conversion, metadata, presets, architectures, unexpected errors). The "Codes d'erreur" wiki page tells what to do for each one, and the code is enough to locate a problem in an issue
- **Preset icon** — once associated, `.rsrgp` files (*RomStation Rebase Game Preset*) show their own icon in Explorer
- **Unit test project** covering naming rules, each metadata format with its merge and backup, presets, archive extraction, cover resizing and the driving of an external tool (progress, cancellation, stall, cleanup)

### Fixed

- **Arcade romsets were renamed** (`mslug.zip` became `Metal Slug.zip`), which FBNeo and MAME refuse to load. Neo-Geo, Arcade, Naomi, Atomiswave, Model 2 and Model 3 archives now keep their original name
- **Several files did not always mean several discs** — regional or revision variants of the same game were numbered as discs and grouped in one M3U. Discs are now recognized from the RomStation file label; variants are named after that label and never grouped
- **Two games with the same title on the same system** overwrote each other. They are now told apart by their RomStation file label, or by their RomStation identifier when the labels are identical
- **CUE files naming a BIN that does not exist** — in many RomStation archives the BIN was renamed but the CUE still names the old file. Some emulators tolerate it, chdman does not. The `FILE` line of the extracted CUE is corrected when the folder holds a single image
- **M3U playlists** are now generated only for systems whose emulator reads them on the selected target
- **Maximized windows covered the taskbar** — the main and rebase windows now stop at the edge of the work area
- **Library scrolling** — covers are now decoded at their display size instead of full size, cards are prepared one page ahead, and a mouse-wheel notch scrolls exactly one row of cards, aligned on the grid
- **A display error could close the application without a message** — the error dialog reopened on top of itself until the program stopped. It now opens once, and the error detail is written to a `RSR_crash_…txt` file in the `logs` folder
- **Hiding a system in the sidebar no longer unchecks its games** — filters only change what is shown, the selection is kept and stays visible in the badges

### Changed

- **Rebase window** reorganized into three option groups (Files, Metadata, Copy), all settings remembered between sessions
- **"Export log" is replaced by "Show log"** — the CSV export of the table, which said nothing more than the screen, gives way to the detailed log
- **Settings window** — the installed version and the update status now show in the window footer, always visible, instead of at the bottom of a scrolling content. The default size of windows no longer exceeds the work area of the screen
- **File naming** for multi-file games, arcade romsets and homonyms has changed: copies made by a previous version will not be recognized as duplicates and will be copied again. Single-file games keep their exact previous name
- Architecture files now describe, per system, whether the archive name must be kept, whether M3U is supported, whether extraction is required and which external tool converts the files

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