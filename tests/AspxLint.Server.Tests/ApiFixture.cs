namespace AspxLint.Server.Tests;

/// <summary>
/// WebApplicationFactory autour de l'entry point AspxLint.Server.
/// Une instance est partagee par toutes les methodes d'une classe de test
/// (IClassFixture). Le token est genere une fois au demarrage du host et
/// reste constant pour toute la duree de la fixture.
///
/// Les tests utilisent CreateAuthClient() pour avoir le cookie d'auth pre-pose,
/// ou CreateClient() (raw) pour tester les comportements d'auth.
/// </summary>
public sealed class ApiFixture : WebApplicationFactory<Program>
{
    public ServerSession Session => Services.GetRequiredService<ServerSession>();
    public string Token => Session.Token;
    public string BuildId => Session.BuildId;

    public HttpClient CreateAuthClient()
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add("Cookie", $"aspx_lint_token={Token}");
        return client;
    }

    /// <summary>
    /// Pour tester les fichiers reels (scan / save / restore), on cree un dossier
    /// temporaire jetable. Les paths y sont uniques (GUID) donc les allowlists
    /// des differents tests ne se collisionnent jamais entre elles meme si la
    /// ServerSession est partagee.
    /// </summary>
    public static TempDir TempDir() => new();
}

public sealed class TempDir : IDisposable
{
    public string Path { get; }

    public TempDir()
    {
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "aspxlint-srv-test-" + Guid.NewGuid().ToString("N"));
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
