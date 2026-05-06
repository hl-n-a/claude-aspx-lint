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

/* ============================================================
   SSE — abonnement aux evenements serveur
   ============================================================
   /api/events est un flux text/event-stream qui pousse les events
   `fileSaved`, `scanned`, etc. Si plusieurs onglets sont ouverts sur
   la meme dashboard, ils voient les mises a jour des autres en
   temps reel (utile aussi pour pairing telephone+desktop).
   ============================================================ */

let _sseSource = null;

function connectSse() {
  if (_sseSource) return;
  try {
    _sseSource = new EventSource('/api/events');
    _sseSource.onmessage = (e) => {
      let msg;
      try { msg = JSON.parse(e.data); } catch { return; }
      onSseEvent(msg);
    };
    _sseSource.onerror = () => {
      // EventSource gere le retry automatiquement (selon le retry: hint).
      // On laisse faire silencieusement.
    };
  } catch { /* navigateur sans EventSource */ }
}

function onSseEvent(msg) {
  if (!msg || !msg.kind) return;
  if (msg.kind === 'fileSaved' && msg.payload) {
    // Un autre client a sauve ce fichier -> on rafraichit le notre s'il est charge.
    const p = msg.payload.path;
    const file = state.files.find(f => f.serverPath && f.serverPath.toLowerCase() === (p || '').toLowerCase());
    if (file) {
      onDesktopFileChanges([p]);   // reuse le bridge handler qui re-read + re-analyse
    }
  } else if (msg.kind === 'scanned' && msg.payload) {
    // Pas d'action automatique — on pourrait afficher un toast discret.
    // showToast(`Scan distant : ${msg.payload.fileCount} fichier(s).`, 'success');
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
  connectSse();
})();
