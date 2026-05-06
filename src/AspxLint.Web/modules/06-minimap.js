/* ============================================================
   MINI-MAP — vignette compacte du fichier (style VSCode)
   ============================================================
   - 1 ligne source = 1 row de pixels (color = severite la plus elevee
     ou gris neutre si propre)
   - Rectangle "viewport" qui suit le scroll de codeArea
   - Click ou drag pour scroller a une position
   ============================================================ */

const MINIMAP_LINE_HEIGHT = 2;   // px par ligne de code (compact)
const MINIMAP_WIDTH = 80;
let minimapDragging = false;

function renderMinimap(file) {
  const canvas = $('codeMinimap');
  if (!canvas || !file) {
    if (canvas) canvas.style.display = 'none';
    return;
  }
  // En mode edit / split, on cache la minimap pour ne pas chevaucher l'overlay.
  if (state.editMode || state.splitView) {
    canvas.style.display = 'none';
    return;
  }
  canvas.style.display = '';

  const lines = file.current.split(/\r?\n/);
  const issuesByLine = new Map();
  for (const i of file.issues || []) {
    const cur = issuesByLine.get(i.line);
    const sevRank = i.severity === 'error' ? 3 : i.severity === 'warning' ? 2 : 1;
    if (!cur || sevRank > cur) issuesByLine.set(i.line, sevRank);
  }

  const area = $('codeArea');
  const visibleHeight = area ? area.clientHeight : 600;
  // Hauteur du canvas = ce qu'on a, mais lisere si plus de lignes que de pixels.
  const desiredHeight = Math.max(visibleHeight, lines.length * MINIMAP_LINE_HEIGHT);
  canvas.height = desiredHeight;
  canvas.width = MINIMAP_WIDTH;

  const ctx = canvas.getContext('2d');
  ctx.clearRect(0, 0, canvas.width, canvas.height);

  // Map theme -> couleurs.
  const css = getComputedStyle(document.documentElement);
  const colorBg     = css.getPropertyValue('--bg-2').trim() || '#14150f';
  const colorClean  = css.getPropertyValue('--text-faint').trim() || '#5a5a4f';
  const colorInfo   = css.getPropertyValue('--info').trim() || '#6db4ff';
  const colorWarn   = css.getPropertyValue('--warning').trim() || '#ffb84d';
  const colorErr    = css.getPropertyValue('--error').trim() || '#ff5e5e';

  ctx.fillStyle = colorBg;
  ctx.fillRect(0, 0, canvas.width, canvas.height);

  // Dessine 1 row par ligne, longueur ~ proportionnelle a la longueur de la ligne.
  for (let i = 0; i < lines.length; i++) {
    const lineLen = lines[i].length;
    const barLen = Math.min(MINIMAP_WIDTH - 6, Math.max(2, Math.round(lineLen / 2)));
    const sev = issuesByLine.get(i + 1);
    let color;
    if (sev === 3) color = colorErr;
    else if (sev === 2) color = colorWarn;
    else if (sev === 1) color = colorInfo;
    else color = colorClean;
    ctx.fillStyle = color;
    ctx.globalAlpha = sev ? 0.95 : 0.18;
    ctx.fillRect(3, i * MINIMAP_LINE_HEIGHT, barLen, Math.max(1, MINIMAP_LINE_HEIGHT - 0.5));
  }
  ctx.globalAlpha = 1;

  // Viewport indicator (rectangle qui suit le scroll de codeArea).
  drawMinimapViewport();

  // Hook scroll listener pour repeindre le viewport.
  if (!area._minimapHooked) {
    area.addEventListener('scroll', drawMinimapViewport, { passive: true });
    area._minimapHooked = true;
  }
  // Click / drag sur la minimap = scroll a la position.
  if (!canvas._minimapClickHooked) {
    canvas.addEventListener('mousedown', onMinimapMouse);
    canvas.addEventListener('mousemove', onMinimapMouse);
    canvas.addEventListener('mouseup',   () => { minimapDragging = false; });
    canvas.addEventListener('mouseleave', () => { minimapDragging = false; });
    canvas._minimapClickHooked = true;
  }
}

function drawMinimapViewport() {
  const canvas = $('codeMinimap');
  const area = $('codeArea');
  if (!canvas || !area || canvas.style.display === 'none') return;

  const ctx = canvas.getContext('2d');
  // On evite de redessiner toute la minimap : on colle un overlay viewport
  // dans un canvas dedie ou on utilise la composition. Pour simplicite,
  // on redessine la minimap entiere (rapide : ~500 lignes max typique).
  const file = currentFile();
  if (!file) return;
  // Re-dessiner les bars + viewport ensemble (cleaner).
  // → on appelle renderMinimap mais avec un short-circuit pour eviter
  //   de re-ajouter les listeners. En pratique on appelle juste paintViewport.
  paintMinimapViewportOnly();
}

function paintMinimapViewportOnly() {
  const canvas = $('codeMinimap');
  const area = $('codeArea');
  const file = currentFile();
  if (!canvas || !area || !file) return;
  const ctx = canvas.getContext('2d');

  // On efface la zone du viewport precedent en redessinant la minimap.
  // Pour eviter le flicker, on garde une "base" et on overlaye le viewport.
  // Strategie simple : on conserve un OffscreenCanvas / cache, mais ici
  // on se contente de redessiner via renderMinimap si pas de cache.
  // Pour rester simple et performant : on dessine le viewport en composite.
  const lines = file.current.split(/\r?\n/);
  const totalH = lines.length * MINIMAP_LINE_HEIGHT;
  const visibleRatio = area.clientHeight / Math.max(1, area.scrollHeight);
  const top = (area.scrollTop / Math.max(1, area.scrollHeight)) * totalH;
  const h = Math.max(20, visibleRatio * totalH);

  // Mode "redessine la base puis overlay" : peu cher sur ~500 lignes.
  renderMinimapBase(ctx, canvas, lines, file.issues || []);

  ctx.fillStyle = 'rgba(255, 255, 255, 0.08)';
  ctx.strokeStyle = 'rgba(255, 255, 255, 0.35)';
  ctx.fillRect(0, top, canvas.width, h);
  ctx.lineWidth = 1;
  ctx.strokeRect(0.5, top + 0.5, canvas.width - 1, h - 1);
}

function renderMinimapBase(ctx, canvas, lines, issues) {
  const css = getComputedStyle(document.documentElement);
  const colorBg     = css.getPropertyValue('--bg-2').trim() || '#14150f';
  const colorClean  = css.getPropertyValue('--text-faint').trim() || '#5a5a4f';
  const colorInfo   = css.getPropertyValue('--info').trim() || '#6db4ff';
  const colorWarn   = css.getPropertyValue('--warning').trim() || '#ffb84d';
  const colorErr    = css.getPropertyValue('--error').trim() || '#ff5e5e';

  ctx.fillStyle = colorBg;
  ctx.fillRect(0, 0, canvas.width, canvas.height);

  const issuesByLine = new Map();
  for (const i of issues) {
    const cur = issuesByLine.get(i.line);
    const sevRank = i.severity === 'error' ? 3 : i.severity === 'warning' ? 2 : 1;
    if (!cur || sevRank > cur) issuesByLine.set(i.line, sevRank);
  }

  for (let i = 0; i < lines.length; i++) {
    const lineLen = lines[i].length;
    const barLen = Math.min(canvas.width - 6, Math.max(2, Math.round(lineLen / 2)));
    const sev = issuesByLine.get(i + 1);
    let color;
    if (sev === 3) color = colorErr;
    else if (sev === 2) color = colorWarn;
    else if (sev === 1) color = colorInfo;
    else color = colorClean;
    ctx.fillStyle = color;
    ctx.globalAlpha = sev ? 0.95 : 0.18;
    ctx.fillRect(3, i * MINIMAP_LINE_HEIGHT, barLen, Math.max(1, MINIMAP_LINE_HEIGHT - 0.5));
  }
  ctx.globalAlpha = 1;
}

function onMinimapMouse(e) {
  if (e.type === 'mousedown') minimapDragging = true;
  if (e.type === 'mousemove' && !minimapDragging) return;
  const canvas = $('codeMinimap');
  const area = $('codeArea');
  if (!canvas || !area) return;
  const rect = canvas.getBoundingClientRect();
  const y = e.clientY - rect.top;
  const ratio = y / canvas.height;
  area.scrollTop = ratio * area.scrollHeight - area.clientHeight / 2;
}

/**
 * Sticky issue panel : quand l'utilisateur scrolle dans le code, on
 * surligne dans le panneau de droite l'issue dont la ligne est la plus
 * proche du haut du viewport. Si le panneau est dans un autre filtre
 * (errors only, etc.) on ne fait rien, le sync n'a pas lieu d'etre.
 */
let scrollSyncRaf = null;
function attachIssueScrollSync() {
  const area = $('codeArea');
  if (!area) return;
  area.onscroll = () => {
    if (scrollSyncRaf) cancelAnimationFrame(scrollSyncRaf);
    scrollSyncRaf = requestAnimationFrame(syncIssuePanel);
  };
}

function syncIssuePanel() {
  const area = $('codeArea');
  const issuesList = $('issuesList');
  if (!area || !issuesList) return;
  const file = currentFile();
  if (!file || file.issues.length === 0) return;

  // Trouve la ligne au sommet du viewport visible.
  const areaRect = area.getBoundingClientRect();
  const lineEls = area.querySelectorAll('.code-line[data-line]');
  let topLine = null;
  for (const el of lineEls) {
    const r = el.getBoundingClientRect();
    if (r.bottom >= areaRect.top) {
      topLine = parseInt(el.dataset.line, 10);
      break;
    }
  }
  if (topLine == null) return;

  // Trouve l'issue la plus proche au-dessus ou a topLine.
  const sorted = file.issues.slice().sort((a, b) => a.line - b.line);
  let active = sorted[0];
  for (const i of sorted) {
    if (i.line <= topLine) active = i;
    else break;
  }

  // Surligne le bloc d'issue correspondant et le scrolle dans le panneau.
  issuesList.querySelectorAll('.issue.sticky-active').forEach(el => el.classList.remove('sticky-active'));
  if (!active) return;
  // On cherche un bloc qui contient L{line}: dans son innerHTML — heuristique simple.
  const issueEls = issuesList.querySelectorAll('.issue');
  for (const el of issueEls) {
    const loc = el.querySelector('.issue-location');
    if (loc && loc.textContent.startsWith('L' + active.line + ':')) {
      el.classList.add('sticky-active');
      const elRect = el.getBoundingClientRect();
      const listRect = issuesList.getBoundingClientRect();
      if (elRect.top < listRect.top || elRect.bottom > listRect.bottom) {
        el.scrollIntoView({ block: 'center', behavior: 'instant' });
      }
      break;
    }
  }
}

