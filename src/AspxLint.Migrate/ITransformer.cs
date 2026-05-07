namespace AspxLint.Migrate;

/// <summary>
/// Un transformer applique une transformation textuelle sur le contenu
/// d'un fichier ASPX et reporte ses actions dans le contexte. Les
/// transformers tournent en pipeline dans l'ordre defini par
/// <see cref="Migrator"/>.
///
/// Convention : un transformer ne doit pas planter sur des entrees
/// arbitraires (commentaires, strings imbriques, contenu vide). Si
/// quelque chose le surprend, il enregistre une action <c>Manual</c>
/// avec <c>@* TODO[aspx-migrate] *@</c> en sortie et continue.
/// </summary>
public interface ITransformer
{
    /// <summary>Nom court pour le rapport (ex : "PageDirective").</summary>
    string Name { get; }

    /// <summary>Renvoie le contenu transforme. Modifie <c>ctx.Report</c>
    /// pour enregistrer ce qui a ete fait.</summary>
    string Transform(string content, MigrationContext ctx);
}
