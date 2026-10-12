# Changelog

Format: [Keep a Changelog](https://keepachangelog.com/en/1.1.0/). Versioning: [SemVer](https://semver.org/), 0.x until the first stable release.

## [Unreleased]

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

### Fixed

- Value enumeration lost names when `RegEnumValue` returned `ERROR_MORE_DATA`; keys such as `HKCU\Control Panel\Desktop` silently showed 0 values (M2)
