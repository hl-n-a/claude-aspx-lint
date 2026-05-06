/* ============================================================
   FIND / REPLACE dans le code (Ctrl+F / Ctrl+H)
   ============================================================
   - Ouverture via raccourci clavier ou bouton
   - Highlight live des matches dans le code (post-render DOM walk pour
     wrapper les portions de texte dans des <mark>, sans casser les
     tokens deja emis par highlightLine)
   - Navigation prev/next avec scroll dans la vue
   - Replace : applique sur file.current, re-analyse, re-render
   ============================================================ */

function openSearchBar(prefill) {
  state.search.open = true;
  $('searchBar').style.display = '';
  if (typeof prefill === 'string') $('searchInput').value = prefill;
  $('searchInput').focus();
  $('searchInput').select();
  onSearchInput();
}

function closeSearchBar() {
  state.search.open = false;
  state.search.replaceOpen = false;
  state.search.query = '';
  state.search.matches = [];
  state.search.current = 0;
  $('searchBar').style.display = 'none';
  $('searchBarReplace').style.display = 'none';
  // Re-render pour nettoyer les marks
  renderCode();
}

function toggleSearchReplace() {
  state.search.replaceOpen = !state.search.replaceOpen;
  $('searchBarReplace').style.display = state.search.replaceOpen ? '' : 'none';
  if (state.search.replaceOpen) $('searchReplaceInput').focus();
}

function onSearchInput() {
  state.search.query = $('searchInput').value || '';
  state.search.caseSensitive = $('searchCaseSensitive').checked;
  state.search.current = 0;
  applySearchHighlights();
}

function onSearchKey(e) {
  if (e.key === 'Escape') { e.preventDefault(); closeSearchBar(); return; }
  if (e.key === 'Enter') {
    e.preventDefault();
    if (e.shiftKey) searchPrev(); else searchNext();
  }
}

function searchPrev() {
  if (state.search.matches.length === 0) return;
  state.search.current = (state.search.current - 1 + state.search.matches.length) % state.search.matches.length;
  highlightActiveMatch();
}

function searchNext() {
  if (state.search.matches.length === 0) return;
  state.search.current = (state.search.current + 1) % state.search.matches.length;
  highlightActiveMatch();
}

/**
 * Walk les text nodes dans .code-area > .line-content (mode normal) ou
 * .code-edit-highlight (mode edit) et wrap les matches dans <mark>.
 */
function applySearchHighlights() {
  const codeArea = $('codeArea');
  if (!codeArea) return;

  // Nettoie les marks existants en deballant leur texte.
  codeArea.querySelectorAll('mark.search-hit').forEach(mark => {
    const txt = document.createTextNode(mark.textContent);
    mark.parentNode.replaceChild(txt, mark);
  });
  // Normalize pour fusionner les text nodes adjacents.
  codeArea.normalize();

  const q = state.search.query;
  if (!q) {
    state.search.matches = [];
    updateSearchCount();
    return;
  }

  const flags = state.search.caseSensitive ? 'g' : 'gi';
  const re = new RegExp(escapeRegex(q), flags);
  const containerSel = '.line-content, .diff-content, .code-edit-highlight';

  // 1) Collecte les text nodes a traiter (sans modifier l'arbre pendant le walk).
  const targets = [];
  const walker = document.createTreeWalker(codeArea, NodeFilter.SHOW_TEXT, {
    acceptNode: (n) => {
      const parent = n.parentElement;
      if (!parent) return NodeFilter.FILTER_REJECT;
      if (parent.tagName === 'MARK') return NodeFilter.FILTER_REJECT;
      if (!parent.closest(containerSel)) return NodeFilter.FILTER_REJECT;
      return NodeFilter.FILTER_ACCEPT;
    }
  });
  let n; while ((n = walker.nextNode())) targets.push(n);

  // 2) Wrap chaque match dans <mark class="search-hit">.
  const allMarks = [];
  for (const node of targets) {
    const text = node.nodeValue;
    re.lastIndex = 0;
    const fragments = [];
    let lastEnd = 0, m, hit = false;
    while ((m = re.exec(text)) !== null) {
      hit = true;
      if (m.index > lastEnd) fragments.push(document.createTextNode(text.slice(lastEnd, m.index)));
      const mark = document.createElement('mark');
      mark.className = 'search-hit';
      mark.textContent = m[0];
      fragments.push(mark);
      allMarks.push(mark);
      lastEnd = m.index + m[0].length;
      if (m[0].length === 0) re.lastIndex++;   // garde-fou regex zero-length
    }
    if (hit) {
      if (lastEnd < text.length) fragments.push(document.createTextNode(text.slice(lastEnd)));
      const parent = node.parentNode;
      for (const f of fragments) parent.insertBefore(f, node);
      parent.removeChild(node);
    }
  }

  state.search.matches = allMarks;
  if (state.search.current >= allMarks.length) state.search.current = 0;
  updateSearchCount();
  highlightActiveMatch();
}

function highlightActiveMatch() {
  state.search.matches.forEach((m, i) =>
    m.classList.toggle('active', i === state.search.current));
  const active = state.search.matches[state.search.current];
  if (active) active.scrollIntoView({ block: 'center', behavior: 'instant' });
  updateSearchCount();
}

function updateSearchCount() {
  const total = state.search.matches.length;
  const cur = total === 0 ? 0 : state.search.current + 1;
  $('searchCount').textContent = `${cur}/${total}`;
}

function escapeRegex(s) { return s.replace(/[.*+?^${}()|[\]\\]/g, '\\$&'); }

async function replaceCurrent() {
  const file = currentFile();
  if (!file) return;
  const q = state.search.query;
  const r = $('searchReplaceInput').value || '';
  if (!q) return;

  // On remplace la N-eme occurrence dans file.current. Calcul des positions
  // sur le contenu source (regex flags = g + optionnel i).
  const flags = state.search.caseSensitive ? 'g' : 'gi';
  const re = new RegExp(escapeRegex(q), flags);
  let m, count = 0;
  let newContent = file.current;
  re.lastIndex = 0;
  // Pour eviter l'infinite loop sur match vide ou regex avec memoire,
  // on collecte d'abord les positions, puis on remplace en partant de la fin.
  const positions = [];
  while ((m = re.exec(file.current)) !== null) {
    positions.push({ start: m.index, end: m.index + m[0].length });
    if (m[0].length === 0) re.lastIndex++;
  }
  if (positions.length === 0) { showToast('Aucun match.', 'error'); return; }
  const idx = state.search.current % positions.length;
  const p = positions[idx];
  newContent = file.current.slice(0, p.start) + r + file.current.slice(p.end);
  file.current = newContent;
  await runAnalysis(file);
  state.search.current = idx;
  renderAll();
  applySearchHighlights();
  showToast(`Remplacé 1 occurrence (${positions.length - 1} restante(s)).`, 'success');
}

async function replaceAll() {
  const file = currentFile();
  if (!file) return;
  const q = state.search.query;
  const r = $('searchReplaceInput').value || '';
  if (!q) return;
  const flags = state.search.caseSensitive ? 'g' : 'gi';
  const re = new RegExp(escapeRegex(q), flags);
  const before = file.current;
  let count = 0;
  file.current = before.replace(re, () => { count++; return r; });
  if (count === 0) { showToast('Aucun match.', 'error'); return; }
  await runAnalysis(file);
  renderAll();
  applySearchHighlights();
  showToast(`${count} occurrence(s) remplacée(s).`, 'success');
}

