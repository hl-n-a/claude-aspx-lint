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
/* ============================================================
   RECENTLY OPENED FILES — LRU persiste dans localStorage
   ============================================================
   Quand l'utilisateur ouvre un fichier, on push son nom dans une LRU
   capee a 30 entrees. La palette Ctrl+P utilise cette liste pour proposer
   les fichiers recemment vus en premier quand le query est vide.
   ============================================================ */
const RECENT_KEY = 'aspxlint.recent.v1';
const RECENT_MAX = 30;

function loadRecentFiles() {
  try {
    const raw = localStorage.getItem(RECENT_KEY);
    return raw ? JSON.parse(raw) : [];
  } catch { return []; }
}
function pushRecentFile(name) {
  if (!name) return;
  try {
    let list = loadRecentFiles().filter(n => n !== name);
    list.unshift(name);
    if (list.length > RECENT_MAX) list = list.slice(0, RECENT_MAX);
    localStorage.setItem(RECENT_KEY, JSON.stringify(list));
  } catch { /* quota plein */ }
}

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
  // Track dans la liste LRU pour la palette
  const file = state.files.find(f => f.id === id);
  if (file) pushRecentFile(file.name);
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

