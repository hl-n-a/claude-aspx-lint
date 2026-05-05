namespace AspxLint.Server.Tests;

public class RestoreApiTests : IClassFixture<ApiFixture>
{
    private readonly ApiFixture _fx;
    public RestoreApiTests(ApiFixture fx) => _fx = fx;

    [Fact]
    public async Task Restore_path_not_scanned_returns_403()
    {
        var client = _fx.CreateAuthClient();
        var path = Path.Combine(Path.GetTempPath(), "never-scanned-" + Guid.NewGuid().ToString("N") + ".aspx");
        var r = await client.PostAsJsonAsync("/api/restore", new { path });
        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
    }

    [Fact]
    public async Task Restore_without_bak_returns_404()
    {
        using var tmp = ApiFixture.TempDir();
        var path = tmp.WriteFile("p.aspx", "x\n");
        var client = _fx.CreateAuthClient();
        await client.PostAsJsonAsync("/api/scan", new { path = tmp.Path });

        // Aucun save n'a eu lieu => pas de .bak
        var r = await client.PostAsJsonAsync("/api/restore", new { path });
        Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
    }

    [Fact]
    public async Task Restore_after_save_returns_original_content()
    {
        using var tmp = ApiFixture.TempDir();
        var path = tmp.WriteFile("p.aspx", "ORIGINAL\n");
        var client = _fx.CreateAuthClient();
        await client.PostAsJsonAsync("/api/scan", new { path = tmp.Path });

        // Save => cree .bak avec ORIGINAL
        await client.PostAsJsonAsync("/api/save", new { path, content = "MODIFIED\n" });
        Assert.Equal("MODIFIED\n", File.ReadAllText(path));

        // Restore
        var r = await client.PostAsJsonAsync("/api/restore", new { path });
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);

        var body = await r.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("ORIGINAL\n", body.GetProperty("content").GetString());
        Assert.Equal("ORIGINAL\n", File.ReadAllText(path)); // disque restaure aussi
    }

    [Fact]
    public async Task Restore_preserves_BOM_in_returned_content()
    {
        using var tmp = ApiFixture.TempDir();
        var bomBytes = new byte[] { 0xEF, 0xBB, 0xBF }
            .Concat(Encoding.UTF8.GetBytes("OG\n"))
            .ToArray();
        var path = tmp.WriteBytes("p.aspx", bomBytes);

        var client = _fx.CreateAuthClient();
        await client.PostAsJsonAsync("/api/scan", new { path = tmp.Path });
        await client.PostAsJsonAsync("/api/save", new { path, content = "no bom\n" });

        var r = await client.PostAsJsonAsync("/api/restore", new { path });
        var body = await r.Content.ReadFromJsonAsync<JsonElement>();
        var restored = body.GetProperty("content").GetString()!;
        Assert.Equal('﻿', restored[0]);
        Assert.StartsWith("﻿OG", restored);
    }

    [Fact]
    public async Task Restore_returns_byte_count_of_bak()
    {
        using var tmp = ApiFixture.TempDir();
        var path = tmp.WriteFile("p.aspx", "12345\n"); // 6 octets
        var client = _fx.CreateAuthClient();
        await client.PostAsJsonAsync("/api/scan", new { path = tmp.Path });
        await client.PostAsJsonAsync("/api/save", new { path, content = "much longer content here\n" });

        var r = await client.PostAsJsonAsync("/api/restore", new { path });
        var body = await r.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(6, body.GetProperty("bytes").GetInt32());
    }
}
