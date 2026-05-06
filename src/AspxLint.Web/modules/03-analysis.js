/* ============================================================
   ANALYSE
   ============================================================ */
async function analyzeFile(file) {
  let data;
  try {
    const r = await fetch('/api/analyze', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ content: file.current, ext: file.ext })
    });
    if (!r.ok) {
      console.warn('analyze HTTP', r.status);
      return [];
    }
    data = await r.json();
  } catch (e) {
    console.warn('analyze échoué :', e);
    return [];
  }

  let id = 1;
  return (data.issues || [])
    .map(i => ({
      id: `i${id++}`,
      ruleId: i.ruleId,
      ruleName: i.ruleName,
      severity: i.severity,
      desc: RULES.find(r => r.id === i.ruleId)?.desc || '',
      line: i.line,
      col: i.col || 1,
      snippet: i.snippet,
      hint: i.hint,
      fixable: !!RULES.find(r => r.id === i.ruleId)?.hasFix,
      fixed: false
    }))
    .sort((a, b) => a.line - b.line);
}

async function runAnalysis(file) {
  file.issues = await analyzeFile(file);
  file.hasRun = true;
}

/* ============================================================
   APPLICATION DES CORRECTIONS — avec historique
   ============================================================
   applyFix renvoie le nombre de problèmes effectivement résolus
   pour la règle, et empile chacun dans file.history.
   ============================================================ */
async function applyFix(file, ruleId) {
  const ruleMeta = RULES.find(r => r.id === ruleId);
  if (!ruleMeta || !ruleMeta.hasFix) return 0;

  const beforeIssues = file.issues.filter(i => i.ruleId === ruleId).map(i => ({...i}));
  if (beforeIssues.length === 0) return 0;

  let data;
  try {
    const r = await fetch('/api/fix', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ content: file.current, ext: file.ext, ruleId })
    });
    if (!r.ok) {
      console.warn('fix HTTP', r.status);
      return 0;
    }
    data = await r.json();
  } catch (e) {
    console.warn('fix échoué :', e);
    return 0;
  }

  if (data.content === file.current || data.applied === 0) return 0;

  file.current = data.content;
  await runAnalysis(file);

  const fixedNow = data.applied;
  for (let k = 0; k < fixedNow && k < beforeIssues.length; k++) {
    const orig = beforeIssues[k];
    file.history.push({
      ruleId: ruleMeta.id,
      ruleName: ruleMeta.name,
      severity: ruleMeta.severity,
      desc: ruleMeta.desc,
      line: orig.line,
      col: orig.col,
      snippet: orig.snippet,
      hint: orig.hint,
      fixedAt: Date.now()
    });
  }
  return fixedNow;
}

async function applyAllFixes(file) {
  let data;
  try {
    const r = await fetch('/api/fix-all', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ content: file.current, ext: file.ext })
    });
    if (!r.ok) {
      console.warn('fix-all HTTP', r.status);
      return 0;
    }
    data = await r.json();
  } catch (e) {
    console.warn('fix-all échoué :', e);
    return 0;
  }

  if (data.content === file.current) return 0;
  file.current = data.content;

  // Historique : on n'a pas le détail des issues effacées (seulement le total
  // par règle), donc on enregistre une entrée synthétique par groupe.
  const total = (data.history || []).reduce((s, h) => s + h.count, 0);
  for (const h of data.history || []) {
    const meta = RULES.find(r => r.id === h.ruleId) || { id: h.ruleId, name: h.ruleId, severity: 'info', desc: '' };
    for (let k = 0; k < h.count; k++) {
      file.history.push({
        ruleId: meta.id,
        ruleName: meta.name,
        severity: meta.severity,
        desc: meta.desc,
        fixedAt: Date.now()
      });
    }
  }

  await runAnalysis(file);
  return total;
}

