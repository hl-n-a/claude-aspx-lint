# Contribuer à aspx-lint

Merci d'envisager une contribution. Ce guide couvre tout ce dont tu as besoin
pour développer en local, ajouter une règle, écrire des tests, et soumettre
une PR. Lecture estimée : 5 minutes.

> **TL;DR** — `dotnet build && dotnet test`. La règle d'or : **idempotence**
> (un fix exécuté deux fois doit donner le même résultat qu'une fois).

---

## Pré-requis

- [.NET SDK 9.0+](https://dotnet.microsoft.com/download) (le repo cible
  `net9.0`, mais le SDK 10 preview marche aussi)
- Git
- Un éditeur (VS Code + extension C# Dev Kit, Rider, ou Visual Studio 2022)

Pour le Desktop seulement : Windows 10/11 (WPF + WebView2 — pas de cross-platform).

---

## Lancer en local

```bash
git clone https://github.com/hl-n-a/claude-aspx-lint.git
cd claude-aspx-lint

# Build + tests
dotnet build
dotnet test                          # ~100 tests, < 5s

# CLI
dotnet run --project src/AspxLint.Cli -- scan ./tests/fixtures

# Dashboard web (puis ouvrir l'URL imprimée en console)
dotnet run --project src/AspxLint.Server

# Desktop (Windows uniquement)
dotnet run --project src/AspxLint.Desktop
```

En mode dev, le serveur **lit la dashboard depuis le disque** (`src/AspxLint.Web/`),
donc les modifs HTML/CSS/JS sont prises en compte au refresh navigateur — pas
besoin de rebuild.

---

## Architecture

```
aspx_lint/
├── src/
│   ├── AspxLint.Core/          ← le linter pur, zéro dépendance ASP.NET
│   │   ├── Rules/              ← 29 règles, une classe par règle
│   │   ├── RuleHelpers.cs      ← masking utilities (CRITIQUE)
│   │   ├── Analyzer.cs         ← orchestrateur (rule loop + IssueFilter)
│   │   ├── ProjectScanner.cs   ← scan séquentiel / parallèle / incrémental
│   │   └── AspxLintConfig.cs   ← .aspxlintrc.json loader
│   ├── AspxLint.Cli/           ← le binaire `aspx-lint`
│   ├── AspxLint.Server/        ← ASP.NET Core minimal API
│   ├── AspxLint.Web/           ← dashboard HTML/CSS/JS modulaire
│   │   ├── index.html          ← shell avec {{include:}} markers
│   │   ├── styles.css
│   │   ├── partials/           ← fragments DOM (modaux)
│   │   └── modules/            ← 17 modules JS concaténés au runtime
│   └── AspxLint.Desktop/       ← WPF + WebView2 (Windows only)
└── tests/
    ├── AspxLint.Core.Tests/    ← rules + helpers + config + filters
    ├── AspxLint.Cli.Tests/     ← formatters, error paths, sous-commandes
    ├── AspxLint.Server.Tests/  ← endpoints HTTP via WebApplicationFactory
    └── AspxLint.E2E.Tests/     ← Playwright (optionnel, navigateur requis)
```

`AspxLint.Core` est **le seul** projet qui contient la logique de linting.
Le CLI, le serveur, l'extension VSCode (à venir) et l'extension Chrome (à
venir) sont tous des consommateurs. Toute règle, tout helper de masking,
toute règle de fix vit dans Core.

---

## Ajouter une règle

1. Choisir un ID dans une catégorie existante (`TAG-`, `ATTR-`, `ASP-`,
   `WS-`, `DIR-`, `CHAR-`, `COM-`, `SEC-`, `A11Y-`, `STYLE-`, `SCRIPT-`,
   `DOC-`, `FORM-`, `SM-`, `MASTER-`) ou en créer une nouvelle.

2. Créer `src/AspxLint.Core/Rules/Xxx999MyRule.cs` :

   ```csharp
   public sealed class Xxx999MyRule : IRule
   {
       public string Id => "XXX-999";
       public string Name => "Description courte";
       public Severity Severity => Severity.Warning;
       public string Description => "Explication détaillée affichée dans la dashboard.";
       public bool HasFix => true;  // false si fix manuel obligatoire

       public IEnumerable<Issue> Detect(string content, string[] lines, RuleContext ctx)
       {
           // Toujours masquer les blocs <% %> avant de regex sur du contenu HTML.
           var (masked, _) = RuleHelpers.MaskAndSplit(content);
           // ... ton détecteur, yield des Issue
       }

       public string? Fix(string content, RuleContext ctx)
       {
           // Retourne null si non-fixable, sinon le content corrigé.
           return RuleHelpers.FixOutsideAspBlocks(content, /* regex */, /* replacement */);
       }
   }
   ```

3. Enregistrer dans `src/AspxLint.Core/RuleRegistry.cs` (`All` array).

4. Ajouter la traduction anglaise dans
   `src/AspxLint.Core/Translations.cs`.

5. Écrire les tests dans `tests/AspxLint.Core.Tests/` (au minimum :
   1 test détection positive, 1 test détection négative, 1 test fix
   idempotent si auto-fixable, 1 test edge case).

6. Mettre à jour le tableau dans `CLAUDE.md` et `README.md`.

7. Mettre à jour les compteurs hardcodés dans les tests (`Fixable_count_is_X`,
   `All_X_rules_registered`, etc. — `dotnet test` te dira lesquels casser).

### La règle d'or : idempotence

```csharp
// Doit toujours passer
var fixed1 = rule.Fix(input, ctx);
var fixed2 = rule.Fix(fixed1, ctx);
Assert.Equal(fixed1, fixed2);
```

Si ton fix re-déclenche la détection au pass suivant, tu vas créer une boucle
infinie côté CLI (`fix` boucle jusqu'à 5 passes pour converger). Toujours tester
l'idempotence.

### Ne PAS toucher au tokenizer sans tester

Le tokenizer dashboard (`src/AspxLint.Web/modules/04-highlight.js`) est en
single-pass volontaire. Les cas piégeux à valider obligatoirement après toute
modification :

- `<DIV class="container">` (casse mixte)
- `<!-- commentaire avec class="x" -->`
- `<a href="?a=1&amp;b=2">` (entités HTML)
- `<input type=text>` (attribut sans guillemets)
- `<` isolé, `&` isolé (tokens incomplets)
- `<asp:Label ID="x"><%= DateTime.Now %></asp:Label>` (interpolation imbriquée)
- Round-trip : retirer tous les `<span>` du résultat doit redonner l'entrée
  exacte.

Cf. `CLAUDE.md` § "Bugs résolus" pour le contexte.

---

## Ajouter un endpoint serveur

1. Définir le record du payload dans
   `src/AspxLint.Server/ServerHost.cs` (à côté des `ScanRequest` etc.).

2. Ajouter le handler dans `MapRoutes()` du même fichier. Convention :
   - Vérifier l'authentification (cookie `aspx_lint_token` ou header
     `Authorization: Bearer ...`) — c'est implicite via le middleware.
   - Pour les actions qui touchent le disque, vérifier l'allowlist
     (`session.IsPathAllowedForWrite(path)`) — sinon 403.
   - Retourner du JSON avec `Results.Json(...)`.

3. Tests dans `tests/AspxLint.Server.Tests/` (créer un nouveau fichier ou
   étendre un existant). Utiliser `ApiFixture` qui fournit un client
   pré-authentifié (`_fx.CreateAuthClient()`).

---

## Ajouter un module dashboard

1. Créer `src/AspxLint.Web/modules/NN-nom.js` (numéroter pour figer l'ordre
   de chargement).

2. Ajouter un `<EmbeddedResource>` dans
   `src/AspxLint.Server/AspxLint.Server.csproj` avec
   `LogicalName="AspxLint.Web.modules.NN-nom.js"` (les `/` deviennent `.`).

3. Ajouter `{{include:modules/NN-nom.js}}` dans le `<script>` à la fin
   d'`src/AspxLint.Web/index.html`.

4. Étendre `DashboardHtmlTests.Dashboard_includes_module` avec une
   fonction-clé du module pour vérifier qu'il est bien injecté.

Tous les modules partagent le même scope global — aucune notion d'ES module,
pas d'import/export, pas de bundler. C'est volontaire (cf. CLAUDE.md).

---

## Tests

```bash
dotnet test                                   # toute la suite (~100 tests)
dotnet test tests/AspxLint.Core.Tests         # juste les règles
dotnet test --filter "FullyQualifiedName~TAG_001"  # une règle précise
```

### Coverage

```bash
dotnet test --collect:"XPlat Code Coverage" --results-directory TestResults
dotnet tool install -g dotnet-reportgenerator-globaltool --version 5.3.11 --framework net9.0
reportgenerator -reports:"TestResults/**/coverage.cobertura.xml" \
                -targetdir:coverage-report -reporttypes:Html
# Ouvre coverage-report/index.html
```

Cibles actuelles : **86.6% lignes / 77.6% branches / 92.7% méthodes**.
Si une PR fait baisser la couverture globale de plus d'un point, ajouter
des tests pour combler le gap avant merge.

### Tests E2E (Playwright)

```bash
cd tests/AspxLint.E2E.Tests
pwsh bin/Debug/net9.0/playwright.ps1 install chromium
dotnet test
```

Les tests E2E ne tournent pas en CI (besoin d'un navigateur installé).
Ils sont là pour la validation manuelle avant release.

---

## Style de code

- **C#** : 4 espaces, brace style Allman (sur sa propre ligne), `var` quand
  le type est évident, pas de `this.` superflu. Éditeur recommandé : `dotnet
  format` avant commit (le projet n'a pas de hook formatant — on fait
  confiance).
- **JS** : 2 espaces, `const` par défaut, fonctions plutôt que classes,
  vanilla pur — **pas de framework, pas de bundler, pas d'import/export**.
- **Commentaires** : par défaut, pas de commentaire. N'écris un commentaire
  que pour expliquer le **POURQUOI** d'un choix non-évident (workaround,
  contrainte cachée, invariant subtil). Jamais le QUOI.
- **Messages de commit** : présent à l'impératif, première ligne ≤ 70 chars,
  préfixe par catégorie (`Linter:`, `Tests:`, `Dashboard:`, `Refactor:`,
  `Docs:`, `Fix:`).

---

## Workflow PR

1. Branche depuis `main` : `git checkout -b feat/ma-nouvelle-regle`.
2. Commits atomiques (un commit = un changement logique).
3. **Tests verts en local** : `dotnet test`.
4. Push + ouvre la PR. Le CI tourne `dotnet build`, `dotnet test`,
   coverage codecov, et publie les artefacts.
5. Review : si la PR ajoute une règle, on vérifie aussi qu'elle ne casse
   pas le scan d'un projet réel — il y a un test sur `tests/fixtures/` qui
   couvre une page Web Forms représentative.
6. Squash-merge dans `main`.

---

## Releases

Les releases sont taguées via `git tag v0.X.0` puis `git push --tags`.
Le workflow GitHub Actions `release.yml` build le binaire CLI, publie sur
NuGet (`aspx-lint`), et attache `AspxLint.Desktop.exe` à la release GitHub.

Avant de tagger : mettre à jour `CHANGELOG.md` avec les commits depuis le
dernier tag, regroupés par catégorie (Rules / CLI / Dashboard / Server /
Desktop / Tests / Docs).

---

## Code de conduite

Soyez courtois. Les commentaires de PR doivent porter sur le code, pas sur
la personne. Les désaccords techniques sont les bienvenus, les attaques
personnelles non.

---

## Contact

- Issues GitHub : <https://github.com/hl-n-a/claude-aspx-lint/issues>
- Discussions : <https://github.com/hl-n-a/claude-aspx-lint/discussions>
