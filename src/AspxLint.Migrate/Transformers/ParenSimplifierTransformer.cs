using System.Text.RegularExpressions;

namespace AspxLint.Migrate.Transformers;

/// <summary>
/// Transformer "polish" : simplifie `@(simple_ident)` en `@simple_ident`
/// quand c'est safe.
///
/// `<%= Foo %>` est transforme en `@(Foo)` par le ServerExpressionTransformer
/// (les parens sont obligatoires pour les expressions complexes).
/// Beaucoup de cas reels sont en fait juste un identifiant simple, pour
/// lesquels Razor accepte la forme `@Foo` (plus lisible).
///
/// Regles de simplification :
///   - L'expression est un identifiant simple ou dotted :
///       `Foo`, `Model.X`, `item.Sub.Prop`
///   - Pas d'operateurs, methodes (), indexeurs [], strings.
///   - Le caractere apres `)` ne doit pas etre word/dot (sinon Razor
///     etendrait l'identifier dans le HTML qui suit).
///
/// Tourne en DERNIER dans le pipeline — apres tous les autres transformers
/// qui peuvent generer des `@(...)`.
/// </summary>
public sealed class ParenSimplifierTransformer : ITransformer
{
    public string Name => "ParenSimplifier";

    // Identifiant dotted : Foo, Foo.Bar, Foo.Bar.Baz, etc. Pas d'operateurs.
    private const string DottedIdent =
        @"[A-Za-z_][A-Za-z0-9_]*(?:\.[A-Za-z_][A-Za-z0-9_]*)*";

    // Pattern complet : `@(IDENT)` suivi d'un char qui n'est NI un mot NI
    // un point. Le negative lookahead `(?![A-Za-z0-9_.])` empeche les cas
    // dangereux comme `@(item)1` -> `@item1` (faux identifier).
    // Inclus le cas fin de chaine `\z` aussi.
    private static readonly Regex SimpleParenExpr = new(
        @"@\((" + DottedIdent + @")\)(?![A-Za-z0-9_.])",
        RegexOptions.Compiled);

    public string Transform(string content, MigrationContext ctx)
    {
        int count = 0;
        var result = SimpleParenExpr.Replace(content, m =>
        {
            count++;
            return "@" + m.Groups[1].Value;
        });
        if (count > 0)
        {
            ctx.Log(MigrationSeverity.Auto, null, Name,
                $"{count} `@(ident)` simplifie(s) en `@ident` (parens inutiles, plus lisible).");
        }
        return result;
    }
}
