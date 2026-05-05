namespace AspxLint.Core.Tests;

/// <summary>
/// Dossier temporaire jetable, utilise par les tests qui doivent ecrire des
/// fichiers reels (Scanner, BOM round-trip, etc.).
/// </summary>
internal sealed class TempDir : IDisposable
{
    public string Path { get; }

    public TempDir()
    {
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "aspxlint-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string WriteFile(string relativeName, string content)
    {
        var full = System.IO.Path.Combine(Path, relativeName);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
        return full;
    }

    public string WriteBytes(string relativeName, byte[] bytes)
    {
        var full = System.IO.Path.Combine(Path, relativeName);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
        File.WriteAllBytes(full, bytes);
        return full;
    }

    public void Dispose()
    {
        try { Directory.Delete(Path, recursive: true); }
        catch { /* best effort */ }
    }
}
