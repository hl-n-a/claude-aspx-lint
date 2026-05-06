# AspxLint.Web

Front-end de la dashboard `aspx-lint`. Vanilla JS modulaire, sans build step.

## Structure

```
src/AspxLint.Web/
├── index.html                 ← shell HTML (~140 lignes)
├── styles.css                 ← thèmes + layout (~1500 lignes)
├── partials/                  ← fragments de DOM (modaux)
│   ├── modal-paste.html
│   ├── modal-browse.html
│   ├── modal-rules.html
│   ├── modal-palette.html
│   └── modal-batch-report.html
└── modules/                   ← logique JS, 17 modules concaténés
    ├── 01-state.js            État global, RULES, theme, persist
    ├── 02-files-tree.js       Modèle fichier, tree builder, filtres
    ├── 03-analysis.js         analyzeFile, runAnalysis, applyFix
    ├── 04-highlight.js        Tokenizer single-pass HTML/ASP.NET
    ├── 05-render-code.js      renderFileList, renderCode, edit, split
    ├── 06-minimap.js          Mini-map canvas style VS Code
    ├── 07-diff.js             LCS line + char diff
    ├── 08-edit-issues-stats.js Edit-in-place toggles, renderIssues, stats, trend
    ├── 09-actions.js          Recent files, selection, fixes, verify
    ├── 10-fileio-server.js    handleFiles, browse, save, restore, download
    ├── 11-bulk.js             Actions en lot + batch report
    ├── 12-modals-toast.js     Modaux paste/rules/demo + toast
    ├── 13-dragdrop.js         Drag-drop + folder drop heuristique
    ├── 14-search.js           Find/Replace (Ctrl+F / Ctrl+H)
    ├── 15-palette.js          Command palette (Ctrl+P)
    ├── 16-keyboard.js         Raccourcis globaux + navigation
    └── 17-desktop-sse.js      Bridge WebView2 + SSE + bootstrap
```

Les modules sont concaténés au runtime par `ServerHost.ExpandIncludes`
(remplace `{{include:modules/NN-name.js}}` dans `index.html` par le contenu
du fichier). Tous tournent dans le même scope global — aucune notion d'ES
module, pas d'import/export.

## Comment c'est servi ?

`index.html`, `styles.css`, les partials et chaque module sont **embarqués
comme `EmbeddedResource`** dans `AspxLint.Server.dll`
(cf. `src/AspxLint.Server/AspxLint.Server.csproj`). Au runtime, le serveur
sert le HTML via la route `GET /` :

1. **En dev** (`dotnet run --project src/AspxLint.Server`) : le serveur essaie
   d'abord de lire `src/AspxLint.Web/index.html` depuis le disque (en remontant
   les dossiers). Permet le **hot-reload** : modifie un module, refresh
   navigateur, pas de rebuild.

2. **En `.exe` self-contained** (`AspxLint.Desktop` ou Server publié) : pas de
   fichier sur disque, le serveur tombe sur les ressources embarquées. Un seul
   fichier livrable.

3. **Hébergé (Docker, cloud)** : idem, l'image contient les ressources.

## Conventions

- **Pas de framework**, pas de bundler. Vanilla JS pur.
- **Pas de localStorage par défaut** : la persistance est opt-in via le toggle
  "Persister" (cf. module `01-state.js`). La promesse "vos fichiers ne quittent
  jamais votre navigateur" tient.
- **Tokenizer** (`04-highlight.js`) : single-pass, jamais de chaînage de
  `replace` sur du HTML déjà échappé. Toute modification doit être validée
  sur les cas piégeux listés dans `CLAUDE.md`.
- **Idempotence** des fixes : exécuter un fix deux fois doit donner le même
  résultat qu'une fois.

## Frontends qui consomment l'API

Le HTML est **un consommateur parmi d'autres** de l'API HTTP exposée par
`AspxLint.Server` :

| Frontend | Status |
|---|---|
| `AspxLint.Web` (cette dashboard HTML) | ✅ |
| `AspxLint.Desktop` (WPF window, ouvre la dashboard) | ✅ |
| `AspxLint.Cli` (terminal, ne sert pas de front, appelle Core directement) | ✅ |
| Extension Chrome (Manifest V3) | 📋 planifié |
| Extension Visual Studio (VSIX) | 📋 planifié |

Tous les frontends frappent les endpoints :

```
POST /api/scan       — scan récursif d'un dossier (renvoie issues + content)
POST /api/analyze    — analyse d'un contenu inline (path-less)
POST /api/fix        — applique un fix d'une règle sur un contenu inline
POST /api/fix-one    — applique un fix sur une seule occurrence (line-local)
POST /api/fix-all    — applique tous les fixes auto-fixables sur un contenu
POST /api/save       — écrit un contenu sur disque (allowlist + .bak)
POST /api/restore    — restaure un fichier depuis son .bak
POST /api/read       — lit un fichier disque + analyse
GET  /api/browse     — liste un dossier (sous-dossiers + count .aspx)
GET  /api/find-folder — BFS heuristique pour retrouver un dossier
GET  /api/events     — Server-Sent Events (multi-clients live updates)
GET  /api/rules      — liste des règles avec metadata (`?lang=` pour i18n)
GET  /healthz        — sans auth, pour les health checks
```

## Ajouter un module

1. Créer `modules/NN-nom.js` (numéroter pour l'ordre de chargement).
2. Ajouter un `<EmbeddedResource>` dans `AspxLint.Server.csproj`, avec
   `LogicalName="AspxLint.Web.modules.NN-nom.js"` (les `/` deviennent `.`).
3. Ajouter `{{include:modules/NN-nom.js}}` dans le `<script>` à la fin
   d'`index.html`.
4. Optionnellement, étendre `DashboardHtmlTests.Dashboard_includes_module`
   avec une fonction-clé pour vérifier que le module est bien injecté.
