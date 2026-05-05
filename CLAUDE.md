# ASPX·LINT — Tableau de bord de formatage Web Forms

Outil de diagnostic pour les fichiers ASP.NET Web Forms (`.aspx`, `.ascx`,
`.master`). Analyse les problèmes de formatage, propose des corrections,
applique les auto-fixes, puis vérifie que les corrections tiennent.

## Stack

Single-page HTML, **zéro dépendance, 100 % local**. Tout tient dans
`aspx_lint_dashboard.html` :

- HTML + CSS dans le `<head>`
- Une seule balise `<script>` à la fin du `<body>`
- Aucune librairie externe (pas même jQuery)
- Polices Google Fonts via `<link>` (Fraunces, JetBrains Mono, Inter)

L'utilisateur ouvre le fichier directement dans son navigateur. Aucun
serveur, aucune build step, aucune télémétrie. Garder cette propriété.

## Architecture du script

```
RULES = [...]              // 23 règles, chacune { id, name, severity, desc, detect, fix? }
state = { files, currentFileId, filter, viewMode, fixedCount }

// Pipeline
addFile → runAnalysis → analyzeFile (boucle sur RULES) → file.issues

// Corrections (avec historique persistant)
applyFix(file, ruleId)     // applique le fix d'une règle, push dans file.history
applyAllFixes(file)        // boucle 5 passes max sur toutes les règles auto-fixables
applySingleFix(ruleId)     // wrapper UI
fixAllInCurrent()          // wrapper UI

// Rendu
renderAll → renderFileList + renderCode + renderIssues + renderStats
renderCode → si state.viewMode === 'diff', délègue à renderDiff
renderDiff → utilise lineDiff (LCS sur les lignes)

// Tokenizer single-pass (NE PAS toucher sans tester)
highlightLine + highlightTag — travaillent sur la chaîne brute,
n'échappent le HTML qu'au moment d'émettre chaque token.
```

## Modèle d'une règle

```js
{
  id: 'TAG-001',                      // préfixe par catégorie : TAG, ATTR, ASP, WS, DIR, CHAR, COM, SEC, DOC, FORM, SM, MASTER
  name: 'libellé court',
  severity: 'error' | 'warning' | 'info',
  desc: "explication détaillée affichée dans le panneau",
  detect: (content, lines, ctx) => [{ line, col, snippet, hint }],
  fix: (content, ctx) => newContent     // optionnel ; null si correction manuelle obligatoire
}
```

`ctx.ext` vaut `'aspx' | 'ascx' | 'master' | 'asax'`. Certaines règles
ne s'activent que pour un ext donné (DOC-001 ASPX uniquement, ASP-003
MASTER uniquement, ASP-004 page enfant, etc.).

## Inventaire des règles (23)

| ID        | Sévérité   | Auto-fix | Description courte                                |
|-----------|------------|----------|---------------------------------------------------|
| DIR-001   | error      | ✓        | Directive @Page/@Control/@Master absente/mal placée |
| TAG-001   | warning    | ✓        | Balise auto-fermante non XHTML (`<br>` → `<br />`) |
| TAG-002   | warning    | ✓        | Casse incohérente des balises HTML                 |
| TAG-003   | error      |          | Balises non équilibrées (manuel)                   |
| ATTR-001  | warning    | ✓        | Attribut sans guillemets                           |
| ATTR-002  | info       | ✓        | Mélange `'` / `"` dans les attributs               |
| ATTR-003  | error      |          | Attribut dupliqué dans une balise (manuel)         |
| ASP-001   | error      | ✓        | Contrôle serveur sans `runat="server"`             |
| ASP-002   | error      |          | ID de contrôle dupliqué (manuel)                   |
| ASP-003   | error      |          | ContentPlaceHolder sans ID (MASTER)                |
| ASP-004   | error      |          | Content sans ContentPlaceHolderID                  |
| ASP-005   | warning    | ✓        | Espaces manquants dans `<% %>`                     |
| WS-001    | info       | ✓        | Trailing whitespace                                |
| WS-002    | warning    | ✓        | Indentation mixte tabs+spaces                      |
| WS-003    | info       | ✓        | Plus de 2 lignes vides consécutives                |
| WS-004    | info       | ✓        | Pas de saut de ligne final                         |
| WS-005    | warning    | ✓        | BOM en début de fichier                            |
| CHAR-001  | warning    |          | `&` non échappé (manuel — risqué d'auto-fixer)     |
| COM-001   | warning    |          | `--` à l'intérieur d'un commentaire HTML           |
| SEC-001   | error      | ✓        | `EnableViewStateMac="false"`                       |
| DOC-001   | warning    | ✓        | DOCTYPE manquant (ASPX standalone uniquement)      |
| FORM-001  | error      | ✓        | `<form>` sans `runat="server"` dans ASPX           |
| SM-001    | error      |          | Plusieurs `<asp:ScriptManager>`                    |

15 règles ont un auto-fix, 8 nécessitent une correction manuelle (renommage
d'IDs, restructuration HTML, etc.).

## Bugs résolus (à ne pas régresser)

1. **Boucle infinie dans `highlightLine`** — un caractère `<` ou `&` qui
   ne matchait aucun token reconnu (ex. `Bonjour & bienvenue`) faisait
   tourner la boucle while sans progresser. Fix : `let j = i + 1;` au
   lieu de `let j = i;` dans la branche "texte ordinaire", pour
   garantir qu'on consomme au moins un caractère par tour.

2. **Tokenizer cassé par auto-collision** — l'ancien `highlightLine`
   chaînait des `replace` sur du HTML déjà échappé. Les regex d'attributs
   venaient mordre dans les `<span class="tk-...">` injectés
   précédemment, produisant du `>class ="tk-` parasite. Réécrit en
   tokenizer single-pass (voir `highlightLine` + `highlightTag`).

3. **ASP-001 / FORM-001 produisaient des tags invalides** — sur
   `<asp:Label></asp:Label>` (sans attributs), le fix sortait
   `<asp:Labelrunat="server">` (espace manquant), tag que la règle
   re-détectait à l'infini. Fix : strip whitespace de fin sur `attrs`,
   ajout systématique d'une espace avant `runat`.

4. **Téléchargement silencieusement KO** — Firefox refuse `.click()`
   sur un `<a>` détaché. Fix : `document.body.appendChild(a)` avant
   `click`, puis `removeChild` 100 ms plus tard.

5. **L'historique des corrections était perdu à chaque ré-analyse** —
   le flag `fixed` sur les issues disparaissait dès qu'`analyzeFile`
   était relancé. Remplacé par `file.history[]` qui persiste, alimenté
   par `applyFix` à partir des issues effectivement disparues entre
   avant/après.

## Idées d'amélioration (non implémentées)

- **Folder upload** via `webkitdirectory` — charger un projet entier
  d'un coup. Aujourd'hui on charge fichier par fichier.
- **Règles personnalisables** — laisser l'utilisateur désactiver des
  règles ou ajouter des règles custom (regex + remplacement).
- **Support .config / Web.config** — les bonnes pratiques de
  configuration ASP.NET sont parfois liées au formatage des pages.
- **Diff intra-ligne** — actuellement, une ligne légèrement modifiée
  apparaît comme `del + add`. Ce serait plus lisible avec un highlight
  caractère par caractère sur la ligne modifiée.
- **Export du fichier corrigé sous forme de patch unifié** plutôt qu'un
  `.fixed.aspx` complet.
- **Mode "lot"** — lancer "Tout corriger" sur tous les fichiers chargés
  d'un coup, avec un rapport global.
- **Persistance** — actuellement tout est perdu au reload du navigateur.
  Un `localStorage` opt-in serait utile pour reprendre une session.
- **Plus de règles** — vérifications de l'attribut `Inherits`, des
  CodeFile vs CodeBehind, des liaisons d'événements en double, etc.

## Tests

Pas de framework de test — historiquement les vérifications ont été
faites avec un node.js + `vm.runInContext` qui charge le `<script>`
extrait du HTML. Voir le dossier `tests/` si on en crée un. Pour un
test rapide :

```bash
node -e "
const fs = require('fs'), vm = require('vm');
const html = fs.readFileSync('aspx_lint_dashboard.html', 'utf8');
const script = html.match(/<script>([\\s\\S]*?)<\\/script>/)[1];
const stub = new Proxy(function(){}, { get: () => stub, apply: () => stub });
const sb = { console, setTimeout: () => {}, document: new Proxy({}, { get: () => stub }), URL: { createObjectURL: () => '', revokeObjectURL: () => {} }, Blob: function(){} };
sb.window = sb;
vm.runInContext(script + ';Object.assign(globalThis, { RULES, loadDemo, state });', vm.createContext(sb));
console.log('Règles chargées:', sb.RULES.length);
"
```

## Conventions de code

- **Pas de framework**. Vanilla JS, pas de build. Tout doit rester
  exécutable juste en ouvrant le fichier dans un navigateur.
- **Pas de `localStorage` / `sessionStorage`** — l'analyse est 100%
  in-memory et le rappeler dans la doc utilisateur ("les fichiers ne
  quittent jamais votre navigateur") fait partie de la promesse.
- **Sécurité du tokenizer** : toute modification de `highlightLine` /
  `highlightTag` doit être validée sur les cas piégeux suivants :
  `<DIV class="container">`, `<!-- commentaire avec class="x" -->`,
  `<a href="?a=1&amp;b=2">`, `<input type=text>`, `<` isolé, `&` isolé,
  `<asp:Label ID="x"><%= DateTime.Now %></asp:Label>`. Round-trip
  test : retirer tous les `<span>` du résultat doit rendre l'entrée
  exacte.
- **Pas de récriture massive d'une règle existante** sans s'assurer
  que les fixes restent idempotents — exécuter le fix deux fois
  d'affilée doit produire le même résultat que l'exécuter une fois.

## Esthétique

Thème dark, typo éditoriale (Fraunces italique) sur les headlines,
JetBrains Mono pour le code, accent jaune-vert électrique
(`#d4ff3a`). Garder cette identité — pas de bascule vers du Material
ou du Bootstrap.
