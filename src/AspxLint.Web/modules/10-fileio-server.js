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

