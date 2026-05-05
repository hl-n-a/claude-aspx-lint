namespace AspxLint.Server.Tests;

public class SaveApiTests : IClassFixture<ApiFixture>
{
    private readonly ApiFixture _fx;
    public SaveApiTests(ApiFixture fx) => _fx = fx;

    [Fact]
    public async Task Save_path_not_scanned_returns_403()
    {
        var client = _fx.CreateAuthClient();
        var path = Path.Combine(Path.GetTempPath(), "never-scanned-" + Guid.NewGuid().ToString("N") + ".aspx");
        var r = await client.PostAsJsonAsync("/api/save", new { path, content = "hijacked" });
        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
    }

    [Fact]
    public async Task Save_after_scan_succeeds_and_creates_bak()
    {
        using var tmp = ApiFixture.TempDir();
        var path = tmp.WriteFile("p.aspx", "original\n");

        var client = _fx.CreateAuthClient();
        // 1) scan pour ajouter le path a l'allowlist
        await client.PostAsJsonAsync("/api/scan", new { path = tmp.Path });

        // 2) save
        var r = await client.PostAsJsonAsync("/api/save", new { path, content = "modified\n" });
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);

        var body = await r.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetProperty("ok").GetBoolean());
        Assert.True(body.GetProperty("backedUp").GetBoolean());

        // Disque coherent
        Assert.Equal("modified\n", File.ReadAllText(path));
        Assert.Equal("original\n", File.ReadAllText(path + ".bak"));
    }

    [Fact]
    public async Task Save_twice_creates_bak_only_once()
    {
        using var tmp = ApiFixture.TempDir();
        var path = tmp.WriteFile("p.aspx", "v0\n");
        var client = _fx.CreateAuthClient();
        await client.PostAsJsonAsync("/api/scan", new { path = tmp.Path });

        var r1 = await client.PostAsJsonAsync("/api/save", new { path, content = "v1\n" });
        var r2 = await client.PostAsJsonAsync("/api/save", new { path, content = "v2\n" });

        var b1 = await r1.Content.ReadFromJsonAsync<JsonElement>();
        var b2 = await r2.Content.ReadFromJsonAsync<JsonElement>();

        Assert.True(b1.GetProperty("backedUp").GetBoolean());   // 1er save : .bak cree
        Assert.False(b2.GetProperty("backedUp").GetBoolean());  // 2nd save : .bak deja la

        Assert.Equal("v2\n", File.ReadAllText(path));         // current
        Assert.Equal("v0\n", File.ReadAllText(path + ".bak")); // .bak = ORIGINAL, pas v1
    }

    [Fact]
    public async Task Save_preserves_BOM_when_content_starts_with_BOM_char()
    {
        using var tmp = ApiFixture.TempDir();
        var path = tmp.WriteFile("p.aspx", "x\n");
        var client = _fx.CreateAuthClient();
        await client.PostAsJsonAsync("/api/scan", new { path = tmp.Path });

        // Contenu commencant par le caractere BOM ﻿ — UTF-8 doit l'encoder en EF BB BF.
        var content = "﻿<%@ Page %>\n";
        var r = await client.PostAsJsonAsync("/api/save", new { path, content });
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);

        var bytes = File.ReadAllBytes(path);
        Assert.Equal(0xEF, bytes[0]);
        Assert.Equal(0xBB, bytes[1]);
        Assert.Equal(0xBF, bytes[2]);
    }

    [Fact]
    public async Task Save_does_not_emit_BOM_when_content_starts_clean()
    {
        using var tmp = ApiFixture.TempDir();
        var path = tmp.WriteFile("p.aspx", "x\n");
        var client = _fx.CreateAuthClient();
        await client.PostAsJsonAsync("/api/scan", new { path = tmp.Path });

        await client.PostAsJsonAsync("/api/save", new { path, content = "<%@ Page %>\n" });

        var bytes = File.ReadAllBytes(path);
        Assert.NotEqual(0xEF, bytes[0]); // pas de BOM auto-injecte par Encoding.UTF8
    }

    [Fact]
    public async Task Save_returns_byte_count_in_response()
    {
        using var tmp = ApiFixture.TempDir();
        var path = tmp.WriteFile("p.aspx", "x\n");
        var client = _fx.CreateAuthClient();
        await client.PostAsJsonAsync("/api/scan", new { path = tmp.Path });

        var content = "Hello world\n"; // 12 octets en ASCII / UTF-8
        var r = await client.PostAsJsonAsync("/api/save", new { path, content });
        var body = await r.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(12, body.GetProperty("bytes").GetInt32());
    }

    [Fact]
    public async Task Save_path_traversal_attempt_still_blocked_by_allowlist()
    {
        // Meme avec des ../, le full-path resolu doit pas etre dans l'allowlist.
        using var tmp = ApiFixture.TempDir();
        tmp.WriteFile("ok.aspx", "x\n");
        var client = _fx.CreateAuthClient();
        await client.PostAsJsonAsync("/api/scan", new { path = tmp.Path });

        var traversal = Path.Combine(tmp.Path, "..", "..", "etc", "passwd");
        var r = await client.PostAsJsonAsync("/api/save", new { path = traversal, content = "pwned" });
        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
    }
}
