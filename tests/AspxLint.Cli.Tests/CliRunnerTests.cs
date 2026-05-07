using System.Text.Json;
using System.Xml.Linq;

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

    /// <summary>
    /// Cree un fichier ASPX avec quelques issues pour tester les formatters.
    /// </summary>
    private static TempDir MakeFixture()
    {
        var tmp = new TempDir();
        tmp.WriteFile("page.aspx",
            "<%@ Page Language=\"C#\" %>\n" +
            "<html><head><title>x</title></head>\n" +
            "<body><img src=\"a.png\"></body>\n" +
            "</html>\n");
        return tmp;
    }

    [Fact]
    public void Scan_junit_format_writes_valid_xml()
    {
        using var tmp = MakeFixture();
        var (code, stdout, _) = Run("scan", tmp.Path, "--junit");
        Assert.True(code == CliRunner.ExitOk || code == CliRunner.ExitIssuesFound);
        var doc = XDocument.Parse(stdout);
        Assert.Equal("testsuites", doc.Root!.Name.LocalName);
        Assert.NotNull(doc.Root.Attribute("name"));
        Assert.NotNull(doc.Root.Attribute("tests"));
        // Au moins un testsuite si des issues ont ete trouvees.
        var suites = doc.Root.Elements("testsuite").ToList();
        Assert.NotEmpty(suites);
    }

    [Fact]
    public void Scan_codeclimate_format_emits_valid_json_array()
    {
        using var tmp = MakeFixture();
        var (_, stdout, _) = Run("scan", tmp.Path, "--codeclimate");
        var doc = JsonDocument.Parse(stdout);
        Assert.Equal(JsonValueKind.Array, doc.RootElement.ValueKind);
        Assert.True(doc.RootElement.GetArrayLength() > 0);
        var first = doc.RootElement[0];
        Assert.Equal("issue", first.GetProperty("type").GetString());
        Assert.False(string.IsNullOrEmpty(first.GetProperty("check_name").GetString()));
        Assert.False(string.IsNullOrEmpty(first.GetProperty("fingerprint").GetString()));
        // 4 categories autorisees par CodeClimate
        var cat = first.GetProperty("categories")[0].GetString();
        Assert.Contains(cat, new[] { "Style", "Bug Risk", "Security", "Compatibility", "Complexity", "Clarity", "Performance" });
    }

    [Fact]
    public void Scan_tap_format_starts_with_version_and_plan()
    {
        using var tmp = MakeFixture();
        var (_, stdout, _) = Run("scan", tmp.Path, "--tap");
        var lines = stdout.Split('\n').Select(l => l.TrimEnd('\r')).ToArray();
        Assert.Equal("TAP version 14", lines[0]);
        Assert.Matches(@"^1\.\.\d+$", lines[1]);
    }

    [Fact]
    public void Scan_quiet_only_prints_summary_line()
    {
        using var tmp = MakeFixture();
        var (_, stdout, _) = Run("scan", tmp.Path, "--quiet");
        var trimmed = stdout.Trim();
        // Une seule ligne, pas de details par fichier.
        var lines = trimmed.Split('\n');
        Assert.Single(lines);
        Assert.Contains("fichier(s) scannes", trimmed);
    }

    [Fact]
    public void Scan_lang_en_translates_rule_names_in_json()
    {
        using var tmp = MakeFixture();
        var (_, stdout, _) = Run("scan", tmp.Path, "--json", "--lang", "en");
        var doc = JsonDocument.Parse(stdout);
        var issues = doc.RootElement.GetProperty("files");
        var found = false;
        foreach (var f in issues.EnumerateArray())
        {
            foreach (var i in f.GetProperty("issues").EnumerateArray())
            {
                if (i.GetProperty("ruleName").GetString()?.Contains(" sans ") == false &&
                    i.GetProperty("ruleId").GetString() != null)
                {
                    found = true;
                    break;
                }
            }
            if (found) break;
        }
        Assert.True(found, "Aucune issue avec un nom de regle non-francais detecte.");
    }

    [Fact]
    public void Scan_lang_invalid_returns_error()
    {
        using var tmp = MakeFixture();
        var (code, _, stderr) = Run("scan", tmp.Path, "--lang", "klingon");
        Assert.Equal(CliRunner.ExitIssuesFound, code);
        Assert.Contains("--lang", stderr);
    }

    [Fact]
    public void Init_creates_aspxlintrc_template()
    {
        using var tmp = new TempDir();
        var prev = Directory.GetCurrentDirectory();
        try
        {
            Directory.SetCurrentDirectory(tmp.Path);
            var (code, stdout, _) = Run("init");
            Assert.Equal(CliRunner.ExitOk, code);
            var configPath = Path.Combine(tmp.Path, ".aspxlintrc.json");
            Assert.True(File.Exists(configPath));
            var content = File.ReadAllText(configPath);
            Assert.Contains("\"ignore\":", content);
            Assert.Contains("\"rules\":", content);
            // Toutes les regles built-in doivent figurer dans le template.
            Assert.Contains("\"DIR-001\"", content);
            Assert.Contains("\"WS-006\"", content);
        }
        finally { Directory.SetCurrentDirectory(prev); }
    }

    [Fact]
    public void Init_refuses_overwrite_without_force()
    {
        using var tmp = new TempDir();
        var prev = Directory.GetCurrentDirectory();
        try
        {
            Directory.SetCurrentDirectory(tmp.Path);
            File.WriteAllText(Path.Combine(tmp.Path, ".aspxlintrc.json"), "{}");
            var (code, _, stderr) = Run("init");
            Assert.Equal(CliRunner.ExitIssuesFound, code);
            Assert.Contains("--force", stderr);
        }
        finally { Directory.SetCurrentDirectory(prev); }
    }

    [Fact]
    public void Init_force_overwrites()
    {
        using var tmp = new TempDir();
        var prev = Directory.GetCurrentDirectory();
        try
        {
            Directory.SetCurrentDirectory(tmp.Path);
            var configPath = Path.Combine(tmp.Path, ".aspxlintrc.json");
            File.WriteAllText(configPath, "{}");
            var (code, _, _) = Run("init", "--force");
            Assert.Equal(CliRunner.ExitOk, code);
            var content = File.ReadAllText(configPath);
            Assert.Contains("\"ignore\":", content);   // template applique
        }
        finally { Directory.SetCurrentDirectory(prev); }
    }

    [Fact]
    public void Benchmark_runs_and_prints_per_rule_breakdown()
    {
        using var tmp = MakeFixture();
        var (code, stdout, _) = Run("benchmark", tmp.Path, "--runs", "1");
        Assert.Equal(CliRunner.ExitOk, code);
        Assert.Contains("Warmup", stdout);
        Assert.Contains("Per-rule", stdout);
        Assert.Contains("Total per-rule sum", stdout);
    }

    [Fact]
    public void Benchmark_missing_path_returns_error()
    {
        var (code, _, stderr) = Run("benchmark");
        Assert.Equal(CliRunner.ExitIssuesFound, code);
        Assert.Contains("Usage", stderr);
    }

    [Fact]
    public void Custom_rules_loaded_from_aspxlintrc_match()
    {
        using var tmp = new TempDir();
        tmp.WriteFile(".aspxlintrc.json", @"
        {
            ""customRules"": [
                {
                    ""id"": ""CUSTOM-TODO"",
                    ""name"": ""TODO"",
                    ""severity"": ""warning"",
                    ""description"": ""..."",
                    ""pattern"": ""TODO[: ]"",
                    ""hint"": ""Resoudre."",
                    ""maskAspBlocks"": false
                }
            ]
        }");
        tmp.WriteFile("page.aspx", "<p>TODO: refactor</p>\n");
        var (_, stdout, _) = Run("scan", tmp.Path, "--json");
        var doc = JsonDocument.Parse(stdout);
        var found = false;
        foreach (var f in doc.RootElement.GetProperty("files").EnumerateArray())
        foreach (var i in f.GetProperty("issues").EnumerateArray())
        {
            if (i.GetProperty("ruleId").GetString() == "CUSTOM-TODO") { found = true; break; }
        }
        Assert.True(found, "La custom rule CUSTOM-TODO devrait avoir matche.");
    }

    // ====== fix --stdin (pour integrations IDE) ======

    /// <summary>
    /// Run la CLI avec un buffer stdin synthetique. Le helper Run() de
    /// cette classe ne supporte pas stdin ; on utilise CliRunner.RunAsync
    /// directement avec un Console.SetIn temporaire.
    /// </summary>
    private static (int exit, string stdout, string stderr) RunWithStdin(string stdinContent, params string[] args)
    {
        var origIn = Console.In;
        try
        {
            Console.SetIn(new StringReader(stdinContent));
            var o = new StringWriter();
            var e = new StringWriter();
            var code = CliRunner.RunAsync(args, o, e).GetAwaiter().GetResult();
            return (code, o.ToString(), e.ToString());
        }
        finally
        {
            Console.SetIn(origIn);
        }
    }

    [Fact]
    public void FixStdin_applies_targeted_rule_only()
    {
        // <br> matche TAG-001 et "line   " matche WS-001. Avec --rule TAG-001
        // seul TAG-001 doit etre applique. WS-001 reste.
        var (code, stdout, _) = RunWithStdin(
            "<%@ Page %>\n<br>\nline   \n",
            "fix", "--stdin", "--ext", "aspx", "--rule", "TAG-001");
        Assert.Equal(CliRunner.ExitOk, code);
        Assert.Contains("<br />", stdout);
        Assert.Contains("line   ", stdout);   // WS-001 pas applique
    }

    [Fact]
    public void FixStdin_applies_all_when_no_rule_specified()
    {
        var (code, stdout, _) = RunWithStdin(
            "<br>\nline   \n",
            "fix", "--stdin", "--ext", "aspx");
        Assert.Equal(CliRunner.ExitOk, code);
        Assert.Contains("<br />", stdout);
        Assert.DoesNotContain("line   \n", stdout);   // WS-001 applique
    }

    [Fact]
    public void FixStdin_unknown_rule_returns_1()
    {
        var (code, _, stderr) = RunWithStdin("<br>\n", "fix", "--stdin", "--ext", "aspx", "--rule", "FAKE-999");
        Assert.Equal(CliRunner.ExitIssuesFound, code);
        Assert.Contains("FAKE-999", stderr);
    }

    [Fact]
    public void FixStdin_clean_content_returns_unchanged()
    {
        // Contenu deja propre : aucun changement, mais on retourne 0 et
        // l'integralite du buffer original.
        var input = "<%@ Page Language=\"C#\" %>\n";
        var (code, stdout, _) = RunWithStdin(input, "fix", "--stdin", "--ext", "aspx");
        Assert.Equal(CliRunner.ExitOk, code);
        Assert.Equal(input, stdout);
    }

    [Fact]
    public void FixStdin_unknown_arg_returns_1()
    {
        var (code, _, stderr) = RunWithStdin("x", "fix", "--stdin", "--ext", "aspx", "--banana");
        Assert.Equal(CliRunner.ExitIssuesFound, code);
        Assert.Contains("--banana", stderr);
    }

    // ====== rules (pour integrations IDE) ======

    [Fact]
    public void Rules_returns_json_with_all_rules()
    {
        var (code, stdout, _) = Run("rules");
        Assert.Equal(CliRunner.ExitOk, code);

        var doc = JsonDocument.Parse(stdout);
        var rules = doc.RootElement.GetProperty("rules");
        Assert.Equal(JsonValueKind.Array, rules.ValueKind);
        Assert.Equal(35, rules.GetArrayLength());

        var first = rules[0];
        Assert.False(string.IsNullOrEmpty(first.GetProperty("id").GetString()));
        Assert.False(string.IsNullOrEmpty(first.GetProperty("name").GetString()));
        Assert.False(string.IsNullOrEmpty(first.GetProperty("description").GetString()));
        Assert.Contains(first.GetProperty("severity").GetString(),
            new[] { "error", "warning", "info" });
        // hasFix doit etre un bool
        Assert.True(first.GetProperty("hasFix").ValueKind == JsonValueKind.True
                 || first.GetProperty("hasFix").ValueKind == JsonValueKind.False);
    }

    [Fact]
    public void Rules_with_lang_en_returns_translated_descriptions()
    {
        var (_, stdoutFr, _) = Run("rules");
        var (_, stdoutEn, _) = Run("rules", "--lang", "en");

        var fr = JsonDocument.Parse(stdoutFr).RootElement.GetProperty("rules");
        var en = JsonDocument.Parse(stdoutEn).RootElement.GetProperty("rules");

        // Au moins une regle doit avoir un nom different en EN qu'en FR.
        bool anyDifferent = false;
        for (int i = 0; i < fr.GetArrayLength(); i++)
        {
            if (fr[i].GetProperty("name").GetString() != en[i].GetProperty("name").GetString())
            {
                anyDifferent = true;
                break;
            }
        }
        Assert.True(anyDifferent, "Aucune traduction EN detectee — Translations.cs vide ?");
    }

    [Fact]
    public void Rules_invalid_lang_returns_1()
    {
        var (code, _, stderr) = Run("rules", "--lang", "klingon");
        Assert.Equal(CliRunner.ExitIssuesFound, code);
        Assert.Contains("lang", stderr);
    }

    [Fact]
    public void Rules_unknown_arg_returns_1()
    {
        var (code, _, stderr) = Run("rules", "--banana");
        Assert.Equal(CliRunner.ExitIssuesFound, code);
        Assert.Contains("--banana", stderr);
    }

    // ====== analyze (pour integrations IDE) ======

    [Fact]
    public void Analyze_path_returns_json_with_issues()
    {
        using var tmp = new TempDir();
        var path = tmp.WriteFile("dirty.aspx",
            "<%@ Page Language=\"C#\" %>\n<br>\n");

        var (code, stdout, _) = Run("analyze", "--ext", "aspx", path);
        Assert.True(code == CliRunner.ExitOk || code == CliRunner.ExitIssuesFound);

        var doc = JsonDocument.Parse(stdout);
        Assert.Equal("aspx", doc.RootElement.GetProperty("ext").GetString());
        var issues = doc.RootElement.GetProperty("issues");
        Assert.Equal(JsonValueKind.Array, issues.ValueKind);
        Assert.True(issues.GetArrayLength() > 0);
        var first = issues[0];
        // Schema attendu : ruleId, severity, line, col, snippet, hint.
        Assert.False(string.IsNullOrEmpty(first.GetProperty("ruleId").GetString()));
        Assert.True(first.GetProperty("line").GetInt32() > 0);
    }

    [Fact]
    public void Analyze_clean_content_returns_empty_issues_and_zero()
    {
        using var tmp = new TempDir();
        var path = tmp.WriteFile("clean.aspx",
            "<%@ Page Language=\"C#\" %>\n" +
            "<!DOCTYPE html>\n" +
            "<html><head><title>x</title></head>\n" +
            "<body><form runat=\"server\"></form></body>\n" +
            "</html>\n");

        var (code, stdout, _) = Run("analyze", "--ext", "aspx", path);
        Assert.Equal(CliRunner.ExitOk, code);

        var doc = JsonDocument.Parse(stdout);
        Assert.Equal(0, doc.RootElement.GetProperty("issues").GetArrayLength());
    }

    [Fact]
    public void Analyze_unknown_path_returns_2()
    {
        var fake = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".aspx");
        var (code, _, stderr) = Run("analyze", "--ext", "aspx", fake);
        Assert.Equal(CliRunner.ExitError, code);
        Assert.Contains("introuvable", stderr);
    }

    [Fact]
    public void Analyze_no_args_returns_1()
    {
        var (code, _, stderr) = Run("analyze");
        Assert.Equal(CliRunner.ExitIssuesFound, code);
        Assert.Contains("Usage", stderr);
    }

    [Fact]
    public void Analyze_unknown_arg_returns_1()
    {
        var (code, _, stderr) = Run("analyze", "--banana");
        Assert.Equal(CliRunner.ExitIssuesFound, code);
        Assert.Contains("--banana", stderr);
    }

    [Fact]
    public void Analyze_config_file_routes_to_cfg_rules()
    {
        using var tmp = new TempDir();
        var path = tmp.WriteFile("Web.config",
            "<configuration><system.web><compilation debug=\"true\" /></system.web></configuration>\n");

        var (_, stdout, _) = Run("analyze", path);
        var doc = JsonDocument.Parse(stdout);
        var ids = doc.RootElement.GetProperty("issues").EnumerateArray()
            .Select(i => i.GetProperty("ruleId").GetString())
            .ToList();
        Assert.Contains("CFG-001", ids);
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
        Assert.Equal(35, driver.GetProperty("rules").GetArrayLength());
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

    // ====== migrate (ASPX -> Razor, Phase 1 syntaxe) ======

    [Fact]
    public void Migrate_single_file_writes_cshtml_output()
    {
        using var tmp = new TempDir();
        var src = tmp.WriteFile("Demo.aspx",
            "<%@ Page Inherits=\"App.Demo\" %>\n" +
            "<h1><%: Model.Name %></h1>\n");

        var (code, stdout, _) = Run("migrate", src,
            "--out", Path.Combine(tmp.Path, "out"));

        Assert.Equal(CliRunner.ExitOk, code);
        Assert.Contains("Demo.aspx", stdout);
        Assert.Contains("Demo.cshtml", stdout);

        var outPath = Path.Combine(tmp.Path, "out", "Demo.cshtml");
        Assert.True(File.Exists(outPath));
        var content = File.ReadAllText(outPath);
        Assert.Contains("@page", content);
        Assert.Contains("@model App.Demo", content);
        Assert.Contains("@(Model.Name)", content);
    }

    [Fact]
    public void Migrate_directory_recurses_and_mirrors_structure()
    {
        using var tmp = new TempDir();
        tmp.WriteFile(Path.Combine("Views", "Home", "Index.aspx"),
            "<%@ Page Inherits=\"App.Home\" %>\nhi\n");
        tmp.WriteFile(Path.Combine("Controls", "Header.ascx"),
            "<%@ Control Inherits=\"App.Header\" %>\n");

        var (code, _, _) = Run("migrate", tmp.Path,
            "--out", Path.Combine(tmp.Path, "out"));

        Assert.Equal(CliRunner.ExitOk, code);
        Assert.True(File.Exists(Path.Combine(tmp.Path, "out", "Views", "Home", "Index.cshtml")));
        // .ascx → _Header.cshtml (Razor convention pour les partials)
        Assert.True(File.Exists(Path.Combine(tmp.Path, "out", "Controls", "_Header.cshtml")));
    }

    [Fact]
    public void Migrate_dry_run_does_not_write_files()
    {
        using var tmp = new TempDir();
        var src = tmp.WriteFile("Foo.aspx", "<%@ Page %>\n");
        var outDir = Path.Combine(tmp.Path, "out");

        var (code, stdout, _) = Run("migrate", src, "--out", outDir, "--dry-run");

        Assert.Equal(CliRunner.ExitOk, code);
        Assert.Contains("[dry-run]", stdout);
        Assert.False(Directory.Exists(outDir) && Directory.GetFiles(outDir, "*", SearchOption.AllDirectories).Length > 0);
    }

    [Fact]
    public void Migrate_emits_markdown_report_when_requested()
    {
        using var tmp = new TempDir();
        var src = tmp.WriteFile("Demo.aspx",
            "<%@ Page Inherits=\"App.Demo\" %>\n" +
            "<%# Eval(\"Name\") %>\n");
        var report = Path.Combine(tmp.Path, "report.md");

        Run("migrate", src,
            "--out", Path.Combine(tmp.Path, "out"),
            "--report", report);

        Assert.True(File.Exists(report));
        var md = File.ReadAllText(report);
        Assert.Contains("# aspx-lint migrate", md);
        Assert.Contains("transformations automatiques", md);
        Assert.Contains("Demo.aspx", md);
    }

    [Fact]
    public void Migrate_unknown_path_returns_2()
    {
        var fake = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".aspx");
        var (code, _, stderr) = Run("migrate", fake);
        Assert.Equal(CliRunner.ExitError, code);
        Assert.Contains("introuvable", stderr);
    }

    [Fact]
    public void Migrate_no_args_returns_1()
    {
        var (code, _, stderr) = Run("migrate");
        Assert.Equal(CliRunner.ExitIssuesFound, code);
        Assert.Contains("Usage", stderr);
    }

    [Fact]
    public void Migrate_unknown_arg_returns_1()
    {
        using var tmp = new TempDir();
        tmp.WriteFile("x.aspx", "<%@ Page %>\n");
        var (code, _, stderr) = Run("migrate", tmp.Path, "--banana");
        Assert.Equal(CliRunner.ExitIssuesFound, code);
        Assert.Contains("--banana", stderr);
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

    // ====== Error paths (couverture des branches d'usage incorrect) ======

    [Fact]
    public void Scan_without_path_prints_usage()
    {
        var (code, _, stderr) = Run("scan");
        Assert.Equal(CliRunner.ExitIssuesFound, code);
        Assert.Contains("Usage", stderr);
    }

    [Fact]
    public void Scan_unknown_arg_returns_1()
    {
        using var tmp = MakeFixture();
        var (code, _, stderr) = Run("scan", tmp.Path, "--banana");
        Assert.Equal(CliRunner.ExitIssuesFound, code);
        Assert.Contains("--banana", stderr);
    }

    [Fact]
    public void Scan_invalid_severity_returns_1()
    {
        using var tmp = MakeFixture();
        var (code, _, stderr) = Run("scan", tmp.Path, "--severity", "criticat");
        Assert.Equal(CliRunner.ExitIssuesFound, code);
        Assert.Contains("severity", stderr);
    }

    [Fact]
    public void Scan_invalid_lang_returns_1()
    {
        using var tmp = MakeFixture();
        var (code, _, stderr) = Run("scan", tmp.Path, "--lang", "klingon");
        Assert.Equal(CliRunner.ExitIssuesFound, code);
        Assert.Contains("lang", stderr);
    }

    [Fact]
    public void Fix_without_path_prints_usage()
    {
        var (code, _, stderr) = Run("fix");
        Assert.Equal(CliRunner.ExitIssuesFound, code);
        Assert.Contains("Usage", stderr);
    }

    [Fact]
    public void Fix_unknown_arg_returns_1()
    {
        using var tmp = MakeFixture();
        var (code, _, stderr) = Run("fix", tmp.Path, "--banana");
        Assert.Equal(CliRunner.ExitIssuesFound, code);
        Assert.Contains("--banana", stderr);
    }

    [Fact]
    public void Fix_missing_directory_returns_2()
    {
        var fake = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var (code, _, _) = Run("fix", fake);
        Assert.Equal(CliRunner.ExitError, code);
    }

    [Fact]
    public void Watch_without_path_prints_usage()
    {
        var (code, _, stderr) = Run("watch");
        Assert.Equal(CliRunner.ExitIssuesFound, code);
        Assert.Contains("Usage", stderr);
    }

    [Fact]
    public void Watch_unknown_arg_returns_1()
    {
        using var tmp = MakeFixture();
        var (code, _, stderr) = Run("watch", tmp.Path, "--banana");
        Assert.Equal(CliRunner.ExitIssuesFound, code);
        Assert.Contains("--banana", stderr);
    }

    [Fact]
    public void Watch_invalid_severity_returns_1()
    {
        using var tmp = MakeFixture();
        var (code, _, stderr) = Run("watch", tmp.Path, "--severity", "criticat");
        Assert.Equal(CliRunner.ExitIssuesFound, code);
        Assert.Contains("severity", stderr);
    }

    [Fact]
    public void Watch_missing_directory_returns_2()
    {
        var fake = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var (code, _, _) = Run("watch", fake);
        Assert.Equal(CliRunner.ExitError, code);
    }

    // ====== pre-commit ======

    [Fact]
    public void PreCommit_unknown_arg_returns_1()
    {
        var (code, _, stderr) = Run("pre-commit", "--banana");
        Assert.Equal(CliRunner.ExitIssuesFound, code);
        Assert.Contains("--banana", stderr);
    }

    [Fact]
    public void PreCommit_invalid_severity_returns_1()
    {
        var (code, _, stderr) = Run("pre-commit", "--severity", "criticat");
        Assert.Equal(CliRunner.ExitIssuesFound, code);
        Assert.Contains("severity", stderr);
    }

    // ====== --version / --help ======

    [Fact]
    public void Version_flag_prints_a_version()
    {
        var (code, stdout, _) = Run("--version");
        Assert.Equal(CliRunner.ExitOk, code);
        Assert.Matches(@"\d+\.\d+", stdout);
    }

    [Fact]
    public void Help_flag_prints_usage()
    {
        var (code, stdout, _) = Run("--help");
        Assert.Equal(CliRunner.ExitOk, code);
        Assert.Contains("aspx-lint", stdout);
        Assert.Contains("scan", stdout);
        Assert.Contains("fix", stdout);
    }

    // ====== --no-color (exercice la branche noColor du parser d'args) ======

    [Fact]
    public void Scan_no_color_returns_ok_or_issues()
    {
        using var tmp = MakeFixture();
        var (code, stdout, _) = Run("scan", tmp.Path, "--no-color");
        Assert.True(code == CliRunner.ExitOk || code == CliRunner.ExitIssuesFound);
        Assert.NotEmpty(stdout);
    }
}
