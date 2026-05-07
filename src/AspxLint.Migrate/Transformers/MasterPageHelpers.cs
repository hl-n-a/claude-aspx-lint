namespace AspxLint.Migrate.Transformers;

/// <summary>
/// Helpers partages entre <see cref="MasterContentPlaceHolderTransformer"/>
/// et <see cref="ChildPageContentTransformer"/>.
/// </summary>
internal static class MasterPageHelpers
{
    /// <summary>
    /// IDs typiques pour la zone "principale" d'un master. Si on en trouve
    /// un dans la liste des placeholders, on le designe primary
    /// (`@RenderBody()` cote master, contenu inline cote child). Sinon on
    /// prend le premier par ordre du document.
    /// </summary>
    private static readonly string[] PrimaryNames =
        { "MainContent", "Body", "Content", "MainBody", "MainPlaceHolder", "Main" };

    /// <summary>
    /// Choisit l'ID "primary" parmi <paramref name="ids"/>. Renvoie l'ID
    /// trouve dans <see cref="PrimaryNames"/> en priorite (case-insensitive),
    /// sinon le premier ID, sinon null si la liste est vide.
    /// </summary>
    public static string? PickPrimary(IReadOnlyList<string> ids)
    {
        if (ids.Count == 0) return null;
        foreach (var name in PrimaryNames)
        {
            var match = ids.FirstOrDefault(id => id.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (match != null) return match;
        }
        return ids[0];
    }

    /// <summary>
    /// Convertit un path de master `~/Site.Master`, `~/MasterPages/Public.Master`,
    /// ou `Site.master` en nom de Layout Razor `_Site`, `_Public`, etc.
    /// (matches notre convention <see cref="Migrator.SuggestOutputName"/>
    /// qui prefixe les masters d'un underscore).
    /// </summary>
    public static string MasterPathToLayoutName(string masterPageFile)
    {
        // Normalise les separateurs et retire le ~/ initial.
        var path = masterPageFile.Replace('\\', '/').TrimStart('~').TrimStart('/');
        var basename = System.IO.Path.GetFileNameWithoutExtension(path);
        if (string.IsNullOrEmpty(basename)) basename = "Layout";
        return "_" + basename;
    }
}
