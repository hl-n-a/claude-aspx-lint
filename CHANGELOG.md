# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).
Versions are derived automatically from git tags via [MinVer](https://github.com/adamralph/minver).

## [Unreleased]

_(empty — start of next cycle)_

## [0.4.0] - 2026-05-07

Big release. Surface étendue : nouvelles règles Web.config, intégration
VS Code complète, package MSBuild pour `dotnet build`, branding fini.
**No breaking changes** — tout est additif.

### Added

#### 6 nouvelles règles CFG-XXX (Web.config)

`ProjectScanner.DefaultExtensions` étendu à `.config`. Les règles `CFG-XXX`
gardent `ctx.Ext == "config"` pour ne fire que sur les `.config`.

- **CFG-001** (warning, auto-fix) — `<compilation debug="true">` en
  Web.config : perf + sécurité.
- **CFG-002** (warning, auto-fix) — `<customErrors mode="Off">` :
  leak de stack traces.
- **CFG-003** (info, auto-fix) — `<trace enabled="true">` : Trace.axd
  expose des données de session.
- **CFG-004** (warning, manuel) — `<httpCookies>` sans
  `httpOnlyCookies` / `requireSSL`.
- **CFG-005** (info, manuel) — `<sessionState mode="InProc">` ne scale pas.
- **CFG-006** (warning, manuel) — `password=...` en clair dans
  `connectionString`.

Recap : **35 règles** (était 29), **22 auto-fixables** (était 19),
**13 manuelles**.

#### Extension VS Code (`hl-n-a.aspx-lint`)

Nouvelle extension publiable sur le marketplace VS Code :

- **Diagnostics inline** sur ouverture / save / frappe (debounced 500ms,
  opt-in). Squigglies coloriés par sévérité.
- **Hover** : survol d'une diagnostic affiche la description complète
  de la règle, sa sévérité, et son statut auto-fixable.
- **Code actions** (Ctrl+. / Cmd+.) : pour chaque issue, propose
  *« appliquer le fix de RULE-ID »*. Le fix est appliqué **uniquement
  sur le buffer courant**, pas sur les autres fichiers. Undoable en Ctrl+Z.
- **Format Document** (Shift+Alt+F) : `DocumentFormattingEditProvider`
  qui applique tous les auto-fixes via `aspx-lint fix --stdin`.
  Compatible avec `editor.formatOnSave`.
- **21 snippets** ASPX (`@page`, `@control`, `@master`, `@register`,
  `<%=`, `<%#`, `<%--`, `asplabel`, `asptextbox`, `aspbutton`, `aspddl`,
  `aspgrid`, `asprepeater`, `aspplaceholder`, `aspcontent`, `aspform`,
  `aspscriptmgr`, `aspupdatepanel`, `aspxdisable`, …).
- **3 commandes** dans la palette : `aspx-lint: Scan workspace`,
  `aspx-lint: Fix current file`, `aspx-lint: Show output`.
- **4 settings** : `aspxLint.path`, `lintOnSave`, `lintOnType`,
  `severityLevel`.
- Workflow CI `vscode.yml` qui build et package `.vsix` à chaque push,
  publie sur le marketplace VS Code à chaque release GitHub si
  `VSCE_PAT` est configuré.

#### Package MSBuild (`aspx-lint.MSBuild`)

Nouveau package NuGet — **10 KB**, props/targets only — qui hook dans
le pipeline de build .NET :

```xml
<PackageReference Include="aspx-lint.MSBuild" Version="0.4.0">
  <PrivateAssets>all</PrivateAssets>
</PackageReference>
```

`dotnet build` lance alors `aspx-lint scan` après la compilation. Si
une issue ≥ severity (default `error`) est trouvée, la build échoue.
Configurable via `<PropertyGroup>` (`AspxLintEnabled`,
`AspxLintFailOnSeverity`, `AspxLintScanPath`, `AspxLintExecutable`,
`AspxLintQuiet`). Pré-requis : CLI `aspx-lint` installé globalement.

Publié automatiquement à côté du CLI sur NuGet à chaque tag.

#### 3 nouvelles commandes CLI (pour intégrations IDE)

- **`aspx-lint analyze`** — analyse un fichier unique sans toucher au
  disque. Lit depuis stdin (`--stdin`) ou depuis un path. Sort du JSON
  `{ ext, issues:[...] }`. Conçu pour les bindings IDE.
- **`aspx-lint fix --stdin`** — applique le fix sur le contenu reçu sur
  stdin, écrit le résultat sur stdout. Avec `--rule X` : un seul fix.
  Sans : tous les auto-fixes (5 passes de convergence).
- **`aspx-lint rules`** — dump JSON des métadonnées de toutes les règles
  (id, name, description, severity, hasFix). `--lang fr|en` pour traduire.

`AnalyzeAsync` et `FixStdinAsync` lisent maintenant via `Console.In`
plutôt que `Console.OpenStandardInput()` — respecte les pipes shell ET
permet aux unit tests d'intercepter via `Console.SetIn`.

#### Branding complet

- **Icône `<%`** chartreuse sur fond charcoal-navy (sources dans
  `assets/source/`, déclinaisons régénérables via
  `assets/build-icons.ps1`, .NET System.Drawing, zéro dépendance).
- **Bannière** `aspx · lint` éditoriale en serif italique pour le
  marketplace VS Code et les partages OG/Twitter.
- Câblage automatique :
  - `src/AspxLint.VSCode/icon.png` (128×128) + `galleryBanner` color
    `#0f1419` dans package.json
  - `src/AspxLint.Desktop/icon.ico` multi-résolution (16/32/48/64/128/256)
    → tray + window title bar + Win32 ApplicationIcon
  - `src/AspxLint.Web/favicon.ico` embarqué + route `GET /favicon.ico`
    sans auth + `<link rel="icon">` dans le `<head>` de la dashboard
  - `docs/favicon.ico`, `apple-touch-icon.png`, `og-image.png` + meta
    OpenGraph/Twitter pour les partages, `brand-icon` dans le header
    du stats site.
- Tests : `Dashboard_links_to_favicon` + `Favicon_is_served_without_auth`.

### Changed

- **Default extensions du scanner** : `.config` ajouté à
  `ProjectScanner.DefaultExtensions`. Les fichiers Web.config sont
  maintenant lintés automatiquement par `aspx-lint scan` sans config
  spéciale.
- **`Console.In` au lieu de `Console.OpenStandardInput()`** dans
  `AnalyzeAsync` et `FixStdinAsync` du CLI : meilleure portabilité
  shell + testabilité.
- **Tray icon Desktop** : charge la ressource embarquée `icon.ico`
  multi-résolution au lieu de générer une icône 16×16 à la volée
  avec une lettre "A" peinte en jaune-vert.

### Migration notes

- **Nouvelles règles CFG-XXX** : si tu scannes un dossier qui contient
  des `Web.config`, tu vas voir de nouvelles issues apparaître. Pour
  garder l'ancien comportement (pas de lint des `.config`), passe
  `aspx-lint scan <dir> --severity error` ou désactive les règles via
  `.aspxlintrc.json` :

  ```json
  {
    "rules": {
      "CFG-001": "off",
      "CFG-002": "off",
      "CFG-003": "off",
      "CFG-004": "off",
      "CFG-005": "off",
      "CFG-006": "off"
    }
  }
  ```

- **Aucune autre action requise** pour les utilisateurs existants. La
  signature publique du CLI, le format SARIF / JSON, l'API HTTP du
  serveur, l'app Desktop sont tous inchangés. Les nouvelles commandes
  CLI (`analyze`, `rules`) et le mode `--stdin` de `fix` sont additifs.

## [0.3.0] - 2026-05-06

A large feature push covering rules, dashboard UX, CLI ergonomics, server
endpoints, Desktop polish, plugin/i18n support, and a refactor pass on the
frontend. **No breaking changes** for end users — existing configs, CLI
commands, and HTTP API continue to work.

### Added

#### Rules (23 → 29, all auto-fix gaps closed where safe)

- **WS-005** (warning, auto-fix) — BOM en début de fichier, supprimé.
- **WS-006** (info, auto-fix) — lignes vides en fin de fichier, collapse → 1 `\n`.
- **A11Y-001** (warning) — `<img>` sans attribut `alt`, manuel.
- **STYLE-001** (info) — `style="..."` inline.
- **SCRIPT-001** (info) — handler JS inline (`onclick=...`).
- **SEC-002** (warning, auto-fix) — `target="_blank"` sans `rel="noopener"` (tabnabbing).
- **SEC-003** (warning) — URL hardcodée vers localhost / `*.local` / port.
- **DOC-001** (warning, auto-fix) — DOCTYPE manquant (ASPX standalone uniquement).
- **FORM-001** (error, auto-fix) — `<form>` sans `runat="server"` dans ASPX.
- **SM-001** (error) — plusieurs `<asp:ScriptManager>` sur la même page.
- **TAG-003** (error, **now auto-fixable**) — balises non équilibrées :
  insère les `</tag>` manquants dans le bon ordre LIFO.
- **ATTR-003** (error, **now auto-fixable**) — attribut dupliqué : merge
  intelligent pour `class` (concaténation), garde le premier ailleurs.

#### Inline disable directives

```aspx
<%-- aspx-lint disable TAG-003 --%>
<%-- aspx-lint disable-line TAG-003,ATTR-002 --%>
<%-- aspx-lint disable-file TAG-003 --%>
<%-- aspx-lint disable-file --%>          (toutes les règles)
```

Reconnu aussi en commentaires HTML (`<!-- ... -->`).

#### Project configuration (`.aspxlintrc.json`)

- Nouveau fichier de config remonté en arborescence depuis le dossier scanné.
- `ignore` : globs (`*` segment, `**` récursif).
- `rules` : override de sévérité par règle (`off` / `error` / `warning` / `info`).
- `customRules` : définition de règles custom en JSON (regex + remplacement),
  chargées sans recompilation.
- Support des commentaires JSON et trailing commas (`JsonCommentHandling.Skip`).
- `aspx-lint init` génère un template + optionnellement un hook pre-commit.

#### Plugin system

- `CustomRule` : règle définie en JSON (id, name, severity, pattern, hint,
  ignoreCase, maskAspBlocks). Chargée via `customRules` du config.
- Validation au chargement (regex compile, severity valide, id unique).
- Tests end-to-end `Custom_rule_loaded_from_config_fires_on_match`.

#### Internationalization

- `Translations.cs` avec `Resolve(rule, locale) → (Name, Description)`.
- Toutes les 29 règles traduites en anglais (`en`).
- CLI `--lang en` pour traduire les noms de règles dans les rapports.
- Endpoint `GET /api/rules?lang=en` pour les frontends multi-langue.
- Test guard `Translations_covers_all_29_rules_in_english`.

#### CLI

- `aspx-lint watch <path>` — re-lint live via `FileSystemWatcher`, debounce
  200ms, cache incrémental SHA1 (~99% de skip après le premier scan).
- `aspx-lint init [--with-hook]` — génère `.aspxlintrc.json` + hook git.
- `aspx-lint pre-commit [--severity ...]` — lint uniquement les fichiers
  staged dans git (utile pour les hooks).
- `aspx-lint benchmark <path> [--runs N]` — warmup + median/min/max + per-rule
  timing breakdown.
- 4 nouveaux formats de sortie :
  - `--junit` : XML JUnit (errors → `<failure>`, warnings/info → `<skipped>`)
  - `--codeclimate` : JSON array CodeClimate avec fingerprint SHA1
  - `--tap` : TAP v14 avec YAML diagnostic blocks
  - `--quiet` : juste le summary, pas le détail des issues
- `--no-color` : désactive ANSI (compatible `NO_COLOR=1`).
- `--lang fr|en` : traduit les noms de règles.

#### Dashboard (frontend)

- **Command palette** (Ctrl+P) — fuzzy search files + commandes globales.
  Recently-opened first quand vide. ↑↓ pour naviguer, Enter pour exécuter.
- **Find / Replace** (Ctrl+F / Ctrl+H) — DOM TreeWalker, highlight tous les
  matches, navigation Enter / Shift+Enter, replace one / replace all.
- **Edit-in-place** — `<textarea>` transparent over `<pre>` colorisé, le
  syntax highlighting se met à jour live pendant la frappe. Ctrl+Entrée
  pour valider, Esc pour annuler.
- **Mini-map** style VS Code — vignette canvas du fichier (1 row par ligne),
  rectangle viewport qui suit le scroll, click pour jump. Cachée < 900px.
- **Split view** — code + diff côte à côte (au lieu du toggle). Caché < 900px.
- **Diff intra-ligne** (char-diff) — paires d'opérations similaires (LCS ≥ 50%)
  affichées avec highlights caractère par caractère.
- **Multi-sélection** dans l'arbre fichier (Ctrl/Shift+click) + bulk bar :
  `fix-all-in-project`, `save-all-modified`, `fix-and-save-project`.
- **Batch report modal** — résumé JSON exportable des actions en lot.
- **Persistance opt-in** (`localStorage`, toggle "Persister") — sauvegarde
  files + filter + selection à chaque modif (debounce 500ms, garde-fou 4 MB).
- **Historique des corrections** — `file.history[]` qui persiste à travers
  les ré-analyses (avant : flag `fixed` perdu à chaque scan).
- **Trend stats** — comparaison vs premier snapshot du jour / 24h / origine.
- **Recent files** — LRU 30 dernières sélections, persisté en localStorage.
- **File tree** — groupement par dossier basé sur `relativePath` issus du
  scan, expand/collapse, statut agrégé (errors / warnings / clean).
- **File search** dans la sidebar — filtre par nom, raccourci Ctrl+P.
- **Status indicators** — pastilles colorées par fichier (errors / warnings /
  info / corrected / modified / clean).
- **File filter** — filtre la sidebar par statut (all / errors / corrected / ...).
- **Theme picker** — VS Code Dark (default), Default (jaune-vert), High
  Contrast, Solarized Dark.
- **Keyboard shortcuts** — Ctrl+S (save), Ctrl+Shift+S (save all), Ctrl+R
  (re-verify), Ctrl+D (download), Ctrl+E (toggle edit), ↑↓ (navigate files).
- **Server-Sent Events** — `GET /api/events` pour live updates multi-clients
  (un autre client save un fichier → on rafraîchit le nôtre).
- **Server-Side Composition** — `index.html` avec marqueurs `{{include:NAME}}`
  expandés au runtime (modules JS + partials HTML + styles.css).

#### Server

- `POST /api/analyze` — analyse inline d'un contenu sans toucher au disque.
- `POST /api/fix` — applique le fix d'une règle sur du contenu inline.
- `POST /api/fix-one {content, ext, ruleId, line}` — fix line-local
  (extraction ligne → fix → réinjection), strategy fallback file-level.
- `POST /api/fix-all` — applique tous les fixes auto-fixables (5 passes).
- `POST /api/read` — lit un fichier disque + retourne content + issues.
- `GET /api/browse?path=` — liste sous-dossiers avec count `.aspx`.
- `GET /api/find-folder?name=` — BFS heuristique (cap 8000 visits, 100 matches).
- `GET /api/events` — Server-Sent Events (5000ms retry hint, 30s heartbeats,
  bounded channel drop-oldest, broadcast `scanned` / `fileSaved`).
- `GET /api/rules?lang=fr|en` — règles traduites.
- CORS reflexif + credentials, `Authorization: Bearer ...` header support.
- Swagger UI sur `/swagger` (sans auth, pour discovery).

#### Desktop

- Fenêtre WebView2 dédiée (sans URL bar, sans menu contextuel, sans
  `Ctrl+Shift+I`, view-source désactivé).
- **Single-instance kill-and-relaunch** : si le `.exe` est déjà lancé,
  ferme l'ancien (`CloseMainWindow` + `Kill` + 400ms TIME_WAIT) avant de
  reprendre.
- `FileSystemWatcher` sur `AllowedRoot`, debounce 300ms, post `fileChanges`
  à JS via `PostWebMessageAsString`.
- Native Windows drag-drop (`PreviewDragOver` + `PreviewDrop` capturant
  `DataFormats.FileDrop` pour les chemins absolus).

#### Tests + Coverage

- **45** tests CLI (était 17), **335** Core (était 241), **85** Server
  (était 32). **+25** tests dashboard HTML (Server).
- Smoke test `DashboardHtmlTests` qui vérifie qu'une fonction-clé de
  chacun des 17 modules JS est présente dans le HTML servi.
- Coverage globale : **86.6% lignes / 77.6% branches / 92.7% méthodes**.
- AspxLint.Core : **97% lignes**.

### Changed

- **`<% %>` masking centralisé** dans `RuleHelpers.MaskAspBlocks` /
  `MaskAndSplit` / `MaskAndSplitFull` — preserve `\n` et `\r` (sinon Split
  fusionnait des lignes), atomic group `(?>(?:<%...%>|[^>]))*?` pour
  empêcher backtrack sur les balises avec `<%= %>` à l'intérieur. Toutes
  les règles HTML utilisent désormais ces helpers.
- Dashboard **modulaire** : `index.html` n'inclut plus de gros blocs
  inline. CSS, partials de modaux, et 17 modules JS sont des fichiers
  séparés concaténés au runtime via `{{include:NAME}}`.
- `app.js` (3460 lignes) découpé en **17 modules** linéaires
  (`modules/01-state.js` … `modules/17-desktop-sse.js`), chacun 100-400
  lignes, scope global commun. Aucun changement de comportement
  (concaténation = fichier d'origine à l'octet près).
- **Default theme** : "VS Code Dark" (avant : "Default" jaune-vert) —
  passe mieux dans WebView / VS Code.
- **Mobile / responsive** : touch targets 36px min, code-actions et
  footer-actions en scroll horizontal sur < 600px (au lieu de wrap), modal
  à 92vh + 95% width, mini-map et split cachés < 900px, brand-name caché
  < 380px, feedback `:active` (scale 0.97) sur pointeurs tactiles.

### Fixed

- **Auto-fix corruption** sur TAG-001 / FORM-001 / ASP-001 — les regex
  `[^>]*?` matchaient le `>` de `%>` quand un attribut contenait
  `<%= ... %>`, ce qui injectait du HTML à l'intérieur d'un bloc serveur.
  Atomic group sur `RuleHelpers.TagInnerPattern` corrige.
- **CHAR-001 false positives** sur le contenu de `<script>` / `<style>`
  (`&&` en JS) et sur les query parameters d'URL (`?a=1&b=2&item-url=...`)
  — `MaskAndSplitFull` masque scripts/styles/HTML comments, et la regex
  CHAR-001 a un negative-lookahead `(?![\w-]+\s*=)` pour skipper les
  paramètres (y compris ceux avec tirets).
- **Disable inline `<%-- ... <%= x %> --%>`** — le regex non-greedy
  matchait jusqu'au premier `%>` (l'interpolation), exposant le reste
  du commentaire. Ajout de `<%--[\s\S]*?--%>` en première alternative
  dans `AspBlock`.
- **`<br>` après auto-fix `<asp:Label></asp:Label>`** — l'absence
  d'attributs faisait sortir `<asp:Labelrunat="server">` (espace manquant)
  → tag invalide → re-detection à l'infini. Strip whitespace + ajout
  espace systématique avant `runat`.
- **Téléchargement silencieusement KO sur Firefox** — `.click()` refusé
  sur un `<a>` détaché. Fix : `appendChild` avant click + `removeChild`
  après 100ms.
- **Historique perdu à chaque ré-analyse** — `fixed` flag sur les issues
  remplacé par `file.history[]` qui persiste, alimenté par diff
  avant/après fix.
- **Boucle infinie dans `highlightLine`** — un `<` ou `&` non-tokenisable
  ne consommait pas de caractères. Fix : `let j = i + 1` au lieu de
  `i` dans la branche texte ordinaire.
- **Tokenizer auto-collision** — l'ancien `highlightLine` chaînait des
  `replace` sur du HTML déjà échappé. Réécrit en single-pass
  (`highlightLine` + `highlightTag`) qui n'échappe les tokens qu'au
  moment de l'émission.
- **Modal "Coller votre code" affiché en permanence** — `.modal-overlay`
  manquait de `display: none` par défaut. Ajout du base CSS.

### Removed

- Plus de duplication JS de l'engine de règles — toutes les règles vivent
  exclusivement dans `AspxLint.Core` (C#), single source of truth (déjà
  fait en 0.2.0, confirmé pour 0.3.0).

### Migration notes

- Aucune action requise pour les utilisateurs existants. La signature
  publique du CLI, les noms de règles, le format SARIF / JSON sont
  inchangés.
- Si tu construisais un consommateur de l'API HTTP : les nouveaux
  endpoints sont additifs, et l'auth supporte toujours cookie + bearer.
- Si tu hébergeais la dashboard via `aspx_lint_dashboard.html` (legacy
  pre-0.2.0) : passe à `dotnet run --project src/AspxLint.Server` ou au
  `.exe` Desktop.

## [0.2.1] - 2026-05-06

### Added (Phase 2 — server hostable)

- Three new env vars to configure a hosted server :
  - `ASPXLINT_API_KEY` — fixed bearer token (vs. random per boot).
  - `ASPXLINT_ALLOWED_ROOT` — confines `/api/scan`, `/api/save`, `/api/restore`
    to a specific directory tree. Out-of-scope returns 403.
  - `ASPXLINT_READ_ONLY` — when `true`, `/api/save` and `/api/restore`
    return 403, leaving only the lint-only surface.
- `ServerStartOptions.ApiKey`, `AllowedRoot`, `ReadOnly` (programmatic config).
- `ServerSession.IsUnderAllowedRoot(string)` helper (path scoping check).
- **Dockerfile** (multi-stage, ~150 MB image, non-root user).
- `docker-compose.yml` for local dev, mounts `./` as `/workspace:ro` by default.
- `.dockerignore` excluding tests / coverage / IDE / docs.
- New workflow `.github/workflows/docker.yml` building and pushing
  `ghcr.io/hl-n-a/claude-aspx-lint` on every push to `main` and on tag `v*`,
  multi-arch (`linux/amd64` + `linux/arm64`), with `/healthz` smoke-test
  before publishing.
- 8 new integration tests in `ConfiguredApiTests.cs` covering
  `ASPXLINT_API_KEY`, `ASPXLINT_ALLOWED_ROOT` (scan-allow, scan-block,
  subdir-allow, traversal-block) and `ASPXLINT_READ_ONLY`
  (save-block, restore-block, scan-still-allowed).

### Fixed

- Pinned MinVer to 6.0.0 (6.1+ targets `net10.0`, doesn't launch on x64
  .NET 9 dev box). Added explicit ignore in `dependabot.yml`.

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

[Unreleased]: https://github.com/hl-n-a/claude-aspx-lint/compare/v0.4.0...HEAD
[0.4.0]: https://github.com/hl-n-a/claude-aspx-lint/compare/v0.3.0...v0.4.0
[0.3.0]: https://github.com/hl-n-a/claude-aspx-lint/compare/v0.2.1...v0.3.0
[0.2.1]: https://github.com/hl-n-a/claude-aspx-lint/compare/v0.2.0...v0.2.1
[0.2.0]: https://github.com/hl-n-a/claude-aspx-lint/compare/v0.1.0...v0.2.0
[0.1.1]: https://github.com/hl-n-a/claude-aspx-lint/compare/v0.1.0...v0.1.1
[0.1.0]: https://github.com/hl-n-a/claude-aspx-lint/releases/tag/v0.1.0
