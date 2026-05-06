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

