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

    /// <summary>
    /// Racine canonique a laquelle scan / save / restore sont confines.
    /// Null = pas de scoping (mode developpement).
    /// </summary>
    public string? AllowedRoot { get; init; }

    /// <summary>
    /// Si true, les endpoints d'ecriture (/api/save, /api/restore) renvoient 403.
    /// </summary>
    public bool ReadOnly { get; init; }

    /// <summary>
    /// Verifie qu'un chemin (apres normalisation Path.GetFullPath) est sous
    /// AllowedRoot. Si AllowedRoot est null, retourne toujours true.
    /// </summary>
    public bool IsUnderAllowedRoot(string fullPath)
    {
        if (AllowedRoot is null) return true;
        var rootFull = Path.GetFullPath(AllowedRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var pathFull = Path.GetFullPath(fullPath);
        return pathFull.StartsWith(rootFull + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || pathFull.Equals(rootFull, StringComparison.OrdinalIgnoreCase);
    }

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

    /// <summary>
    /// Subscribers SSE actuellement connectes a /api/events. Chaque element
    /// est un canal qui sert a envoyer des events texte au client.
    /// </summary>
    private readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, System.Threading.Channels.Channel<string>> _eventSubscribers = new();

    public Guid SubscribeToEvents(System.Threading.Channels.Channel<string> channel)
    {
        var id = Guid.NewGuid();
        _eventSubscribers[id] = channel;
        return id;
    }

    public void UnsubscribeFromEvents(Guid id)
    {
        if (_eventSubscribers.TryRemove(id, out var ch))
        {
            try { ch.Writer.TryComplete(); } catch { }
        }
    }

    /// <summary>
    /// Diffuse un event a tous les abonnes SSE. Le format est celui du wire
    /// SSE : "data: {json}\n\n". Chaque subscriber recoit une copie via son
    /// channel ; les channels saturent ne bloquent pas (drop-newest).
    /// </summary>
    public void BroadcastEvent(string kind, object? payload = null)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(new { kind, payload, ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() });
        var formatted = $"data: {json}\n\n";
        foreach (var (id, ch) in _eventSubscribers)
        {
            try
            {
                if (!ch.Writer.TryWrite(formatted))
                {
                    // Channel sature : on drop. Si ca persiste, l'abonne sera
                    // ferme par un timeout cote endpoint.
                }
            }
            catch
            {
                _eventSubscribers.TryRemove(id, out _);
            }
        }
    }
}
