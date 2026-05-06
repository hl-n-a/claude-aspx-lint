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

