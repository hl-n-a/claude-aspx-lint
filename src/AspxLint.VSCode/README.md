# aspx-lint pour VS Code

Diagnostics et auto-fixes en temps réel pour les fichiers ASP.NET Web Forms
(`.aspx`, `.ascx`, `.master`, `.asax`) et `Web.config`. **35 règles**, **22
auto-fixables**.

## Pré-requis

L'extension shell-out vers le CLI `aspx-lint`. Installe-le globalement :

```bash
dotnet tool install --global aspx-lint
```

(Nécessite [.NET SDK 9.0+](https://dotnet.microsoft.com/download).)

L'extension le détecte automatiquement dans `PATH`. Si tu veux pointer vers
un binaire spécifique, configure `aspxLint.path` dans `settings.json`.

## Fonctionnalités

- **Diagnostics inline** sur ouverture et sauvegarde des fichiers supportés.
- **Code actions** (Ctrl+. / Cmd+.) → applique l'auto-fix d'une règle.
- **Commandes** (Ctrl+Shift+P) :
  - `aspx-lint: Scan workspace` — rapport complet du dossier ouvert
  - `aspx-lint: Fix current file` — applique tous les auto-fixes
  - `aspx-lint: Show output` — ouvre le panneau de logs

## Configuration

| Setting | Default | Description |
|---|---|---|
| `aspxLint.path` | `aspx-lint` | Path vers le binaire (utilise PATH si non absolu) |
| `aspxLint.lintOnSave` | `true` | Lint sur Ctrl+S |
| `aspxLint.lintOnType` | `false` | Lint debounced 500ms en frappe (off par défaut, ↗ CPU) |
| `aspxLint.severityLevel` | `info` | Sévérité minimum affichée (`error`, `warning`, `info`) |

## Configuration par projet

Place un `.aspxlintrc.json` à la racine du projet (le CLI le détecte
automatiquement en remontant l'arborescence) :

```json
{
  "ignore": ["**/Generated/**"],
  "rules": {
    "TAG-003": "off",
    "STYLE-001": "info"
  }
}
```

## Disable inline

```aspx
<%-- aspx-lint disable TAG-003 --%>
<div>... ligne suivante ignorée pour TAG-003 ...</div>

<%-- aspx-lint disable-line TAG-003,ATTR-002 --%>
<%-- aspx-lint disable-file --%>
```

## Catégories de règles

- **TAG / ATTR** : balises XHTML, casse, équilibre, attributs (10 règles)
- **ASP** : contrôles serveur (`runat="server"`, IDs, ContentPlaceHolder) (5)
- **WS** : whitespace, BOM, indentation, sauts de ligne (6)
- **SEC** : ViewState, tabnabbing, URLs locales (3)
- **A11Y / STYLE / SCRIPT** : accessibilité, anti-patterns inline (3)
- **DIR / DOC / FORM / SM / CHAR / COM** : directives, DOCTYPE, formulaires, etc. (6)
- **CFG** : Web.config (debug, customErrors, trace, sessions, secrets) (6)

## Liens

- Code source : <https://github.com/hl-n-a/claude-aspx-lint>
- CLI sur NuGet : <https://www.nuget.org/packages/aspx-lint>
- Stats site : <https://hl-n-a.github.io/claude-aspx-lint/>
- Liste complète des règles : <https://github.com/hl-n-a/claude-aspx-lint#règles-35-au-total>

## Licence

MIT.
