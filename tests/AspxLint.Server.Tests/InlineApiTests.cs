namespace AspxLint.Server.Tests;

/// <summary>
/// Tests des endpoints "inline" (path-less) ajoutes pour permettre aux frontends
/// (dashboard Web, extension Chrome, VsExt, etc.) d'analyser ou corriger un
/// contenu sans avoir besoin de toucher au disque.
/// </summary>
public class InlineApiTests : IClassFixture<ApiFixture>
{
    private readonly ApiFixture _fx;
    public InlineApiTests(ApiFixture fx) => _fx = fx;

    // ========== /api/analyze ==========

    [Fact]
    public async Task Analyze_dirty_content_returns_issues()
    {
        var client = _fx.CreateAuthClient();
        // Pas de \n final => WS-004 fire ; trailing spaces => WS-001 ; <br> => TAG-001
        var r = await client.PostAsJsonAsync("/api/analyze",
            new { content = "<%@ Page %>\n<br>\nline   ", ext = "aspx" });

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var body = await r.Content.ReadFromJsonAsync<JsonElement>();
        var issues = body.GetProperty("issues").EnumerateArray()
            .Select(i => i.GetProperty("ruleId").GetString())
            .ToHashSet();

        Assert.Contains("WS-001", issues);
        Assert.Contains("TAG-001", issues);
        Assert.Contains("WS-004", issues);
    }

    [Fact]
    public async Task Analyze_clean_content_returns_empty_issues()
    {
        var client = _fx.CreateAuthClient();
        var content =
            "<%@ Page Language=\"C#\" %>\n" +
            "<!DOCTYPE html>\n" +
            "<html>\n<head><title>x</title></head>\n" +
            "<body><form runat=\"server\"></form></body>\n" +
            "</html>\n";

        var r = await client.PostAsJsonAsync("/api/analyze", new { content, ext = "aspx" });
        var body = await r.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0, body.GetProperty("issues").GetArrayLength());
    }

    [Fact]
    public async Task Analyze_unauthorized_returns_401()
    {
        var client = _fx.CreateClient();
        var r = await client.PostAsJsonAsync("/api/analyze", new { content = "x", ext = "aspx" });
        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
    }

    // ========== /api/fix ==========

    [Fact]
    public async Task Fix_applies_one_rule_and_reports_count()
    {
        var client = _fx.CreateAuthClient();
        // Contient WS-001 (trailing) ET TAG-001 (<br>)
        var r = await client.PostAsJsonAsync("/api/fix",
            new { content = "<br>\nline   \n", ext = "aspx", ruleId = "WS-001" });

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var body = await r.Content.ReadFromJsonAsync<JsonElement>();
        var fixedContent = body.GetProperty("content").GetString()!;
        Assert.True(body.GetProperty("applied").GetInt32() > 0);

        Assert.DoesNotContain("line   \n", fixedContent); // WS-001 fixed
        Assert.Contains("<br>", fixedContent);             // TAG-001 NOT fixed
        Assert.DoesNotContain("<br />", fixedContent);
    }

    [Fact]
    public async Task Fix_unknown_rule_returns_404()
    {
        var client = _fx.CreateAuthClient();
        var r = await client.PostAsJsonAsync("/api/fix",
            new { content = "x", ext = "aspx", ruleId = "DOES-NOT-EXIST" });
        Assert.Equal(HttpStatusCode.NotFound, r.StatusCode);
    }

    [Fact]
    public async Task Fix_non_fixable_rule_returns_400()
    {
        var client = _fx.CreateAuthClient();
        // CHAR-001 (& non echappe) reste non-fixable : trop risque d'auto-encoder.
        var r = await client.PostAsJsonAsync("/api/fix",
            new { content = "Tom & Jerry", ext = "aspx", ruleId = "CHAR-001" });
        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
    }

    [Fact]
    public async Task Fix_clean_content_returns_zero_applied()
    {
        var client = _fx.CreateAuthClient();
        var r = await client.PostAsJsonAsync("/api/fix",
            new { content = "ok\n", ext = "aspx", ruleId = "WS-001" });
        var body = await r.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0, body.GetProperty("applied").GetInt32());
    }

    // ========== /api/fix-all ==========

    [Fact]
    public async Task FixAll_applies_all_fixable_rules()
    {
        var client = _fx.CreateAuthClient();
        var r = await client.PostAsJsonAsync("/api/fix-all",
            new { content = "<br>\nline   \n", ext = "aspx" });

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var body = await r.Content.ReadFromJsonAsync<JsonElement>();
        var fixedContent = body.GetProperty("content").GetString()!;
        var history = body.GetProperty("history").EnumerateArray()
            .Select(h => h.GetProperty("ruleId").GetString())
            .ToHashSet();

        Assert.Contains("WS-001", history);
        Assert.Contains("TAG-001", history);
        Assert.Contains("<br />", fixedContent);
        Assert.DoesNotContain("line   \n", fixedContent);
    }

    [Fact]
    public async Task FixAll_idempotent_after_first_pass()
    {
        var client = _fx.CreateAuthClient();
        var r1 = await client.PostAsJsonAsync("/api/fix-all",
            new { content = "<br>\n", ext = "aspx" });
        var fixed1 = (await r1.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("content").GetString()!;

        var r2 = await client.PostAsJsonAsync("/api/fix-all",
            new { content = fixed1, ext = "aspx" });
        var body2 = await r2.Content.ReadFromJsonAsync<JsonElement>();
        var fixed2 = body2.GetProperty("content").GetString()!;

        Assert.Equal(fixed1, fixed2);
        Assert.Equal(0, body2.GetProperty("history").GetArrayLength());
    }

    [Fact]
    public async Task FixAll_clean_content_returns_empty_history()
    {
        var client = _fx.CreateAuthClient();
        // Contenu deja propre : @Page + DOCTYPE + form runat = aucune issue auto-fixable
        var content =
            "<%@ Page Language=\"C#\" %>\n" +
            "<!DOCTYPE html>\n" +
            "<html>\n<head><title>x</title></head>\n" +
            "<body><form runat=\"server\"></form></body>\n" +
            "</html>\n";
        var r = await client.PostAsJsonAsync("/api/fix-all", new { content, ext = "aspx" });
        var body = await r.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0, body.GetProperty("history").GetArrayLength());
    }

    // ========== Authorization header (CORS / extensions) ==========

    [Fact]
    public async Task Bearer_token_in_Authorization_header_authenticates()
    {
        var client = _fx.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _fx.Token);

        var r = await client.PostAsJsonAsync("/api/analyze",
            new { content = "ok\n", ext = "aspx" });
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
    }

    [Fact]
    public async Task Wrong_bearer_token_returns_401()
    {
        var client = _fx.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "00000000000000000000000000000000");

        var r = await client.PostAsJsonAsync("/api/analyze",
            new { content = "x", ext = "aspx" });
        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
    }
}
