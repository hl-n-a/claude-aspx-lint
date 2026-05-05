namespace AspxLint.Server;

/// <summary>
/// Etat partage du serveur pour la session courante : identite (build + token),
/// chemin du dashboard, fichier de log, set des paths inscriptibles via /api/save.
/// Resolu par injection de dependances dans les handlers de route.
/// </summary>
public sealed class ServerSession
{
    public required string BuildId { get; init; }
    public required string Token { get; init; }
    public required string DashboardPath { get; init; }
    public required string LogFile { get; init; }

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
