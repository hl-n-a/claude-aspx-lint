/* ============================================================
   ACTIONS EN LOT — sur la selection ou sur tout le projet
   ============================================================ */

/**
 * Renvoie les fichiers cibles pour une action en lot :
 *  - si la multi-selection contient 2+ items, on prend ceux-la
 *  - sinon on prend tous les fichiers du projet
 * Permet a l'utilisateur de basculer "tout" ↔ "selection" sans toucher au menu.
 */
function targetsForBulk() {
  const sel = Array.from(state.selectedFileIds)
    .map(id => state.files.find(f => f.id === id))
    .filter(Boolean);
  if (sel.length >= 2) return { files: sel, scope: 'selection' };
  return { files: state.files.slice(), scope: 'all' };
}

function renderBulkBar() {
  const bar = $('bulkBar');
  if (!bar) return;
  const sel = state.selectedFileIds.size;
  const total = state.files.length;
  if (total === 0) { bar.style.display = 'none'; return; }
  bar.style.display = '';
  const label = sel >= 2
    ? `${sel} fichier(s) sélectionné(s)`
    : `${total} fichier(s) au total`;
  $('bulkBarLabel').textContent = label;
  $('btnBulkClear').style.display = sel >= 2 ? '' : 'none';
}

async function fixAllInProject() {
  const { files, scope } = targetsForBulk();
  if (files.length === 0) { showToast('Aucun fichier.', 'error'); return; }
  if (!confirm(`Lancer "Tout corriger" sur ${files.length} fichier(s) (${scope === 'selection' ? 'sélection' : 'projet entier'}) ?`)) return;

  const report = { title: `Tout corriger — ${scope === 'selection' ? 'sélection' : 'projet entier'}`, rows: [] };
  let fixedFiles = 0;
  let totalFixes = 0;
  for (const f of files) {
    const issuesBefore = f.issues.length;
    const n = await applyAllFixes(f);
    const issuesAfter = f.issues.length;
    report.rows.push({
      file: f.name,
      fixes: n,
      issuesBefore,
      issuesAfter,
      delta: issuesBefore - issuesAfter,
      status: n > 0 ? 'fixed' : (issuesBefore === 0 ? 'clean' : 'unchanged')
    });
    if (n > 0) { fixedFiles++; totalFixes += n; }
  }
  showToast(`Auto-fix : ${totalFixes} correction(s) sur ${fixedFiles} fichier(s).`,
            totalFixes > 0 ? 'success' : 'error');
  renderAll();
  showBatchReport(report, [
    `Total fichiers traités : <strong>${files.length}</strong>`,
    `Fichiers modifiés : <strong>${fixedFiles}</strong>`,
    `Corrections appliquées : <strong style="color:var(--success)">${totalFixes}</strong>`,
  ]);
}

async function saveAllModified() {
  const { files, scope } = targetsForBulk();
  const targets = files.filter(f => f.serverPath && f.current !== f.original);
  if (targets.length === 0) {
    showToast('Aucun fichier modifié issu d\'un scan à enregistrer.', 'error');
    return;
  }
  if (!confirm(`Écrire ${targets.length} fichier(s) sur le disque (${scope === 'selection' ? 'sélection' : 'projet entier'}) ?\nUn .bak est créé avant chaque écrasement.`)) return;

  const report = { title: `Tout enregistrer — ${scope === 'selection' ? 'sélection' : 'projet entier'}`, rows: [] };
  let ok = 0, ko = 0;
  for (const f of targets) {
    try {
      const r = await fetch('/api/save', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ path: f.serverPath, content: f.current })
      });
      if (r.ok) {
        const data = await r.json();
        f.original = f.current;
        await runAnalysis(f);
        ok++;
        report.rows.push({ file: f.name, bytes: data.bytes, status: 'saved' });
      } else {
        ko++;
        const txt = await r.text().catch(() => '');
        report.rows.push({ file: f.name, status: 'error', error: `${r.status}: ${(txt || '').slice(0, 120)}` });
      }
    } catch (e) {
      ko++;
      report.rows.push({ file: f.name, status: 'error', error: e.message });
    }
  }
  showToast(`Sauvegarde : ${ok} OK${ko > 0 ? `, ${ko} KO` : ''}.`, ko > 0 ? 'error' : 'success');
  renderAll();
  showBatchReport(report, [
    `Fichiers candidats : <strong>${targets.length}</strong>`,
    `Enregistrés : <strong style="color:var(--success)">${ok}</strong>`,
    ko > 0 ? `Échecs : <strong style="color:var(--error)">${ko}</strong>` : null,
  ].filter(Boolean));
}

async function fixAndSaveProject() {
  // Combo : auto-fix puis save sur les memes cibles, en une seule action.
  const { files, scope } = targetsForBulk();
  const writable = files.filter(f => f.serverPath);
  if (writable.length === 0) {
    showToast('Aucun fichier issu d\'un scan dans la cible — rien à enregistrer.', 'error');
    return;
  }
  if (!confirm(`Corriger + enregistrer ${writable.length} fichier(s) (${scope === 'selection' ? 'sélection' : 'projet entier'}) ?`)) return;

  const report = { title: `Corriger & enregistrer — ${scope === 'selection' ? 'sélection' : 'projet entier'}`, rows: [] };
  let fixed = 0, saved = 0, ko = 0;
  for (const f of writable) {
    const issuesBefore = f.issues.length;
    const n = await applyAllFixes(f);
    fixed += n;
    if (f.current === f.original) {
      report.rows.push({ file: f.name, fixes: n, issuesBefore, issuesAfter: f.issues.length, delta: issuesBefore - f.issues.length, status: 'unchanged' });
      continue;
    }
    try {
      const r = await fetch('/api/save', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ path: f.serverPath, content: f.current })
      });
      if (r.ok) {
        const data = await r.json();
        f.original = f.current;
        await runAnalysis(f);
        saved++;
        report.rows.push({ file: f.name, fixes: n, issuesBefore, issuesAfter: f.issues.length, delta: issuesBefore - f.issues.length, bytes: data.bytes, status: 'fixed-saved' });
      } else {
        ko++;
        const txt = await r.text().catch(() => '');
        report.rows.push({ file: f.name, fixes: n, issuesBefore, issuesAfter: f.issues.length, delta: issuesBefore - f.issues.length, status: 'save-error', error: `${r.status}: ${(txt || '').slice(0, 120)}` });
      }
    } catch (e) {
      ko++;
      report.rows.push({ file: f.name, fixes: n, status: 'save-error', error: e.message });
    }
  }
  showToast(`${fixed} correction(s), ${saved} fichier(s) enregistré(s)${ko > 0 ? `, ${ko} KO` : ''}.`,
            ko > 0 ? 'error' : 'success');
  renderAll();
  showBatchReport(report, [
    `Fichiers traités : <strong>${writable.length}</strong>`,
    `Corrections : <strong style="color:var(--success)">${fixed}</strong>`,
    `Enregistrés : <strong>${saved}</strong>`,
    ko > 0 ? `Échecs save : <strong style="color:var(--error)">${ko}</strong>` : null,
  ].filter(Boolean));
}

/* ============================================================
   BATCH REPORT MODAL — utilise par les actions en lot
   ============================================================ */
function showBatchReport(report, summaryLines) {
  $('batchReportTitle').textContent = report.title;
  $('batchReportSummary').innerHTML = summaryLines.map(l => `<div>${l}</div>`).join('');
  const list = $('batchReportList');
  if (!report.rows || report.rows.length === 0) {
    list.innerHTML = '<div class="batch-report-empty">Aucun détail à afficher.</div>';
  } else {
    list.innerHTML = report.rows.map(r => {
      let statusBadge = '';
      let statusClass = '';
      switch (r.status) {
        case 'fixed':       statusBadge = `+${r.fixes} fix`; statusClass = 'ok'; break;
        case 'fixed-saved': statusBadge = `+${r.fixes} fix · ${r.bytes}o`; statusClass = 'ok'; break;
        case 'saved':       statusBadge = `${r.bytes}o`; statusClass = 'ok'; break;
        case 'unchanged':   statusBadge = 'inchangé'; statusClass = 'muted'; break;
        case 'clean':       statusBadge = 'propre'; statusClass = 'muted'; break;
        case 'error':       statusBadge = 'erreur'; statusClass = 'err'; break;
        case 'save-error':  statusBadge = 'save KO'; statusClass = 'err'; break;
        default:            statusBadge = r.status || ''; break;
      }
      const delta = r.delta !== undefined ? ` <span class="batch-row-delta">issues : ${r.issuesBefore} → ${r.issuesAfter}</span>` : '';
      const err = r.error ? `<div class="batch-row-error">${escapeHtml(r.error)}</div>` : '';
      return `
        <div class="batch-row ${statusClass}">
          <span class="batch-row-status">${statusBadge}</span>
          <div class="batch-row-main">
            <div class="batch-row-file">${escapeHtml(r.file)}</div>
            ${delta}
            ${err}
          </div>
        </div>
      `;
    }).join('');
  }
  $('batchReportModal').classList.add('show');
}

function closeBatchReport() { $('batchReportModal').classList.remove('show'); }

/** Combo sur le fichier courant : Tout corriger + Enregistrer. */
async function fixAndSaveCurrent() {
  const file = currentFile();
  if (!file) return;
  if (!file.serverPath) {
    showToast('Ce fichier n\'a pas été chargé via /api/scan, on ne sait pas où l\'écrire.', 'error');
    return;
  }
  const n = await applyAllFixes(file);
  if (n === 0 && file.current === file.original) {
    showToast('Rien à corriger ni à enregistrer.', 'error');
    return;
  }
  if (file.current !== file.original) {
    try {
      const r = await fetch('/api/save', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ path: file.serverPath, content: file.current })
      });
      if (!r.ok) {
        const txt = await r.text();
        showToast(`Save KO (${r.status}) : ${txt.substring(0, 120)}`, 'error');
        return;
      }
      const data = await r.json();
      file.original = file.current;
      await runAnalysis(file);
      showToast(`${n} correction(s) appliquée(s) + enregistré (${data.bytes} octets).`, 'success');
    } catch (e) {
      showToast('Save échoué : ' + e.message, 'error');
      return;
    }
  } else {
    showToast(`${n} correction(s) appliquée(s) (déjà à jour sur disque).`, 'success');
  }
  renderAll();
}

