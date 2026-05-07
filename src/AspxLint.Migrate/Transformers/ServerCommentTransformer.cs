using System.Text.RegularExpressions;

namespace AspxLint.Migrate.Transformers;

/// <summary>
/// `&lt;%-- comment --%&gt;` → `@* comment *@`
///
/// Note : tourne EN PREMIER dans le pipeline, avant les autres regex sur
/// `&lt;% %&gt;`. Sinon les autres transformers verraient les commentaires
/// serveur comme des blocs de code.
/// </summary>
public sealed class ServerCommentTransformer : ITransformer
{
    public string Name => "ServerComment";

    // Accepte la forme officielle `<%-- ... --%>` ET la forme manuscrite
    // `<% -- ... -- %>` (avec espaces) qu'on rencontre dans du code legacy.
    // ASP.NET tolere `<% -- ... -- %>` (souvent traite comme C# qui no-op),
    // mais en Razor le `--` serait interprete comme l'operateur de
    // decrement, ce qui casse la compilation. On les traite tous les deux
    // comme des commentaires.
    private static readonly Regex ServerComment =
        new(@"<%[ \t]*--([\s\S]*?)--[ \t]*%>", RegexOptions.Compiled);

    public string Transform(string content, MigrationContext ctx)
    {
        int count = 0;
        var result = ServerComment.Replace(content, m =>
        {
            count++;
            var inner = m.Groups[1].Value;
            // `*@` dans le commentaire casserait le commentaire Razor,
            // mais c'est extremement rare en pratique. On signale plutot
            // que d'echapper bizarrement.
            if (inner.Contains("*@"))
            {
                ctx.Log(MigrationSeverity.Warning,
                    LineOf(content, m.Index),
                    Name,
                    "Le commentaire serveur contient `*@` qui est un delimiteur Razor. Verifier la sortie.");
            }
            return "@*" + inner + "*@";
        });
        if (count > 0)
            ctx.Log(MigrationSeverity.Auto, null, Name,
                $"{count} commentaire(s) serveur transforme(s) `<%-- ... --%>` → `@* ... *@`.");
        return result;
    }

    internal static int LineOf(string content, int index)
    {
        int line = 1;
        for (int i = 0; i < index && i < content.Length; i++)
            if (content[i] == '\n') line++;
        return line;
    }
}
