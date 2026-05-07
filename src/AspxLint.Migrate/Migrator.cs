using AspxLint.Migrate.Transformers;

namespace AspxLint.Migrate;

/// <summary>
/// Resultat d'une migration : le contenu transforme, le path de sortie
/// suggere (`.cshtml`), et les actions enregistrees pour ce fichier.
/// </summary>
public sealed record MigrationResult(
    string Content,
    string SuggestedOutputName,
    IReadOnlyList<MigrationAction> Actions
);

/// <summary>
/// Orchestrateur du pipeline de migration ASPX → Razor.
///
/// Phase 1 (cette implementation) : transformations purement syntaxiques.
///   1. Server comments    `<%-- --%>`     → `@* *@`
///   2. Page directives    `<%@ Page %>`   → `@page` / `@model` / `@using`
///   3. Server expressions `<%= %>`,`<%: %>`,`<%# %>` → `@(...)`
///   4. Server statements  `<% %>`         → `@{ }`
///
/// Phase 2 (a venir) : master pages → layouts.
/// Phase 3 : controles serveur courants → HTML5.
/// Phase 4 : data binding (`Eval(...)`) → `@Model.X`.
/// Phase 5 : code-behind (Roslyn).
/// </summary>
public static class Migrator
{
    /// <summary>Pipeline ordonne. L'ordre compte :
    ///   - Comments en premier (sinon les expressions matchent dedans)
    ///   - Directives ensuite (on ne veut pas que `&lt;%@ Page %&gt;` matche le
    ///     pattern d'expression non-greedy)
    ///   - Expressions avant statements (les expressions ont des prefixes
    ///     specifiques `=`, `:`, `#` que les statements excluent).
    /// </summary>
    public static IReadOnlyList<ITransformer> DefaultPipeline { get; } = new ITransformer[]
    {
        new ServerCommentTransformer(),
        new PageDirectiveTransformer(),
        new ServerExpressionTransformer(),
        new ServerStatementTransformer(),
    };

    /// <summary>
    /// Migre un seul fichier. <paramref name="sourceRelativePath"/> sert
    /// uniquement pour le rapport (libelle des actions). Le contenu
    /// retourne est le .cshtml complet.
    /// </summary>
    public static MigrationResult Migrate(
        string content,
        string sourceRelativePath,
        MigrationReport? report = null,
        IReadOnlyList<ITransformer>? pipeline = null)
    {
        report ??= new MigrationReport();
        pipeline ??= DefaultPipeline;

        var ext = Path.GetExtension(sourceRelativePath);
        var ctx = new MigrationContext(sourceRelativePath, ext, report);

        var startCount = report.Actions.Count;
        var current = content;
        foreach (var transformer in pipeline)
        {
            current = transformer.Transform(current, ctx);
        }

        var fileActions = report.Actions.Skip(startCount).ToList();
        var outputName = SuggestOutputName(sourceRelativePath);
        return new MigrationResult(current, outputName, fileActions);
    }

    /// <summary>
    /// Suggere un nom de fichier de sortie en fonction du type d'entree.
    ///   `Foo.aspx`     → `Foo.cshtml`
    ///   `Foo.ascx`     → `_Foo.cshtml` (Razor convention pour les partials)
    ///   `Site.master`  → `_Site.cshtml` (Razor layout)
    ///   `Foo.asax`     → `Foo.cshtml`   (rare, Phase 5 le gerera mieux)
    ///
    /// Le caller decide ou ecrire — cette fonction donne juste un nom
    /// relatif suggere.
    /// </summary>
    public static string SuggestOutputName(string sourceRelativePath)
    {
        var dir = Path.GetDirectoryName(sourceRelativePath) ?? "";
        var nameNoExt = Path.GetFileNameWithoutExtension(sourceRelativePath);
        var ext = Path.GetExtension(sourceRelativePath).TrimStart('.').ToLowerInvariant();

        var prefix = ext switch
        {
            "ascx"   => "_",
            "master" => "_",
            _        => ""
        };
        var newName = prefix + nameNoExt + ".cshtml";
        return string.IsNullOrEmpty(dir) ? newName : Path.Combine(dir, newName);
    }
}
