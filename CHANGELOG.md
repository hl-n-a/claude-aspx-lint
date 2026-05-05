# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).
Versions are derived automatically from git tags via [MinVer](https://github.com/adamralph/minver).

## [Unreleased]

## [0.2.0] - 2026-05-06

### Changed (BREAKING)
- **Dropped file:// mode for the dashboard.** The HTML is now strictly a
  client of AspxLint.Server's HTTP API. Opening `index.html` directly in a
  browser displays a friendly error pointing at AspxLint.Server / Desktop.
- Dashboard moved from repo root (`aspx_lint_dashboard.html`) to its own
  module at `src/AspxLint.Web/index.html`, embedded in `AspxLint.Server.dll`
  as a resource (with disk fallback in dev for hot-reload).
- Removed the duplicate JS rules engine (~800 lines). All 23 rules now live
  exclusively in `AspxLint.Core` (C#), single source of truth.
- `ServerSession.DashboardPath` → `DashboardSource` (info string only) +
  `LoadDashboardHtml: Func<Task<string>>` delegate.
- `StartedServer.DashboardPath` → `DashboardSource`, `ProjectRoot` is now nullable.

### Added
- New `AspxLint.Web/` module hosting the dashboard frontend (vanilla HTML/JS
  for now, build step to come).
- Three path-less HTTP endpoints for inline analysis/fixing without disk:
  - `POST /api/analyze` → returns issues for a content+ext
  - `POST /api/fix` → applies a single rule's fix to inline content
  - `POST /api/fix-all` → applies all auto-fixable rules (5-pass convergence)
- CORS support (reflexive origin + credentials) for multi-frontend clients.
- `Authorization: Bearer <token>` header support (in addition to URL ?token
  and cookie) — required for cross-origin clients that can't set cookies.
- Swagger UI at `/swagger` and OpenAPI spec at `/swagger/v1/swagger.json`,
  publicly accessible (no auth) so frontends can discover the contract.
- `Microsoft.AspNetCore.OpenApi` + `Swashbuckle.AspNetCore` 7.2.0.
- 12 new integration tests in `AspxLint.Server.Tests/InlineApiTests.cs`
  covering the three new endpoints + the bearer header auth path.

### Migration notes
- If you previously double-clicked `aspx_lint_dashboard.html`: now run
  `dotnet run --project src/AspxLint.Server` and open the URL printed in
  the console. The dashboard requires the server.
- If you embedded the dashboard somewhere: it's now `src/AspxLint.Web/index.html`.

## [0.1.1] - 2026-05-05

### Added
- Dependabot configuration (NuGet + GitHub Actions, weekly).
- Auto-merge workflow for Dependabot patch updates (semver-patch + minor on
  GitHub Actions).
- Issue templates (bug, feature, new rule) and pull request template.
- GitHub Pages deployment of the coverage HTML report on every push to `main`.

### Fixed
- CI: filter Desktop FlaUI tests on hosted runners (no interactive session).
- CI: tolerate exit-1 from `aspx-lint scan` self-scan.
- Release: skip NuGet push cleanly when `NUGET_API_KEY` is unset.
- CI: add `security-events: write` permission for SARIF upload.

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

[Unreleased]: https://github.com/hl-n-a/claude-aspx-lint/compare/v0.2.0...HEAD
[0.2.0]: https://github.com/hl-n-a/claude-aspx-lint/compare/v0.1.0...v0.2.0
[0.1.1]: https://github.com/hl-n-a/claude-aspx-lint/compare/v0.1.0...v0.1.1
[0.1.0]: https://github.com/hl-n-a/claude-aspx-lint/releases/tag/v0.1.0
