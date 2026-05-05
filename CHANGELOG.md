# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).
Versions are derived automatically from git tags via [MinVer](https://github.com/adamralph/minver).

## [Unreleased]

## [0.1.0] - 2026-05-05

### Added
- 23 lint rules covering ASP.NET Web Forms (`.aspx`, `.ascx`, `.master`, `.asax`).
  15 auto-fixable, 8 detection-only.
- CLI `aspx-lint` (NuGet global tool) with `scan` / `fix` commands and
  text / JSON / SARIF output formats.
- Standalone HTML dashboard (`aspx_lint_dashboard.html`) — zero install,
  100 % local-in-browser analysis.
- WPF Desktop app for Windows : tray icon, QR code pairing, embedded
  ASP.NET Core local server.
- ASP.NET Core 9 server exposing `/api/scan`, `/api/save`, `/api/restore`
  with token auth, write allowlist, and `.bak` backup on save.
- Composite GitHub Action (`.github/actions/scan`) for one-line CI
  integration with SARIF upload to Code Scanning.
- 300+ tests across 5 projects:
  - `AspxLint.Core.Tests` (241 unit tests)
  - `AspxLint.Server.Tests` (32 integration tests via WebApplicationFactory)
  - `AspxLint.Cli.Tests` (17 CLI tests)
  - `AspxLint.E2E.Tests` (7 Playwright tests on the served dashboard)
  - `AspxLint.Desktop.Tests` (3 FlaUI UIA tests)
- Coverage : 95.7 % lines on Core + Server, via coverlet + ReportGenerator,
  uploaded to Codecov.
- CI workflow (`dotnet.yml`) : build + test + coverage + self-scan SARIF.
- Release workflow (`release.yml`) : tag-driven, creates GitHub Release
  with `.nupkg` + Desktop self-contained `.exe`, pushes to NuGet.org.
- Auto-versioning via MinVer : version derived from git tags.

[Unreleased]: https://github.com/hlabaste/aspx-lint/compare/v0.1.0...HEAD
[0.1.0]: https://github.com/hlabaste/aspx-lint/releases/tag/v0.1.0
