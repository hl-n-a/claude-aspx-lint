# aspx-lint

[![CI](https://github.com/hl-n-a/claude-aspx-lint/actions/workflows/dotnet.yml/badge.svg)](https://github.com/hl-n-a/claude-aspx-lint/actions/workflows/dotnet.yml)
[![codecov](https://codecov.io/gh/hl-n-a/claude-aspx-lint/graph/badge.svg)](https://codecov.io/gh/hl-n-a/claude-aspx-lint)
[![NuGet](https://img.shields.io/nuget/v/aspx-lint.svg)](https://www.nuget.org/packages/aspx-lint/)
[![License](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)
[![Coverage report](https://img.shields.io/badge/coverage-browse-d4ff3a)](https://hl-n-a.github.io/claude-aspx-lint/)

Linter et auto-fixer pour fichiers **ASP.NET Web Forms** (`.aspx`, `.ascx`,
`.master`, `.asax`). 23 règles couvrant directives de page, balises XHTML,
contrôles serveur, indentation, encodage, sécurité ViewState.

Trois manières de l'utiliser :

| Forme | Pour qui | Install |
|---|---|---|
| **CLI** `aspx-lint` | CI, scripts, lint local | `dotnet tool install -g aspx-lint` |
| **Dashboard Web** | Inspection ponctuelle, mobile, équipe | Servie par `AspxLint.Server` (`/`) |
| **App desktop** | Pairing tél / desktop, tray Windows | `dotnet run --project src/AspxLint.Desktop` |

---

## CLI

### Installation

```bash
dotnet tool install --global aspx-lint
```

### Usage

```bash
aspx-lint scan ./MyWebFormsProject                    # rapport texte
aspx-lint scan ./MyWebFormsProject --json             # JSON pour pipeline
aspx-lint scan ./MyWebFormsProject --sarif            # SARIF pour Code Scanning
aspx-lint scan . --severity error                     # filtre niveau

aspx-lint fix  ./MyWebFormsProject --dry-run          # voir ce qui serait corrigé
aspx-lint fix  ./MyWebFormsProject                    # appliquer
aspx-lint fix  . --rule WS-001                        # une seule règle
```

Codes de sortie :
- `0` ok (scan clean / fix appliqué)
- `1` issues détectées ou usage incorrect
- `2` erreur d'exécution (path absent, IO, etc.)

### Intégration GitHub Actions — version 1 ligne (composite action)

```yaml
permissions:
  contents: read
  security-events: write

steps:
  - uses: actions/checkout@v4
  - uses: actions/setup-dotnet@v4
    with:
      dotnet-version: 9.0.x
  - uses: hl-n-a/claude-aspx-lint/.github/actions/scan@v0.1.0
    with:
      path: src/Web
      severity: error          # PR rouge si une issue error+
```

Détails des inputs : voir [.github/actions/scan/README.md](.github/actions/scan/README.md).

### Intégration GitHub Actions — version manuelle

```yaml
- run: dotnet tool install --global aspx-lint
- run: aspx-lint scan . --sarif > aspx-lint.sarif
  continue-on-error: true
- uses: github/codeql-action/upload-sarif@v3
  with:
    sarif_file: aspx-lint.sarif
```

Les findings apparaissent dans l'onglet *Security → Code scanning alerts* du repo.

---

## Dashboard Web

La dashboard est un site statique (`src/AspxLint.Web/index.html`) consommé
par tout frontend. Elle ne tourne **que servie par AspxLint.Server** (l'analyse
et les fixes sont délégués au moteur C#, plus de duplication JS / C#).

```bash
dotnet run --project src/AspxLint.Server
# Ouvre l'URL Local + token affichée en console
```

Si vous voulez aussi inspecter / corriger depuis votre téléphone, lancez
plutôt l'app desktop (ci-dessous) qui embarque le serveur dans un .exe et
expose un QR code pour pairer le tél.

## API HTTP

Le serveur expose un contrat REST consommé par tous les frontends (Web
dashboard, Desktop, futures extensions Chrome/VS) :

```
GET  /                  → dashboard HTML
GET  /healthz           → no auth, healthcheck
GET  /api/rules         → liste des 23 règles (id, name, severity, hasFix)
POST /api/scan          → scan récursif d'un dossier (path → issues + content)
POST /api/analyze       → analyse d'un contenu inline (content + ext)
POST /api/fix           → applique un fix d'une règle (content + ext + ruleId)
POST /api/fix-all       → applique tous les fixes auto-fixables
POST /api/save          → écrit sur disque (allowlist + .bak)
POST /api/restore       → restaure depuis .bak
```

**Auth** : token bearer, accepté via `?token=`, cookie `aspx_lint_token`,
ou header `Authorization: Bearer <token>`. Le token est régénéré à chaque
démarrage du serveur, affiché en console.

**CORS** : ouvert (origin réflexif + credentials), prêt pour les frontends
hébergés ailleurs ou les extensions browser.

**OpenAPI** : la spec est exposée à `/swagger/v1/swagger.json` et l'UI Swagger
à `/swagger` (sans auth, pour faciliter l'intégration).

---

## Déploiement Docker

L'image officielle est publiée sur GHCR à chaque push sur `main` et tag `vX.Y.Z` :

```bash
docker run --rm -d \
    --name aspx-lint \
    -p 5173:5173 \
    -e ASPXLINT_API_KEY=ma-cle-secrete \
    -e ASPXLINT_ALLOWED_ROOT=/workspace \
    -v /chemin/vers/projets:/workspace \
    ghcr.io/hl-n-a/claude-aspx-lint:latest
```

Puis : `http://localhost:5173/?token=ma-cle-secrete`

### Variables d'environnement

| Variable | Défaut | Description |
|---|---|---|
| `ASPXLINT_API_KEY` | aléatoire au boot | Token bearer accepté par tous les endpoints (sauf `/healthz` et `/swagger`). Pose-le pour avoir une clé stable. |
| `ASPXLINT_ALLOWED_ROOT` | aucun (libre) | Confine les paths manipulés (scan/save/restore) à ce dossier. Hors-scope = 403. **Indispensable en hosting public.** |
| `ASPXLINT_READ_ONLY` | `false` | Si `true`, `/api/save` et `/api/restore` renvoient 403. Mode lecture seule. |

### docker-compose

Un [`docker-compose.yml`](docker-compose.yml) prêt-à-l'emploi est fourni :

```bash
docker compose up
```

Par défaut il monte le dossier courant en `/workspace:ro` (lecture seule, donc
pas de save ni de restore possible). Ôte le `:ro` du volume pour activer
l'écriture.

### Tags d'image disponibles

| Tag | Pointe sur |
|---|---|
| `latest` | dernier commit sur `main` |
| `main` | idem |
| `0.2.0`, `0.2`, `0` | release semver |
| `sha-abc1234` | commit précis (immuable) |

Le workflow [docker.yml](.github/workflows/docker.yml) build en multi-arch
(`linux/amd64` + `linux/arm64`) et fait un smoke test (`/healthz`) avant
de pousser.

---

## App desktop (Windows)

**Option 1 — `.exe` self-contained (zéro install, ~86 Mo)**
Téléchargez `aspx-lint-desktop-X.Y.Z-win-x64.exe` depuis la
[dernière release GitHub](../../releases/latest) et double-cliquez.

**Option 2 — depuis les sources** (nécessite .NET 9 SDK)

```bash
dotnet run --project src/AspxLint.Desktop
```

Dans les deux cas :
- Une icône s'installe dans le tray Windows
- Clic droit → **Afficher le QR code**, scannez-le depuis votre tél (même Wi-Fi)
- Le dashboard est servi en HTTP local avec un token d'auth régénéré à chaque
  démarrage
- Endpoints : `/api/scan`, `/api/save` (avec backup `.bak`), `/api/restore`

---

## Règles (23 au total)

| ID | Catégorie | Sévérité | Auto-fix |
|---|---|---|---|
| DIR-001 | Directive `@Page`/`@Control`/`@Master` | error | ✓ |
| TAG-001 | Balise auto-fermante non XHTML (`<br>` → `<br />`) | warning | ✓ |
| TAG-002 | Casse incohérente des balises HTML | warning | ✓ |
| TAG-003 | Balises non équilibrées | error | — |
| ATTR-001 | Attribut sans guillemets | warning | ✓ |
| ATTR-002 | `attr='val'` (simple quote) → double | info | ✓ |
| ATTR-003 | Attribut dupliqué | error | — |
| ASP-001 | `<asp:...>` sans `runat="server"` | error | ✓ |
| ASP-002 | ID de contrôle dupliqué | error | — |
| ASP-003 | `ContentPlaceHolder` sans ID | error | — |
| ASP-004 | `<asp:Content>` sans `ContentPlaceHolderID` | error | — |
| ASP-005 | Espaces manquants dans `<%= … %>` | warning | ✓ |
| WS-001 | Espaces en fin de ligne | info | ✓ |
| WS-002 | Indentation mixte tabs / espaces | warning | ✓ |
| WS-003 | Plus de 2 lignes vides consécutives | info | ✓ |
| WS-004 | Pas de `\n` final | info | ✓ |
| WS-005 | BOM UTF-8 en début de fichier | warning | ✓ |
| CHAR-001 | `&` non échappé | warning | — |
| COM-001 | `--` à l'intérieur de `<!-- -->` | warning | — |
| SEC-001 | `EnableViewStateMac="false"` | error | ✓ |
| DOC-001 | DOCTYPE manquant (ASPX standalone) | warning | ✓ |
| FORM-001 | `<form>` sans `runat="server"` | error | ✓ |
| SM-001 | Plusieurs `<asp:ScriptManager>` | error | — |

**15 règles auto-fixables, 8 manuelles** (renommages, restructurations).

---

## Build depuis les sources

```bash
dotnet build
dotnet test                                     # 300 tests, ~8 s
dotnet run --project src/AspxLint.Cli -- scan tests/fixtures
```

### Composition

| Projet | Rôle |
|---|---|
| `AspxLint.Core` | Moteur de règles, scanner, modèle d'`Issue` |
| `AspxLint.Cli` | exe `aspx-lint` (text / JSON / SARIF) |
| `AspxLint.Server` | ASP.NET Core 9, sert le dashboard + 5 endpoints HTTP |
| `AspxLint.Desktop` | WPF + tray Windows + WebView du dashboard |

### Couverture

```bash
dotnet test --collect:"XPlat Code Coverage" --settings coverlet.runsettings --results-directory coverage/raw
dotnet reportgenerator "-reports:coverage/raw/**/coverage.cobertura.xml" "-targetdir:coverage/html" "-reporttypes:Html;TextSummary"
```

Actuellement **95.7 % lines** sur Core + Server.
Rapport HTML browsable en ligne : https://hl-n-a.github.io/claude-aspx-lint/
(deployé automatiquement par la CI sur push de `main`).

---

## Process de release

Une release est déclenchée par un push de tag `vX.Y.Z` :

```bash
git tag v0.2.0
git push origin v0.2.0
```

Le [workflow release.yml](.github/workflows/release.yml) exécute alors :
1. Build + tests (Core + Cli + Server) sur runner Windows
2. `dotnet pack` du CLI → `aspx-lint.X.Y.Z.nupkg`
3. `dotnet publish` du Desktop self-contained → `.exe` ~86 Mo
4. Création d'une **GitHub Release** avec les deux artefacts attachés et
   release-notes auto-générées
5. Push du `.nupkg` sur **NuGet.org** (nécessite secret repo `NUGET_API_KEY`)

Le workflow accepte aussi un déclenchement manuel via `workflow_dispatch`
(saisir une version) — utile pour tester sans pousser sur NuGet.

## Licence

MIT.
