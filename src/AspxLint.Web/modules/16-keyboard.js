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

