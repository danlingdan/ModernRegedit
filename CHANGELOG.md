# Changelog

Format: [Keep a Changelog](https://keepachangelog.com/en/1.1.0/). Versioning: [SemVer](https://semver.org/), 0.x until the first stable release.

## [Unreleased]

### Added

- Integrated Fluent title bar: app icon and title over Mica end-to-end with tall caption buttons; the window title carries the current key path (research/08)
- InfoBar banners: write / navigation / import failures in a closable error bar, and a tiered warning bar (red = startup-critical, amber = sensitive) when a high-risk key is selected
- Value list empty-state guidance (icon + hint when a key has no values), tooltips for value name and data, compact 32 px rows, status-bar loading ring
- regedit keyboard parity: F5 refresh, Delete deletes the selected value/key, F2/Enter edit, Ctrl+F focuses search, Ctrl+L focuses the address bar, Esc clears the query / closes results / reverts the address
- Copy support: Ctrl+C in the tree copies the key path, in the value list copies the data; context menus gain Copy path / Copy name / Copy data
- .reg import by dropping the file onto the window (same diff preview as the menu command)
- Window size and position persistence, clamped into the nearest work area; maximized state restored
- About section in Settings: package version, MIT license line, repository link, open-backups-folder shortcut

### Changed

- Toolbar regrouped: navigation | undo/redo | address bar | search | view and tools; scope flyout, search panel buttons and column headers localized
- Full zh-CN / en localization for context menus, all dialogs, search status, command palette and high-risk key texts; the command palette matches entries by index so localized labels stay safe

### Fixed

- Renaming a value in the edit dialog used to leave the old name behind; it now applies set-new + delete-old as one undoable batch
- The 32-bit toolbar toggle now reflects the restored view on startup
- en resource pack was missing `Header_Data2` (PRI263) and the Scope button label

## [1.0.0] - 2026-10-13

### Added

- Windows app: packaged WinUI 3 shell with registry tree (lazy loading), value list and status bar (M0)
- Virtualized key tree with background value loading; value list keeps a (Default) row (M1)
- Address bar navigation with root aliases (HKLM…), `Computer\` prefix and forward slashes (M1)
- Back / forward / up navigation with history and Alt+Left/Right/Up accelerators (M1)
- 32-bit / 64-bit registry view switch; status bar shows the active view (M1)
- System / light / dark theme cycling with Mica backdrop and themed title bar (M1)
- Color-coded icon per registry value type (M1)
- Parallel full-registry search: root-sharded background walk, streaming results panel, 250 ms debounce, cancel, 10 000-result cap (M2)
- Search scope checkboxes: key names / value names / value data (M2)
- Jump from a search result to its key, with the matched value row selected (M2)
- Registry editing: create / rename / delete keys and create / edit / delete values of all types, with context menus and confirmations (M3)
- Per-type value editors: DWORD / QWORD with hex and decimal input, binary hex box, multi-string lines (M3)
- Writes route through a UAC-elevated secondary instance (AppExecutionAlias + runas) so changes reach the real registry; unelevated and CLI (`--apply`) runs write directly (M3)
- .reg export of a subtree in Registry Editor 5.00 format (UTF-16LE BOM, dword/hex/hex(2)/hex(7)/hex(b) encodings) to Downloads (M3)
- .reg import with a diff preview (added / modified / deleted counts and rows) before applying (M3)
- Automatic pre-write backups of affected subtrees to `%LOCALAPPDATA%\ModernRegedit\backups` (200-file retention); a failed backup aborts the write (M4)
- Undo / redo for all registry writes (50 levels, subtree snapshots, Ctrl+Z / Ctrl+Y and toolbar buttons) (M4)
- Tiered high-risk key warnings (startup-critical / sensitive) on deletes and in the import preview (M4)
- Favorites with jump-to-key, persisted to `settings.json` (M4)
- Last-location memory: reopens the key and view from the previous session (M4)

### Fixed

- Value enumeration lost names when `RegEnumValue` returned `ERROR_MORE_DATA`; keys such as `HKCU\Control Panel\Desktop` silently showed 0 values (M2)
- Key creation failed when intermediate keys did not exist; `RegCreateKeyEx` now receives the full path (M3)
