using System.Text.RegularExpressions;

namespace AspxLint.Migrate.Transformers;

/// <summary>
/// Strip `runat="server"` (ou `runat='server'`) sur les tags HTML qui
/// l'avaient en ASPX (typiquement `&lt;form runat="server"&gt;`,
/// `&lt;head runat="server"&gt;`). Razor n'en a pas besoin — au contraire,
/// le laisser produit du HTML invalide vu par le navigateur.
///
/// Ne touche PAS les controles `&lt;asp:...&gt;` (qui ont aussi `runat="server"`
/// mais relevent de la Phase 3 — strip prematuremment changerait leur
/// comportement avant qu'on les transforme proprement).
/// </summary>
public sealed class RunatServerTransformer : ITransformer
{
    public string Name => "RunatServer";

    // Detecte un tag HTML (lettre minuscule en debut de tag, pas asp:) avec
    // `runat="server"` ou `runat='server'`. On capture le tout pour pouvoir
    // emettre la version sans l'attribut.
    //
    //   <head id="Head1" runat="server">
    //   <form runat="server" id="form1">
    //   <body runat='server'>
    //
    // On respecte le sous-pattern apres l'attribut : si runat est suivi
    // d'autres attributs, ils sont conserves.
    // Negative lookahead `(?!asp:)` apres le `<` : on exclut explicitement
    // les tags `<asp:...>` qui auront leur propre transformer en Phase 3.
    private static readonly Regex Pattern = new(
        @"(<(?!asp:)[a-z][\w-]*\b[^>]*?)\s+runat\s*=\s*[""']server[""']([^>]*?>)",
        RegexOptions.Compiled);

    public string Transform(string content, MigrationContext ctx)
    {
        int count = 0;
        var result = Pattern.Replace(content, m =>
        {
            count++;
            // Reconstitue le tag sans l'attribut runat.
            return m.Groups[1].Value + m.Groups[2].Value;
        });
        if (count > 0)
        {
            ctx.Log(MigrationSeverity.Auto, null, Name,
                $"{count} attribut(s) `runat=\"server\"` retire(s) de tags HTML — non requis en Razor.");
        }
        return result;
    }
}
