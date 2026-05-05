namespace AspxLint.Server.Tests;

public class AuthTests : IClassFixture<ApiFixture>
{
    private readonly ApiFixture _fx;
    public AuthTests(ApiFixture fx) => _fx = fx;

    [Fact]
    public async Task Healthz_no_token_returns_200()
    {
        var client = _fx.CreateClient();
        var r = await client.GetAsync("/healthz");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
    }

    [Fact]
    public async Task Healthz_returns_buildId()
    {
        var client = _fx.CreateClient();
        var r = await client.GetFromJsonAsync<JsonElement>("/healthz");
        Assert.True(r.GetProperty("ok").GetBoolean());
        Assert.Equal(_fx.BuildId, r.GetProperty("buildId").GetString());
    }

    [Fact]
    public async Task Root_no_token_returns_401()
    {
        var client = _fx.CreateClient();
        var r = await client.GetAsync("/");
        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
    }

    [Fact]
    public async Task Root_wrong_token_returns_401()
    {
        var client = _fx.CreateClient();
        var r = await client.GetAsync("/?token=00000000000000000000000000000000");
        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
    }

    [Fact]
    public async Task Root_with_valid_token_returns_200()
    {
        var client = _fx.CreateClient();
        var r = await client.GetAsync($"/?token={_fx.Token}");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
    }

    [Fact]
    public async Task Token_in_query_sets_cookie_for_subsequent_requests()
    {
        // 1er appel : token en query, on attend un Set-Cookie
        var client = _fx.CreateClient();
        var first = await client.GetAsync($"/?token={_fx.Token}");
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Contains(first.Headers, h => h.Key == "Set-Cookie" &&
                                            h.Value.Any(v => v.Contains("aspx_lint_token=")));
    }

    [Fact]
    public async Task Cookie_alone_authenticates_subsequent_requests()
    {
        var client = _fx.CreateClient();
        client.DefaultRequestHeaders.Add("Cookie", $"aspx_lint_token={_fx.Token}");

        var r = await client.GetAsync("/api/rules");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
    }

    [Fact]
    public async Task ApiScan_unauthorized_returns_401()
    {
        var client = _fx.CreateClient();
        var r = await client.PostAsJsonAsync("/api/scan", new { path = "irrelevant" });
        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
    }

    [Fact]
    public async Task ApiSave_unauthorized_returns_401()
    {
        var client = _fx.CreateClient();
        var r = await client.PostAsJsonAsync("/api/save", new { path = "irrelevant", content = "x" });
        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
    }

    [Fact]
    public async Task ApiRestore_unauthorized_returns_401()
    {
        var client = _fx.CreateClient();
        var r = await client.PostAsJsonAsync("/api/restore", new { path = "irrelevant" });
        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
    }
}
