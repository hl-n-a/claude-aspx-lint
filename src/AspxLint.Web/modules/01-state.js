/* ============================================================
   ASPX·LINT — Frontend pur, le moteur tourne dans AspxLint.Server
   ============================================================
   Toute analyse + auto-fix passe par les endpoints HTTP :
     POST /api/analyze    → renvoie les issues d'un contenu
     POST /api/fix        → applique le fix d'une regle
     POST /api/fix-all    → applique toutes les regles fixables
     GET  /api/rules      → liste des regles (id, name, severity, hasFix)
   ============================================================ */

// Garde-fou : cette dashboard NE marche QUE servie par AspxLint.Server.
// Si quelqu'un ouvre index.html en double-clic (file://), on remplace
// le DOM par un message clair plutot que de planter en silence.
if (location.protocol === 'file:') {
  document.body.innerHTML = `
    <div style="padding:60px;font-family:sans-serif;max-width:600px;margin:auto;background:#0f1419;color:#e0e3e7;min-height:100vh;box-sizing:border-box">
      <h1 style="color:#d4ff3a;font-style:italic">aspx &middot; lint</h1>
      <p>Cette dashboard nécessite <strong>AspxLint.Server</strong> pour fonctionner.</p>
      <p>Lance le serveur depuis le repo :</p>
      <pre style="background:#1c2025;color:#d4ff3a;padding:16px;border-radius:6px;font-family:monospace">dotnet run --project src/AspxLint.Server</pre>
      <p>… puis ouvre l'URL <code>http://localhost:5173</code> avec le token affiché en console.</p>
      <p>Ou télécharge <strong>AspxLint.Desktop.exe</strong> depuis la <a href="https://github.com/hl-n-a/claude-aspx-lint/releases" style="color:#d4ff3a">dernière release</a>.</p>
    </div>
  `;
  throw new Error('aspx-lint dashboard requires AspxLint.Server (file:// not supported)');
}

// Metadonnees des regles, peuplees au demarrage par loadRulesFromServer().
// Les fonctions detect/fix vivent dans AspxLint.Core (cote serveur).
let RULES = [];

async function loadRulesFromServer() {
  try {
    const r = await fetch('/api/rules');
    if (r.ok) RULES = await r.json();
  } catch (e) {
    console.warn('Impossible de charger /api/rules :', e);
  }
}

/* ============================================================
   ÉTAT GLOBAL
   ============================================================ */
const state = {
  files: [],            // { id, name, ext, original, current, issues, history, hasRun }
  currentFileId: null,
  filter: 'all',        // filtre des issues dans le panneau de droite
  fileFilter: 'all',    // filtre des fichiers dans la sidebar (all|errors|issues|clean|corrected|modified)
  viewMode: 'code',     // 'code' ou 'diff'
  fixedCount: 0,
  expandedFolders: new Set(),  // chemins de dossiers ouverts dans l'arbre
  selectedFileIds: new Set(),  // multi-selection (pour les actions en lot)
  fileSearch: '',              // texte du filtre de recherche dans la sidebar
  splitView: false,            // vue code+diff cote a cote au lieu du toggle
  editMode: false,             // edition in-place dans le code area
  search: {
    open: false,
    replaceOpen: false,
    query: '',
    caseSensitive: false,
    matches: [],   // [{node, start, end}] positions DOM apres render
    current: 0
  },
  theme: 'default'
};

let fileIdSeq = 1;

/* ============================================================
   THEME PICKER — persisté dans localStorage
   ============================================================ */
function applyTheme(name) {
  state.theme = name;
  document.documentElement.dataset.theme = name;
  try { localStorage.setItem('aspxlint.theme', name); } catch (e) { /* private mode */ }
  const picker = $('themePicker');
  if (picker) picker.value = name;
}
function loadThemeFromStorage() {
  // Defaut "vsdark" : la palette neutre passe mieux dans une fenetre WebView
  // / un VS Code que l'accent jaune-vert tres affirme du theme par defaut.
  let saved = 'vsdark';
  try { saved = localStorage.getItem('aspxlint.theme') || 'vsdark'; } catch (e) { }
  applyTheme(saved);
}

/* ============================================================
   PERSISTANCE LOCALSTORAGE — opt-in via le toggle "Persister"
   ============================================================
   On sauvegarde la liste des fichiers (avec original/current/history/
   serverPath) + le currentFileId. Au reload, si le toggle est coche on
   propose la restauration. Garde-fou taille : si > 4 MB, on coupe les
   fichiers les plus gros pour rester sous le quota localStorage (~5MB).
   ============================================================ */

const PERSIST_KEY = 'aspxlint.session.v1';
const PERSIST_FLAG_KEY = 'aspxlint.persist';
const PERSIST_LIMIT_BYTES = 4 * 1024 * 1024;   // 4 MB

let persistEnabled = false;
let persistDebounce = null;

function isPersistEnabled() {
  try { return localStorage.getItem(PERSIST_FLAG_KEY) === '1'; } catch { return false; }
}

function togglePersist(checked) {
  persistEnabled = checked;
  try {
    if (checked) {
      localStorage.setItem(PERSIST_FLAG_KEY, '1');
      schedulePersist();
      showToast('Session sauvegardée localement à chaque modification.', 'success');
    } else {
      localStorage.removeItem(PERSIST_FLAG_KEY);
      localStorage.removeItem(PERSIST_KEY);
      showToast('Persistance désactivée, état effacé du navigateur.', 'success');
    }
  } catch (e) {
    showToast('localStorage indisponible : ' + e.message, 'error');
  }
}

function schedulePersist() {
  if (!persistEnabled) return;
  clearTimeout(persistDebounce);
  persistDebounce = setTimeout(persistNow, 500);
}

function persistNow() {
  if (!persistEnabled) return;
  try {
    const payload = {
      version: 1,
      savedAt: Date.now(),
      currentFileId: state.currentFileId,
      fileFilter: state.fileFilter,
      fileSearch: state.fileSearch,
      files: state.files.map(f => ({
        id: f.id, name: f.name, ext: f.ext,
        original: f.original, current: f.current,
        history: f.history, serverPath: f.serverPath
      }))
    };
    let json = JSON.stringify(payload);
    // Garde-fou taille : on coupe les plus gros fichiers d'abord.
    while (json.length > PERSIST_LIMIT_BYTES && payload.files.length > 0) {
      payload.files.sort((a, b) => b.current.length - a.current.length);
      payload.files.shift();
      json = JSON.stringify(payload);
    }
    localStorage.setItem(PERSIST_KEY, json);
  } catch (e) {
    // Quota plein, JSON invalide, etc. — silencieux pour ne pas spammer l'UI.
    console.warn('persist failed:', e);
  }
}

async function maybeRestoreFromStorage() {
  if (!persistEnabled) return false;
  let payload;
  try {
    const raw = localStorage.getItem(PERSIST_KEY);
    if (!raw) return false;
    payload = JSON.parse(raw);
  } catch { return false; }
  if (!payload || !payload.files || payload.files.length === 0) return false;

  const ageMin = Math.round((Date.now() - (payload.savedAt || 0)) / 60000);
  const ageStr = ageMin < 1 ? 'à l\'instant' : ageMin < 60 ? `il y a ${ageMin} min` : `il y a ${Math.round(ageMin / 60)}h`;
  if (!confirm(`Restaurer la session précédente (${payload.files.length} fichier(s), sauvegardée ${ageStr}) ?`)) {
    return false;
  }

  // Restaure les fichiers sans relancer une analyse serveur (les issues
  // sont recalculees a la demande, ou on en relance une apres restore).
  state.files = payload.files.map(f => ({
    id: f.id, name: f.name, ext: f.ext,
    original: f.original, current: f.current,
    issues: [],   // sera rempli par runAnalysis
    history: f.history || [],
    hasRun: false,
    serverPath: f.serverPath || null
  }));
  fileIdSeq = Math.max(...state.files.map(f => parseInt((f.id || 'f0').slice(1)) || 0)) + 1;
  state.currentFileId = payload.currentFileId;
  state.fileFilter = payload.fileFilter || 'all';
  state.fileSearch = payload.fileSearch || '';
  if (state.fileSearch) $('fileSearch').value = state.fileSearch;

  showToast(`Restauration de ${state.files.length} fichier(s)…`, 'success');
  for (const f of state.files) await runAnalysis(f);
  return true;
}

