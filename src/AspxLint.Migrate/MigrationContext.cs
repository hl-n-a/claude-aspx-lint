namespace AspxLint.Migrate;

/// <summary>
/// Contexte partage entre tous les transformers d'une meme migration.
/// Porte le path relatif du fichier source (pour le report) et le rapport
/// dans lequel les transformers ajoutent leurs actions.
/// </summary>
public sealed class MigrationContext
{
    public MigrationContext(string sourceFile, string ext, MigrationReport report)
    {
        SourceFile = sourceFile;
        Ext = ext.TrimStart('.').ToLowerInvariant();
        Report = report;
    }

    /// <summary>Path relatif du fichier .aspx/.ascx/.master source. Sert
    /// dans le rapport pour referencer les actions par fichier.</summary>
    public string SourceFile { get; }

    /// <summary>"aspx" / "ascx" / "master" / "asax". Utilise par les
    /// transformers qui se comportent differemment selon le type de
    /// fichier (ex : @page n'a pas de sens dans un .ascx).</summary>
    public string Ext { get; }

    public MigrationReport Report { get; }

    public void Log(MigrationSeverity sev, int? line, string transformer, string message)
        => Report.Add(sev, SourceFile, line, transformer, message);
}
