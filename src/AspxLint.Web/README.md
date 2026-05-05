# AspxLint.Web

Front-end de la dashboard `aspx-lint`. Pour l'instant : un seul fichier HTML
vanilla JS (`index.html`), sans build step.

## Comment c'est servi ?

Le fichier est **embarqué comme `EmbeddedResource`** dans `AspxLint.Server.dll`
(cf. `src/AspxLint.Server/AspxLint.Server.csproj`). Au runtime, le serveur
sert le HTML via la route `GET /` :

1. **En dev** (`dotnet run --project src/AspxLint.Server`) : le serveur essaie
   d'abord de lire `src/AspxLint.Web/index.html` depuis le disque (en remontant
   les dossiers). Permet le **hot-reload** : modifie le HTML, refresh navigateur,
   pas de rebuild.

2. **En `.exe` self-contained** (`AspxLint.Desktop` ou Server publié) : pas de
   fichier sur disque, le serveur tombe sur la ressource embarquée. Un seul
   fichier livrable.

3. **Hébergé (Docker, cloud)** : idem, l'image contient la ressource embarquée.

## Frontends qui consomment l'API

Le HTML est **un consommateur parmi d'autres** de l'API HTTP exposée par
`AspxLint.Server` :

| Frontend | Status |
|---|---|
| `AspxLint.Web` (cette dashboard HTML) | ✅ |
| `AspxLint.Desktop` (WPF tray, ouvre la dashboard) | ✅ |
| `AspxLint.Cli` (terminal, ne sert pas de front, appelle Core directement) | ✅ |
| Extension Chrome (Manifest V3) | 📋 planifié |
| Extension Visual Studio (VSIX) | 📋 planifié |

Tous les frontends frappent les endpoints :

```
POST /api/scan       — scan récursif d'un dossier (renvoie issues + content)
POST /api/analyze    — analyse d'un contenu inline (path-less)
POST /api/fix        — applique un fix d'une règle sur un contenu inline
POST /api/fix-all    — applique tous les fixes auto-fixables sur un contenu
POST /api/save       — écrit un contenu sur disque (allowlist + .bak)
POST /api/restore    — restaure un fichier depuis son .bak
GET  /api/rules      — liste les 23 règles avec metadata
GET  /healthz        — sans auth, pour les health checks
```

## Si demain on passe en TS + Vite

Cette dossier accueillera :

```
src/AspxLint.Web/
├── package.json
├── vite.config.ts
├── tsconfig.json
├── src/
│   ├── main.ts
│   ├── components/
│   └── styles/
└── dist/                    (généré par `npm run build`, exclu de git)
```

Le build front (`npm run build`) sortirait dans `dist/`, et Server.csproj
embarquerait le contenu de `dist/` au lieu de `index.html`.
