# ModernRegedit

A modern registry editor for Windows — Fluent design, safe editing, free and open source.

![Status](https://img.shields.io/badge/status-in%20development-orange) ![Platform](https://img.shields.io/badge/platform-Windows%2011-0078D4) ![.NET](https://img.shields.io/badge/.NET-10.0-512BD4) ![License](https://img.shields.io/badge/license-MIT-blue)

🚧 Under active development. 中文文档：[简体中文](README.zh-CN.md)。

## Why

The built-in Registry Editor freezes during search, has no undo, and its UI predates modern Windows. Commercial alternatives are dated and paid; the alternatives in the Microsoft Store are read-only. ModernRegedit fills the gap: a modern, safe, fully writable registry editor, free for everyone.

## Planned features

- Browse and edit keys and values of all registry types
- Non-blocking full-registry search with a result panel
- Automatic backup before every modify or delete, one-click undo
- Diff preview before importing .reg files
- Command palette (Ctrl+K)
- Tabs, address bar, 32/64-bit views, dark mode

## Roadmap

| Milestone | Scope |
|---|---|
| M0 | WinUI 3 project scaffold, registry service |
| M1 | Virtualized tree and value list, address bar |
| M2 | Parallel non-blocking search |
| M3 | Editing, .reg import/export with diff preview |
| M4 | Auto backup, undo, dangerous-key warnings |
| M5 | Command palette, settings, Microsoft Store release |

Full plan: [PLAN.md](PLAN.md) · Research notes: [docs/research](docs/research) (Chinese)

## Build

Prerequisites: Windows 11, [Visual Studio 2026](https://visualstudio.microsoft.com/) 18.10+ with the ".NET desktop development" and "Windows application development" workloads, .NET 10 SDK.

```bash
git clone https://github.com/danlingdan/ModernRegedit.git
```

Open the solution in Visual Studio and press F5. The solution lands with milestone M0.

## Contributing

Contributions are welcome — see [CONTRIBUTING.md](CONTRIBUTING.md). By participating you agree to the [Code of Conduct](.github/CODE_OF_CONDUCT.md).

## License

[MIT](LICENSE)
