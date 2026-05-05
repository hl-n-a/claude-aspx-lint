# aspx-lint scan — composite action

Linte tes fichiers ASP.NET Web Forms (`.aspx`, `.ascx`, `.master`) et publie
le rapport dans GitHub Code Scanning, en une ligne de YAML.

## Usage minimal

```yaml
- uses: hlabaste/aspx-lint/.github/actions/scan@v0.1.0
```

## Usage avec options

```yaml
- uses: hlabaste/aspx-lint/.github/actions/scan@v0.1.0
  with:
    path: src/Web                  # default: .
    severity: error                # echec si une issue >= severity (vide = jamais)
    output: aspx-lint.sarif        # nom du fichier SARIF
    upload-sarif: 'true'           # default true
    version: '0.1.0'               # pin une version (vide = derniere)
```

## Permissions requises

```yaml
permissions:
  contents: read
  security-events: write   # pour upload-sarif
```

## Pre-requis

Le runner doit avoir `dotnet` installe (>= .NET 9). Sur les runners GitHub
hostes (`ubuntu-latest`, `windows-latest`, `macos-latest`), .NET est
preinstalle. Sinon, ajoute `actions/setup-dotnet@v4` avant cette action.

## Exemple complet

```yaml
name: Lint Web Forms

on:
  pull_request:
  push:
    branches: [main]

jobs:
  aspx-lint:
    runs-on: ubuntu-latest
    permissions:
      contents: read
      security-events: write
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: 9.0.x
      - uses: hlabaste/aspx-lint/.github/actions/scan@v0.1.0
        with:
          path: src/Web
          severity: error
```

Sur PR : les findings apparaissent en commentaires inline + dans l'onglet
*Security → Code scanning alerts* du repo. La PR echoue si une issue de
severite `error` est detectee.
