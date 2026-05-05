using System.Net.Http.Headers;
using Microsoft.Extensions.Hosting;

namespace AspxLint.Server.Tests;

/// <summary>
/// Tests qui verifient les options de deploiement (ASPXLINT_API_KEY,
/// ASPXLINT_ALLOWED_ROOT, ASPXLINT_READ_ONLY). Chaque test cree sa propre
/// WebApplicationFactory avec des env vars specifiques, lues au demarrage
/// par CreateSession().
///
/// La collection [DisableParallelization] empeche les races sur les env vars
/// partagees pendant la phase de construction du host.
/// </summary>
[CollectionDefinition("ConfigEnvVars", DisableParallelization = true)]
public class ConfigEnvVarsCollection { }

[Collection("ConfigEnvVars")]
public class ConfiguredApiTests
{
    private static WebApplicationFactory<Program> CreateFactoryWithEnv(
        Dictionary<string, string?> envVars,
        out string token)
    {
        var apiKey = "test-cfg-" + Guid.NewGuid().ToString("N");
        envVars["ASPXLINT_API_KEY"] = apiKey;
        token = apiKey;

        var factory = new EnvVarFactory(envVars);
        // Force le host a se construire maintenant pour que les env vars
        // soient lues, puis on les nettoie.
        _ = factory.Services;
        return factory;
    }

    private sealed class EnvVarFactory : WebApplicationFactory<Program>
    {
        private readonly Dictionary<string, string?> _envVars;
        public EnvVarFactory(Dictionary<string, string?> envVars) => _envVars = envVars;

        protected override IHost CreateHost(IHostBuilder builder)
        {
            var saved = new Dictionary<string, string?>();
            foreach (var (k, _) in _envVars)
                saved[k] = Environment.GetEnvironmentVariable(k);
            try
            {
                foreach (var (k, v) in _envVars)
                    Environment.SetEnvironmentVariable(k, v);
                return base.CreateHost(builder);
            }
            finally
            {
                foreach (var (k, v) in saved)
                    Environment.SetEnvironmentVariable(k, v);
            }
        }
    }

    private static HttpClient CreateAuthed(WebApplicationFactory<Program> fx, string token)
    {
        var c = fx.CreateClient();
        c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return c;
    }

    private static string MakeTempDir()
    {
        var p = Path.Combine(Path.GetTempPath(), "aspxlint-cfg-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(p);
        return p;
    }

    // ========== ASPXLINT_API_KEY ==========

    [Fact]
    public async Task Custom_api_key_via_env_var_is_accepted()
    {
        using var fx = CreateFactoryWithEnv(new() { }, out var token);
        var r = await CreateAuthed(fx, token).GetAsync("/api/rules");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
    }

    // ========== ASPXLINT_READ_ONLY ==========

    [Fact]
    public async Task ReadOnly_mode_blocks_save_with_403()
    {
        var tmp = MakeTempDir();
        try
        {
            using var fx = CreateFactoryWithEnv(
                new() { ["ASPXLINT_READ_ONLY"] = "true" }, out var token);
            var client = CreateAuthed(fx, token);

            // On scan d'abord pour pre-remplir l'allowlist
            await client.PostAsJsonAsync("/api/scan", new { path = tmp });

            var r = await client.PostAsJsonAsync("/api/save",
                new { path = Path.Combine(tmp, "x.aspx"), content = "x" });
            Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
        }
        finally { Directory.Delete(tmp, true); }
    }

    [Fact]
    public async Task ReadOnly_mode_blocks_restore_with_403()
    {
        var tmp = MakeTempDir();
        try
        {
            using var fx = CreateFactoryWithEnv(
                new() { ["ASPXLINT_READ_ONLY"] = "true" }, out var token);
            var client = CreateAuthed(fx, token);
            await client.PostAsJsonAsync("/api/scan", new { path = tmp });

            var r = await client.PostAsJsonAsync("/api/restore",
                new { path = Path.Combine(tmp, "x.aspx") });
            Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
        }
        finally { Directory.Delete(tmp, true); }
    }

    [Fact]
    public async Task ReadOnly_mode_still_allows_scan_and_analyze()
    {
        var tmp = MakeTempDir();
        try
        {
            File.WriteAllText(Path.Combine(tmp, "p.aspx"), "<%@ Page %>\n<br>\n");
            using var fx = CreateFactoryWithEnv(
                new() { ["ASPXLINT_READ_ONLY"] = "true" }, out var token);
            var client = CreateAuthed(fx, token);

            var scan = await client.PostAsJsonAsync("/api/scan", new { path = tmp });
            Assert.Equal(HttpStatusCode.OK, scan.StatusCode);

            var analyze = await client.PostAsJsonAsync("/api/analyze",
                new { content = "<br>\n", ext = "aspx" });
            Assert.Equal(HttpStatusCode.OK, analyze.StatusCode);
        }
        finally { Directory.Delete(tmp, true); }
    }

    // ========== ASPXLINT_ALLOWED_ROOT ==========

    [Fact]
    public async Task AllowedRoot_blocks_scan_outside_root_with_403()
    {
        var allowed = MakeTempDir();
        var outside = MakeTempDir();
        try
        {
            using var fx = CreateFactoryWithEnv(
                new() { ["ASPXLINT_ALLOWED_ROOT"] = allowed }, out var token);
            var client = CreateAuthed(fx, token);

            var r = await client.PostAsJsonAsync("/api/scan", new { path = outside });
            Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
        }
        finally
        {
            Directory.Delete(allowed, true);
            Directory.Delete(outside, true);
        }
    }

    [Fact]
    public async Task AllowedRoot_allows_scan_inside_root()
    {
        var allowed = MakeTempDir();
        try
        {
            File.WriteAllText(Path.Combine(allowed, "p.aspx"), "<%@ Page %>\n");
            using var fx = CreateFactoryWithEnv(
                new() { ["ASPXLINT_ALLOWED_ROOT"] = allowed }, out var token);
            var client = CreateAuthed(fx, token);

            var r = await client.PostAsJsonAsync("/api/scan", new { path = allowed });
            Assert.Equal(HttpStatusCode.OK, r.StatusCode);
            var body = await r.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(1, body.GetProperty("fileCount").GetInt32());
        }
        finally { Directory.Delete(allowed, true); }
    }

    [Fact]
    public async Task AllowedRoot_allows_subdirectories()
    {
        var allowed = MakeTempDir();
        try
        {
            var sub = Path.Combine(allowed, "Sub", "Deep");
            Directory.CreateDirectory(sub);
            File.WriteAllText(Path.Combine(sub, "p.aspx"), "<%@ Page %>\n");

            using var fx = CreateFactoryWithEnv(
                new() { ["ASPXLINT_ALLOWED_ROOT"] = allowed }, out var token);
            var client = CreateAuthed(fx, token);

            var r = await client.PostAsJsonAsync("/api/scan", new { path = sub });
            Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        }
        finally { Directory.Delete(allowed, true); }
    }

    [Fact]
    public async Task AllowedRoot_blocks_path_traversal_attempt()
    {
        var allowed = MakeTempDir();
        try
        {
            using var fx = CreateFactoryWithEnv(
                new() { ["ASPXLINT_ALLOWED_ROOT"] = allowed }, out var token);
            var client = CreateAuthed(fx, token);

            // ../../etc resolved in Path.GetFullPath, then checked against allowed
            var traversal = Path.Combine(allowed, "..", "..", "etc");
            var r = await client.PostAsJsonAsync("/api/scan", new { path = traversal });
            Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
        }
        finally { Directory.Delete(allowed, true); }
    }
}
