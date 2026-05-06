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
  renderMinimap(file);
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
  renderMinimap(file);
}

