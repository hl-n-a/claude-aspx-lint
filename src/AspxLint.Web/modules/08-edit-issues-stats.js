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

