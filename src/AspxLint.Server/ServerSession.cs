namespace AspxLint.Server;

/// <summary>
/// Etat partage du serveur pour la session courante : identite (build + token),
/// fichier de log, set des paths inscriptibles via /api/save, et delegate
/// pour charger la dashboard HTML (depuis disque en dev, depuis ressource
/// embarquee sinon).
/// Resolu par injection de dependances dans les handlers de route.
/// </summary>
public sealed class ServerSession
{
    public required string BuildId { get; init; }
    public required string Token { get; init; }
    public required string LogFile { get; init; }

    /// <summary>
    /// Description de la source de la dashboard (pour les logs). Format :
    /// "disk:&lt;chemin&gt;" en dev avec hot-reload, "embedded:&lt;ressource&gt;"
    /// en .exe self-contained ou conteneur.
    /// </summary>
    public required string DashboardSource { get; init; }

    /// <summary>
    /// Charge le HTML de la dashboard (rappelle a chaque requete /, donc
    /// supporte le hot-reload en dev sans cache).
    /// </summary>
    public required Func<Task<string>> LoadDashboardHtml { get; init; }

    private readonly object _logLock = new();
    private readonly object _writableLock = new();
    private readonly HashSet<string> _writablePaths = new(StringComparer.OrdinalIgnoreCase);

    public void AddWritable(string fullPath)
    {
        lock (_writableLock) _writablePaths.Add(fullPath);
    }

    public bool IsWritable(string fullPath)
    {
        lock (_writableLock) return _writablePaths.Contains(fullPath);
    }

    public void Log(string level, string msg)
    {
        var line = $"{DateTime.UtcNow:O} {BuildId} {level,-5} {msg}";
        lock (_logLock) File.AppendAllText(LogFile, line + Environment.NewLine);
    }
}
