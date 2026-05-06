# aspx-lint.MSBuild

Intégration MSBuild pour [aspx-lint](https://www.nuget.org/packages/aspx-lint).
Ajoute ce package à ton csproj et **`dotnet build` lint automatiquement** les
fichiers ASP.NET Web Forms du projet (`.aspx`, `.ascx`, `.master`, `.asax`,
`Web.config`). Les issues remontent comme des warnings MSBuild classiques,
visibles dans la liste d'erreurs Visual Studio et dans la sortie CI.

## Pré-requis

Le package shell-out vers le CLI `aspx-lint`. Installe-le une fois
globalement :

```bash
dotnet tool install --global aspx-lint
```

## Installation

Dans le csproj du projet web :

```xml
<ItemGroup>
  <PackageReference Include="aspx-lint.MSBuild" Version="0.3.0">
    <PrivateAssets>all</PrivateAssets>
  </PackageReference>
</ItemGroup>
```

`PrivateAssets="all"` empêche le package de se propager aux consommateurs
(c'est un outil de dev, pas une dépendance runtime).

## Configuration

Toutes les options s'override via une `<PropertyGroup>` du csproj :

| Propriété | Défaut | Description |
|---|---|---|
| `AspxLintEnabled` | `true` | Active / désactive l'execution |
| `AspxLintFailOnSeverity` | `error` | Sévérité min qui fait échouer la build (`error`, `warning`, `info`) |
| `AspxLintScanPath` | `$(MSBuildProjectDirectory)` | Dossier scanné |
| `AspxLintExecutable` | `aspx-lint` | Path du binaire (par défaut cherche dans PATH) |
| `AspxLintQuiet` | `false` | Mode silencieux : juste le summary |

Exemples courants :

```xml
<!-- Lint mais ne pas faire échouer la build (mode soft) -->
<PropertyGroup>
  <AspxLintFailOnSeverity>info</AspxLintFailOnSeverity>
  <AspxLintQuiet>true</AspxLintQuiet>
</PropertyGroup>
```

```xml
<!-- Lint uniquement en CI, jamais en dev local -->
<PropertyGroup>
  <AspxLintEnabled Condition="'$(CI)' != 'true'">false</AspxLintEnabled>
</PropertyGroup>
```

```xml
<!-- Lint un sous-dossier précis -->
<PropertyGroup>
  <AspxLintScanPath>$(MSBuildProjectDirectory)\Views</AspxLintScanPath>
</PropertyGroup>
```

## Configuration projet (.aspxlintrc.json)

Le CLI charge automatiquement un fichier `.aspxlintrc.json` en remontant
l'arborescence depuis le dossier scanné. Pas besoin de le configurer dans
MSBuild :

```json
{
  "ignore": ["**/Generated/**"],
  "rules": {
    "TAG-003": "off",
    "STYLE-001": "info"
  }
}
```

## Codes de sortie

Le target convertit les exit codes du CLI en logs MSBuild :

- `0` (clean) → message `aspx-lint : OK`
- `1` (issues détectées ≥ AspxLintFailOnSeverity) → erreur MSBuild → build échoue
- `2` (erreur d'exécution) → erreur MSBuild → build échoue

## Skip d'une règle

Comme partout dans aspx-lint, les directives inline sont reconnues :

```aspx
<%-- aspx-lint disable TAG-003 --%>
<div>... ligne suivante ignorée ...</div>

<%-- aspx-lint disable-file --%>
<%-- ... fichier entier ignoré ... --%>
```

## Liens

- CLI : <https://www.nuget.org/packages/aspx-lint>
- Code source : <https://github.com/hl-n-a/claude-aspx-lint>
- Stats site : <https://hl-n-a.github.io/claude-aspx-lint/>
- Liste des règles : <https://github.com/hl-n-a/claude-aspx-lint#règles-35-au-total>

## Licence

MIT.
