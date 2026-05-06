/* ============================================================
   UI : RENDU
   ============================================================ */
function $(id) { return document.getElementById(id); }

function fileExtFromName(name) {
  const m = name.toLowerCase().match(/\.([a-z]+)$/);
  if (!m) return 'txt';
  if (['aspx','ascx','master','asax'].includes(m[1])) return m[1];
  return m[1];
}

function escapeHtml(s) {
  return s.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;')
          .replace(/"/g, '&quot;').replace(/'/g, '&#39;');
}

/** Encadre la sous-chaine matchee par state.fileSearch dans <span class="match">. */
function highlightSearch(s) {
  const escaped = escapeHtml(s);
  if (!state.fileSearch) return escaped;
  const q = state.fileSearch.toLowerCase();
  const lower = s.toLowerCase();
  const idx = lower.indexOf(q);
  if (idx < 0) return escaped;
  // On reapplique l'escape sur les 3 segments en se basant sur la chaine d'origine.
  return escapeHtml(s.substring(0, idx))
       + '<span class="match">' + escapeHtml(s.substring(idx, idx + q.length)) + '</span>'
       + escapeHtml(s.substring(idx + q.length));
}

/* Tokenizer single-pass : on travaille sur la chaîne BRUTE et on n'échappe
   le HTML qu'au moment d'émettre chaque token. Aucune regex ne peut donc
   re-matcher un <span> qu'on vient d'injecter. */
function highlightLine(line) {
  let out = '';
  let i = 0;
  const len = line.length;

  // Trouve la fin de balise (en sautant les > à l'intérieur de guillemets)
  const findTagEnd = (s, from) => {
    let q = null;
    for (let k = from; k < s.length; k++) {
      const c = s[k];
      if (q) { if (c === q) q = null; }
      else if (c === '"' || c === "'") q = c;
      else if (c === '>') return k;
    }
    return -1;
  };

  while (i < len) {
    // 1. Directive serveur <%@ ... %>
    if (line.startsWith('<%@', i)) {
      const end = line.indexOf('%>', i);
      if (end !== -1) {
        out += '<span class="tk-dir">' + escapeHtml(line.substring(i, end + 2)) + '</span>';
        i = end + 2; continue;
      }
    }
    // 2. Code serveur <% ... %> / <%= ... %> / <%# ... %> / <%: ... %>
    if (line.startsWith('<%', i)) {
      const end = line.indexOf('%>', i);
      if (end !== -1) {
        out += '<span class="tk-asp">' + escapeHtml(line.substring(i, end + 2)) + '</span>';
        i = end + 2; continue;
      }
    }
    // 3. Commentaire HTML <!-- ... -->
    if (line.startsWith('<!--', i)) {
      const end = line.indexOf('-->', i);
      if (end !== -1) {
        out += '<span class="tk-com">' + escapeHtml(line.substring(i, end + 3)) + '</span>';
        i = end + 3; continue;
      }
      // Commentaire qui se poursuit hors de la ligne
      out += '<span class="tk-com">' + escapeHtml(line.substring(i)) + '</span>';
      break;
    }
    // 4. <!DOCTYPE ...>
    if (line[i] === '<' && line[i + 1] === '!') {
      const end = line.indexOf('>', i);
      if (end !== -1) {
        out += '<span class="tk-dir">' + escapeHtml(line.substring(i, end + 1)) + '</span>';
        i = end + 1; continue;
      }
    }
    // 5. Balise <tag ...> ou </tag>
    if (line[i] === '<') {
      const end = findTagEnd(line, i);
      if (end !== -1) {
        out += highlightTag(line.substring(i, end + 1));
        i = end + 1; continue;
      }
    }
    // 6. Entité HTML &xxx;
    if (line[i] === '&') {
      const m = line.substring(i).match(/^&(?:[a-zA-Z][a-zA-Z0-9]{1,8}|#\d+|#x[0-9a-fA-F]+);/);
      if (m) {
        out += '<span class="tk-str">' + escapeHtml(m[0]) + '</span>';
        i += m[0].length; continue;
      }
    }
    // 7. Texte ordinaire — on consomme au moins UN caractère pour garantir la
    //    progression, puis on avance jusqu'au prochain caractère "spécial".
    //    Cas important : un '<' ou un '&' non reconnu (ex. "Bonjour & bienvenue")
    //    doit être traité comme du texte sans bloquer la boucle.
    let j = i + 1;
    while (j < len && line[j] !== '<' && line[j] !== '&') j++;
    out += escapeHtml(line.substring(i, j));
    i = j;
  }
  return out;
}

/* Tokenize une balise complète, p.ex. <asp:Button ID="b1" runat="server" /> */
function highlightTag(tag) {
  const m = tag.match(/^<(\/?)([a-zA-Z][a-zA-Z0-9:_\-]*)([\s\S]*?)(\s*\/?)>$/);
  if (!m) return escapeHtml(tag);
  const [, slash, name, attrs, tail] = m;
  const tagCls = name.includes(':') ? 'tk-asp' : 'tk-tag';

  let out = '<span class="tk-punct">' + escapeHtml('<' + slash) + '</span>';
  out += '<span class="' + tagCls + '">' + escapeHtml(name) + '</span>';

  // Tokenise la portion d'attributs sans toucher aux spans déjà émis.
  let p = 0;
  const a = attrs;
  while (p < a.length) {
    // Whitespace
    const ws = a.substring(p).match(/^\s+/);
    if (ws) { out += escapeHtml(ws[0]); p += ws[0].length; continue; }
    // attr=valeur (avec ou sans guillemets)
    const av = a.substring(p).match(/^([a-zA-Z_][a-zA-Z0-9\-:_]*)(\s*=\s*)("[^"]*"|'[^']*'|[^\s"'<>=`]+)?/);
    if (av && av[2]) {
      out += '<span class="tk-attr">' + escapeHtml(av[1]) + '</span>';
      out += escapeHtml(av[2]);
      if (av[3] !== undefined) {
        out += '<span class="tk-str">' + escapeHtml(av[3]) + '</span>';
      }
      p += av[0].length; continue;
    }
    // Attribut booléen seul
    const sa = a.substring(p).match(/^([a-zA-Z_][a-zA-Z0-9\-:_]*)/);
    if (sa) {
      out += '<span class="tk-attr">' + escapeHtml(sa[0]) + '</span>';
      p += sa[0].length; continue;
    }
    // Caractère isolé
    out += escapeHtml(a[p]); p++;
  }

  out += '<span class="tk-punct">' + escapeHtml(tail + '>') + '</span>';
  return out;
}

