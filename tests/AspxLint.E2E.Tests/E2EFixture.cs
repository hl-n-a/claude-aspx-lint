namespace AspxLint.E2E.Tests;

/// <summary>
/// Fixture partagee par tous les tests E2E :
///   - Demarre AspxLint.Server in-process sur un port OS-assigne (port=0).
///   - Resoud le port reel en interrogeant IServerAddressesFeature.
///   - Lance Chromium headless via Playwright.
///   - Auto-installe les browsers si manquants (one-shot, ~150 Mo la 1re fois).
///
/// Une instance est partagee par toute la classe de test (IClassFixture).
/// </summary>
public sealed class E2EFixture : IAsyncLifetime
{
    public StartedServer Server { get; private set; } = null!;
    public IBrowser Browser { get; private set; } = null!;
    public string BaseUrl { get; private set; } = null!;
    public string AuthUrl => $"{BaseUrl}/?token={Server.Token}";

    private IPlaywright _playwright = null!;

    public async Task InitializeAsync()
    {
        // 1) Browsers Playwright (no-op si deja en cache local)
        var exitCode = Microsoft.Playwright.Program.Main(new[] { "install", "chromium" });
        if (exitCode != 0)
            throw new InvalidOperationException(
                $"Playwright install a echoue (exit {exitCode}). Lance manuellement : " +
                "pwsh tests/AspxLint.E2E.Tests/bin/Debug/net9.0/playwright.ps1 install chromium");

        // 2) Serveur in-process, port OS-assigne
        Server = ServerHost.Start(new ServerStartOptions(0));
        BaseUrl = ResolveActualUrl(Server.App);

        // 3) Browser headless
        _playwright = await Microsoft.Playwright.Playwright.CreateAsync();
        Browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true
        });
    }

    public async Task DisposeAsync()
    {
        try { await Browser.CloseAsync(); } catch { }
        _playwright?.Dispose();
        try { await Server.App.StopAsync(); } catch { }
    }

    public async Task<IPage> NewPageAsync()
    {
        var ctx = await Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = 1280, Height = 800 },
            IgnoreHTTPSErrors = true
        });
        return await ctx.NewPageAsync();
    }

    private static string ResolveActualUrl(WebApplication app)
    {
        var addresses = app.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()?.Addresses;
        var addr = addresses?.FirstOrDefault()
            ?? throw new InvalidOperationException("Kestrel n'a pas annonce d'adresse.");

        // Kestrel renvoie "http://[::]:NNNN" ou "http://0.0.0.0:NNNN" — on remplace par localhost.
        var normalized = addr.Replace("[::]", "localhost").Replace("0.0.0.0", "localhost");
        var uri = new Uri(normalized);
        return $"http://localhost:{uri.Port}";
    }
}

internal sealed class TempDir : IDisposable
{
    public string Path { get; }

    public TempDir()
    {
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "aspxlint-e2e-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string WriteFile(string relativeName, string content)
    {
        var full = System.IO.Path.Combine(Path, relativeName);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
        return full;
    }

    public void Dispose()
    {
        try { Directory.Delete(Path, recursive: true); } catch { }
    }
}
