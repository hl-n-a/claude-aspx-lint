namespace AspxLint.Core;

/// <summary>
/// Traductions des noms et descriptions de regles pour les locales autres que
/// la langue source (francais). Pour ajouter une langue, dupliquer le bloc et
/// adapter chaque entree. Les hints (messages par-issue) restent en francais
/// car ils sont generes dynamiquement par les regles.
/// </summary>
public static class Translations
{
    public const string DefaultLocale = "fr";

    private static readonly Dictionary<string, Dictionary<string, (string Name, string Description)>>
        _byLocale = new(StringComparer.OrdinalIgnoreCase)
    {
        ["en"] = new()
        {
            ["DIR-001"]    = ("Missing or misplaced @Page/@Control/@Master directive",
                              "ASP.NET Web Forms files must start with their declarative directive (@Page for .aspx, @Control for .ascx, @Master for .master). Without it, the runtime can't determine the type to compile."),
            ["TAG-001"]    = ("Self-closing tag not XHTML-conform",
                              "Empty tags like <br>, <hr>, <img>, <input>, <meta>, <link> must be written as self-closing (e.g. <br />) to comply with XHTML."),
            ["TAG-002"]    = ("Inconsistent tag casing",
                              "HTML tags should be lowercase to match HTML5/XHTML. Mixed case (<DIV>, <Div>) hurts readability and breaks some parsers."),
            ["TAG-003"]    = ("Unbalanced tags",
                              "Open tags must be closed (except void elements). Auto-fix inserts the missing </tag> for tags never closed by end-of-file or before a mismatched closing."),
            ["ATTR-001"]   = ("Unquoted attribute value",
                              "Attribute values must always be in double quotes for XHTML conformance. Special characters and spaces break unquoted values."),
            ["ATTR-002"]   = ("Mixed single and double quotes in attributes",
                              "For consistency, prefer double quotes (\") for HTML/ASP.NET attributes. Single quotes are valid but unusual."),
            ["ATTR-003"]   = ("Duplicate attribute on a tag",
                              "A tag must not contain the same attribute twice. Browsers usually keep only the first one, causing subtle bugs. Auto-fix merges `class` values; for other attributes it keeps the first occurrence."),
            ["ASP-001"]    = ("Server control without runat=\"server\"",
                              "Any <asp:...> control must have runat=\"server\", otherwise it's treated as literal HTML and has no server behavior."),
            ["ASP-002"]    = ("Duplicate server control ID",
                              "Two server controls with the same ID in the same naming container produce a runtime error at compile."),
            ["ASP-003"]    = ("ContentPlaceHolder without ID (master page)",
                              "Each <asp:ContentPlaceHolder> in a master must have an ID for child pages to target it via ContentPlaceHolderID."),
            ["ASP-004"]    = ("<asp:Content> missing ContentPlaceHolderID",
                              "Each <asp:Content> in a child page must reference a ContentPlaceHolderID matching one defined in its master."),
            ["ASP-005"]    = ("Missing spaces in <%= ... %>",
                              "ASP server tags read better with one space after <% (or <%=, <%#, <%:, <%@) and before %>. Doesn't change the runtime behavior, just consistency."),
            ["WS-001"]     = ("Trailing whitespace",
                              "Whitespace at end of line is invisible noise that shows up in diffs and breaks some editors' end-of-line cursor."),
            ["WS-002"]     = ("Mixed tab + space indentation",
                              "An indentation that mixes tabs and spaces renders inconsistently across editors. Standardize on one or the other."),
            ["WS-003"]     = ("Multiple consecutive blank lines (>2)",
                              "More than 2 consecutive blank lines hurt readability without adding visual separation."),
            ["WS-004"]     = ("Missing final newline",
                              "POSIX recommends a text file ends with a newline. Without it, some Unix tools display poorly and Git complains."),
            ["WS-005"]     = ("UTF-8 BOM at start of file",
                              "A UTF-8 BOM (EF BB BF) is unnecessary on Unix and Windows in 2024+. Some tools choke on it."),
            ["WS-006"]     = ("Trailing blank lines at end of file",
                              "A file should end with exactly one newline (POSIX), without surplus blank lines. WS-006 targets the tail; WS-003 targets >2 in the middle; WS-004 ensures the final newline is there."),
            ["CHAR-001"]   = ("& not escaped (skips URL params)",
                              "The & character should be encoded as &amp; unless it starts an entity (&lt;, &amp;, &#123;...) or a URL parameter (?a=1&b=2). HTML5 tolerates raw & in URLs; XHTML strict doesn't."),
            ["COM-001"]    = ("-- inside an HTML comment",
                              "<!-- ... -- ... --> is invalid HTML : `--` is the comment delimiter. Replace with another character or split the comment."),
            ["SEC-001"]    = ("EnableViewStateMac=\"false\" — major security flaw",
                              "ViewState MAC validation is what prevents an attacker from forging the session state. Disabling it opens .NET serialization deserialization vulnerabilities."),
            ["SEC-002"]    = ("<a target=\"_blank\"> without rel=\"noopener\"",
                              "A <a target=\"_blank\"> link lets the opened page access window.opener and manipulate the source page (tabnabbing). Auto-fix appends rel=\"noopener noreferrer\"."),
            ["SEC-003"]    = ("Hardcoded URL to a local environment",
                              "Detects hardcoded URLs to localhost / 127.0.0.1 / *.local / *.test or with explicit ports. These break in production."),
            ["A11Y-001"]   = ("<img> without alt attribute",
                              "Every <img> tag must have an alt attribute, even empty (alt=\"\") for decorative images. Required by WCAG/WAI-ARIA and screen readers."),
            ["STYLE-001"]  = ("Inline style=\"...\" attribute",
                              "Inline styles mix presentation and structure, bypass the project's CSS, and complicate theming/maintenance."),
            ["SCRIPT-001"] = ("Inline JS event handler",
                              "Inline event handlers (onclick=\"...\", onload=\"...\", etc.) mix JS and HTML, complicate CSP analysis, and don't support multiple handlers on the same element."),
            ["DOC-001"]    = ("Missing DOCTYPE (standalone ASPX only)",
                              "An ASPX page that's not a child of a master must declare a DOCTYPE so the browser doesn't fall back to quirks mode."),
            ["FORM-001"]   = ("<form> without runat=\"server\" in an ASPX",
                              "An ASPX page using server controls needs an enclosing <form runat=\"server\">. Without it, ViewState and postbacks don't work."),
            ["SM-001"]     = ("Multiple <asp:ScriptManager>",
                              "Only one <asp:ScriptManager> can live per page. ScriptManagerProxy handles content pages."),
            ["CFG-001"]    = ("compilation debug=\"true\" in Web.config",
                              "Debug mode disables code optimization, loads PDBs, extends timeouts, and exposes stack traces. Should be false in production (use Web.Debug.config for local debugging)."),
            ["CFG-002"]    = ("customErrors mode=\"Off\" leaks stack traces",
                              "With customErrors mode=\"Off\", unhandled exceptions show full stack traces, disk paths, and sometimes connection strings to the visitor. Use \"RemoteOnly\" (default) or \"On\"."),
            ["CFG-003"]    = ("trace enabled=\"true\" in Web.config",
                              "trace enabled=\"true\" exposes Trace.axd with session/cookie/server-variable data. Disable in production unless explicitly protected by localOnly + network restrictions."),
            ["CFG-004"]    = ("httpCookies missing httpOnlyCookies/requireSSL",
                              "Protect session cookies: add httpOnlyCookies=\"true\" (blocks JS access, mitigates XSS) and requireSSL=\"true\" (forces Secure flag, mitigates MITM). Don't enable requireSSL if the site isn't served over HTTPS."),
            ["CFG-005"]    = ("sessionState mode=\"InProc\" doesn't scale",
                              "InProc stores sessions in IIS process memory: lost on app pool recycle, incompatible with multi-instance. For real multi-instance use StateServer or SQLServer (Redis via custom provider)."),
            ["CFG-006"]    = ("Plaintext password in connectionString",
                              "A connectionString containing \"password=...\" exposes credentials to anyone with repo or filesystem access. Use Integrated Security, a secret manager, or encrypt the section with aspnet_regiis -pe."),
        }
    };

    /// <summary>
    /// Renvoie (Name, Description) pour la regle dans la locale demandee, ou
    /// les valeurs par defaut de la regle si la locale ou la regle est inconnue.
    /// </summary>
    public static (string Name, string Description) Resolve(IRule rule, string? locale)
    {
        if (string.IsNullOrEmpty(locale) || locale.Equals(DefaultLocale, StringComparison.OrdinalIgnoreCase))
            return (rule.Name, rule.Description);

        if (_byLocale.TryGetValue(locale, out var dict)
            && dict.TryGetValue(rule.Id, out var t))
        {
            return t;
        }
        return (rule.Name, rule.Description);
    }

    public static IEnumerable<string> AvailableLocales =>
        new[] { DefaultLocale }.Concat(_byLocale.Keys);
}
