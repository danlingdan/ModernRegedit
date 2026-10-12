# Contributing to ModernRegedit

Thanks for your interest. This document covers the environment and the contribution workflow. 项目规划与调研文档为中文（[PLAN.md](PLAN.md)、[docs/](docs/)）。

## Environment

- Windows 11
- Visual Studio 2026 18.10+ with the ".NET desktop development" and "Windows application development" workloads
- .NET 10 SDK
- Windows App SDK 2.5.1 (restored via NuGet)

Pinned versions and machine setup: [docs/开发环境.md](docs/开发环境.md) (Chinese).

## Workflow

1. Branch from `main`. Branch names: `feat/<topic>`, `fix/<topic>`, `docs/<topic>`.
2. One topic per pull request.
3. Commit messages follow [Conventional Commits](https://www.conventionalcommits.org/): `feat:`, `fix:`, `docs:`, `refactor:`, `chore:`.
4. Formatting is enforced by [.editorconfig](.editorconfig); run `dotnet format` before pushing.
5. Open a pull request with a short description and the issue reference (`Fixes #123`).

## Issues

- Bug reports: use the bug template. Include the app version, Windows build, and steps to reproduce.
- Feature requests: use the feature template. Describe the problem first, then the proposal.

## Security

Do not open public issues for security problems. See [SECURITY.md](SECURITY.md).

## License

By contributing you agree that your contributions are licensed under the [MIT License](LICENSE).
