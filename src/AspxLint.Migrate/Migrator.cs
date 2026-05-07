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
/// Phase 1 : transformations purement syntaxiques.
///   - Server comments    `<%-- --%>`     → `@* *@`
///   - Page directives    `<%@ Page %>`   → `@page` / `@model` / `@using`
///   - Server expressions `<%= %>`,`<%: %>`,`<%# %>` → `@(...)`
///   - Server statements  `<% %>`         → `@{ }` ou `@if/@foreach`
///
/// Phase 2 (cette extension) : master pages → layouts.
///   - `<%@ Master %>` retire (le fichier devient un layout)
///   - `<asp:ContentPlaceHolder ID="X">` → `@RenderBody()` (primary)
///                                       ou `@RenderSection("X", required: false)`
///   - `<%@ Page MasterPageFile="..." %>` → ajoute `Layout = "_Site"`
///   - `<asp:Content ContentPlaceHolderID="X">` → contenu inline (primary)
///                                              ou `@section X { ... }`
///
/// Phase 3 (a venir) : controles serveur courants → HTML5.
/// Phase 4 : data binding (`Eval(...)`) → `@Model.X`.
/// Phase 5 : code-behind (Roslyn).
/// </summary>
public static class Migrator
{
    /// <summary>Pipeline ordonne. L'ordre compte :
    ///   1. Comments — sinon les expressions / directives matchent dedans
    ///   2. Page directives — emit @page / @model / Layout = "..."
    ///   3. Master ContentPlaceHolder → @RenderBody / @RenderSection
    ///      (uniquement sur les .master)
    ///   4. Child page Content → inline ou @section
    ///      (sur les .aspx qui ont `<asp:Content>`)
    ///   5. Server expressions (= / : / #) avant les statements
    ///   6. Server statements (qui excluent les prefixes d'expression)
    /// </summary>
    public static IReadOnlyList<ITransformer> DefaultPipeline { get; } = new ITransformer[]
    {
        new ServerCommentTransformer(),
        new PageDirectiveTransformer(),
        new MasterContentPlaceHolderTransformer(),
        new ChildPageContentTransformer(),
        new ServerExpressionTransformer(),
        new ServerStatementTransformer(),
        // En dernier : on a deja transforme les <asp:Content> et
        // <asp:ContentPlaceHolder>, donc le strip runat="server" ne
        // touche que les tags HTML (form, head, body, div, ...).
        new RunatServerTransformer(),
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
