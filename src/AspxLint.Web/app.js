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

/* ============================================================
   ÉTAT D'UN FICHIER (utilisé par l'arbre + le filtre)
   ============================================================
   - 'errors'    : il reste au moins 1 issue de severité error
   - 'warnings'  : il reste des warnings (mais pas d'errors)
   - 'info'      : il reste des infos seulement
   - 'modified'  : pas d'issues mais le contenu a changé (in-flight)
   - 'corrected' : 0 issue, history.length > 0 (a été corrigé)
   - 'clean'     : 0 issue, 0 history (jamais eu de problème)
   ============================================================ */
function computeFileStatus(file) {
  const issues = (file.issues || []).filter(i => !i.fixed);
  const errors = issues.filter(i => i.severity === 'error').length;
  const warnings = issues.filter(i => i.severity === 'warning').length;
  const infos = issues.filter(i => i.severity === 'info').length;
  if (errors > 0) return 'errors';
  if (warnings > 0) return 'warnings';
  if (infos > 0) return 'info';
  if (file.current !== file.original) return 'modified';
  if ((file.history || []).length > 0) return 'corrected';
  return 'clean';
}

function fileMatchesFilter(file, f) {
  if (f !== 'all') {
    const status = computeFileStatus(file);
    if (f === 'errors'    && status !== 'errors')    return false;
    if (f === 'issues'    && status !== 'errors' && status !== 'warnings' && status !== 'info') return false;
    if (f === 'clean'     && status !== 'clean')     return false;
    if (f === 'corrected' && status !== 'corrected') return false;
    if (f === 'modified'  && status !== 'modified')  return false;
  }
  // Recherche par nom (substring case-insensitive sur le path complet).
  if (state.fileSearch) {
    const q = state.fileSearch.toLowerCase();
    if (!(file.name || '').toLowerCase().includes(q)) return false;
  }
  return true;
}

function setFileSearch(text) {
  state.fileSearch = (text || '').trim();
  $('fileSearchClear').style.display = state.fileSearch ? '' : 'none';
  // Auto-expand tous les dossiers quand une recherche est active, sinon on
  // ne voit pas les fichiers profondement enfouis qui matchent.
  if (state.fileSearch) {
    for (const f of state.files) {
      const parts = (f.name || '').split(/[\\/]/);
      let path = '';
      for (let i = 0; i < parts.length - 1; i++) {
        path = path ? path + '/' + parts[i] : parts[i];
        state.expandedFolders.add(path);
      }
    }
  }
  renderFileList();
  renderBulkBar();
}

function clearFileSearch() {
  $('fileSearch').value = '';
  setFileSearch('');
  $('fileSearch').focus();
}

function setFileFilter(f) {
  state.fileFilter = f;
  document.querySelectorAll('.file-filter .filter-pill').forEach(p =>
    p.classList.toggle('active', p.dataset.fileFilter === f));
  renderFileList();
}

/* ============================================================
   TREE BUILDER — group files by folder, based on file.name which
   contient le relativePath quand le fichier vient d'un /api/scan.
   Les fichiers sans separateur ('/') vont a la racine.
   ============================================================ */
function buildFileTree(files) {
  const root = {
    type: 'folder', name: '', path: '',
    folders: new Map(), files: []
  };
  for (const f of files) {
    if (!fileMatchesFilter(f, state.fileFilter)) continue;
    const parts = (f.name || '').split(/[\\/]/);
    if (parts.length === 1) { root.files.push(f); continue; }
    let cur = root;
    for (let i = 0; i < parts.length - 1; i++) {
      const seg = parts[i];
      if (!cur.folders.has(seg)) {
        cur.folders.set(seg, {
          type: 'folder',
          name: seg,
          path: cur.path ? cur.path + '/' + seg : seg,
          folders: new Map(),
          files: []
        });
      }
      cur = cur.folders.get(seg);
    }
    cur.files.push({ ...f, _displayName: parts[parts.length - 1] });
  }
  return root;
}

function aggregateFolderStats(node) {
  let errors = 0, warnings = 0, infos = 0, fileCount = 0;
  for (const f of node.files) {
    const issues = (f.issues || []).filter(i => !i.fixed);
    errors += issues.filter(i => i.severity === 'error').length;
    warnings += issues.filter(i => i.severity === 'warning').length;
    infos += issues.filter(i => i.severity === 'info').length;
    fileCount++;
  }
  for (const child of node.folders.values()) {
    const s = aggregateFolderStats(child);
    errors += s.errors; warnings += s.warnings; infos += s.infos;
    fileCount += s.fileCount;
  }
  return { errors, warnings, infos, fileCount };
}

function toggleFolder(path) {
  if (state.expandedFolders.has(path)) state.expandedFolders.delete(path);
  else state.expandedFolders.add(path);
  renderFileList();
}

/* ============================================================
   ANALYSE
   ============================================================ */
async function analyzeFile(file) {
  let data;
  try {
    const r = await fetch('/api/analyze', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ content: file.current, ext: file.ext })
    });
    if (!r.ok) {
      console.warn('analyze HTTP', r.status);
      return [];
    }
    data = await r.json();
  } catch (e) {
    console.warn('analyze échoué :', e);
    return [];
  }

  let id = 1;
  return (data.issues || [])
    .map(i => ({
      id: `i${id++}`,
      ruleId: i.ruleId,
      ruleName: i.ruleName,
      severity: i.severity,
      desc: RULES.find(r => r.id === i.ruleId)?.desc || '',
      line: i.line,
      col: i.col || 1,
      snippet: i.snippet,
      hint: i.hint,
      fixable: !!RULES.find(r => r.id === i.ruleId)?.hasFix,
      fixed: false
    }))
    .sort((a, b) => a.line - b.line);
}

async function runAnalysis(file) {
  file.issues = await analyzeFile(file);
  file.hasRun = true;
}

/* ============================================================
   APPLICATION DES CORRECTIONS — avec historique
   ============================================================
   applyFix renvoie le nombre de problèmes effectivement résolus
   pour la règle, et empile chacun dans file.history.
   ============================================================ */
async function applyFix(file, ruleId) {
  const ruleMeta = RULES.find(r => r.id === ruleId);
  if (!ruleMeta || !ruleMeta.hasFix) return 0;

  const beforeIssues = file.issues.filter(i => i.ruleId === ruleId).map(i => ({...i}));
  if (beforeIssues.length === 0) return 0;

  let data;
  try {
    const r = await fetch('/api/fix', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ content: file.current, ext: file.ext, ruleId })
    });
    if (!r.ok) {
      console.warn('fix HTTP', r.status);
      return 0;
    }
    data = await r.json();
  } catch (e) {
    console.warn('fix échoué :', e);
    return 0;
  }

  if (data.content === file.current || data.applied === 0) return 0;

  file.current = data.content;
  await runAnalysis(file);

  const fixedNow = data.applied;
  for (let k = 0; k < fixedNow && k < beforeIssues.length; k++) {
    const orig = beforeIssues[k];
    file.history.push({
      ruleId: ruleMeta.id,
      ruleName: ruleMeta.name,
      severity: ruleMeta.severity,
      desc: ruleMeta.desc,
      line: orig.line,
      col: orig.col,
      snippet: orig.snippet,
      hint: orig.hint,
      fixedAt: Date.now()
    });
  }
  return fixedNow;
}

async function applyAllFixes(file) {
  let data;
  try {
    const r = await fetch('/api/fix-all', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ content: file.current, ext: file.ext })
    });
    if (!r.ok) {
      console.warn('fix-all HTTP', r.status);
      return 0;
    }
    data = await r.json();
  } catch (e) {
    console.warn('fix-all échoué :', e);
    return 0;
  }

  if (data.content === file.current) return 0;
  file.current = data.content;

  // Historique : on n'a pas le détail des issues effacées (seulement le total
  // par règle), donc on enregistre une entrée synthétique par groupe.
  const total = (data.history || []).reduce((s, h) => s + h.count, 0);
  for (const h of data.history || []) {
    const meta = RULES.find(r => r.id === h.ruleId) || { id: h.ruleId, name: h.ruleId, severity: 'info', desc: '' };
    for (let k = 0; k < h.count; k++) {
      file.history.push({
        ruleId: meta.id,
        ruleName: meta.name,
        severity: meta.severity,
        desc: meta.desc,
        fixedAt: Date.now()
      });
    }
  }

  await runAnalysis(file);
  return total;
}

/* ============================================================
   UI : RENDU
   ============================================================ */
function $(id) { return document.getElementById(id); }

function fileExtFromName(name) {
  const m = name.toLowerCase().match(/\.([a-z]+)$/);
  if (!m) return 'txt';
  if (['aspx','ascx','master','asax'].includes(m[1])) return m[1];
  return m[1];
}

function escapeHtml(s) {
  return s.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;')
          .replace(/"/g, '&quot;').replace(/'/g, '&#39;');
}

/** Encadre la sous-chaine matchee par state.fileSearch dans <span class="match">. */
function highlightSearch(s) {
  const escaped = escapeHtml(s);
  if (!state.fileSearch) return escaped;
  const q = state.fileSearch.toLowerCase();
  const lower = s.toLowerCase();
  const idx = lower.indexOf(q);
  if (idx < 0) return escaped;
  // On reapplique l'escape sur les 3 segments en se basant sur la chaine d'origine.
  return escapeHtml(s.substring(0, idx))
       + '<span class="match">' + escapeHtml(s.substring(idx, idx + q.length)) + '</span>'
       + escapeHtml(s.substring(idx + q.length));
}

/* Tokenizer single-pass : on travaille sur la chaîne BRUTE et on n'échappe
   le HTML qu'au moment d'émettre chaque token. Aucune regex ne peut donc
   re-matcher un <span> qu'on vient d'injecter. */
function highlightLine(line) {
  let out = '';
  let i = 0;
  const len = line.length;

  // Trouve la fin de balise (en sautant les > à l'intérieur de guillemets)
  const findTagEnd = (s, from) => {
    let q = null;
    for (let k = from; k < s.length; k++) {
      const c = s[k];
      if (q) { if (c === q) q = null; }
      else if (c === '"' || c === "'") q = c;
      else if (c === '>') return k;
    }
    return -1;
  };

  while (i < len) {
    // 1. Directive serveur <%@ ... %>
    if (line.startsWith('<%@', i)) {
      const end = line.indexOf('%>', i);
      if (end !== -1) {
        out += '<span class="tk-dir">' + escapeHtml(line.substring(i, end + 2)) + '</span>';
        i = end + 2; continue;
      }
    }
    // 2. Code serveur <% ... %> / <%= ... %> / <%# ... %> / <%: ... %>
    if (line.startsWith('<%', i)) {
      const end = line.indexOf('%>', i);
      if (end !== -1) {
        out += '<span class="tk-asp">' + escapeHtml(line.substring(i, end + 2)) + '</span>';
        i = end + 2; continue;
      }
    }
    // 3. Commentaire HTML <!-- ... -->
    if (line.startsWith('<!--', i)) {
      const end = line.indexOf('-->', i);
      if (end !== -1) {
        out += '<span class="tk-com">' + escapeHtml(line.substring(i, end + 3)) + '</span>';
        i = end + 3; continue;
      }
      // Commentaire qui se poursuit hors de la ligne
      out += '<span class="tk-com">' + escapeHtml(line.substring(i)) + '</span>';
      break;
    }
    // 4. <!DOCTYPE ...>
    if (line[i] === '<' && line[i + 1] === '!') {
      const end = line.indexOf('>', i);
      if (end !== -1) {
        out += '<span class="tk-dir">' + escapeHtml(line.substring(i, end + 1)) + '</span>';
        i = end + 1; continue;
      }
    }
    // 5. Balise <tag ...> ou </tag>
    if (line[i] === '<') {
      const end = findTagEnd(line, i);
      if (end !== -1) {
        out += highlightTag(line.substring(i, end + 1));
        i = end + 1; continue;
      }
    }
    // 6. Entité HTML &xxx;
    if (line[i] === '&') {
      const m = line.substring(i).match(/^&(?:[a-zA-Z][a-zA-Z0-9]{1,8}|#\d+|#x[0-9a-fA-F]+);/);
      if (m) {
        out += '<span class="tk-str">' + escapeHtml(m[0]) + '</span>';
        i += m[0].length; continue;
      }
    }
    // 7. Texte ordinaire — on consomme au moins UN caractère pour garantir la
    //    progression, puis on avance jusqu'au prochain caractère "spécial".
    //    Cas important : un '<' ou un '&' non reconnu (ex. "Bonjour & bienvenue")
    //    doit être traité comme du texte sans bloquer la boucle.
    let j = i + 1;
    while (j < len && line[j] !== '<' && line[j] !== '&') j++;
    out += escapeHtml(line.substring(i, j));
    i = j;
  }
  return out;
}

/* Tokenize une balise complète, p.ex. <asp:Button ID="b1" runat="server" /> */
function highlightTag(tag) {
  const m = tag.match(/^<(\/?)([a-zA-Z][a-zA-Z0-9:_\-]*)([\s\S]*?)(\s*\/?)>$/);
  if (!m) return escapeHtml(tag);
  const [, slash, name, attrs, tail] = m;
  const tagCls = name.includes(':') ? 'tk-asp' : 'tk-tag';

  let out = '<span class="tk-punct">' + escapeHtml('<' + slash) + '</span>';
  out += '<span class="' + tagCls + '">' + escapeHtml(name) + '</span>';

  // Tokenise la portion d'attributs sans toucher aux spans déjà émis.
  let p = 0;
  const a = attrs;
  while (p < a.length) {
    // Whitespace
    const ws = a.substring(p).match(/^\s+/);
    if (ws) { out += escapeHtml(ws[0]); p += ws[0].length; continue; }
    // attr=valeur (avec ou sans guillemets)
    const av = a.substring(p).match(/^([a-zA-Z_][a-zA-Z0-9\-:_]*)(\s*=\s*)("[^"]*"|'[^']*'|[^\s"'<>=`]+)?/);
    if (av && av[2]) {
      out += '<span class="tk-attr">' + escapeHtml(av[1]) + '</span>';
      out += escapeHtml(av[2]);
      if (av[3] !== undefined) {
        out += '<span class="tk-str">' + escapeHtml(av[3]) + '</span>';
      }
      p += av[0].length; continue;
    }
    // Attribut booléen seul
    const sa = a.substring(p).match(/^([a-zA-Z_][a-zA-Z0-9\-:_]*)/);
    if (sa) {
      out += '<span class="tk-attr">' + escapeHtml(sa[0]) + '</span>';
      p += sa[0].length; continue;
    }
    // Caractère isolé
    out += escapeHtml(a[p]); p++;
  }

  out += '<span class="tk-punct">' + escapeHtml(tail + '>') + '</span>';
  return out;
}

function renderFileList() {
  const list = $('fileList');
  list.innerHTML = '';
  $('fileCount').textContent = `${state.files.length} fichier${state.files.length > 1 ? 's' : ''}`;

  if (state.files.length === 0) {
    list.innerHTML = '<div class="tree-empty">Aucun fichier chargé.<br/>Utilise les boutons ci-dessous.</div>';
    return;
  }

  const tree = buildFileTree(state.files);
  const matchingTotal = aggregateFolderStats(tree).fileCount;
  if (matchingTotal === 0) {
    list.innerHTML = `<div class="tree-empty">Aucun fichier ne correspond au filtre <b>${state.fileFilter}</b>.</div>`;
    return;
  }

  // Ouvre par défaut tous les dossiers de premier niveau si state.expandedFolders vide.
  if (state.expandedFolders.size === 0) {
    for (const folder of tree.folders.values()) state.expandedFolders.add(folder.path);
  }

  list.appendChild(renderTreeNode(tree, 0));
}

function renderTreeNode(node, depth) {
  const wrap = document.createElement('div');

  // Sous-dossiers (triés alpha)
  const folders = Array.from(node.folders.values()).sort((a, b) => a.name.localeCompare(b.name));
  for (const folder of folders) {
    const stats = aggregateFolderStats(folder);
    if (stats.fileCount === 0) continue;     // tout filtré dedans

    const expanded = state.expandedFolders.has(folder.path);

    const folderDiv = document.createElement('div');
    folderDiv.className = 'tree-folder';
    folderDiv.dataset.path = folder.path;

    const header = document.createElement('div');
    header.className = 'tree-folder-header';
    header.style.paddingLeft = (8 + depth * 14) + 'px';
    header.onclick = () => toggleFolder(folder.path);

    const statsHtml = [];
    if (stats.errors > 0)   statsHtml.push(`<span class="has-error">●${stats.errors}</span>`);
    if (stats.warnings > 0) statsHtml.push(`<span class="has-warning">●${stats.warnings}</span>`);
    if (stats.infos > 0)    statsHtml.push(`<span class="has-info">●${stats.infos}</span>`);
    statsHtml.push(`<span>${stats.fileCount} ƒ</span>`);

    header.innerHTML = `
      <span class="tree-chevron">${expanded ? '▼' : '▶'}</span>
      <span class="tree-folder-name">${escapeHtml(folder.name)}</span>
      <span class="tree-folder-stats">${statsHtml.join('')}</span>
    `;
    folderDiv.appendChild(header);

    if (expanded) {
      const children = document.createElement('div');
      children.className = 'tree-folder-children';
      children.appendChild(renderTreeNode(folder, depth + 1));
      folderDiv.appendChild(children);
    }
    wrap.appendChild(folderDiv);
  }

  // Fichiers (triés alpha)
  const files = node.files.slice().sort((a, b) =>
    (a._displayName || a.name).localeCompare(b._displayName || b.name));
  for (const f of files) {
    const status = computeFileStatus(f);
    const remaining = (f.issues || []).filter(i => !i.fixed).length;
    const errors = (f.issues || []).filter(i => i.severity === 'error' && !i.fixed).length;
    const warnings = (f.issues || []).filter(i => i.severity === 'warning' && !i.fixed).length;

    const fileDiv = document.createElement('div');
    const isSelected = state.selectedFileIds.has(f.id);
    fileDiv.className = 'tree-file'
                      + (state.currentFileId === f.id ? ' active' : '')
                      + (isSelected ? ' selected' : '')
                      + (status === 'clean' || status === 'corrected' ? ' muted' : '');
    fileDiv.dataset.status = status;
    fileDiv.style.paddingLeft = (8 + depth * 14) + 'px';
    fileDiv.onclick = (ev) => selectFile(f.id, ev);

    let countHtml = '';
    if (errors > 0)        countHtml = `<span class="tree-file-count has-error">${errors}</span>`;
    else if (warnings > 0) countHtml = `<span class="tree-file-count has-warning">${warnings}</span>`;
    else if (remaining > 0) countHtml = `<span class="tree-file-count has-info">${remaining}</span>`;
    else if (status === 'corrected') countHtml = `<span class="tree-file-count" title="Corrigé">✓</span>`;
    else if (status === 'modified')  countHtml = `<span class="tree-file-count" title="Modifié">●</span>`;

    fileDiv.innerHTML = `
      <span class="file-status-dot ${status}" title="${status}"></span>
      <span class="tree-file-name">${highlightSearch(f._displayName || f.name)}</span>
      ${countHtml}
    `;
    wrap.appendChild(fileDiv);
  }

  return wrap;
}

function renderCode() {
  const file = currentFile();
  const area = $('codeArea');
  if (!file) {
    area.innerHTML = `<div class="code-empty">
      <div class="code-empty-headline">Une analyse soignée pour vos pages serveurs.</div>
      <div class="code-empty-sub">Chargez vos fichiers ASPX, ASCX ou MASTER. L'outil détectera les problèmes de formatage, expliquera chaque cas, proposera des corrections, puis revérifiera tout après application.</div>
      <div class="code-empty-hint">Les fichiers ne quittent jamais votre navigateur — analyse 100% locale.</div>
    </div>`;
    $('codeTitle').innerHTML = '<span style="color:var(--text-faint)">aucun fichier sélectionné</span>';
    $('btnFixAll').disabled = true;
    $('btnFixAndSave').disabled = true;
    $('btnVerify').disabled = true;
    $('btnEdit').disabled = true;
    $('btnDiff').disabled = true;
    $('btnSplit').disabled = true;
    $('btnDownload').disabled = true;
    $('btnSaveServer').disabled = true;
    $('btnRestoreServer').disabled = true;
    return;
  }

  const modified = file.current !== file.original;
  $('codeTitle').innerHTML = `<span class="code-title-name">${escapeHtml(file.name)}</span> <span style="color:var(--text-faint)">— ${file.current.split(/\r?\n/).length} lignes · ${file.current.length} octets${modified ? ' · <span style="color:var(--accent)">modifié</span>' : ''}</span>`;
  $('btnFixAll').disabled = !file.issues.some(i => i.fixable);
  // "Corriger & enregistrer" ne s'active que si on a un serverPath (sans ca on
  // ne sait pas ou ecrire) ET soit il y a des fixes a appliquer, soit le contenu
  // a deja ete modifie en local.
  $('btnFixAndSave').disabled = !file.serverPath
                              || (!file.issues.some(i => i.fixable) && !modified);
  $('btnVerify').disabled = false;
  $('btnEdit').disabled = false;
  $('btnEdit').classList.toggle('toggle-on', state.editMode);
  $('btnDiff').disabled = !modified;
  // Le bouton Split reste actif meme sans modif : le pane droit affichera
  // "Aucune modification" et l'utilisateur peut basculer pour preparer la vue.
  $('btnSplit').disabled = false;
  $('btnSplit').classList.toggle('toggle-on', state.splitView);
  $('btnDownload').disabled = !modified;
  // Save sur le serveur : seulement si le fichier vient d'un scan ET a ete modifie.
  $('btnSaveServer').disabled = !modified || !file.serverPath;
  // Restore : possible des qu'on a un serverPath (le serveur dira 404 s'il n'y a pas de .bak).
  $('btnRestoreServer').disabled = !file.serverPath;
  $('btnDiff').classList.toggle('toggle-on', state.viewMode === 'diff');

  if (state.viewMode === 'diff') {
    renderDiff();
    return;
  }

  if (state.splitView) {
    renderSplit(file);
    return;
  }

  if (state.editMode) {
    renderEditMode(file);
    return;
  }

  const lines = file.current.split(/\r?\n/);
  const issuesByLine = new Map();
  file.issues.forEach(i => {
    if (!issuesByLine.has(i.line)) issuesByLine.set(i.line, []);
    issuesByLine.get(i.line).push(i);
  });

  let html = '<div class="code-viewer">';
  lines.forEach((line, idx) => {
    const lineNum = idx + 1;
    const lineIssues = issuesByLine.get(lineNum) || [];
    let cls = 'code-line';
    let marker = '';
    if (lineIssues.length > 0) {
      const sev = lineIssues.some(i => i.severity === 'error') ? 'error'
                : lineIssues.some(i => i.severity === 'warning') ? 'warning' : 'info';
      cls += ' has-' + (sev === 'error' ? 'issue' : sev);
      marker = `<span class="line-marker ${sev}">●</span>`;
    } else {
      marker = `<span class="line-marker"></span>`;
    }
    html += `<div class="${cls}" data-line="${lineNum}">
      <span class="line-number">${lineNum}</span>
      ${marker}
      <span class="line-content">${highlightLine(line) || ' '}</span>
    </div>`;
  });
  html += '</div>';
  area.innerHTML = html;
  attachIssueScrollSync();
  if (state.search.open && state.search.query) applySearchHighlights();
}

/**
 * Mode edition : technique d'overlay textarea + pre.
 *   - <pre> en background avec le contenu tokenize (couleurs visibles)
 *   - <textarea> superpose, texte transparent + caret colore (frappe + selection)
 * Les deux partagent exactement la meme typo / padding / line-height pour que
 * le caret tombe pile sur les caracteres affiches. Chaque keystroke re-tokenize
 * juste la version <pre> (notre highlightLine est rapide). Validation au blur
 * (sauf si --no-blur-commit), Ctrl+Entree ou bouton ✓. Tab insere 4 espaces.
 */
function renderEditMode(file) {
  const area = $('codeArea');
  const tokenized = tokenizeForEditor(file.current);
  area.innerHTML = `
    <div class="edit-bar">
      <span class="edit-bar-hint">Édition in-place avec syntax highlighting — Ctrl+Entrée pour valider, Échap pour annuler</span>
      <div class="edit-bar-actions">
        <button class="btn small ghost" onclick="cancelEdit()">✕ Annuler</button>
        <button class="btn small primary" onclick="commitEdit()">✓ Valider</button>
      </div>
    </div>
    <div class="code-edit-container">
      <pre id="codeEditHighlight" class="code-edit-highlight" aria-hidden="true">${tokenized}</pre>
      <textarea id="codeEditTextarea" class="code-edit-textarea"
        spellcheck="false" autocomplete="off" autocapitalize="off"
        oninput="onEditInput()"
        onscroll="syncEditScroll()"
        onblur="onEditBlur(event)"
        onkeydown="onEditKey(event)">${escapeHtml(file.current)}</textarea>
    </div>
  `;
}

/** Re-tokenize tout le contenu pour le pre d'arrière-plan. */
function tokenizeForEditor(content) {
  // On garde le \n final pour que la hauteur du <pre> matche celle du textarea
  // (sinon la derniere ligne vide n'est pas comptee).
  return content.split(/\r?\n/).map(l => highlightLine(l) || ' ').join('\n') + '\n';
}

function onEditInput() {
  const ta = $('codeEditTextarea');
  const pre = $('codeEditHighlight');
  if (!ta || !pre) return;
  pre.innerHTML = tokenizeForEditor(ta.value);
  // Sync scroll au cas ou la frappe a change la hauteur (insertion d'une ligne).
  syncEditScroll();
}

/**
 * Auto-commit au blur (l'utilisateur clique ailleurs). On verifie que le focus
 * ne va pas vers les boutons Valider/Annuler — sinon on laisse leur handler
 * decider du sort des modifs.
 */
function onEditBlur(e) {
  // relatedTarget = element qui prend le focus. Si c'est un bouton de notre
  // bar d'edition, on ne fait rien : leur onclick gere deja commit/cancel.
  const next = e.relatedTarget;
  if (next && next.closest && next.closest('.edit-bar-actions')) return;
  // Sinon (clic dans la sidebar, le file tree, etc.) on commit silencieusement.
  commitEdit();
}

function syncEditScroll() {
  const ta = $('codeEditTextarea');
  const pre = $('codeEditHighlight');
  if (!ta || !pre) return;
  pre.scrollTop = ta.scrollTop;
  pre.scrollLeft = ta.scrollLeft;
}

function onEditKey(e) {
  if (e.key === 'Escape') { e.preventDefault(); cancelEdit(); return; }
  if (e.ctrlKey && e.key === 'Enter') { e.preventDefault(); commitEdit(); return; }
  // Tab : insere 4 espaces au lieu de changer le focus.
  if (e.key === 'Tab' && !e.shiftKey) {
    e.preventDefault();
    const ta = e.target;
    const start = ta.selectionStart, end = ta.selectionEnd;
    ta.value = ta.value.slice(0, start) + '    ' + ta.value.slice(end);
    ta.selectionStart = ta.selectionEnd = start + 4;
    onEditInput();
    return;
  }
  // Shift+Tab : dedent (4 espaces si presents en tete de ligne courante).
  if (e.key === 'Tab' && e.shiftKey) {
    e.preventDefault();
    const ta = e.target;
    const start = ta.selectionStart;
    const lineStart = ta.value.lastIndexOf('\n', start - 1) + 1;
    const lineHead = ta.value.slice(lineStart, lineStart + 4);
    if (lineHead === '    ') {
      ta.value = ta.value.slice(0, lineStart) + ta.value.slice(lineStart + 4);
      ta.selectionStart = ta.selectionEnd = Math.max(lineStart, start - 4);
      onEditInput();
    }
    return;
  }
}

/**
 * Vue split : code (avec issues) a gauche, diff avant/apres a droite.
 * Les deux panneaux partagent la meme zone scrollable verticale. Si pas
 * de modif, le pane droit affiche un placeholder explicite pour que
 * l'utilisateur sache pourquoi le diff est vide.
 */
function renderSplit(file) {
  const area = $('codeArea');
  const modified = file.current !== file.original;
  const lines = file.current.split(/\r?\n/);
  const issuesByLine = new Map();
  file.issues.forEach(i => {
    if (!issuesByLine.has(i.line)) issuesByLine.set(i.line, []);
    issuesByLine.get(i.line).push(i);
  });

  let leftHtml = '<div class="code-viewer">';
  lines.forEach((line, idx) => {
    const lineNum = idx + 1;
    const lineIssues = issuesByLine.get(lineNum) || [];
    let cls = 'code-line';
    let marker = '';
    if (lineIssues.length > 0) {
      const sev = lineIssues.some(i => i.severity === 'error') ? 'error'
                : lineIssues.some(i => i.severity === 'warning') ? 'warning' : 'info';
      cls += ' has-' + (sev === 'error' ? 'issue' : sev);
      marker = `<span class="line-marker ${sev}">●</span>`;
    } else {
      marker = `<span class="line-marker"></span>`;
    }
    leftHtml += `<div class="${cls}" data-line="${lineNum}">
      <span class="line-number">${lineNum}</span>
      ${marker}
      <span class="line-content">${highlightLine(line) || ' '}</span>
    </div>`;
  });
  leftHtml += '</div>';

  let rightHtml;
  if (!modified) {
    rightHtml = `<div class="diff-empty">
      <div class="diff-empty-headline">Aucune modification.</div>
      <div>Applique une correction (⚡ ou un bouton "Appliquer la correction" sur une issue) pour voir le diff côté droit.</div>
    </div>`;
  } else {
    const oldLines = file.original.split(/\r?\n/);
    const newLines = file.current.split(/\r?\n/);
    const ops = pairSimilarOps(lineDiff(oldLines, newLines));
    rightHtml = '<div class="diff-viewer">';
    ops.forEach(op => rightHtml += renderDiffOp(op));
    rightHtml += '</div>';
  }

  area.innerHTML = `
    <div class="split-container">
      <div class="split-pane">
        <div class="split-pane-label">Actuel</div>
        ${leftHtml}
      </div>
      <div class="split-pane">
        <div class="split-pane-label">Diff avant / après</div>
        ${rightHtml}
      </div>
    </div>
  `;
  attachIssueScrollSync();
  if (state.search.open && state.search.query) applySearchHighlights();
}

/**
 * Sticky issue panel : quand l'utilisateur scrolle dans le code, on
 * surligne dans le panneau de droite l'issue dont la ligne est la plus
 * proche du haut du viewport. Si le panneau est dans un autre filtre
 * (errors only, etc.) on ne fait rien, le sync n'a pas lieu d'etre.
 */
let scrollSyncRaf = null;
function attachIssueScrollSync() {
  const area = $('codeArea');
  if (!area) return;
  area.onscroll = () => {
    if (scrollSyncRaf) cancelAnimationFrame(scrollSyncRaf);
    scrollSyncRaf = requestAnimationFrame(syncIssuePanel);
  };
}

function syncIssuePanel() {
  const area = $('codeArea');
  const issuesList = $('issuesList');
  if (!area || !issuesList) return;
  const file = currentFile();
  if (!file || file.issues.length === 0) return;

  // Trouve la ligne au sommet du viewport visible.
  const areaRect = area.getBoundingClientRect();
  const lineEls = area.querySelectorAll('.code-line[data-line]');
  let topLine = null;
  for (const el of lineEls) {
    const r = el.getBoundingClientRect();
    if (r.bottom >= areaRect.top) {
      topLine = parseInt(el.dataset.line, 10);
      break;
    }
  }
  if (topLine == null) return;

  // Trouve l'issue la plus proche au-dessus ou a topLine.
  const sorted = file.issues.slice().sort((a, b) => a.line - b.line);
  let active = sorted[0];
  for (const i of sorted) {
    if (i.line <= topLine) active = i;
    else break;
  }

  // Surligne le bloc d'issue correspondant et le scrolle dans le panneau.
  issuesList.querySelectorAll('.issue.sticky-active').forEach(el => el.classList.remove('sticky-active'));
  if (!active) return;
  // On cherche un bloc qui contient L{line}: dans son innerHTML — heuristique simple.
  const issueEls = issuesList.querySelectorAll('.issue');
  for (const el of issueEls) {
    const loc = el.querySelector('.issue-location');
    if (loc && loc.textContent.startsWith('L' + active.line + ':')) {
      el.classList.add('sticky-active');
      const elRect = el.getBoundingClientRect();
      const listRect = issuesList.getBoundingClientRect();
      if (elRect.top < listRect.top || elRect.bottom > listRect.bottom) {
        el.scrollIntoView({ block: 'center', behavior: 'instant' });
      }
      break;
    }
  }
}

/* ============================================================
   DIFF AVANT / APRÈS — algorithme LCS sur les lignes
   ============================================================ */
function lineDiff(oldLines, newLines) {
  const m = oldLines.length, n = newLines.length;
  // Garde-fou pour très gros fichiers — au-delà, on sort un diff brut
  if (m * n > 4_000_000) {
    const ops = [];
    oldLines.forEach((l, i) => ops.push({ type: 'del', text: l, oi: i + 1 }));
    newLines.forEach((l, i) => ops.push({ type: 'add', text: l, ni: i + 1 }));
    return ops;
  }
  // Matrice LCS
  const dp = [];
  for (let i = 0; i <= m; i++) dp.push(new Array(n + 1).fill(0));
  for (let i = 1; i <= m; i++) {
    for (let j = 1; j <= n; j++) {
      dp[i][j] = oldLines[i - 1] === newLines[j - 1]
        ? dp[i - 1][j - 1] + 1
        : Math.max(dp[i - 1][j], dp[i][j - 1]);
    }
  }
  // Backtrack
  const ops = [];
  let i = m, j = n;
  while (i > 0 || j > 0) {
    if (i > 0 && j > 0 && oldLines[i - 1] === newLines[j - 1]) {
      ops.unshift({ type: 'eq', text: oldLines[i - 1], oi: i, ni: j });
      i--; j--;
    } else if (j > 0 && (i === 0 || dp[i][j - 1] >= dp[i - 1][j])) {
      ops.unshift({ type: 'add', text: newLines[j - 1], ni: j });
      j--;
    } else {
      ops.unshift({ type: 'del', text: oldLines[i - 1], oi: i });
      i--;
    }
  }
  return ops;
}

function renderDiff() {
  const file = currentFile();
  const area = $('codeArea');
  if (!file) return;
  if (file.current === file.original) {
    area.innerHTML = `<div class="diff-empty">
      <div class="diff-empty-headline">Aucune modification appliquée.</div>
      <div>Appliquez des corrections pour voir le diff avant/après ici.</div>
    </div>`;
    return;
  }
  const oldLines = file.original.split(/\r?\n/);
  const newLines = file.current.split(/\r?\n/);
  const ops = lineDiff(oldLines, newLines);
  // Apparie les del+add adjacents qui sont "similaires" pour faire un char-diff
  // intra-ligne — bien plus lisible quand un fix change juste 2-3 caracteres.
  const pairedOps = pairSimilarOps(ops);
  const adds = pairedOps.filter(o => o.type === 'add' || o.type === 'paired').length;
  const dels = pairedOps.filter(o => o.type === 'del' || o.type === 'paired').length;
  const eqs  = pairedOps.filter(o => o.type === 'eq').length;

  let html = `<div class="diff-stats">
    <span class="diff-stats-label">comparaison avant / après</span>
    <span style="color:var(--success);font-weight:600">+${adds}</span>
    <span style="color:var(--error);font-weight:600">−${dels}</span>
    <span style="color:var(--text-faint)">${eqs} ligne(s) inchangée(s)</span>
  </div>`;
  html += '<div class="diff-viewer">';
  pairedOps.forEach(op => html += renderDiffOp(op));
  html += '</div>';
  area.innerHTML = html;
  attachIssueScrollSync();
  if (state.search.open && state.search.query) applySearchHighlights();
}

/**
 * Apparie les del/add adjacents en "paired" si les lignes sont similaires
 * (>= 50% de caracteres communs via LCS). Sur un fix typique du linter qui
 * change quelques tokens, ca evite l'affichage del + add complet et permet
 * un highlight intra-ligne.
 */
function pairSimilarOps(ops) {
  const out = [];
  for (let i = 0; i < ops.length; i++) {
    const op = ops[i];
    // Cherche un pattern del puis add (ou add puis del) consecutif ou separe
    // par d'autres del/add du meme groupe de hunk.
    if (op.type === 'del' && i + 1 < ops.length && ops[i + 1].type === 'add') {
      const next = ops[i + 1];
      if (lineSimilarity(op.text, next.text) >= 0.5) {
        out.push({
          type: 'paired',
          oldText: op.text, newText: next.text,
          oi: op.oi, ni: next.ni
        });
        i++;
        continue;
      }
    }
    out.push(op);
  }
  return out;
}

function lineSimilarity(a, b) {
  // Similarite estimee = 2 * LCS(a, b) / (|a| + |b|), borne dans [0, 1].
  // Pour des lignes courtes on utilise une matrice DP ; au-dela de 200 chars
  // on degrade en heuristique approximative pour ne pas exploser le CPU.
  if (a === b) return 1;
  if (!a || !b) return 0;
  const al = a.length, bl = b.length;
  if (al + bl === 0) return 1;
  if (al > 200 || bl > 200) {
    // Approximation : ratio de chars communs (sans ordre).
    const setB = new Map();
    for (const c of b) setB.set(c, (setB.get(c) || 0) + 1);
    let common = 0;
    for (const c of a) {
      const n = setB.get(c) || 0;
      if (n > 0) { common++; setB.set(c, n - 1); }
    }
    return (2 * common) / (al + bl);
  }
  // LCS DP exact
  const dp = new Array(bl + 1).fill(0);
  for (let i = 1; i <= al; i++) {
    let prev = 0;
    for (let j = 1; j <= bl; j++) {
      const tmp = dp[j];
      dp[j] = a[i - 1] === b[j - 1] ? prev + 1 : Math.max(dp[j], dp[j - 1]);
      prev = tmp;
    }
  }
  return (2 * dp[bl]) / (al + bl);
}

/**
 * Char-diff via LCS sur les caracteres. Renvoie une liste de segments :
 * { type: 'eq'|'del'|'add', text }. Capping a 1000 chars/cote pour eviter
 * des matrices > 1M cellules sur des lignes pathologiques.
 */
function charDiff(oldLine, newLine) {
  const a = oldLine, b = newLine;
  if (a === b) return [{ type: 'eq', text: a }];
  if (a.length > 1000 || b.length > 1000) {
    return [{ type: 'del', text: a }, { type: 'add', text: b }];
  }
  const m = a.length, n = b.length;
  const dp = [];
  for (let i = 0; i <= m; i++) dp.push(new Array(n + 1).fill(0));
  for (let i = 1; i <= m; i++) {
    for (let j = 1; j <= n; j++) {
      dp[i][j] = a[i - 1] === b[j - 1]
        ? dp[i - 1][j - 1] + 1
        : Math.max(dp[i - 1][j], dp[i][j - 1]);
    }
  }
  const segs = [];
  let i = m, j = n;
  while (i > 0 || j > 0) {
    if (i > 0 && j > 0 && a[i - 1] === b[j - 1]) {
      segs.unshift({ type: 'eq', text: a[i - 1] }); i--; j--;
    } else if (j > 0 && (i === 0 || dp[i][j - 1] >= dp[i - 1][j])) {
      segs.unshift({ type: 'add', text: b[j - 1] }); j--;
    } else {
      segs.unshift({ type: 'del', text: a[i - 1] }); i--;
    }
  }
  // Coalesce les segments contigus de meme type.
  const merged = [];
  for (const s of segs) {
    if (merged.length > 0 && merged[merged.length - 1].type === s.type) {
      merged[merged.length - 1].text += s.text;
    } else {
      merged.push({ ...s });
    }
  }
  return merged;
}

function renderDiffOp(op) {
  const oldNum = op.oi !== undefined ? op.oi : '';
  const newNum = op.ni !== undefined ? op.ni : '';
  if (op.type === 'paired') {
    // Une ligne avec char-diff inline.
    const segs = charDiff(op.oldText, op.newText);
    const oldHtml = segs
      .filter(s => s.type === 'eq' || s.type === 'del')
      .map(s => s.type === 'eq' ? escapeHtml(s.text) : `<span class="char-del">${escapeHtml(s.text)}</span>`)
      .join('') || ' ';
    const newHtml = segs
      .filter(s => s.type === 'eq' || s.type === 'add')
      .map(s => s.type === 'eq' ? escapeHtml(s.text) : `<span class="char-add">${escapeHtml(s.text)}</span>`)
      .join('') || ' ';
    return `<div class="diff-line del paired">
      <span class="diff-old-num">${oldNum}</span>
      <span class="diff-new-num"></span>
      <span class="diff-mark">−</span>
      <span class="diff-content">${oldHtml}</span>
    </div>
    <div class="diff-line add paired">
      <span class="diff-old-num"></span>
      <span class="diff-new-num">${newNum}</span>
      <span class="diff-mark">+</span>
      <span class="diff-content">${newHtml}</span>
    </div>`;
  }
  let cls = 'diff-line';
  let mark = ' ';
  if (op.type === 'add') { cls += ' add'; mark = '+'; }
  else if (op.type === 'del') { cls += ' del'; mark = '−'; }
  return `<div class="${cls}">
    <span class="diff-old-num">${oldNum}</span>
    <span class="diff-new-num">${newNum}</span>
    <span class="diff-mark">${mark}</span>
    <span class="diff-content">${highlightLine(op.text) || ' '}</span>
  </div>`;
}

function toggleDiff() {
  state.viewMode = state.viewMode === 'diff' ? 'code' : 'diff';
  state.splitView = false;   // diff seul desactive le split
  renderCode();
}

function toggleSplitView() {
  state.splitView = !state.splitView;
  state.viewMode = 'code';   // base : on affiche le code, le split ajoute le diff a droite
  renderCode();
}

/* ============================================================
   EDIT-IN-PLACE — toggle entre code coloré (lecture) et textarea (édition)
   ============================================================
   Activation : bouton ✎ Éditer ou Ctrl+E. Validation : bouton ✓ Valider,
   Ctrl+Entrée, ou click hors du textarea (blur). On met `file.current` a jour,
   on relance l'analyse et on revient en mode lecture.
   ============================================================ */

function toggleEdit() {
  const file = currentFile();
  if (!file) return;
  state.editMode = !state.editMode;
  state.viewMode = 'code';
  state.splitView = false;
  renderCode();
  if (state.editMode) {
    setTimeout(() => {
      const ta = $('codeEditTextarea');
      if (ta) ta.focus();
    }, 50);
  }
}

async function commitEdit() {
  const file = currentFile();
  if (!file || !state.editMode) return;
  const ta = $('codeEditTextarea');
  if (!ta) return;
  const newContent = ta.value;
  if (newContent === file.current) {
    state.editMode = false;
    renderCode();
    return;
  }
  file.current = newContent;
  state.editMode = false;
  await runAnalysis(file);
  showToast('Modifications appliquées et ré-analysées.', 'success');
  setVerifyStatus('idle', '⊙ revérification disponible');
  renderAll();
}

function cancelEdit() {
  state.editMode = false;
  renderCode();
}

function renderIssues() {
  const file = currentFile();
  const list = $('issuesList');
  if (!file) {
    list.innerHTML = `<div class="issues-empty"><div class="issues-empty-sub" style="color:var(--text-faint)">Sélectionnez un fichier pour voir le diagnostic.</div></div>`;
    $('issuesCount').textContent = '—';
    return;
  }

  // Cas spécial : filtre "fixed" → afficher l'historique des corrections
  if (state.filter === 'fixed') {
    const history = [...file.history].reverse(); // plus récent en haut
    $('issuesCount').textContent = `${history.length}`;
    if (history.length === 0) {
      list.innerHTML = `<div class="issues-empty">
        <div class="issues-empty-sub" style="color:var(--text-faint)">Aucune correction appliquée pour l'instant. Cliquez sur "Appliquer la correction" sur un problème, ou sur "Tout corriger".</div>
      </div>`;
      return;
    }
    list.innerHTML = history.map(h => `
      <div class="issue fixed">
        <div class="issue-severity fixed">corrigé · ${h.ruleId}</div>
        <div class="issue-head">
          <div class="issue-title">${escapeHtml(h.ruleName)}</div>
          <div class="issue-location">L${h.line}</div>
        </div>
        <div class="issue-desc">${escapeHtml(h.desc)}</div>
        <span class="issue-snippet-label">avant correction</span>
        <div class="issue-snippet before">${escapeHtml(h.snippet || '')}</div>
        <span class="issue-snippet-label">action appliquée</span>
        <div class="issue-snippet after">${escapeHtml(h.hint)}</div>
      </div>
    `).join('');
    return;
  }

  let issues = file.issues;
  if (state.filter !== 'all') issues = issues.filter(i => i.severity === state.filter);

  $('issuesCount').textContent = `${issues.length}`;

  if (issues.length === 0) {
    if (file.issues.length === 0) {
      list.innerHTML = `<div class="issues-empty">
        <div class="issues-empty-headline">Aucun problème détecté.</div>
        <div class="issues-empty-sub">Ce fichier est propre selon les ${RULES.length} règles activées.${file.history.length > 0 ? `<br/><br/>${file.history.length} correction(s) ont été appliquées — visibles dans l'onglet "Corrigés".` : ''}</div>
      </div>`;
    } else {
      list.innerHTML = `<div class="issues-empty"><div class="issues-empty-sub" style="color:var(--text-faint)">Aucun élément ne correspond à ce filtre.</div></div>`;
    }
    return;
  }

  // Tri en deux groupes : auto-fixable d'abord (la valeur la plus actionnable),
  // puis correction manuelle. Au sein de chaque groupe, tri par numero de ligne.
  const fixable = issues.filter(i => i.fixable).sort((a, b) => a.line - b.line);
  const manual = issues.filter(i => !i.fixable).sort((a, b) => a.line - b.line);

  const renderIssue = i => `
    <div class="issue" onclick="jumpToLine(${i.line})">
      <div class="issue-severity ${i.severity}">${i.severity === 'error' ? 'erreur' : i.severity === 'warning' ? 'avertissement' : 'info'} · ${i.ruleId}</div>
      <div class="issue-head">
        <div class="issue-title">${escapeHtml(i.ruleName)}</div>
        <div class="issue-location">L${i.line}:${i.col}</div>
      </div>
      <div class="issue-desc">${escapeHtml(i.desc)}</div>
      <span class="issue-snippet-label">extrait</span>
      <div class="issue-snippet before">${escapeHtml(i.snippet || '')}</div>
      <span class="issue-snippet-label">solution proposée</span>
      <div class="issue-snippet after">${escapeHtml(i.hint)}</div>
      ${i.fixable ? `
        <div class="issue-actions">
          <button class="btn small" onclick="event.stopPropagation(); applyFixHere('${i.ruleId}', ${i.line})" title="Corrige uniquement cette occurrence">⚡ Cette ligne</button>
          <button class="btn small primary" onclick="event.stopPropagation(); applySingleFix('${i.ruleId}')" title="Corrige toutes les occurrences de cette règle dans le fichier">⚡ Tous (${i.ruleId})</button>
        </div>` : `
        <div class="issue-actions">
          <span style="font-size:11px;color:var(--text-faint);font-style:italic">Correction manuelle requise — voir explication ci-dessus.</span>
        </div>`}
    </div>
  `;

  let html = '';
  if (fixable.length > 0) {
    html += `<div class="issues-group-header">⚡ Auto-corrigeables · ${fixable.length}</div>`;
    html += fixable.map(renderIssue).join('');
  }
  if (manual.length > 0) {
    html += `<div class="issues-group-header manual">✎ Correction manuelle · ${manual.length}</div>`;
    html += manual.map(renderIssue).join('');
  }
  list.innerHTML = html;
}

function renderStats() {
  let e = 0, w = 0, n = 0, f = 0;
  state.files.forEach(file => {
    file.issues.forEach(i => {
      if (i.severity === 'error') e++;
      else if (i.severity === 'warning') w++;
      else if (i.severity === 'info') n++;
    });
    f += file.history.length;
  });
  $('statError').textContent = e;
  $('statWarn').textContent = w;
  $('statInfo').textContent = n;
  $('statFixed').textContent = f;

  // Tendance par rapport au dernier snapshot persiste (necessite "Persister" actif).
  const total = e + w + n;
  const trend = computeTrend(total);
  const elt = $('statTrend');
  if (!elt) return;
  if (trend == null) {
    elt.style.display = 'none';
  } else {
    elt.style.display = '';
    elt.classList.remove('up', 'down', 'flat');
    const arrow = elt.querySelector('.trend-arrow');
    const num = elt.querySelector('.trend-num');
    if (trend.delta < 0) {
      elt.classList.add('down');
      arrow.textContent = '↓';
      num.textContent = `${trend.delta} (${trend.label})`;
    } else if (trend.delta > 0) {
      elt.classList.add('up');
      arrow.textContent = '↑';
      num.textContent = `+${trend.delta} (${trend.label})`;
    } else {
      elt.classList.add('flat');
      arrow.textContent = '=';
      num.textContent = `0 (${trend.label})`;
    }
  }
}

const TREND_HISTORY_KEY = 'aspxlint.history.v1';
const TREND_MAX_ENTRIES = 50;

function loadTrendHistory() {
  if (!persistEnabled) return [];
  try {
    const raw = localStorage.getItem(TREND_HISTORY_KEY);
    return raw ? JSON.parse(raw) : [];
  } catch { return []; }
}

function appendTrendSnapshot(total) {
  if (!persistEnabled) return;
  try {
    const hist = loadTrendHistory();
    // Coalesce : si le dernier snapshot a moins de 30 secondes, on remplace
    // au lieu d'ajouter (evite de saturer le storage avec 100 snapshots/min).
    const now = Date.now();
    if (hist.length > 0 && (now - hist[hist.length - 1].t) < 30_000) {
      hist[hist.length - 1] = { t: now, total };
    } else {
      hist.push({ t: now, total });
    }
    while (hist.length > TREND_MAX_ENTRIES) hist.shift();
    localStorage.setItem(TREND_HISTORY_KEY, JSON.stringify(hist));
  } catch { /* quota plein */ }
}

/**
 * Calcule la variation entre le total courant et un snapshot reference :
 *   - le 1er snapshot du jour s'il existe (label "aujourd'hui")
 *   - sinon le snapshot le plus ancien des dernieres 24h
 *   - sinon le snapshot le plus ancien tout court
 * Renvoie null si aucun historique ou persistance off.
 */
function computeTrend(currentTotal) {
  if (!persistEnabled) return null;
  const hist = loadTrendHistory();
  // Append le current pour la prochaine fois.
  appendTrendSnapshot(currentTotal);
  if (hist.length === 0) return null;

  const now = Date.now();
  const dayMs = 24 * 60 * 60 * 1000;
  const startOfToday = new Date(); startOfToday.setHours(0, 0, 0, 0);

  // Cherche le 1er snapshot du jour (apres minuit).
  const todayFirst = hist.find(s => s.t >= startOfToday.getTime());
  if (todayFirst) {
    const delta = currentTotal - todayFirst.total;
    return { delta, label: 'vs début de journée' };
  }
  // Sinon le plus ancien des dernieres 24h.
  const within24 = hist.find(s => (now - s.t) <= dayMs);
  if (within24) {
    return { delta: currentTotal - within24.total, label: 'vs 24h' };
  }
  // Sinon le tout premier.
  return { delta: currentTotal - hist[0].total, label: 'vs début' };
}

function renderAll() {
  renderFileList();
  renderBulkBar();
  renderCode();
  renderIssues();
  renderStats();
  schedulePersist();

  const file = currentFile();
  if (file) {
    $('footerCurrentFile').textContent = `${file.name} · ${file.issues.filter(i => !i.fixed).length} issue(s) ouverte(s)`;
  } else {
    $('footerCurrentFile').textContent = 'prêt';
  }
}

/* ============================================================
   ACTIONS UI
   ============================================================ */
function currentFile() {
  return state.files.find(f => f.id === state.currentFileId);
}

/**
 * Selection d'un fichier dans l'arbre. Si l'evenement est passe, on supporte :
 *   - Ctrl/Cmd+click  : toggle dans la multi-selection (sans changer la vue)
 *   - Shift+click     : etend la multi-selection entre le fichier courant et celui-ci
 *   - click simple    : focus le fichier (vue centrale) et reset la multi-selection
 * Sans evenement (appels programmatiques), comportement = click simple.
 */
function selectFile(id, ev) {
  const ctrl = ev && (ev.ctrlKey || ev.metaKey);
  const shift = ev && ev.shiftKey;

  if (ctrl) {
    if (state.selectedFileIds.has(id)) state.selectedFileIds.delete(id);
    else state.selectedFileIds.add(id);
    renderFileList();
    return;
  }

  if (shift && state.currentFileId) {
    // Range select : on prend l'ordre actuel des fichiers dans state.files
    const idx = state.files.findIndex(f => f.id === id);
    const idxCur = state.files.findIndex(f => f.id === state.currentFileId);
    if (idx >= 0 && idxCur >= 0) {
      const [a, b] = idx < idxCur ? [idx, idxCur] : [idxCur, idx];
      state.selectedFileIds = new Set();
      for (let k = a; k <= b; k++) state.selectedFileIds.add(state.files[k].id);
    }
    renderFileList();
    return;
  }

  // Click simple : focus + reset multi-selection
  state.currentFileId = id;
  state.selectedFileIds = new Set([id]);
  state.filter = 'all';
  state.viewMode = 'code';
  document.querySelectorAll('.filter-pill').forEach(p => p.classList.toggle('active', p.dataset.filter === 'all'));
  setVerifyStatus('idle', '⊙ aucune vérification');
  renderAll();
}

function clearSelection() {
  state.selectedFileIds = state.currentFileId ? new Set([state.currentFileId]) : new Set();
  renderFileList();
  renderBulkBar();
}

function selectAllVisible() {
  // Tous les fichiers qui correspondent au filtre courant.
  state.selectedFileIds = new Set(
    state.files.filter(f => fileMatchesFilter(f, state.fileFilter)).map(f => f.id));
  renderFileList();
  renderBulkBar();
}

function filterIssues(f) {
  state.filter = f;
  document.querySelectorAll('.filter-pill').forEach(p => p.classList.toggle('active', p.dataset.filter === f));
  renderIssues();
}

function jumpToLine(line) {
  const el = document.querySelector(`.code-line[data-line="${line}"]`);
  if (el) {
    document.querySelectorAll('.code-line.selected').forEach(e => e.classList.remove('selected'));
    el.classList.add('selected');
    el.scrollIntoView({ behavior: 'smooth', block: 'center' });
    setTimeout(() => el.classList.remove('selected'), 2000);
  }
}

async function applySingleFix(ruleId) {
  const file = currentFile();
  if (!file) return;
  const fixed = await applyFix(file, ruleId);
  if (fixed > 0) {
    showToast(`${fixed} correction(s) appliquée(s).`, 'success');
    setVerifyStatus('idle', '⊙ revérification disponible');
    renderAll();
  } else {
    showToast('Aucun changement appliqué.', 'error');
  }
}

/** Per-occurrence fix : applique seulement a la ligne donnee via /api/fix-one. */
async function applyFixHere(ruleId, line) {
  const file = currentFile();
  if (!file) return;
  const ruleMeta = RULES.find(r => r.id === ruleId);
  if (!ruleMeta || !ruleMeta.hasFix) {
    showToast('Cette regle n\'a pas d\'auto-fix.', 'error');
    return;
  }

  // Snapshot de l'issue ciblee pour l'historique avant que runAnalysis recharge.
  const targetIssue = file.issues.find(i => i.ruleId === ruleId && i.line === line);

  let data;
  try {
    const r = await fetch('/api/fix-one', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ content: file.current, ext: file.ext, ruleId, line })
    });
    if (!r.ok) {
      showToast(`Fix KO (${r.status}).`, 'error');
      return;
    }
    data = await r.json();
  } catch (e) {
    showToast('Fix échoué : ' + e.message, 'error');
    return;
  }

  if (data.applied === 0 || data.content === file.current) {
    showToast('Aucun changement applique a cette ligne.', 'error');
    return;
  }

  file.current = data.content;
  await runAnalysis(file);
  if (targetIssue) {
    file.history.push({
      ruleId: ruleMeta.id,
      ruleName: ruleMeta.name,
      severity: ruleMeta.severity,
      desc: ruleMeta.desc,
      line: targetIssue.line,
      col: targetIssue.col,
      snippet: targetIssue.snippet,
      hint: targetIssue.hint,
      fixedAt: Date.now()
    });
  }
  showToast(`Correction appliquée a L${line} (${data.strategy}).`, 'success');
  setVerifyStatus('idle', '⊙ revérification disponible');
  renderAll();
}

async function fixAllInCurrent() {
  const file = currentFile();
  if (!file) return;
  const total = await applyAllFixes(file);
  if (total > 0) {
    showToast(`${total} problème(s) corrigé(s) automatiquement.`, 'success');
    setVerifyStatus('idle', '⊙ revérification disponible');
    renderAll();
  } else {
    showToast('Aucune correction automatique applicable.', 'error');
  }
}

async function verifyCurrent() {
  const file = currentFile();
  if (!file) return;
  setVerifyStatus('running', '◐ vérification en cours…');
  $('btnVerify').disabled = true;

  // L'appel reseau remplace le delai artificiel d'avant
  file.issues = await analyzeFile(file);
  const remaining = file.issues.length;
  const autoFixable = file.issues.filter(i => RULES.find(r => r.id === i.ruleId)?.hasFix).length;
  const manual = remaining - autoFixable;
  const fixedTotal = file.history.length;

  if (remaining === 0) {
    setVerifyStatus('success', `✓ vérifié — fichier propre`);
    showToast(`Vérification réussie : ${fixedTotal > 0 ? fixedTotal + ' correction(s) appliquée(s), ' : ''}aucun problème restant.`, 'success');
  } else if (manual === remaining) {
    setVerifyStatus('success', `✓ vérifié — ${manual} à corriger manuellement`);
    showToast(`${fixedTotal} corrigé(s), ${manual} nécessite(nt) une correction manuelle.`, 'success');
  } else {
    const errors = file.issues.filter(i => i.severity === 'error').length;
    setVerifyStatus(errors > 0 ? 'failed' : 'success',
      errors > 0
        ? `✗ ${errors} erreur(s), ${remaining - errors} avert. — ${autoFixable} auto-corrigeable(s)`
        : `✓ vérifié — ${remaining} avert/info restant`);
  }
  $('btnVerify').disabled = false;
  renderAll();
}

function setVerifyStatus(cls, text) {
  const el = $('verifyStatus');
  el.className = 'verify-status ' + cls;
  el.textContent = text;
}

/* ============================================================
   FILE I/O
   ============================================================ */
async function handleFiles(fileList) {
  for (const f of fileList) {
    const text = await f.text();
    await addFile(f.name, text);
  }
  if (state.files.length > 0 && !state.currentFileId) {
    state.currentFileId = state.files[0].id;
  }
  renderAll();
}

async function addFile(name, content, serverPath = null) {
  const ext = fileExtFromName(name);
  const file = {
    id: 'f' + (fileIdSeq++),
    name,
    ext,
    original: content,
    current: content,
    issues: [],
    history: [],   // chronologie des corrections appliquées
    hasRun: false,
    serverPath    // chemin disque cote serveur, null pour les fichiers uploades manuellement
  };
  state.files.push(file);
  await runAnalysis(file);
  return file;
}

/* ============================================================
   SCAN SERVEUR — appel /api/scan, recoit content + issues,
   passe a addFile() qui fait l'analyse via /api/analyze.
   ============================================================ */

// Etat du folder browser : path courant cote serveur (null = racine logique).
let browseState = { current: null, parent: null, allowedRoot: null, hereAspxCount: 0 };

function scanServerFolder() {
  // Ouvre l'explorer en mode normal (pas drop).
  browseState.dropMode = false;
  browseState.droppedFolderName = null;
  droppedEntriesPending = null;
  $('browseModal').classList.add('show');
  loadBrowse(null);
}

function closeBrowseModal() {
  $('browseModal').classList.remove('show');
  // Sortie du mode drop : on ne garde pas les entries en attente, sinon
  // un click ulterieur sur "Charger en local" partirait avec un drop perime.
  browseState.dropMode = false;
  browseState.droppedFolderName = null;
  droppedEntriesPending = null;
}

async function loadBrowse(path) {
  const list = $('browseList');
  list.innerHTML = '<div class="browse-empty">Chargement…</div>';
  $('browsePath').innerHTML = '<strong>' + escapeHtml(path || '(racine)') + '</strong>';

  let data;
  try {
    const url = '/api/browse' + (path ? '?path=' + encodeURIComponent(path) : '');
    const r = await fetch(url);
    if (!r.ok) {
      const txt = await r.text();
      list.innerHTML = '<div class="browse-empty">Erreur ' + r.status + ' : ' +
        escapeHtml(txt.substring(0, 200)) + '</div>';
      return;
    }
    data = await r.json();
  } catch (e) {
    list.innerHTML = '<div class="browse-empty">Erreur : ' + escapeHtml(e.message) + '</div>';
    return;
  }

  browseState.current = data.path || null;
  browseState.parent = data.parent || null;
  browseState.allowedRoot = data.allowedRoot || null;
  browseState.hereAspxCount = data.hereAspxCount || 0;

  $('browsePath').innerHTML = data.path
    ? '<strong>' + escapeHtml(data.path) + '</strong>'
    : '<em>Choisissez un point de départ</em>';

  $('browseUpBtn').disabled = !data.parent;

  $('browseScanBtn').disabled = !data.path;
  $('browseScanBtn').textContent = data.path
    ? `Scanner ce dossier${browseState.hereAspxCount ? ' (' + browseState.hereAspxCount + ' fichier(s) ici)' : ''}`
    : 'Scanner ce dossier';

  $('browseHere').textContent = data.allowedRoot
    ? 'Limité à : ' + data.allowedRoot
    : '';

  if (!data.entries || data.entries.length === 0) {
    list.innerHTML = '<div class="browse-empty">Aucun sous-dossier.</div>';
    return;
  }

  list.innerHTML = '';
  for (const e of data.entries) {
    const row = document.createElement('div');
    row.className = 'browse-entry';
    const badge = e.aspxCount && e.aspxCount > 0
      ? `<span class="badge">${e.aspxCount} .aspx</span>`
      : '';
    row.innerHTML =
      '<span class="icon">📁</span>' +
      '<span class="name">' + escapeHtml(e.name) + '</span>' +
      badge;
    row.onclick = () => loadBrowse(e.path);
    list.appendChild(row);
  }
}

function browseUp() {
  if (browseState.parent) loadBrowse(browseState.parent);
  else loadBrowse(null);
}

function browseRefresh() {
  loadBrowse(browseState.current);
}

async function browseScanCurrent() {
  const path = browseState.current;
  if (!path) return;
  closeBrowseModal();
  showToast('Scan en cours…', 'success');

  let data;
  try {
    const r = await fetch('/api/scan', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ path })
    });
    if (!r.ok) {
      const txt = await r.text();
      showToast(`Scan KO (${r.status}) : ${txt.substring(0, 100)}`, 'error');
      return;
    }
    data = await r.json();
  } catch (e) {
    showToast('Scan échoué : ' + e.message, 'error');
    return;
  }

  if (!data.files || data.files.length === 0) {
    showToast('Aucun fichier .aspx / .ascx / .master / .asax dans ce dossier.', 'error');
    return;
  }

  const beforeCount = state.files.length;
  for (const sf of data.files) {
    const name = sf.relativePath || (sf.path || '').split(/[\\/]/).pop() || 'inconnu';
    await addFile(name, sf.content, sf.path);
  }
  if (state.files.length > beforeCount) {
    state.currentFileId = state.files[beforeCount].id;
  }
  showToast(`Scan OK : ${data.fileCount} fichier(s) chargé(s).`, 'success');
  renderAll();
}

async function restoreCurrentFromServer() {
  const file = currentFile();
  if (!file) return;
  if (!file.serverPath) {
    showToast('Pas de serverPath, impossible de restaurer.', 'error');
    return;
  }
  if (!confirm(`Restaurer ${file.name} depuis son backup .bak ? Toutes les modifications locales seront perdues.`)) return;
  try {
    const r = await fetch('/api/restore', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ path: file.serverPath })
    });
    if (!r.ok) {
      const txt = await r.text();
      const reason = r.status === 404 ? 'aucun .bak (le fichier n\'a jamais été sauvegardé via /api/save)' :
                     r.status === 403 ? 'chemin pas dans la liste blanche du scan' :
                     (txt || `HTTP ${r.status}`);
      showToast(`Restore KO : ${reason}`, 'error');
      return;
    }
    const data = await r.json();
    file.original = data.content;
    file.current = data.content;
    file.history = [];
    // BUG fix : sans `await`, renderAll() s'execute avant que les issues
    // soient retournees, donc le tree est rendu avec les anciens counters.
    await runAnalysis(file);
    showToast(`Restauré depuis .bak (${data.bytes} octets).`, 'success');
    renderAll();
  } catch (e) {
    showToast('Restore échoué : ' + e.message, 'error');
  }
}

async function saveCurrentToServer() {
  const file = currentFile();
  if (!file) return;
  if (!file.serverPath) {
    showToast('Ce fichier n\'a pas été chargé via /api/scan, on ne sait pas où l\'écrire.', 'error');
    return;
  }
  if (file.current === file.original) {
    showToast('Aucune modification à sauvegarder.', 'error');
    return;
  }
  try {
    const r = await fetch('/api/save', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ path: file.serverPath, content: file.current })
    });
    if (!r.ok) {
      const txt = await r.text();
      showToast(`Save KO (${r.status}) : ${txt.substring(0, 120) || '(403 = chemin pas dans la liste blanche du scan)'}`, 'error');
      return;
    }
    const data = await r.json();
    file.original = file.current; // disque et mémoire sont desormais alignés
    // Re-analyse pour garantir que les counters de la sidebar sont en sync
    // avec le contenu reel du fichier (couvre le cas ou le disque aurait
    // ete modifie entre temps + sert de filet de securite).
    await runAnalysis(file);
    const note = data.backedUp ? ' (backup .bak créé)' : '';
    showToast(`Enregistré sur le serveur : ${data.bytes} octets${note}.`, 'success');
    renderAll();
  } catch (e) {
    showToast('Save échoué : ' + e.message, 'error');
  }
}

function downloadCurrent() {
  const file = currentFile();
  if (!file) { showToast('Aucun fichier sélectionné.', 'error'); return; }
  if (file.current === file.original) {
    showToast('Le fichier n\'a pas été modifié — rien à télécharger.', 'error');
    return;
  }
  try {
    const blob = new Blob([file.current], { type: 'text/plain;charset=utf-8' });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = file.name.replace(/(\.[^.]+)$/, '.fixed$1');
    a.style.display = 'none';
    document.body.appendChild(a);   // certains navigateurs (Firefox) exigent que le <a> soit dans le DOM
    a.click();
    setTimeout(() => {
      document.body.removeChild(a);
      URL.revokeObjectURL(url);
    }, 100);
    showToast(`Téléchargement de ${a.download}.`, 'success');
  } catch (e) {
    showToast('Erreur de téléchargement : ' + e.message, 'error');
    console.error(e);
  }
}

function exportReport() {
  if (state.files.length === 0) { showToast('Aucun fichier à exporter.', 'error'); return; }
  const lines = [];
  lines.push('# Rapport ASPX·LINT');
  lines.push(`Date : ${new Date().toLocaleString('fr-FR')}`);
  lines.push(`Fichiers analysés : ${state.files.length}`);
  lines.push('');
  state.files.forEach(f => {
    lines.push(`## ${f.name}`);
    lines.push(`- Type : ${f.ext.toUpperCase()}`);
    lines.push(`- Lignes : ${f.current.split(/\r?\n/).length}`);
    lines.push(`- Problèmes : ${f.issues.length}`);
    lines.push('');
    if (f.issues.length === 0) {
      lines.push('*Aucun problème détecté.*');
    } else {
      f.issues.forEach(i => {
        lines.push(`### [${i.severity.toUpperCase()}] ${i.ruleId} — ${i.ruleName}  (L${i.line}:${i.col})`);
        lines.push('');
        lines.push(i.desc);
        lines.push('');
        lines.push('**Extrait :** `' + i.snippet + '`');
        lines.push('');
        lines.push('**Solution :** ' + i.hint);
        lines.push('');
      });
    }
    lines.push('---');
    lines.push('');
  });
  const blob = new Blob([lines.join('\n')], { type: 'text/markdown;charset=utf-8' });
  const url = URL.createObjectURL(blob);
  const a = document.createElement('a');
  a.href = url;
  a.download = `aspx-lint-report-${Date.now()}.md`;
  a.click();
  URL.revokeObjectURL(url);
  showToast('Rapport exporté en Markdown.', 'success');
}

/* ============================================================
   ACTIONS EN LOT — sur la selection ou sur tout le projet
   ============================================================ */

/**
 * Renvoie les fichiers cibles pour une action en lot :
 *  - si la multi-selection contient 2+ items, on prend ceux-la
 *  - sinon on prend tous les fichiers du projet
 * Permet a l'utilisateur de basculer "tout" ↔ "selection" sans toucher au menu.
 */
function targetsForBulk() {
  const sel = Array.from(state.selectedFileIds)
    .map(id => state.files.find(f => f.id === id))
    .filter(Boolean);
  if (sel.length >= 2) return { files: sel, scope: 'selection' };
  return { files: state.files.slice(), scope: 'all' };
}

function renderBulkBar() {
  const bar = $('bulkBar');
  if (!bar) return;
  const sel = state.selectedFileIds.size;
  const total = state.files.length;
  if (total === 0) { bar.style.display = 'none'; return; }
  bar.style.display = '';
  const label = sel >= 2
    ? `${sel} fichier(s) sélectionné(s)`
    : `${total} fichier(s) au total`;
  $('bulkBarLabel').textContent = label;
  $('btnBulkClear').style.display = sel >= 2 ? '' : 'none';
}

async function fixAllInProject() {
  const { files, scope } = targetsForBulk();
  if (files.length === 0) { showToast('Aucun fichier.', 'error'); return; }
  if (!confirm(`Lancer "Tout corriger" sur ${files.length} fichier(s) (${scope === 'selection' ? 'sélection' : 'projet entier'}) ?`)) return;

  const report = { title: `Tout corriger — ${scope === 'selection' ? 'sélection' : 'projet entier'}`, rows: [] };
  let fixedFiles = 0;
  let totalFixes = 0;
  for (const f of files) {
    const issuesBefore = f.issues.length;
    const n = await applyAllFixes(f);
    const issuesAfter = f.issues.length;
    report.rows.push({
      file: f.name,
      fixes: n,
      issuesBefore,
      issuesAfter,
      delta: issuesBefore - issuesAfter,
      status: n > 0 ? 'fixed' : (issuesBefore === 0 ? 'clean' : 'unchanged')
    });
    if (n > 0) { fixedFiles++; totalFixes += n; }
  }
  showToast(`Auto-fix : ${totalFixes} correction(s) sur ${fixedFiles} fichier(s).`,
            totalFixes > 0 ? 'success' : 'error');
  renderAll();
  showBatchReport(report, [
    `Total fichiers traités : <strong>${files.length}</strong>`,
    `Fichiers modifiés : <strong>${fixedFiles}</strong>`,
    `Corrections appliquées : <strong style="color:var(--success)">${totalFixes}</strong>`,
  ]);
}

async function saveAllModified() {
  const { files, scope } = targetsForBulk();
  const targets = files.filter(f => f.serverPath && f.current !== f.original);
  if (targets.length === 0) {
    showToast('Aucun fichier modifié issu d\'un scan à enregistrer.', 'error');
    return;
  }
  if (!confirm(`Écrire ${targets.length} fichier(s) sur le disque (${scope === 'selection' ? 'sélection' : 'projet entier'}) ?\nUn .bak est créé avant chaque écrasement.`)) return;

  const report = { title: `Tout enregistrer — ${scope === 'selection' ? 'sélection' : 'projet entier'}`, rows: [] };
  let ok = 0, ko = 0;
  for (const f of targets) {
    try {
      const r = await fetch('/api/save', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ path: f.serverPath, content: f.current })
      });
      if (r.ok) {
        const data = await r.json();
        f.original = f.current;
        await runAnalysis(f);
        ok++;
        report.rows.push({ file: f.name, bytes: data.bytes, status: 'saved' });
      } else {
        ko++;
        const txt = await r.text().catch(() => '');
        report.rows.push({ file: f.name, status: 'error', error: `${r.status}: ${(txt || '').slice(0, 120)}` });
      }
    } catch (e) {
      ko++;
      report.rows.push({ file: f.name, status: 'error', error: e.message });
    }
  }
  showToast(`Sauvegarde : ${ok} OK${ko > 0 ? `, ${ko} KO` : ''}.`, ko > 0 ? 'error' : 'success');
  renderAll();
  showBatchReport(report, [
    `Fichiers candidats : <strong>${targets.length}</strong>`,
    `Enregistrés : <strong style="color:var(--success)">${ok}</strong>`,
    ko > 0 ? `Échecs : <strong style="color:var(--error)">${ko}</strong>` : null,
  ].filter(Boolean));
}

async function fixAndSaveProject() {
  // Combo : auto-fix puis save sur les memes cibles, en une seule action.
  const { files, scope } = targetsForBulk();
  const writable = files.filter(f => f.serverPath);
  if (writable.length === 0) {
    showToast('Aucun fichier issu d\'un scan dans la cible — rien à enregistrer.', 'error');
    return;
  }
  if (!confirm(`Corriger + enregistrer ${writable.length} fichier(s) (${scope === 'selection' ? 'sélection' : 'projet entier'}) ?`)) return;

  const report = { title: `Corriger & enregistrer — ${scope === 'selection' ? 'sélection' : 'projet entier'}`, rows: [] };
  let fixed = 0, saved = 0, ko = 0;
  for (const f of writable) {
    const issuesBefore = f.issues.length;
    const n = await applyAllFixes(f);
    fixed += n;
    if (f.current === f.original) {
      report.rows.push({ file: f.name, fixes: n, issuesBefore, issuesAfter: f.issues.length, delta: issuesBefore - f.issues.length, status: 'unchanged' });
      continue;
    }
    try {
      const r = await fetch('/api/save', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ path: f.serverPath, content: f.current })
      });
      if (r.ok) {
        const data = await r.json();
        f.original = f.current;
        await runAnalysis(f);
        saved++;
        report.rows.push({ file: f.name, fixes: n, issuesBefore, issuesAfter: f.issues.length, delta: issuesBefore - f.issues.length, bytes: data.bytes, status: 'fixed-saved' });
      } else {
        ko++;
        const txt = await r.text().catch(() => '');
        report.rows.push({ file: f.name, fixes: n, issuesBefore, issuesAfter: f.issues.length, delta: issuesBefore - f.issues.length, status: 'save-error', error: `${r.status}: ${(txt || '').slice(0, 120)}` });
      }
    } catch (e) {
      ko++;
      report.rows.push({ file: f.name, fixes: n, status: 'save-error', error: e.message });
    }
  }
  showToast(`${fixed} correction(s), ${saved} fichier(s) enregistré(s)${ko > 0 ? `, ${ko} KO` : ''}.`,
            ko > 0 ? 'error' : 'success');
  renderAll();
  showBatchReport(report, [
    `Fichiers traités : <strong>${writable.length}</strong>`,
    `Corrections : <strong style="color:var(--success)">${fixed}</strong>`,
    `Enregistrés : <strong>${saved}</strong>`,
    ko > 0 ? `Échecs save : <strong style="color:var(--error)">${ko}</strong>` : null,
  ].filter(Boolean));
}

/* ============================================================
   BATCH REPORT MODAL — utilise par les actions en lot
   ============================================================ */
function showBatchReport(report, summaryLines) {
  $('batchReportTitle').textContent = report.title;
  $('batchReportSummary').innerHTML = summaryLines.map(l => `<div>${l}</div>`).join('');
  const list = $('batchReportList');
  if (!report.rows || report.rows.length === 0) {
    list.innerHTML = '<div class="batch-report-empty">Aucun détail à afficher.</div>';
  } else {
    list.innerHTML = report.rows.map(r => {
      let statusBadge = '';
      let statusClass = '';
      switch (r.status) {
        case 'fixed':       statusBadge = `+${r.fixes} fix`; statusClass = 'ok'; break;
        case 'fixed-saved': statusBadge = `+${r.fixes} fix · ${r.bytes}o`; statusClass = 'ok'; break;
        case 'saved':       statusBadge = `${r.bytes}o`; statusClass = 'ok'; break;
        case 'unchanged':   statusBadge = 'inchangé'; statusClass = 'muted'; break;
        case 'clean':       statusBadge = 'propre'; statusClass = 'muted'; break;
        case 'error':       statusBadge = 'erreur'; statusClass = 'err'; break;
        case 'save-error':  statusBadge = 'save KO'; statusClass = 'err'; break;
        default:            statusBadge = r.status || ''; break;
      }
      const delta = r.delta !== undefined ? ` <span class="batch-row-delta">issues : ${r.issuesBefore} → ${r.issuesAfter}</span>` : '';
      const err = r.error ? `<div class="batch-row-error">${escapeHtml(r.error)}</div>` : '';
      return `
        <div class="batch-row ${statusClass}">
          <span class="batch-row-status">${statusBadge}</span>
          <div class="batch-row-main">
            <div class="batch-row-file">${escapeHtml(r.file)}</div>
            ${delta}
            ${err}
          </div>
        </div>
      `;
    }).join('');
  }
  $('batchReportModal').classList.add('show');
}

function closeBatchReport() { $('batchReportModal').classList.remove('show'); }

/** Combo sur le fichier courant : Tout corriger + Enregistrer. */
async function fixAndSaveCurrent() {
  const file = currentFile();
  if (!file) return;
  if (!file.serverPath) {
    showToast('Ce fichier n\'a pas été chargé via /api/scan, on ne sait pas où l\'écrire.', 'error');
    return;
  }
  const n = await applyAllFixes(file);
  if (n === 0 && file.current === file.original) {
    showToast('Rien à corriger ni à enregistrer.', 'error');
    return;
  }
  if (file.current !== file.original) {
    try {
      const r = await fetch('/api/save', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ path: file.serverPath, content: file.current })
      });
      if (!r.ok) {
        const txt = await r.text();
        showToast(`Save KO (${r.status}) : ${txt.substring(0, 120)}`, 'error');
        return;
      }
      const data = await r.json();
      file.original = file.current;
      await runAnalysis(file);
      showToast(`${n} correction(s) appliquée(s) + enregistré (${data.bytes} octets).`, 'success');
    } catch (e) {
      showToast('Save échoué : ' + e.message, 'error');
      return;
    }
  } else {
    showToast(`${n} correction(s) appliquée(s) (déjà à jour sur disque).`, 'success');
  }
  renderAll();
}

/* ============================================================
   MODALS & DEMO
   ============================================================ */
function openPasteModal() { $('pasteModal').classList.add('show'); }
function closePasteModal() { $('pasteModal').classList.remove('show'); }
async function addPastedFile() {
  const name = $('pasteFileName').value.trim() || 'snippet.aspx';
  const content = $('pasteContent').value;
  if (!content.trim()) { showToast('Contenu vide.', 'error'); return; }
  await addFile(name, content);
  state.currentFileId = state.files[state.files.length - 1].id;
  $('pasteContent').value = '';
  closePasteModal();
  renderAll();
  showToast(`${name} ajouté.`, 'success');
}

function openRulesModal() {
  $('rulesModal').classList.add('show');
  const list = $('rulesList');
  list.innerHTML = RULES.map(r => `
    <div class="rule-card">
      <div>
        <span class="rule-id">${r.id}</span>
        <span class="rule-name">${escapeHtml(r.name)}</span>
        <span class="issue-severity ${r.severity}" style="margin-left:8px">${r.severity}</span>
        ${r.hasFix ? '<span class="issue-severity info" style="background:transparent;color:var(--accent);border:1px solid var(--accent)">auto-fix</span>' : ''}
      </div>
      <div class="rule-desc">${escapeHtml(r.desc)}</div>
    </div>
  `).join('');
}
function closeRulesModal() { $('rulesModal').classList.remove('show'); }

async function loadDemo() {
  const demoAspx = `<%@ Page Language="C#" AutoEventWireup=true CodeBehind="Default.aspx.cs" Inherits="MyApp.Default"%>
<!DOCTYPE html>
<HTML xmlns="http://www.w3.org/1999/xhtml">
<head runat='server'>
    <title>Page de démonstration</title>
    <meta charset=utf-8>
    <link rel="stylesheet" href="style.css?v=1&debug=true">
</head>
<body>
    <form id="form1">
        <DIV class="container">
            <h1>Bonjour & bienvenue</h1>
            <asp:Label ID="lblMessage" Text="Hello"></asp:Label>
            <asp:Button ID="btnSubmit" Text="Envoyer" />
            <asp:TextBox ID="lblMessage" runat="server" />
            <br>
            <img src="logo.png" alt="logo">
            <input type=text name=username>
        </DIV>



            <p>Du contenu</p>
        <span>tag jamais fermé
        <!-- commentaire avec -- problème -->
        <%=DateTime.Now%>
    </form>
</body>
</HTML>`;

  const demoMaster = `<%@ Master Language="C#" AutoEventWireup="true" CodeBehind="Site.master.cs" Inherits="MyApp.SiteMaster" %>
<!DOCTYPE html>
<html>
<head runat="server">
    <title>Site</title>
    <asp:ContentPlaceHolder runat="server" />
</head>
<body>
    <form runat="server">
        <header>
            <h1>Mon Site</h1>
        </header>
        <main>
            <asp:ContentPlaceHolder ID="MainContent" runat="server"></asp:ContentPlaceHolder>
        </main>
        <footer>© 2024</footer>
    </form>
</body>
</html>`;

  const demoAscx = `<%@ Control Language="C#" AutoEventWireup="true" CodeBehind="Menu.ascx.cs" Inherits="MyApp.Menu" %>
<nav class="main-menu">
    <ul>
        <li><a href="?page=home&lang=fr">Accueil</a></li>
        <li><a href='?page=about'>À propos</a></li>
    </ul>
    <asp:LoginStatus ID="LoginStatus1" />
</nav>`;

  await addFile('Default.aspx', demoAspx);
  await addFile('Site.master', demoMaster);
  await addFile('Menu.ascx', demoAscx);
  state.currentFileId = state.files[state.files.length - 3].id;
  renderAll();
  showToast('3 exemples chargés.', 'success');
}

/* ============================================================
   TOAST
   ============================================================ */
let toastTimer;
function showToast(msg, type = '') {
  const t = $('toast');
  t.textContent = msg;
  t.className = 'toast show ' + type;
  clearTimeout(toastTimer);
  toastTimer = setTimeout(() => t.classList.remove('show'), 2800);
}

/* ============================================================
   INIT — Drag & drop (fichiers OU dossiers)
   ============================================================
   La zone de drop visible a ete remplacee par un bouton compact
   "Charger un fichier" : on garde le drag&drop sur toute la fenetre
   et on signale visuellement le mode drag via une classe sur <body>.
   Le drop d'un dossier traverse l'arborescence via webkitGetAsEntry()
   et envoie chaque fichier .aspx/.ascx/.master/.asax/.cs avec son
   chemin relatif (pour que l'arbre de la sidebar les groupe par dossier).
   ============================================================ */

const DROP_EXT = new Set(['aspx', 'ascx', 'master', 'asax', 'cs']);

/** Recursivement collecte les fichiers utiles a partir d'un FileSystemEntry. */
async function walkDropEntry(entry, prefix, out) {
  if (entry.isFile) {
    const file = await new Promise((res, rej) => entry.file(res, rej));
    const name = prefix ? prefix + '/' + file.name : file.name;
    const m = name.toLowerCase().match(/\.([a-z]+)$/);
    if (m && DROP_EXT.has(m[1])) out.push({ name, file });
    return;
  }
  if (entry.isDirectory) {
    const reader = entry.createReader();
    // readEntries renvoie par batchs (~100 max) — il faut boucler jusqu'a vide.
    while (true) {
      const batch = await new Promise((res, rej) => reader.readEntries(res, rej));
      if (!batch || batch.length === 0) return;
      for (const child of batch) {
        await walkDropEntry(child, prefix ? prefix + '/' + entry.name : entry.name, out);
      }
    }
  }
}

async function handleDroppedEntries(collected) {
  if (collected.length === 0) {
    showToast('Aucun .aspx / .ascx / .master / .asax / .cs dans ce drop.', 'error');
    return;
  }
  showToast(`Chargement de ${collected.length} fichier(s)…`, 'success');
  const beforeCount = state.files.length;
  for (const e of collected) {
    const text = await e.file.text();
    await addFile(e.name, text);
  }
  if (state.files.length > beforeCount && !state.currentFileId) {
    state.currentFileId = state.files[beforeCount].id;
  }
  showToast(`${collected.length} fichier(s) ajouté(s).`, 'success');
  renderAll();
}

function initDragDrop() {
  const input = $('fileInput');
  input.addEventListener('change', (e) => handleFiles(e.target.files));

  // Compteur de dragenter/dragleave : sans ca, le passage entre 2 enfants
  // declenche un dragleave parasite et la classe disparait avant le drop.
  let dragCounter = 0;

  document.addEventListener('dragenter', (e) => {
    if (!e.dataTransfer || !Array.from(e.dataTransfer.types || []).includes('Files')) return;
    e.preventDefault();
    dragCounter++;
    document.body.classList.add('dragging-files');
  });

  document.addEventListener('dragover', (e) => {
    if (e.dataTransfer && Array.from(e.dataTransfer.types || []).includes('Files')) {
      e.preventDefault();
    }
  });

  document.addEventListener('dragleave', (e) => {
    dragCounter--;
    if (dragCounter <= 0) {
      dragCounter = 0;
      document.body.classList.remove('dragging-files');
    }
  });

  document.addEventListener('drop', async (e) => {
    e.preventDefault();
    dragCounter = 0;
    document.body.classList.remove('dragging-files');

    // CRITICAL : capturer les entries SYNCHRONIQUEMENT — apres la fin du
    // handler, dataTransfer.items est invalide et webkitGetAsEntry retourne null.
    const entries = [];
    if (e.dataTransfer.items) {
      for (const item of e.dataTransfer.items) {
        if (item.kind !== 'file') continue;
        const entry = item.webkitGetAsEntry ? item.webkitGetAsEntry() : null;
        if (entry) entries.push(entry);
      }
    }

    if (entries.length > 0) {
      // Au moins un dossier ? On essaie d'abord un scan serveur (heuristique :
      // chercher ce nom sous AllowedRoot via /api/find-folder). Si l'utilisateur
      // annule ou que rien ne matche, on retombe sur le walk client.
      const dirEntry = entries.find(en => en.isDirectory);
      if (dirEntry) {
        // Capture les entries pour le fallback walk client.
        droppedEntriesPending = entries;
        await openBrowseForDroppedFolder(dirEntry.name);
        return;
      }
    }

    // Fichiers plats : ancienne API, plus simple.
    if (e.dataTransfer.files.length > 0) handleFiles(e.dataTransfer.files);
  });
}

/* ============================================================
   FOLDER DROP → BROWSE MODAL HEURISTIQUE
   ============================================================
   Le drag&drop d'un dossier ne donne PAS le path absolu (sandbox
   navigateur). Strategie : ouvrir le modal de scan, chercher ce nom
   sous AllowedRoot. Si l'utilisateur clique "Scanner", on a un vrai
   serverPath (save-to-disk possible). S'il annule ou que rien ne
   matche, on retombe sur un upload client (sans serverPath).
   ============================================================ */

let droppedEntriesPending = null;   // FileSystemEntry[] capturees au drop

async function openBrowseForDroppedFolder(folderName) {
  // Met le modal en "drop mode" : banner + bouton de fallback different.
  browseState.dropMode = true;
  browseState.droppedFolderName = folderName;

  $('browseModal').classList.add('show');
  $('browsePath').innerHTML = '<em>Recherche…</em>';
  $('browseList').innerHTML =
    `<div class="browse-empty">Recherche du dossier "${escapeHtml(folderName)}" sur le serveur…</div>`;
  $('browseUpBtn').disabled = true;
  $('browseScanBtn').disabled = true;
  renderBrowseDropBanner();

  let matches = [];
  try {
    const r = await fetch('/api/find-folder?name=' + encodeURIComponent(folderName) + '&limit=20');
    if (r.ok) matches = (await r.json()).matches || [];
  } catch (e) {
    console.warn('find-folder echoue :', e);
  }

  if (matches.length === 1) {
    // Un seul match : on y va direct, l'utilisateur n'a plus qu'a cliquer "Scanner".
    showToast(`Dossier "${folderName}" trouvé sur le serveur.`, 'success');
    await loadBrowse(matches[0].path);
    renderBrowseDropBanner();   // re-affiche apres loadBrowse qui le retire
    return;
  }

  if (matches.length > 1) {
    // Plusieurs matches : on les liste, l'utilisateur choisit.
    await loadBrowse(null);
    const list = $('browseList');
    list.innerHTML = '';
    const header = document.createElement('div');
    header.className = 'browse-empty';
    header.innerHTML = `<strong>${matches.length} dossier(s) "${escapeHtml(folderName)}" trouvé(s)</strong> — choisissez celui à scanner :`;
    list.appendChild(header);
    for (const m of matches) {
      const row = document.createElement('div');
      row.className = 'browse-entry';
      const badge = m.aspxCount > 0 ? `<span class="badge">${m.aspxCount} .aspx</span>` : '';
      row.innerHTML =
        '<span class="icon">📁</span>' +
        '<span class="name">' + escapeHtml(m.path) + '</span>' +
        badge;
      row.onclick = () => loadBrowse(m.path).then(renderBrowseDropBanner);
      list.appendChild(row);
    }
    renderBrowseDropBanner();
    return;
  }

  // Zero match : on demarre a la racine, l'utilisateur navigue manuellement
  // ou retombe sur le walk client via le bouton de fallback.
  showToast(`Dossier "${folderName}" introuvable sur le serveur.`, 'error');
  await loadBrowse(null);
  renderBrowseDropBanner();
}

function renderBrowseDropBanner() {
  const here = $('browseHere');
  if (!browseState.dropMode) return;
  here.innerHTML =
    `<strong>Mode drop :</strong> dossier "${escapeHtml(browseState.droppedFolderName || '?')}" déposé. ` +
    `Choisis-le sur le serveur pour un scan complet (avec save), ou ` +
    `<a href="#" onclick="event.preventDefault(); useDroppedEntriesFallback()" style="color:var(--accent)">charge en local à la place</a>.`;
}

async function useDroppedEntriesFallback() {
  // closeBrowseModal() reset le drop mode et clear les entries — on les capture AVANT.
  const entries = droppedEntriesPending;
  closeBrowseModal();
  if (!entries || entries.length === 0) {
    showToast('Aucun fichier en attente.', 'error');
    return;
  }
  const collected = [];
  for (const en of entries) await walkDropEntry(en, '', collected);
  await handleDroppedEntries(collected);
}

/* ============================================================
   FIND / REPLACE dans le code (Ctrl+F / Ctrl+H)
   ============================================================
   - Ouverture via raccourci clavier ou bouton
   - Highlight live des matches dans le code (post-render DOM walk pour
     wrapper les portions de texte dans des <mark>, sans casser les
     tokens deja emis par highlightLine)
   - Navigation prev/next avec scroll dans la vue
   - Replace : applique sur file.current, re-analyse, re-render
   ============================================================ */

function openSearchBar(prefill) {
  state.search.open = true;
  $('searchBar').style.display = '';
  if (typeof prefill === 'string') $('searchInput').value = prefill;
  $('searchInput').focus();
  $('searchInput').select();
  onSearchInput();
}

function closeSearchBar() {
  state.search.open = false;
  state.search.replaceOpen = false;
  state.search.query = '';
  state.search.matches = [];
  state.search.current = 0;
  $('searchBar').style.display = 'none';
  $('searchBarReplace').style.display = 'none';
  // Re-render pour nettoyer les marks
  renderCode();
}

function toggleSearchReplace() {
  state.search.replaceOpen = !state.search.replaceOpen;
  $('searchBarReplace').style.display = state.search.replaceOpen ? '' : 'none';
  if (state.search.replaceOpen) $('searchReplaceInput').focus();
}

function onSearchInput() {
  state.search.query = $('searchInput').value || '';
  state.search.caseSensitive = $('searchCaseSensitive').checked;
  state.search.current = 0;
  applySearchHighlights();
}

function onSearchKey(e) {
  if (e.key === 'Escape') { e.preventDefault(); closeSearchBar(); return; }
  if (e.key === 'Enter') {
    e.preventDefault();
    if (e.shiftKey) searchPrev(); else searchNext();
  }
}

function searchPrev() {
  if (state.search.matches.length === 0) return;
  state.search.current = (state.search.current - 1 + state.search.matches.length) % state.search.matches.length;
  highlightActiveMatch();
}

function searchNext() {
  if (state.search.matches.length === 0) return;
  state.search.current = (state.search.current + 1) % state.search.matches.length;
  highlightActiveMatch();
}

/**
 * Walk les text nodes dans .code-area > .line-content (mode normal) ou
 * .code-edit-highlight (mode edit) et wrap les matches dans <mark>.
 */
function applySearchHighlights() {
  const codeArea = $('codeArea');
  if (!codeArea) return;

  // Nettoie les marks existants en deballant leur texte.
  codeArea.querySelectorAll('mark.search-hit').forEach(mark => {
    const txt = document.createTextNode(mark.textContent);
    mark.parentNode.replaceChild(txt, mark);
  });
  // Normalize pour fusionner les text nodes adjacents.
  codeArea.normalize();

  const q = state.search.query;
  if (!q) {
    state.search.matches = [];
    updateSearchCount();
    return;
  }

  const flags = state.search.caseSensitive ? 'g' : 'gi';
  const re = new RegExp(escapeRegex(q), flags);
  const containerSel = '.line-content, .diff-content, .code-edit-highlight';

  // 1) Collecte les text nodes a traiter (sans modifier l'arbre pendant le walk).
  const targets = [];
  const walker = document.createTreeWalker(codeArea, NodeFilter.SHOW_TEXT, {
    acceptNode: (n) => {
      const parent = n.parentElement;
      if (!parent) return NodeFilter.FILTER_REJECT;
      if (parent.tagName === 'MARK') return NodeFilter.FILTER_REJECT;
      if (!parent.closest(containerSel)) return NodeFilter.FILTER_REJECT;
      return NodeFilter.FILTER_ACCEPT;
    }
  });
  let n; while ((n = walker.nextNode())) targets.push(n);

  // 2) Wrap chaque match dans <mark class="search-hit">.
  const allMarks = [];
  for (const node of targets) {
    const text = node.nodeValue;
    re.lastIndex = 0;
    const fragments = [];
    let lastEnd = 0, m, hit = false;
    while ((m = re.exec(text)) !== null) {
      hit = true;
      if (m.index > lastEnd) fragments.push(document.createTextNode(text.slice(lastEnd, m.index)));
      const mark = document.createElement('mark');
      mark.className = 'search-hit';
      mark.textContent = m[0];
      fragments.push(mark);
      allMarks.push(mark);
      lastEnd = m.index + m[0].length;
      if (m[0].length === 0) re.lastIndex++;   // garde-fou regex zero-length
    }
    if (hit) {
      if (lastEnd < text.length) fragments.push(document.createTextNode(text.slice(lastEnd)));
      const parent = node.parentNode;
      for (const f of fragments) parent.insertBefore(f, node);
      parent.removeChild(node);
    }
  }

  state.search.matches = allMarks;
  if (state.search.current >= allMarks.length) state.search.current = 0;
  updateSearchCount();
  highlightActiveMatch();
}

function highlightActiveMatch() {
  state.search.matches.forEach((m, i) =>
    m.classList.toggle('active', i === state.search.current));
  const active = state.search.matches[state.search.current];
  if (active) active.scrollIntoView({ block: 'center', behavior: 'instant' });
  updateSearchCount();
}

function updateSearchCount() {
  const total = state.search.matches.length;
  const cur = total === 0 ? 0 : state.search.current + 1;
  $('searchCount').textContent = `${cur}/${total}`;
}

function escapeRegex(s) { return s.replace(/[.*+?^${}()|[\]\\]/g, '\\$&'); }

async function replaceCurrent() {
  const file = currentFile();
  if (!file) return;
  const q = state.search.query;
  const r = $('searchReplaceInput').value || '';
  if (!q) return;

  // On remplace la N-eme occurrence dans file.current. Calcul des positions
  // sur le contenu source (regex flags = g + optionnel i).
  const flags = state.search.caseSensitive ? 'g' : 'gi';
  const re = new RegExp(escapeRegex(q), flags);
  let m, count = 0;
  let newContent = file.current;
  re.lastIndex = 0;
  // Pour eviter l'infinite loop sur match vide ou regex avec memoire,
  // on collecte d'abord les positions, puis on remplace en partant de la fin.
  const positions = [];
  while ((m = re.exec(file.current)) !== null) {
    positions.push({ start: m.index, end: m.index + m[0].length });
    if (m[0].length === 0) re.lastIndex++;
  }
  if (positions.length === 0) { showToast('Aucun match.', 'error'); return; }
  const idx = state.search.current % positions.length;
  const p = positions[idx];
  newContent = file.current.slice(0, p.start) + r + file.current.slice(p.end);
  file.current = newContent;
  await runAnalysis(file);
  state.search.current = idx;
  renderAll();
  applySearchHighlights();
  showToast(`Remplacé 1 occurrence (${positions.length - 1} restante(s)).`, 'success');
}

async function replaceAll() {
  const file = currentFile();
  if (!file) return;
  const q = state.search.query;
  const r = $('searchReplaceInput').value || '';
  if (!q) return;
  const flags = state.search.caseSensitive ? 'g' : 'gi';
  const re = new RegExp(escapeRegex(q), flags);
  const before = file.current;
  let count = 0;
  file.current = before.replace(re, () => { count++; return r; });
  if (count === 0) { showToast('Aucun match.', 'error'); return; }
  await runAnalysis(file);
  renderAll();
  applySearchHighlights();
  showToast(`${count} occurrence(s) remplacée(s).`, 'success');
}

/* ============================================================
   COMMAND PALETTE (Ctrl+P) — fichiers + commandes
   ============================================================ */

let paletteState = { items: [], selected: 0 };

const PALETTE_COMMANDS = [
  { name: 'Tout corriger (fichier courant)', icon: '⚡', run: () => fixAllInCurrent() },
  { name: 'Tout corriger (projet)',          icon: '⚡', run: () => fixAllInProject() },
  { name: 'Corriger & enregistrer (fichier)', icon: '✨', run: () => fixAndSaveCurrent() },
  { name: 'Corriger & enregistrer (projet)', icon: '✨', run: () => fixAndSaveProject() },
  { name: 'Tout enregistrer',                icon: '💾', run: () => saveAllModified() },
  { name: 'Re-vérifier le fichier courant',  icon: '🔍', run: () => verifyCurrent() },
  { name: 'Basculer Avant / Après',          icon: '⇆',  run: () => toggleDiff() },
  { name: 'Basculer Split View',             icon: '◫',  run: () => toggleSplitView() },
  { name: 'Télécharger le fichier corrigé',  icon: '⬇',  run: () => downloadCurrent() },
  { name: 'Exporter le rapport (Markdown)',  icon: '📊', run: () => exportReport() },
  { name: 'Scanner un dossier serveur',      icon: '📂', run: () => scanServerFolder() },
  { name: 'Charger un fichier',              icon: '📄', run: () => $('fileInput').click() },
  { name: 'Coller du code',                  icon: '📋', run: () => openPasteModal() },
  { name: 'Charger un exemple',              icon: '🎯', run: () => loadDemo() },
  { name: 'Voir les règles',                 icon: '📖', run: () => openRulesModal() }
];

function openPalette() {
  $('paletteModal').classList.add('show');
  $('paletteInput').value = '';
  $('paletteInput').focus();
  paletteState.selected = 0;
  renderPalette();
}

function closePalette() { $('paletteModal').classList.remove('show'); }

function renderPalette() {
  const q = ($('paletteInput').value || '').trim();
  let items;
  if (q.startsWith('>')) {
    // Mode commandes : on filtre les commandes par leur nom
    const sub = q.slice(1).trim().toLowerCase();
    items = PALETTE_COMMANDS
      .filter(c => !sub || c.name.toLowerCase().includes(sub))
      .map(c => ({ kind: 'cmd', cmd: c, label: c.name, icon: c.icon }));
  } else {
    // Mode fichiers : substring case-insensitive sur le path
    const sub = q.toLowerCase();
    items = state.files
      .filter(f => !sub || (f.name || '').toLowerCase().includes(sub))
      .slice(0, 100)
      .map(f => {
        const status = computeFileStatus(f);
        const dot = `<span class="palette-dot ${status}"></span>`;
        return { kind: 'file', file: f, label: f.name, html: dot };
      });
  }
  paletteState.items = items;
  if (paletteState.selected >= items.length) paletteState.selected = Math.max(0, items.length - 1);

  const list = $('paletteResults');
  if (items.length === 0) {
    list.innerHTML = '<div class="palette-empty">Aucun résultat.</div>';
    return;
  }
  list.innerHTML = items.map((it, i) => `
    <div class="palette-item ${i === paletteState.selected ? 'selected' : ''}" data-index="${i}" onclick="runPaletteItem(${i})" onmouseenter="paletteHover(${i})">
      ${it.icon ? `<span class="palette-icon">${it.icon}</span>` : (it.html || '')}
      <span class="palette-label">${highlightSearchInPalette(it.label, q.startsWith('>') ? q.slice(1).trim() : q)}</span>
      <span class="palette-kind">${it.kind === 'cmd' ? 'commande' : ''}</span>
    </div>
  `).join('');
  // Scroll the selected item into view.
  const sel = list.querySelector('.palette-item.selected');
  if (sel) sel.scrollIntoView({ block: 'nearest' });
}

function highlightSearchInPalette(text, q) {
  if (!q) return escapeHtml(text);
  const lower = text.toLowerCase();
  const idx = lower.indexOf(q.toLowerCase());
  if (idx < 0) return escapeHtml(text);
  return escapeHtml(text.substring(0, idx))
       + '<span class="match">' + escapeHtml(text.substring(idx, idx + q.length)) + '</span>'
       + escapeHtml(text.substring(idx + q.length));
}

function paletteHover(i) {
  if (paletteState.selected === i) return;
  paletteState.selected = i;
  renderPalette();
}

function runPaletteItem(index) {
  const it = paletteState.items[index];
  if (!it) return;
  closePalette();
  if (it.kind === 'file') {
    state.currentFileId = it.file.id;
    state.selectedFileIds = new Set([it.file.id]);
    state.viewMode = 'code';
    renderAll();
  } else if (it.kind === 'cmd') {
    it.cmd.run();
  }
}

function handlePaletteKey(e) {
  if (e.key === 'ArrowDown') {
    e.preventDefault();
    paletteState.selected = Math.min(paletteState.items.length - 1, paletteState.selected + 1);
    renderPalette();
  } else if (e.key === 'ArrowUp') {
    e.preventDefault();
    paletteState.selected = Math.max(0, paletteState.selected - 1);
    renderPalette();
  } else if (e.key === 'Enter') {
    e.preventDefault();
    runPaletteItem(paletteState.selected);
  } else if (e.key === 'Escape') {
    closePalette();
  }
}

/* ============================================================
   GLOBAL KEYBOARD SHORTCUTS
   ============================================================
   Ctrl+P        — palette (fichiers + commandes via prefix `>`)
   Ctrl+S        — Enregistrer le fichier courant
   Ctrl+Shift+S  — Enregistrer tous les fichiers modifies
   Ctrl+Shift+F  — Tout corriger (fichier courant)
   Ctrl+Alt+F    — Tout corriger (projet)
   Ctrl+R        — Re-verifier
   Ctrl+D        — Toggle Avant/Apres
   Esc           — Ferme le modal courant
   F5            — desactive (nuisible dans une dashboard)
   ============================================================ */
function isInputFocused() {
  const el = document.activeElement;
  if (!el) return false;
  const tag = el.tagName;
  return tag === 'INPUT' || tag === 'TEXTAREA' || el.isContentEditable;
}

document.addEventListener('keydown', (e) => {
  // Esc ferme tout modal ouvert.
  if (e.key === 'Escape') {
    closePasteModal();
    closeRulesModal();
    closeBrowseModal();
    closePalette();
    closeBatchReport();
    return;
  }

  // Si l'utilisateur tape dans un input/textarea, on laisse passer la plupart
  // des raccourcis sauf Ctrl+P qu'on intercepte toujours (sinon Chrome ouvre
  // sa boite "Imprimer").
  const inInput = isInputFocused();

  // Ctrl/Cmd+P — palette (intercept toujours)
  if ((e.ctrlKey || e.metaKey) && !e.shiftKey && !e.altKey && e.key.toLowerCase() === 'p') {
    e.preventDefault();
    openPalette();
    return;
  }

  // Ctrl/Cmd+F — find dans le code (intercept toujours, meme dans inputs ?
  // non — si l'utilisateur tape dans un champ on laisse le navigateur gerer).
  if ((e.ctrlKey || e.metaKey) && !e.shiftKey && !e.altKey && e.key.toLowerCase() === 'f' && !inInput) {
    e.preventDefault();
    openSearchBar();
    return;
  }
  // Ctrl/Cmd+H — find + replace
  if ((e.ctrlKey || e.metaKey) && !e.altKey && e.key.toLowerCase() === 'h' && !inInput) {
    e.preventDefault();
    openSearchBar();
    if (!state.search.replaceOpen) toggleSearchReplace();
    return;
  }

  if (inInput) return;   // les autres ne s'appliquent pas dans un input

  // Ctrl+Shift+F — Tout corriger projet
  if ((e.ctrlKey || e.metaKey) && e.shiftKey && e.key.toLowerCase() === 'f') {
    e.preventDefault(); fixAllInProject(); return;
  }
  // Ctrl+Shift+S — Enregistrer tout
  if ((e.ctrlKey || e.metaKey) && e.shiftKey && e.key.toLowerCase() === 's') {
    e.preventDefault(); saveAllModified(); return;
  }
  // Ctrl+S — Enregistrer fichier courant
  if ((e.ctrlKey || e.metaKey) && !e.shiftKey && e.key.toLowerCase() === 's') {
    e.preventDefault(); saveCurrentToServer(); return;
  }
  // Ctrl+Alt+F — Tout corriger (fichier courant)
  if ((e.ctrlKey || e.metaKey) && e.altKey && e.key.toLowerCase() === 'f') {
    e.preventDefault(); fixAllInCurrent(); return;
  }
  // Ctrl+R — Re-verifier
  if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 'r') {
    e.preventDefault(); verifyCurrent(); return;
  }
  // Ctrl+D — toggle diff
  if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 'd') {
    e.preventDefault(); toggleDiff(); return;
  }
  // Ctrl+E — toggle edit
  if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 'e') {
    e.preventDefault(); toggleEdit(); return;
  }
  // Fleches haut/bas : naviguer entre fichiers
  if (e.key === 'ArrowDown' || e.key === 'ArrowUp') {
    e.preventDefault();
    navigateFile(e.key === 'ArrowDown' ? 1 : -1);
  }
});

function navigateFile(direction) {
  const visible = state.files.filter(f => fileMatchesFilter(f, state.fileFilter));
  if (visible.length === 0) return;
  let idx = visible.findIndex(f => f.id === state.currentFileId);
  if (idx < 0) idx = direction > 0 ? -1 : visible.length;
  let next = idx + direction;
  if (next < 0) next = visible.length - 1;
  if (next >= visible.length) next = 0;
  state.currentFileId = visible[next].id;
  state.selectedFileIds = new Set([visible[next].id]);
  state.viewMode = 'code';
  renderAll();
  // Scroll the active file into view in the tree
  setTimeout(() => {
    const el = document.querySelector('.tree-file.active');
    if (el) el.scrollIntoView({ block: 'nearest' });
  }, 50);
}

initDragDrop();

/* ============================================================
   WEBVIEW2 BRIDGE — Desktop file watcher
   ============================================================
   Quand l'app Desktop tourne avec ASPXLINT_ALLOWED_ROOT, le watcher C#
   poste un message {kind:"fileChanges", paths:[...]} sur chaque change
   disque. On rafraichit le fichier concerne via /api/scan ou un read direct.
   ============================================================ */
if (window.chrome && window.chrome.webview && typeof window.chrome.webview.addEventListener === 'function') {
  window.chrome.webview.addEventListener('message', (e) => {
    let msg;
    try { msg = typeof e.data === 'string' ? JSON.parse(e.data) : e.data; }
    catch { return; }
    if (!msg) return;
    if (msg.kind === 'fileChanges' && Array.isArray(msg.paths)) {
      onDesktopFileChanges(msg.paths);
    } else if (msg.kind === 'droppedNativePaths' && Array.isArray(msg.paths)) {
      onDesktopNativeDrop(msg.paths);
    }
  });
}

/**
 * Drop d'un fichier ou dossier dans le WebView2 Desktop : on a recu les
 * chemins absolus Windows. Si c'est un dossier, on declenche /api/scan direct
 * (avec serverPath -> save-to-disk possible). Si c'est des fichiers, on
 * appelle /api/read pour chacun.
 */
async function onDesktopNativeDrop(paths) {
  // Heuristique : on traite le PREMIER dossier en priorite (cas le plus
  // courant : drag d'un dossier de projet). Si pas de dossier, on tente
  // les fichiers individuellement.
  let folderHandled = false;
  for (const p of paths) {
    try {
      // Detect dossier vs fichier en interrogeant /api/browse (qui repond
      // 200 sur un dossier, 404/403 sinon).
      const r = await fetch('/api/browse?path=' + encodeURIComponent(p));
      if (!r.ok) continue;
      const data = await r.json();
      if (data.path) {
        // C'est un dossier valide sous AllowedRoot. On le scanne.
        await scanServerFolderAtPath(p);
        folderHandled = true;
        break;
      }
    } catch { /* ignore */ }
  }
  if (folderHandled) return;

  // Sinon : tente comme fichiers individuels via /api/read.
  let added = 0;
  for (const p of paths) {
    try {
      const r = await fetch('/api/read', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ path: p })
      });
      if (!r.ok) continue;
      const data = await r.json();
      const name = p.split(/[\\/]/).pop() || 'inconnu';
      await addFile(name, data.content, p);
      added++;
    } catch { /* ignore */ }
  }
  if (added > 0) {
    showToast(`${added} fichier(s) chargé(s) depuis le drop natif.`, 'success');
    if (!state.currentFileId && state.files.length > 0) {
      state.currentFileId = state.files[state.files.length - 1].id;
    }
    renderAll();
  }
}

/** Lance un /api/scan direct sur un chemin absolu (depuis le drop natif). */
async function scanServerFolderAtPath(path) {
  showToast('Scan en cours…', 'success');
  try {
    const r = await fetch('/api/scan', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ path })
    });
    if (!r.ok) {
      const txt = await r.text();
      showToast(`Scan KO (${r.status}) : ${txt.substring(0, 100)}`, 'error');
      return;
    }
    const data = await r.json();
    if (!data.files || data.files.length === 0) {
      showToast('Aucun fichier .aspx / .ascx / .master / .asax dans ce dossier.', 'error');
      return;
    }
    const beforeCount = state.files.length;
    for (const sf of data.files) {
      const name = sf.relativePath || (sf.path || '').split(/[\\/]/).pop() || 'inconnu';
      await addFile(name, sf.content, sf.path);
    }
    if (state.files.length > beforeCount) {
      state.currentFileId = state.files[beforeCount].id;
    }
    showToast(`Scan OK : ${data.fileCount} fichier(s) chargé(s).`, 'success');
    renderAll();
  } catch (e) {
    showToast('Scan échoué : ' + e.message, 'error');
  }
}

async function onDesktopFileChanges(paths) {
  // On match les paths recus avec les fichiers actuellement charges via
  // serverPath, et on relit chacun via /api/read. Affiche un toast.
  let touched = 0;
  for (const p of paths) {
    const file = state.files.find(f => f.serverPath && f.serverPath.toLowerCase() === p.toLowerCase());
    if (!file) continue;
    try {
      const r = await fetch('/api/read', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ path: file.serverPath })
      });
      if (!r.ok) continue;
      const data = await r.json();
      file.original = data.content;
      file.current = data.content;
      file.history = [];
      await runAnalysis(file);
      touched++;
    } catch { /* ignore */ }
  }
  if (touched > 0) {
    showToast(`${touched} fichier(s) rechargé(s) (modification disque détectée).`, 'success');
    renderAll();
  }
}

// Bootstrap : on load les regles depuis /api/rules, puis on rend l'UI.
// La dashboard tourne toujours derriere AspxLint.Server (file:// bloque tout
// au debut du script).
(async () => {
  loadThemeFromStorage();
  persistEnabled = isPersistEnabled();
  $('persistToggle').checked = persistEnabled;
  await loadRulesFromServer();
  if (persistEnabled) await maybeRestoreFromStorage();
  renderAll();
})();
