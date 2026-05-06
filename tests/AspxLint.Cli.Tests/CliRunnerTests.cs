namespace AspxLint.Cli.Tests;

public class CliRunnerTests
{
    private static (int exit, string stdout, string stderr) Run(params string[] args)
    {
        var o = new StringWriter();
        var e = new StringWriter();
        var code = CliRunner.RunAsync(args, o, e).GetAwaiter().GetResult();
        return (code, o.ToString(), e.ToString());
    }

    [Fact]
    public void No_args_prints_usage_and_returns_1()
    {
        var (code, stdout, _) = Run();
        Assert.Equal(CliRunner.ExitIssuesFound, code);
        Assert.Contains("Usage:", stdout);
    }

    [Fact]
    public void Help_returns_0()
    {
        var (code, stdout, _) = Run("--help");
        Assert.Equal(CliRunner.ExitOk, code);
        Assert.Contains("Usage:", stdout);
    }

    [Fact]
    public void Version_prints_version_and_returns_0()
    {
        var (code, stdout, _) = Run("--version");
        Assert.Equal(CliRunner.ExitOk, code);
        Assert.Contains("aspx-lint", stdout);
    }

    [Fact]
    public void Unknown_command_returns_1()
    {
        var (code, _, stderr) = Run("doStuff");
        Assert.Equal(CliRunner.ExitIssuesFound, code);
        Assert.Contains("Commande inconnue", stderr);
    }

    [Fact]
    public void Scan_missing_path_returns_2()
    {
        var bogus = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var (code, _, stderr) = Run("scan", bogus);
        Assert.Equal(CliRunner.ExitError, code);
        Assert.Contains("introuvable", stderr);
    }

    [Fact]
    public void Scan_clean_dir_returns_0()
    {
        using var tmp = new TempDir();
        // .aspx propre : <%@ Page %>, doctype, form runat, etc.
        tmp.WriteFile("clean.aspx",
            "<%@ Page Language=\"C#\" %>\n" +
            "<!DOCTYPE html>\n" +
            "<html>\n<head><title>x</title></head>\n" +
            "<body><form runat=\"server\"></form></body>\n" +
            "</html>\n");

        var (code, _, _) = Run("scan", tmp.Path);
        Assert.Equal(CliRunner.ExitOk, code);
    }

    [Fact]
    public void Scan_dirty_dir_returns_1()
    {
        using var tmp = new TempDir();
        tmp.WriteFile("dirty.aspx", "<br>"); // void tag non auto-ferme + pas de \n final

        var (code, _, _) = Run("scan", tmp.Path);
        Assert.Equal(CliRunner.ExitIssuesFound, code);
    }

    [Fact]
    public void Scan_text_format_lists_issues()
    {
        using var tmp = new TempDir();
        tmp.WriteFile("dirty.aspx", "<br>\n");

        var (_, stdout, _) = Run("scan", tmp.Path);
        Assert.Contains("TAG-001", stdout);
        Assert.Contains("dirty.aspx", stdout);
    }

    [Fact]
    public void Scan_json_format_returns_valid_json_with_metadata()
    {
        using var tmp = new TempDir();
        tmp.WriteFile("dirty.aspx", "<br>\n");

        var (_, stdout, _) = Run("scan", tmp.Path, "--json");
        var doc = JsonDocument.Parse(stdout);
        Assert.True(doc.RootElement.TryGetProperty("scannedAt", out _));
        Assert.Equal(1, doc.RootElement.GetProperty("fileCount").GetInt32());
        Assert.True(doc.RootElement.GetProperty("issueCount").GetInt32() > 0);
    }

    [Fact]
    public void Scan_sarif_format_has_runs_and_results()
    {
        using var tmp = new TempDir();
        tmp.WriteFile("dirty.aspx", "<br>\n");

        var (_, stdout, _) = Run("scan", tmp.Path, "--sarif");
        var doc = JsonDocument.Parse(stdout);
        Assert.Equal("2.1.0", doc.RootElement.GetProperty("version").GetString());
        Assert.True(doc.RootElement.TryGetProperty("$schema", out _));
        var runs = doc.RootElement.GetProperty("runs");
        Assert.Equal(1, runs.GetArrayLength());
        var driver = runs[0].GetProperty("tool").GetProperty("driver");
        Assert.Equal("aspx-lint", driver.GetProperty("name").GetString());
        Assert.Equal(29, driver.GetProperty("rules").GetArrayLength());
        Assert.True(runs[0].GetProperty("results").GetArrayLength() > 0);
    }

    [Fact]
    public void Scan_severity_filter_only_keeps_errors_when_minSev_is_error()
    {
        using var tmp = new TempDir();
        // Directive presente => DIR-001 ne fire pas. <br> => TAG-001 warning seul.
        // Avec --severity error => 0 issues, exit 0.
        tmp.WriteFile("dirty.aspx", "<%@ Page %>\n<br>\n");

        var (code, stdout, _) = Run("scan", tmp.Path, "--severity", "error");
        Assert.Equal(CliRunner.ExitOk, code);
        Assert.DoesNotContain("TAG-001", stdout);
    }

    [Fact]
    public void Fix_dry_run_does_not_modify_disk()
    {
        using var tmp = new TempDir();
        var path = tmp.WriteFile("dirty.aspx", "<br>\n");
        var before = File.ReadAllText(path);

        var (code, stdout, _) = Run("fix", tmp.Path, "--dry-run");
        Assert.Equal(CliRunner.ExitOk, code);
        Assert.Contains("dry-run", stdout);
        Assert.Equal(before, File.ReadAllText(path)); // disque inchange
    }

    [Fact]
    public void Fix_actually_writes_corrected_content()
    {
        using var tmp = new TempDir();
        var path = tmp.WriteFile("dirty.aspx", "<br>\n");

        var (code, _, _) = Run("fix", tmp.Path);
        Assert.Equal(CliRunner.ExitOk, code);

        var after = File.ReadAllText(path);
        Assert.Contains("<br />", after); // TAG-001 fixed
    }

    [Fact]
    public void Fix_with_specific_rule_only_applies_that_rule()
    {
        using var tmp = new TempDir();
        // contient WS-001 (trailing) ET TAG-001 (<br>)
        var path = tmp.WriteFile("dirty.aspx", "line   \n<br>\n");

        var (code, _, _) = Run("fix", tmp.Path, "--rule", "WS-001");
        Assert.Equal(CliRunner.ExitOk, code);

        var after = File.ReadAllText(path);
        Assert.DoesNotContain("line   \n", after); // WS-001 fixed
        Assert.Contains("<br>", after);             // TAG-001 PAS fixed
        Assert.DoesNotContain("<br />", after);
    }

    [Fact]
    public void Fix_unknown_rule_returns_1()
    {
        using var tmp = new TempDir();
        tmp.WriteFile("x.aspx", "x\n");

        var (code, _, stderr) = Run("fix", tmp.Path, "--rule", "DOES-NOT-EXIST");
        Assert.Equal(CliRunner.ExitIssuesFound, code);
        Assert.Contains("inconnue", stderr);
    }

    [Fact]
    public void Fix_is_idempotent_across_runs()
    {
        using var tmp = new TempDir();
        var path = tmp.WriteFile("dirty.aspx", "line   \n<br>\n");

        Run("fix", tmp.Path);
        var afterFirst = File.ReadAllText(path);
        Run("fix", tmp.Path);
        var afterSecond = File.ReadAllText(path);

        Assert.Equal(afterFirst, afterSecond);
    }

    [Fact]
    public void Fix_then_scan_returns_0_for_fixable_issues()
    {
        using var tmp = new TempDir();
        // Issues toutes auto-fixables : trailing ws + <br>
        tmp.WriteFile("dirty.aspx",
            "<%@ Page Language=\"C#\" %>\n" +
            "<!DOCTYPE html>\n" +
            "<html><body><form runat=\"server\"><br></form></body></html>\n");

        Run("fix", tmp.Path);
        var (code, _, _) = Run("scan", tmp.Path);
        Assert.Equal(CliRunner.ExitOk, code);
    }
}
