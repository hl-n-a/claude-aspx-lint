namespace AspxLint.Server.Tests;

public class ScanApiTests : IClassFixture<ApiFixture>
{
    private readonly ApiFixture _fx;
    public ScanApiTests(ApiFixture fx) => _fx = fx;

    [Fact]
    public async Task Scan_missing_dir_returns_404()
    {
        var client = _fx.CreateAuthClient();
        var r = await client.PostAsJsonAsync("/api/scan", new { path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")) });
        Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
    }

    [Fact]
    public async Task Scan_empty_dir_returns_zero_files()
    {
        using var tmp = ApiFixture.TempDir();
        var client = _fx.CreateAuthClient();
        var r = await client.PostAsJsonAsync("/api/scan", new { path = tmp.Path });

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var body = await r.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0, body.GetProperty("fileCount").GetInt32());
        Assert.Equal(0, body.GetProperty("issueCount").GetInt32());
    }

    [Fact]
    public async Task Scan_returns_content_and_issues()
    {
        using var tmp = ApiFixture.TempDir();
        // Fichier crade : trailing whitespace + pas de \n final
        tmp.WriteFile("page.aspx", "<%@ Page %>\n<html>   \n<body>\n<br>\n</body>\n</html>");

        var client = _fx.CreateAuthClient();
        var r = await client.PostAsJsonAsync("/api/scan", new { path = tmp.Path });

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var body = await r.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(1, body.GetProperty("fileCount").GetInt32());
        Assert.True(body.GetProperty("issueCount").GetInt32() > 0);

        var file = body.GetProperty("files")[0];
        Assert.Equal("page.aspx", file.GetProperty("relativePath").GetString());
        Assert.Contains("<%@ Page %>", file.GetProperty("content").GetString()!);

        // Au moins WS-001 (trailing) ou WS-004 (pas de \n) doit firer.
        var ruleIds = file.GetProperty("issues").EnumerateArray()
            .Select(i => i.GetProperty("ruleId").GetString()).ToHashSet();
        Assert.Contains("WS-001", ruleIds);
        Assert.Contains("WS-004", ruleIds);
        Assert.Contains("TAG-001", ruleIds); // <br>
    }

    [Fact]
    public async Task Scan_filters_to_aspnet_extensions()
    {
        using var tmp = ApiFixture.TempDir();
        tmp.WriteFile("a.aspx", "<%@ Page %>\n");
        tmp.WriteFile("b.ascx", "<%@ Control %>\n");
        tmp.WriteFile("c.master", "<%@ Master %>\n");
        tmp.WriteFile("ignored.txt", "garbage");
        tmp.WriteFile("ignored.cs", "namespace X;");

        var client = _fx.CreateAuthClient();
        var body = await (await client.PostAsJsonAsync("/api/scan", new { path = tmp.Path }))
            .Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(3, body.GetProperty("fileCount").GetInt32());
    }

    [Fact]
    public async Task Scan_recurses_into_subdirectories()
    {
        using var tmp = ApiFixture.TempDir();
        tmp.WriteFile("root.aspx", "<%@ Page %>\n");
        tmp.WriteFile("Sub/inner.aspx", "<%@ Page %>\n");
        tmp.WriteFile("Sub/Deep/leaf.ascx", "<%@ Control %>\n");

        var client = _fx.CreateAuthClient();
        var body = await (await client.PostAsJsonAsync("/api/scan", new { path = tmp.Path }))
            .Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(3, body.GetProperty("fileCount").GetInt32());
    }

    [Fact]
    public async Task Scan_response_includes_buildId_and_scannedAt()
    {
        using var tmp = ApiFixture.TempDir();
        var client = _fx.CreateAuthClient();
        var body = await (await client.PostAsJsonAsync("/api/scan", new { path = tmp.Path }))
            .Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(_fx.BuildId, body.GetProperty("buildId").GetString());
        Assert.True(body.TryGetProperty("scannedAt", out _));
    }

    [Fact]
    public async Task Scan_preserves_BOM_in_returned_content()
    {
        using var tmp = ApiFixture.TempDir();
        var bytes = new byte[] { 0xEF, 0xBB, 0xBF }
            .Concat(Encoding.UTF8.GetBytes("<%@ Page %>\n"))
            .ToArray();
        tmp.WriteBytes("with-bom.aspx", bytes);

        var client = _fx.CreateAuthClient();
        var body = await (await client.PostAsJsonAsync("/api/scan", new { path = tmp.Path }))
            .Content.ReadFromJsonAsync<JsonElement>();

        var content = body.GetProperty("files")[0].GetProperty("content").GetString()!;
        Assert.Equal('﻿', content[0]);
    }
}
