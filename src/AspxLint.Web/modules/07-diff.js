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
  renderMinimap(file);
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

