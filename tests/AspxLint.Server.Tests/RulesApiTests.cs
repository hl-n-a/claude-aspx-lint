namespace AspxLint.Server.Tests;

public class RulesApiTests : IClassFixture<ApiFixture>
{
    private readonly ApiFixture _fx;
    public RulesApiTests(ApiFixture fx) => _fx = fx;

    [Fact]
    public async Task Rules_returns_24_entries()
    {
        var client = _fx.CreateAuthClient();
        var r = await client.GetFromJsonAsync<JsonElement>("/api/rules");
        Assert.Equal(24, r.GetArrayLength());
    }

    [Fact]
    public async Task Rules_each_has_required_fields()
    {
        var client = _fx.CreateAuthClient();
        var r = await client.GetFromJsonAsync<JsonElement>("/api/rules");

        foreach (var rule in r.EnumerateArray())
        {
            Assert.False(string.IsNullOrEmpty(rule.GetProperty("id").GetString()));
            Assert.False(string.IsNullOrEmpty(rule.GetProperty("name").GetString()));
            Assert.False(string.IsNullOrEmpty(rule.GetProperty("desc").GetString()));
            // severity doit etre lowercase pour matcher la convention CSS du dashboard.
            var sev = rule.GetProperty("severity").GetString();
            Assert.True(sev == "error" || sev == "warning" || sev == "info",
                $"Severity inattendue : {sev}");
            Assert.True(rule.GetProperty("hasFix").GetBoolean() ||
                        !rule.GetProperty("hasFix").GetBoolean()); // existence
        }
    }

    [Fact]
    public async Task Rules_hasFix_count_matches_18()
    {
        var client = _fx.CreateAuthClient();
        var r = await client.GetFromJsonAsync<JsonElement>("/api/rules");
        var fixable = r.EnumerateArray().Count(rule => rule.GetProperty("hasFix").GetBoolean());
        Assert.Equal(18, fixable);
    }
}
