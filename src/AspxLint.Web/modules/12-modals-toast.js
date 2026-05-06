/* ============================================================
   MODALS & DEMO
   ============================================================ */
function openPasteModal() { $('pasteModal').classList.add('show'); }
function closePasteModal() { $('pasteModal').classList.remove('show'); }
async function addPastedFile() {
  const name = $('pasteFileName').value.trim() || 'snippet.aspx';
  const content = $('pasteContent').value;
  if (!content.trim()) { showToast('Contenu vide.', 'error'); return; }
  await addFile(name, content);
  state.currentFileId = state.files[state.files.length - 1].id;
  $('pasteContent').value = '';
  closePasteModal();
  renderAll();
  showToast(`${name} ajouté.`, 'success');
}

function openRulesModal() {
  $('rulesModal').classList.add('show');
  const list = $('rulesList');
  list.innerHTML = RULES.map(r => `
    <div class="rule-card">
      <div>
        <span class="rule-id">${r.id}</span>
        <span class="rule-name">${escapeHtml(r.name)}</span>
        <span class="issue-severity ${r.severity}" style="margin-left:8px">${r.severity}</span>
        ${r.hasFix ? '<span class="issue-severity info" style="background:transparent;color:var(--accent);border:1px solid var(--accent)">auto-fix</span>' : ''}
      </div>
      <div class="rule-desc">${escapeHtml(r.desc)}</div>
    </div>
  `).join('');
}
function closeRulesModal() { $('rulesModal').classList.remove('show'); }

async function loadDemo() {
  const demoAspx = `<%@ Page Language="C#" AutoEventWireup=true CodeBehind="Default.aspx.cs" Inherits="MyApp.Default"%>
<!DOCTYPE html>
<HTML xmlns="http://www.w3.org/1999/xhtml">
<head runat='server'>
    <title>Page de démonstration</title>
    <meta charset=utf-8>
    <link rel="stylesheet" href="style.css?v=1&debug=true">
</head>
<body>
    <form id="form1">
        <DIV class="container">
            <h1>Bonjour & bienvenue</h1>
            <asp:Label ID="lblMessage" Text="Hello"></asp:Label>
            <asp:Button ID="btnSubmit" Text="Envoyer" />
            <asp:TextBox ID="lblMessage" runat="server" />
            <br>
            <img src="logo.png" alt="logo">
            <input type=text name=username>
        </DIV>



            <p>Du contenu</p>
        <span>tag jamais fermé
        <!-- commentaire avec -- problème -->
        <%=DateTime.Now%>
    </form>
</body>
</HTML>`;

  const demoMaster = `<%@ Master Language="C#" AutoEventWireup="true" CodeBehind="Site.master.cs" Inherits="MyApp.SiteMaster" %>
<!DOCTYPE html>
<html>
<head runat="server">
    <title>Site</title>
    <asp:ContentPlaceHolder runat="server" />
</head>
<body>
    <form runat="server">
        <header>
            <h1>Mon Site</h1>
        </header>
        <main>
            <asp:ContentPlaceHolder ID="MainContent" runat="server"></asp:ContentPlaceHolder>
        </main>
        <footer>© 2024</footer>
    </form>
</body>
</html>`;

  const demoAscx = `<%@ Control Language="C#" AutoEventWireup="true" CodeBehind="Menu.ascx.cs" Inherits="MyApp.Menu" %>
<nav class="main-menu">
    <ul>
        <li><a href="?page=home&lang=fr">Accueil</a></li>
        <li><a href='?page=about'>À propos</a></li>
    </ul>
    <asp:LoginStatus ID="LoginStatus1" />
</nav>`;

  await addFile('Default.aspx', demoAspx);
  await addFile('Site.master', demoMaster);
  await addFile('Menu.ascx', demoAscx);
  state.currentFileId = state.files[state.files.length - 3].id;
  renderAll();
  showToast('3 exemples chargés.', 'success');
}

/* ============================================================
   TOAST
   ============================================================ */
let toastTimer;
function showToast(msg, type = '') {
  const t = $('toast');
  t.textContent = msg;
  t.className = 'toast show ' + type;
  clearTimeout(toastTimer);
  toastTimer = setTimeout(() => t.classList.remove('show'), 2800);
}

