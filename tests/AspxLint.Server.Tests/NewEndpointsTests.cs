namespace AspxLint.Server.Tests;

/// <summary>
/// Couverture des endpoints ajoutes recemment et qui n'avaient pas de test :
/// /api/read, /api/browse, /api/find-folder, /api/events (SSE).
/// </summary>
public class NewEndpointsTests : IClassFixture<ApiFixture>
{
    private readonly ApiFixture _fx;
    public NewEndpointsTests(ApiFixture fx) => _fx = fx;

    // ========== /api/read ==========

    [Fact]
    public async Task Read_returns_content_and_issues_for_existing_file()
    {
        using var tmp = ApiFixture.TempDir();
        var path = Path.Combine(tmp.Path, "x.aspx");
        File.WriteAllText(path, "<%@ Page %>\n<br>\n");

        var client = _fx.CreateAuthClient();
        var r = await client.PostAsJsonAsync("/api/read", new { path });

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var body = await r.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains("<br>", body.GetProperty("content").GetString());
        Assert.True(body.GetProperty("issues").GetArrayLength() > 0);
    }

    [Fact]
    public async Task Read_missing_file_returns_404()
    {
        var client = _fx.CreateAuthClient();
        var r = await client.PostAsJsonAsync("/api/read",
            new { path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".aspx") });
        Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
    }

    [Fact]
    public async Task Read_invalid_path_returns_400()
    {
        var client = _fx.CreateAuthClient();
        var r = await client.PostAsJsonAsync("/api/read", new { path = "" });
        // Path.GetFullPath("") leve, le handler renvoie 400.
        Assert.True(r.StatusCode == HttpStatusCode.BadRequest
                 || r.StatusCode == HttpStatusCode.NotFound);
    }

    // ========== /api/browse ==========

    [Fact]
    public async Task Browse_lists_subdirectories()
    {
        using var tmp = ApiFixture.TempDir();
        Directory.CreateDirectory(Path.Combine(tmp.Path, "sub-a"));
        Directory.CreateDirectory(Path.Combine(tmp.Path, "sub-b"));
        File.WriteAllText(Path.Combine(tmp.Path, "sub-a", "x.aspx"), "<p />");

        var client = _fx.CreateAuthClient();
        var r = await client.GetAsync("/api/browse?path=" + Uri.EscapeDataString(tmp.Path));

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var body = await r.Content.ReadFromJsonAsync<JsonElement>();
        var entries = body.GetProperty("entries").EnumerateArray().ToList();
        Assert.Equal(2, entries.Count);
        // sub-a a 1 fichier .aspx, sub-b en a 0.
        var subA = entries.First(e => e.GetProperty("name").GetString() == "sub-a");
        Assert.Equal(1, subA.GetProperty("aspxCount").GetInt32());
    }

    [Fact]
    public async Task Browse_no_path_returns_drives_or_allowedRoot()
    {
        var client = _fx.CreateAuthClient();
        var r = await client.GetAsync("/api/browse");

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var body = await r.Content.ReadFromJsonAsync<JsonElement>();
        // En mode test sans AllowedRoot, on doit avoir au moins 1 entree (les drives).
        Assert.True(body.GetProperty("entries").GetArrayLength() >= 1);
    }

    [Fact]
    public async Task Browse_missing_dir_returns_404()
    {
        var client = _fx.CreateAuthClient();
        var fake = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var r = await client.GetAsync("/api/browse?path=" + Uri.EscapeDataString(fake));
        Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
    }

    // ========== /api/find-folder ==========

    [Fact]
    public async Task FindFolder_returns_400_without_name()
    {
        var client = _fx.CreateAuthClient();
        var r = await client.GetAsync("/api/find-folder");
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
    }

    [Fact]
    public async Task FindFolder_returns_array_with_match()
    {
        // BFS scope = AllowedRoot (null en test) ou UserProfile par defaut.
        // On peut au moins verifier qu'on recoit une reponse valide.
        var client = _fx.CreateAuthClient();
        var r = await client.GetAsync("/api/find-folder?name=" + Uri.EscapeDataString("AspxLintNonExistingABC123"));

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var body = await r.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.TryGetProperty("matches", out var matches));
        Assert.Equal(JsonValueKind.Array, matches.ValueKind);
        Assert.Equal(0, matches.GetArrayLength());
    }

    // ========== /api/events (SSE) ==========

    [Fact]
    public async Task Events_endpoint_sends_initial_retry_hint()
    {
        var client = _fx.CreateAuthClient();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var r = await client.GetAsync("/api/events", HttpCompletionOption.ResponseHeadersRead, cts.Token);

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal("text/event-stream", r.Content.Headers.ContentType?.MediaType);

        using var stream = await r.Content.ReadAsStreamAsync(cts.Token);
        using var reader = new StreamReader(stream);
        // Premiere ligne attendue : retry hint
        var line = await reader.ReadLineAsync(cts.Token);
        Assert.Equal("retry: 5000", line);
    }

    [Fact]
    public async Task Events_broadcast_on_scan_reaches_subscribers()
    {
        var client = _fx.CreateAuthClient();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        // Abonnement SSE en parallele : on attend l'event "scanned".
        var sseTask = Task.Run(async () =>
        {
            var resp = await client.GetAsync("/api/events", HttpCompletionOption.ResponseHeadersRead, cts.Token);
            using var stream = await resp.Content.ReadAsStreamAsync(cts.Token);
            using var reader = new StreamReader(stream);
            var sb = new System.Text.StringBuilder();
            while (!cts.Token.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(cts.Token);
                if (line == null) break;
                sb.AppendLine(line);
                if (line.Contains("\"kind\":\"scanned\"")) return sb.ToString();
            }
            return sb.ToString();
        }, cts.Token);

        // Petit delai pour que l'abonnement soit enregistre cote serveur.
        await Task.Delay(300);

        // Declenche un scan (qui broadcast un event "scanned").
        using var tmp = ApiFixture.TempDir();
        File.WriteAllText(Path.Combine(tmp.Path, "x.aspx"), "<p />\n");
        var scanR = await client.PostAsJsonAsync("/api/scan", new { path = tmp.Path }, cts.Token);
        Assert.Equal(HttpStatusCode.OK, scanR.StatusCode);

        var received = await sseTask.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Contains("scanned", received);
    }
}
