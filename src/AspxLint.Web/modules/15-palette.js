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
    // Mode fichiers : substring case-insensitive sur le path. Quand le query
    // est vide, on trie par "recently opened" (LRU en localStorage) pour
    // proposer en premier ce que l'utilisateur a vu recemment.
    const sub = q.toLowerCase();
    let candidates = state.files.filter(f => !sub || (f.name || '').toLowerCase().includes(sub));
    if (!sub) {
      const recent = loadRecentFiles();
      const recentIdx = new Map(recent.map((name, i) => [name, i]));
      candidates.sort((a, b) => {
        const ai = recentIdx.has(a.name) ? recentIdx.get(a.name) : Number.MAX_SAFE_INTEGER;
        const bi = recentIdx.has(b.name) ? recentIdx.get(b.name) : Number.MAX_SAFE_INTEGER;
        if (ai !== bi) return ai - bi;
        return a.name.localeCompare(b.name);
      });
    }
    items = candidates.slice(0, 100).map(f => {
      const status = computeFileStatus(f);
      const dot = `<span class="palette-dot ${status}"></span>`;
      const recent = !sub && loadRecentFiles().includes(f.name);
      return { kind: 'file', file: f, label: f.name, html: dot, recent };
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
      <span class="palette-kind">${it.kind === 'cmd' ? 'commande' : (it.recent ? '⏱ récent' : '')}</span>
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

