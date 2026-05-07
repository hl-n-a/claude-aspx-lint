using System.Text.RegularExpressions;

namespace AspxLint.Migrate;

/// <summary>
/// Severite d'un residu ASPX detecte dans un .cshtml :
///
///   <see cref="VerifySeverity.Bug"/>     : un transformer DEVAIT le gerer
///                                          mais l'a rate (regression).
///   <see cref="VerifySeverity.Pending"/>  : c'est dans une phase pas encore
///                                          implementee (controles serveur,
///                                          code-behind...).
///   <see cref="VerifySeverity.Manual"/>   : l'utilisateur doit traiter
///                                          (data-binding, etc.).
/// </summary>
public enum VerifySeverity
{
    Bug,
    Pending,
    Manual
}

public sealed record VerifyIssue(
    VerifySeverity Severity,
    string File,
    int Line,
    string Pattern,    // ex : "<asp:Label"
    string Snippet,    // courte capture pour situer
    string Suggestion  // explication + pointer vers la phase / regle
);

/// <summary>
/// Une regle de detection : un regex + des metadata. Chaque match produit
/// un <see cref="VerifyIssue"/>.
/// </summary>
internal sealed record VerifyRule(
    string Pattern,
    Regex Regex,
    VerifySeverity Severity,
    string Suggestion
);

/// <summary>
/// Scanne des fichiers .cshtml apres migration pour detecter des residus
/// ASPX qui n'ont pas ete transformes. Permet d'identifier :
///   1. Les bugs des transformers existants (regression)
///   2. Les patterns que les phases futures vont gerer (controles serveur,
///      data-binding, code-behind)
///   3. Les patterns qu'aucune phase ne gerera (a traiter manuellement)
///
/// Conçu pour tourner soit comme post-step de `migrate`, soit en standalone
/// via `aspx-lint migrate-verify`.
/// </summary>
public static class MigrationVerifier
{
    // ============= Regles de detection =============
    //
    // Ordre important : les patterns plus specifiques (asp:Label) avant les
    // plus generiques (asp:*). Le scanner ne matche pas deux fois le meme
    // span, mais on prefere des libelles precis quand on peut.

    private static readonly VerifyRule[] Rules =
    {
        // --- Residus syntaxiques (bugs des transformers Phase 1) ---
        new("server-comment", new(@"<%--[\s\S]*?--%>", RegexOptions.Compiled),
            VerifySeverity.Bug,
            "Commentaire ASPX residuel — devait etre transforme par ServerCommentTransformer."),
        new("server-directive", new(@"<%@[^%]*%>", RegexOptions.Compiled),
            VerifySeverity.Bug,
            "Directive ASPX residuelle — devait etre transformee par PageDirectiveTransformer."),
        new("server-expression", new(@"<%[=:#][\s\S]*?%>", RegexOptions.Compiled),
            VerifySeverity.Bug,
            "Expression ASPX residuelle — devait etre transformee par ServerExpressionTransformer."),
        new("server-statement", new(@"<%(?![=:#@\-])[\s\S]*?%>", RegexOptions.Compiled),
            VerifySeverity.Bug,
            "Bloc d'instruction ASPX residuel — devait etre transforme par ServerStatementTransformer."),

        // --- Tags <asp:Content> et <asp:ContentPlaceHolder> residuels ---
        new("asp:Content", new(@"<asp:Content\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            VerifySeverity.Bug,
            "Tag <asp:Content> residuel — devait etre transforme par ChildPageContentTransformer en @section ou contenu inline."),
        new("asp:ContentPlaceHolder", new(@"<asp:ContentPlaceHolder\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            VerifySeverity.Bug,
            "Tag <asp:ContentPlaceHolder> residuel — devait etre transforme par MasterContentPlaceHolderTransformer en @RenderBody / @RenderSection."),

        // --- Controles serveur courants (Phase 3 a venir) ---
        new("asp:Label", new(@"<asp:Label\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            VerifySeverity.Pending,
            "<asp:Label> → en Razor, utiliser `<span>` ou afficher la valeur directement avec @Html.DisplayFor / @Model.X. Phase 3."),
        new("asp:TextBox", new(@"<asp:TextBox\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            VerifySeverity.Pending,
            "<asp:TextBox> → `<input type=\"text\">` ou `@Html.TextBoxFor(m => m.X)`. Phase 3."),
        new("asp:Button", new(@"<asp:Button\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            VerifySeverity.Pending,
            "<asp:Button> → `<button>` ou `<input type=\"submit\">`. Le handler `OnClick` devient une action Razor (`OnPostX`). Phase 3."),
        new("asp:LinkButton", new(@"<asp:LinkButton\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            VerifySeverity.Pending,
            "<asp:LinkButton> → `<a>` avec un form/submit. Le handler devient une action Razor. Phase 3."),
        new("asp:HyperLink", new(@"<asp:HyperLink\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            VerifySeverity.Pending,
            "<asp:HyperLink> → `<a href=\"...\">` ou `@Html.ActionLink(...)`. Phase 3."),
        new("asp:Image", new(@"<asp:Image\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            VerifySeverity.Pending,
            "<asp:Image> → `<img>` direct. Phase 3."),
        new("asp:ImageButton", new(@"<asp:ImageButton\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            VerifySeverity.Pending,
            "<asp:ImageButton> → `<input type=\"image\">` ou `<button type=\"submit\">`. Phase 3."),
        new("asp:Panel", new(@"<asp:Panel\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            VerifySeverity.Pending,
            "<asp:Panel> → `<div>` simple. Phase 3."),
        new("asp:Literal", new(@"<asp:Literal\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            VerifySeverity.Pending,
            "<asp:Literal> → `@Html.Raw(...)` ou simple `@Model.X`. Phase 3."),
        new("asp:PlaceHolder", new(@"<asp:PlaceHolder\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            VerifySeverity.Pending,
            "<asp:PlaceHolder> → souvent supprimable, ou `<div>`. Phase 3."),

        // --- Listes / saisies ---
        new("asp:DropDownList", new(@"<asp:DropDownList\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            VerifySeverity.Pending,
            "<asp:DropDownList> → `<select>` + `<option>` ou `@Html.DropDownListFor(...)`. Phase 3."),
        new("asp:CheckBox", new(@"<asp:CheckBox\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            VerifySeverity.Pending,
            "<asp:CheckBox> → `<input type=\"checkbox\">` ou `@Html.CheckBoxFor(...)`. Phase 3."),
        new("asp:RadioButton", new(@"<asp:RadioButton\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            VerifySeverity.Pending,
            "<asp:RadioButton> → `<input type=\"radio\">`. Phase 3."),
        new("asp:CheckBoxList", new(@"<asp:CheckBoxList\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            VerifySeverity.Pending,
            "<asp:CheckBoxList> → `@foreach` qui emet plusieurs checkboxes. Phase 3."),
        new("asp:RadioButtonList", new(@"<asp:RadioButtonList\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            VerifySeverity.Pending,
            "<asp:RadioButtonList> → `@foreach` qui emet plusieurs radios. Phase 3."),
        new("asp:ListBox", new(@"<asp:ListBox\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            VerifySeverity.Pending,
            "<asp:ListBox> → `<select multiple>`. Phase 3."),

        // --- Iteration / tabulaire ---
        new("asp:Repeater", new(@"<asp:Repeater\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            VerifySeverity.Pending,
            "<asp:Repeater> → `@foreach (var item in Model.X) { ... }`. Le ItemTemplate devient le corps du foreach. Phase 3."),
        new("asp:GridView", new(@"<asp:GridView\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            VerifySeverity.Pending,
            "<asp:GridView> → table HTML classique avec `@foreach`. Pas d'equivalent automatique pour le sorting/paging — utiliser un helper ou un js datatable. Phase 3."),
        new("asp:DataList", new(@"<asp:DataList\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            VerifySeverity.Pending,
            "<asp:DataList> → `@foreach` qui emet le markup voulu. Phase 3."),
        new("asp:ListView", new(@"<asp:ListView\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            VerifySeverity.Pending,
            "<asp:ListView> → `@foreach` + sections de layout custom. Phase 3."),
        new("asp:FormView", new(@"<asp:FormView\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            VerifySeverity.Pending,
            "<asp:FormView> → vue Razor classique avec `@Html.DisplayFor` / `@Html.EditorFor`. Phase 3."),
        new("asp:DetailsView", new(@"<asp:DetailsView\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            VerifySeverity.Pending,
            "<asp:DetailsView> → idem FormView : `@Html.DisplayFor`. Phase 3."),

        // --- AJAX / partial postback ---
        new("asp:UpdatePanel", new(@"<asp:UpdatePanel\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            VerifySeverity.Pending,
            "<asp:UpdatePanel> → AJAX coupe-papier (jQuery, fetch, htmx) : pas d'equivalent natif Razor. Manuel. Phase 3."),
        new("asp:Timer", new(@"<asp:Timer\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            VerifySeverity.Pending,
            "<asp:Timer> → setInterval JS qui appelle un endpoint Razor. Manuel. Phase 3."),
        new("asp:ScriptManager", new(@"<asp:ScriptManager\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            VerifySeverity.Pending,
            "<asp:ScriptManager> → souvent supprimable (les UpdatePanel partent). Phase 3."),

        // --- Validation ---
        new("asp:RequiredFieldValidator", new(@"<asp:RequiredFieldValidator\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            VerifySeverity.Pending,
            "<asp:RequiredFieldValidator> → `[Required]` sur le modele + `@Html.ValidationMessageFor(...)`. Phase 3."),
        new("asp:RegularExpressionValidator", new(@"<asp:RegularExpressionValidator\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            VerifySeverity.Pending,
            "<asp:RegularExpressionValidator> → `[RegularExpression]` sur le modele + ValidationMessageFor. Phase 3."),
        new("asp:ValidationSummary", new(@"<asp:ValidationSummary\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            VerifySeverity.Pending,
            "<asp:ValidationSummary> → `@Html.ValidationSummary()`. Phase 3."),
        new("asp:CompareValidator", new(@"<asp:CompareValidator\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            VerifySeverity.Pending,
            "<asp:CompareValidator> → `[Compare]` sur le modele. Phase 3."),
        new("asp:RangeValidator", new(@"<asp:RangeValidator\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            VerifySeverity.Pending,
            "<asp:RangeValidator> → `[Range]` sur le modele. Phase 3."),
        new("asp:CustomValidator", new(@"<asp:CustomValidator\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            VerifySeverity.Pending,
            "<asp:CustomValidator> → validation custom dans le PageModel + `@Html.ValidationMessageFor(...)`. Phase 3."),

        // --- Navigation ---
        new("asp:Menu", new(@"<asp:Menu\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            VerifySeverity.Pending,
            "<asp:Menu> → markup HTML/CSS standard ou helper Razor custom. Phase 3."),
        new("asp:TreeView", new(@"<asp:TreeView\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            VerifySeverity.Pending,
            "<asp:TreeView> → composant front (jstree, etc.) ou markup recursif. Manuel. Phase 3."),
        new("asp:SiteMapPath", new(@"<asp:SiteMapPath\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            VerifySeverity.Pending,
            "<asp:SiteMapPath> → markup breadcrumb manuel ou helper. Phase 3."),

        // --- Formulaires multi-etapes ---
        new("asp:Wizard", new(@"<asp:Wizard\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            VerifySeverity.Pending,
            "<asp:Wizard> → series d'etapes en Razor + state cote serveur. Refactor complet. Manuel. Phase 3."),
        new("asp:MultiView", new(@"<asp:MultiView\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            VerifySeverity.Pending,
            "<asp:MultiView> → conditionnel `@if` + sections HTML. Phase 3."),

        // --- Sécurité / membership (vieux) ---
        new("asp:LoginView", new(@"<asp:LoginView\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            VerifySeverity.Pending,
            "<asp:LoginView> → `@if (User.Identity.IsAuthenticated) { ... } else { ... }`. Phase 3."),
        new("asp:Login", new(@"<asp:Login\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            VerifySeverity.Pending,
            "<asp:Login> → form Razor manuel + AccountController/PageModel. Phase 3."),
        new("asp:LoginStatus", new(@"<asp:LoginStatus\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            VerifySeverity.Pending,
            "<asp:LoginStatus> → `@Html.ActionLink(\"Sign in\", ...)` conditionnel. Phase 3."),

        // --- Generique : tout autre <asp:Xxx> non capture ci-dessus ---
        new("asp:OtherControl", new(@"<asp:[A-Z]\w*\b", RegexOptions.Compiled),
            VerifySeverity.Pending,
            "Controle <asp:...> non specifique — voir documentation MS pour son equivalent Razor. Phase 3."),

        // --- Data-binding (Phase 4 a venir) ---
        new("Eval(", new(@"\bEval\s*\(\s*[""']", RegexOptions.Compiled),
            VerifySeverity.Pending,
            "Eval(\"X\") → `@Model.X` ou `@item.X` selon le contexte du foreach. Phase 4."),
        new("Bind(", new(@"\bBind\s*\(\s*[""']", RegexOptions.Compiled),
            VerifySeverity.Pending,
            "Bind(\"X\") → en Razor on utilise un input bind avec `@Html.EditorFor(m => m.X)`. Phase 4."),
        new("Container.DataItem", new(@"\bContainer\s*\.\s*DataItem\b", RegexOptions.Compiled),
            VerifySeverity.Pending,
            "Container.DataItem → `@item` ou un cast explicite vers le type d'element. Phase 4."),
        new("DataBinder.Eval", new(@"\bDataBinder\s*\.\s*Eval\s*\(", RegexOptions.Compiled),
            VerifySeverity.Pending,
            "DataBinder.Eval(...) → `@Model.X` direct. Phase 4."),

        // --- Attributs runat="server" sur tags HTML (souvent <form> et <head>) ---
        new("runat-server-html", new(@"<(form|head|body|div)\b[^>]*\brunat\s*=\s*[""']server[""']", RegexOptions.IgnoreCase | RegexOptions.Compiled),
            VerifySeverity.Manual,
            "runat=\"server\" sur un tag HTML — retirer simplement (Razor n'en a pas besoin)."),
    };

    // Razor comments preserve souvent du code ASPX d'origine en commentaire
    // TODO (cf. PageDirectiveTransformer.HandleOutputCache). On ne veut pas
    // que le verifier les signale — c'est volontaire.
    private static readonly Regex RazorComment = new(@"@\*[\s\S]*?\*@", RegexOptions.Compiled);

    /// <summary>
    /// Verifie un fichier .cshtml (apres migration). Renvoie la liste des
    /// residus ASPX detectes.
    /// </summary>
    public static IReadOnlyList<VerifyIssue> Verify(string content, string fileRelative)
    {
        // Pre-processing : masque les zones qui contiennent volontairement
        // du code ASPX d'origine (commentaires Razor avec TODO) pour eviter
        // les faux positifs.
        var masked = MaskRazorComments(content);

        // Pour eviter les double-detections (un `<asp:Label>` matchera aussi
        // la regle generique `<asp:OtherControl>`), on suit les spans deja
        // capturees par une regle plus specifique.
        var captured = new List<(int Start, int End)>();
        var issues = new List<VerifyIssue>();

        foreach (var rule in Rules)
        {
            foreach (Match m in rule.Regex.Matches(masked))
            {
                if (Overlaps(captured, m.Index, m.Index + m.Length)) continue;
                captured.Add((m.Index, m.Index + m.Length));

                // Snippet et line a partir du content ORIGINAL pour avoir
                // un libelle propre dans le rapport.
                var snippet = ExtractSnippet(content, m.Index, m.Length);
                var line = LineOf(content, m.Index);
                issues.Add(new VerifyIssue(
                    rule.Severity, fileRelative, line, rule.Pattern, snippet, rule.Suggestion));
            }
        }

        return issues.OrderBy(i => i.Line).ThenBy(i => i.Pattern).ToList();
    }

    /// <summary>
    /// Remplace les `@* ... *@` par des espaces de meme longueur pour que
    /// les indices restent identiques mais que les regex de detection ne
    /// matchent rien dedans.
    /// </summary>
    private static string MaskRazorComments(string content)
    {
        return RazorComment.Replace(content, m =>
        {
            // Garde les \n / \r pour ne pas decaler les numeros de ligne.
            var span = m.Value;
            var chars = new char[span.Length];
            for (int i = 0; i < span.Length; i++)
                chars[i] = (span[i] == '\n' || span[i] == '\r') ? span[i] : ' ';
            return new string(chars);
        });
    }

    /// <summary>
    /// Verifie tous les .cshtml d'un dossier (recursif). Retourne (issues totales).
    /// </summary>
    public static IReadOnlyList<VerifyIssue> VerifyDirectory(string root)
    {
        var all = new List<VerifyIssue>();
        foreach (var file in Directory.EnumerateFiles(root, "*.cshtml", SearchOption.AllDirectories))
        {
            string content;
            try { content = File.ReadAllText(file); }
            catch { continue; }
            var rel = Path.GetRelativePath(root, file);
            all.AddRange(Verify(content, rel));
        }
        return all;
    }

    /// <summary>Genere un rapport markdown des issues, groupe par fichier.</summary>
    public static string Markdown(IReadOnlyList<VerifyIssue> issues)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("# aspx-lint migrate-verify — rapport");
        sb.AppendLine();
        var bug     = issues.Count(i => i.Severity == VerifySeverity.Bug);
        var pending = issues.Count(i => i.Severity == VerifySeverity.Pending);
        var manual  = issues.Count(i => i.Severity == VerifySeverity.Manual);
        sb.AppendLine($"- **{bug}** residus syntaxiques (bugs des transformers actuels — a fixer)");
        sb.AppendLine($"- **{pending}** controles serveur / data-binding (Phase 3-4 a venir)");
        sb.AppendLine($"- **{manual}** items manuels");
        sb.AppendLine();

        if (bug > 0)
        {
            sb.AppendLine("> ⚠ Des residus syntaxiques signalent des bugs dans le pipeline. ");
            sb.AppendLine("> Verifie les patterns marques `Bug` ci-dessous.");
            sb.AppendLine();
        }

        // Top patterns par frequence — utile pour prioriser quelle Phase 3 faire d'abord.
        var byPattern = issues.GroupBy(i => i.Pattern)
                              .Select(g => (Pattern: g.Key, Count: g.Count(), Severity: g.First().Severity))
                              .OrderByDescending(x => x.Count)
                              .Take(15)
                              .ToList();
        if (byPattern.Count > 0)
        {
            sb.AppendLine("## Top patterns residuels");
            sb.AppendLine();
            sb.AppendLine("| Pattern | Niveau | Occurrences |");
            sb.AppendLine("|:--------|:-------|------------:|");
            foreach (var p in byPattern)
            {
                var sev = p.Severity switch
                {
                    VerifySeverity.Bug     => "🔴 bug",
                    VerifySeverity.Pending => "⏳ pending",
                    VerifySeverity.Manual  => "✋ manual",
                    _ => p.Severity.ToString()
                };
                sb.AppendLine($"| `{p.Pattern}` | {sev} | {p.Count} |");
            }
            sb.AppendLine();
        }

        // Detail par fichier (limite aux 50 premiers fichiers pour ne pas
        // exploser la taille du rapport sur de gros projets).
        var byFile = issues.GroupBy(i => i.File).OrderBy(g => g.Key).Take(50);
        foreach (var group in byFile)
        {
            sb.AppendLine($"## `{group.Key}`");
            sb.AppendLine();
            sb.AppendLine("| Ligne | Niveau | Pattern | Snippet | Suggestion |");
            sb.AppendLine("|------:|:-------|:--------|:--------|:-----------|");
            foreach (var i in group)
            {
                var sev = i.Severity switch
                {
                    VerifySeverity.Bug     => "🔴 bug",
                    VerifySeverity.Pending => "⏳",
                    VerifySeverity.Manual  => "✋",
                    _ => i.Severity.ToString()
                };
                sb.AppendLine($"| {i.Line} | {sev} | `{i.Pattern}` | `{EscapeMd(i.Snippet)}` | {EscapeMd(i.Suggestion)} |");
            }
            sb.AppendLine();
        }

        if (issues.Count == 0)
        {
            sb.AppendLine("_Aucun residu ASPX detecte. La migration semble complete._");
        }
        return sb.ToString();
    }

    // ============= helpers =============

    private static bool Overlaps(IList<(int Start, int End)> ranges, int start, int end)
    {
        foreach (var (s, e) in ranges)
            if (start < e && end > s) return true;
        return false;
    }

    private static int LineOf(string content, int index)
    {
        int line = 1;
        for (int i = 0; i < index && i < content.Length; i++)
            if (content[i] == '\n') line++;
        return line;
    }

    private static string ExtractSnippet(string content, int index, int length)
    {
        // Borne autour : on prend 40 chars max, en s'arretant a la fin de
        // ligne pour ne pas spammer le rapport.
        var maxLen = Math.Min(length, 60);
        var slice = content.Substring(index, Math.Min(maxLen, content.Length - index));
        var nl = slice.IndexOf('\n');
        if (nl > 0) slice = slice.Substring(0, nl);
        return slice.Trim();
    }

    private static string EscapeMd(string s)
        => s.Replace("|", "\\|").Replace("\n", " ").Replace("\r", "");
}
