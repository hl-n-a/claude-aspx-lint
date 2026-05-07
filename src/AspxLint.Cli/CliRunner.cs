using System.Reflection;
using AspxLint.Core;

namespace AspxLint.Cli;

/// <summary>
/// Entree principale du CLI, factorisee de Program.cs pour etre testable.
/// Toute IO passe par les TextWriter recus en parametres (pas Console direct).
///
/// Codes de sortie :
///   0   ok (scan : aucun probleme ; fix : succes)
///   1   probleme detecte (scan trouve des issues, ou erreur usage)
///   2   erreur d'execution (path inexistant, IO echouee, etc.)
/// </summary>
public static class CliRunner
{
    public const int ExitOk = 0;
    public const int ExitIssuesFound = 1;
    public const int ExitError = 2;

    public static async Task<int> RunAsync(string[] args, TextWriter stdout, TextWriter stderr)
    {
        if (args.Length == 0 || args[0] is "--help" or "-h" or "help")
        {
            PrintUsage(stdout);
            return args.Length == 0 ? ExitIssuesFound : ExitOk;
        }

        if (args[0] is "--version" or "-v")
        {
            stdout.WriteLine($"aspx-lint {ReadVersion()}");
            return ExitOk;
        }

        try
        {
            return args[0] switch
            {
                "scan"        => await ScanAsync(args[1..], stdout, stderr),
                "fix"         => await FixAsync(args[1..], stdout, stderr),
                "analyze"     => await AnalyzeAsync(args[1..], stdout, stderr),
                "rules"       => RulesAsync(args[1..], stdout, stderr),
                "migrate"     => await MigrateAsync(args[1..], stdout, stderr),
                "pre-commit"  => await PreCommitAsync(args[1..], stdout, stderr),
                "watch"       => await WatchAsync(args[1..], stdout, stderr),
                "init"        => await InitAsync(args[1..], stdout, stderr),
                "benchmark"   => await BenchmarkAsync(args[1..], stdout, stderr),
                _             => UnknownCommand(args[0], stderr),
            };
        }
        catch (Exception ex)
        {
            stderr.WriteLine($"Erreur : {ex.Message}");
            return ExitError;
        }
    }

    /// <summary>
    /// Lit la version depuis les metadonnees d'assemblage (set par le csproj
    /// via &lt;Version&gt; ou &lt;InformationalVersion&gt;), pour eviter d'avoir
    /// la version dupliquee en hard-code.
    /// </summary>
    private static string ReadVersion()
    {
        var asm = Assembly.GetExecutingAssembly();
        var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrEmpty(info))
        {
            // .NET injecte parfois "+sha" pour le source-link, on coupe.
            var plus = info.IndexOf('+');
            return plus >= 0 ? info[..plus] : info;
        }
        return asm.GetName().Version?.ToString(3) ?? "0.0.0";
    }

    private static int UnknownCommand(string cmd, TextWriter stderr)
    {
        stderr.WriteLine($"Commande inconnue : {cmd} (--help pour l'aide)");
        return ExitIssuesFound;
    }

    private static async Task<int> ScanAsync(string[] args, TextWriter stdout, TextWriter stderr)
    {
        if (args.Length == 0)
        {
            stderr.WriteLine("Usage: aspx-lint scan <path> [--json | --sarif] [--severity error|warning|info]");
            return ExitIssuesFound;
        }

        var path = args[0];
        var format = "text";
        Severity? minSev = null;
        bool quiet = false;
        bool noColor = false;
        string? lang = null;

        for (int i = 1; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--json": format = "json"; break;
                case "--sarif": format = "sarif"; break;
                case "--junit": format = "junit"; break;
                case "--codeclimate": format = "codeclimate"; break;
                case "--tap": format = "tap"; break;
                case "--quiet": case "-q": quiet = true; break;
                case "--no-color": noColor = true; break;
                case "--lang" when i + 1 < args.Length:
                    lang = args[++i];
                    if (!Translations.AvailableLocales.Any(l => l.Equals(lang, StringComparison.OrdinalIgnoreCase)))
                    {
                        stderr.WriteLine($"--lang doit etre l'une de : {string.Join(", ", Translations.AvailableLocales)}");
                        return ExitIssuesFound;
                    }
                    break;
                case "--severity" when i + 1 < args.Length:
                    if (!Enum.TryParse<Severity>(args[++i], ignoreCase: true, out var s))
                    {
                        stderr.WriteLine($"--severity doit etre error/warning/info (recu : {args[i]}).");
                        return ExitIssuesFound;
                    }
                    minSev = s;
                    break;
                default:
                    stderr.WriteLine($"Argument inconnu : {args[i]}");
                    return ExitIssuesFound;
            }
        }

        if (!Directory.Exists(path))
        {
            stderr.WriteLine($"Dossier introuvable : {path}");
            return ExitError;
        }

        var config = AspxLintConfig.LoadFromOrAbove(path);
        // Scan parallele : sur un projet de 300+ fichiers, divise le temps par
        // ~le nombre de coeurs. L'ordre est preserve (tri alphabetique sur le path).
        var rules = config.ResolveRules(RuleRegistry.All);
        var scanned = ProjectScanner.ScanParallel(path, rules, config: config).ToList();
        // Si --lang non-default, on traduit le nom de regle dans les issues
        // (le hint, lui, est genere par la regle et reste en langue source).
        if (!string.IsNullOrEmpty(lang) && !lang.Equals(Translations.DefaultLocale, StringComparison.OrdinalIgnoreCase))
        {
            scanned = scanned.Select(f => f with
            {
                Issues = f.Issues.Select(i =>
                {
                    var rule = rules.FirstOrDefault(r => r.Id == i.RuleId);
                    if (rule == null) return i;
                    var (name, _) = Translations.Resolve(rule, lang);
                    return i with { RuleName = name };
                }).ToList()
            }).ToList();
        }

        var filtered = minSev is null
            ? scanned
            : scanned.Select(f => f with { Issues = f.Issues.Where(i => (int)i.Severity <= (int)minSev).ToList() })
                     .Where(f => f.Issues.Count > 0)
                     .ToList();

        var totalIssues = filtered.Sum(f => f.Issues.Count);

        switch (format)
        {
            case "json":
                await JsonFormatter.WriteAsync(filtered, totalIssues, stdout);
                break;
            case "sarif":
                await SarifFormatter.WriteAsync(filtered, rules, stdout);
                break;
            case "junit":
                await JunitFormatter.WriteAsync(filtered, totalIssues, stdout);
                break;
            case "codeclimate":
                await CodeClimateFormatter.WriteAsync(filtered, stdout);
                break;
            case "tap":
                await TapFormatter.WriteAsync(filtered, stdout);
                break;
            default:
                bool useColor = !noColor && SupportsColor(stdout);
                TextFormatter.Write(filtered, totalIssues, stdout, useColor, quiet);
                break;
        }

        return totalIssues > 0 ? ExitIssuesFound : ExitOk;
    }

    private static async Task<int> FixAsync(string[] args, TextWriter stdout, TextWriter stderr)
    {
        if (args.Length == 0)
        {
            stderr.WriteLine("Usage: aspx-lint fix <path> [--rule <id>] [--dry-run]");
            stderr.WriteLine("       aspx-lint fix --stdin --ext <ext> [--rule <id>]   (lit stdin, ecrit stdout)");
            return ExitIssuesFound;
        }

        // Mode stdin : applique le fix sur le contenu lu depuis stdin et
        // ecrit le resultat sur stdout. Conçu pour les integrations IDE.
        if (args.Contains("--stdin"))
        {
            return await FixStdinAsync(args, stdout, stderr);
        }

        var path = args[0];
        string? onlyRule = null;
        bool dryRun = false;

        for (int i = 1; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--rule" when i + 1 < args.Length:
                    onlyRule = args[++i];
                    break;
                case "--dry-run":
                    dryRun = true;
                    break;
                default:
                    stderr.WriteLine($"Argument inconnu : {args[i]}");
                    return ExitIssuesFound;
            }
        }

        if (!Directory.Exists(path))
        {
            stderr.WriteLine($"Dossier introuvable : {path}");
            return ExitError;
        }

        var rules = onlyRule is null
            ? RuleRegistry.All.Where(r => r.HasFix).ToList()
            : RuleRegistry.All.Where(r => r.HasFix && r.Id.Equals(onlyRule, StringComparison.OrdinalIgnoreCase)).ToList();

        if (rules.Count == 0)
        {
            stderr.WriteLine(onlyRule is null
                ? "Aucune regle auto-fixable enregistree."
                : $"Regle inconnue ou non fixable : {onlyRule}");
            return ExitIssuesFound;
        }

        var config = AspxLintConfig.LoadFromOrAbove(path);
        int totalFiles = 0, modifiedFiles = 0, totalFixes = 0;

        foreach (var f in ProjectScanner.Scan(path, RuleRegistry.All, config: config))
        {
            totalFiles++;
            var ext = Path.GetExtension(f.AbsolutePath).TrimStart('.').ToLowerInvariant();
            var ctx = new RuleContext(ext, f.AbsolutePath);

            var content = f.Content;
            int filePasses = 0;

            // Boucle jusqu'a 5 passes pour converger (matche le comportement JS).
            for (int pass = 0; pass < 5; pass++)
            {
                var before = content;
                foreach (var rule in rules)
                {
                    var fixedContent = rule.Fix(content, ctx);
                    if (fixedContent != null && fixedContent != content)
                    {
                        content = fixedContent;
                        filePasses++;
                    }
                }
                if (content == before) break;
            }

            if (content != f.Content)
            {
                modifiedFiles++;
                totalFixes += filePasses;
                if (!dryRun)
                {
                    var bytes = System.Text.Encoding.UTF8.GetBytes(content);
                    await File.WriteAllBytesAsync(f.AbsolutePath, bytes);
                }
                stdout.WriteLine($"{(dryRun ? "[dry-run] " : "")}fix {f.RelativePath} ({filePasses} change(s))");
            }
        }

        stdout.WriteLine();
        stdout.WriteLine($"{(dryRun ? "[dry-run] " : "")}{modifiedFiles}/{totalFiles} fichier(s) modifie(s), {totalFixes} correction(s).");
        return ExitOk;
    }

    private static void PrintUsage(TextWriter o)
    {
        o.WriteLine("aspx-lint — analyseur de fichiers ASP.NET Web Forms");
        o.WriteLine();
        o.WriteLine("Usage:");
        o.WriteLine("  aspx-lint analyze --ext <ext> (--stdin | <file>)        [JSON, pour IDE]");
        o.WriteLine("  aspx-lint fix --stdin --ext <ext> [--rule <id>]          [stdin -> stdout, pour IDE]");
        o.WriteLine("  aspx-lint rules [--lang fr|en]                           [JSON metadata des regles]");
        o.WriteLine("  aspx-lint migrate <path> [--out <dir>] [--dry-run] [--report <file.md>]");
        o.WriteLine("                                                           [.aspx -> .cshtml + rapport]");
        o.WriteLine("  aspx-lint scan <path> [--json | --sarif | --junit | --codeclimate | --tap]");
        o.WriteLine("                       [--severity error|warning|info] [--quiet] [--no-color] [--lang fr|en]");
        o.WriteLine("  aspx-lint fix  <path> [--rule <id>] [--dry-run]");
        o.WriteLine("  aspx-lint watch <path> [--severity error|warning|info]");
        o.WriteLine("  aspx-lint init [--with-hook]");
        o.WriteLine("  aspx-lint pre-commit [--severity error|warning|info]");
        o.WriteLine("  aspx-lint benchmark <path> [--runs <N>]");
        o.WriteLine("  aspx-lint --version | --help");
        o.WriteLine();
        o.WriteLine("watch       : re-lint a chaque modification de fichier (Ctrl+C pour arreter).");
        o.WriteLine("init        : genere .aspxlintrc.json (et optionnellement .git/hooks/pre-commit).");
        o.WriteLine("pre-commit  : ne lint que les fichiers staged dans git.");
        o.WriteLine();
        o.WriteLine("Codes de sortie :");
        o.WriteLine("  0  ok (scan sans probleme, ou fix applique)");
        o.WriteLine("  1  scan a trouve au moins un probleme, ou usage incorrect");
        o.WriteLine("  2  erreur d'execution (path absent, IO echouee, etc.)");
    }

    /// <summary>
    /// Watch mode : re-scan le path a chaque modification d'un fichier ASPX/
    /// ASCX/MASTER/ASAX. Compare avec la passe precedente et affiche le delta
    /// (issues ajoutees / resolues) plutot qu'un dump complet.
    /// </summary>
    private static async Task<int> WatchAsync(string[] args, TextWriter stdout, TextWriter stderr)
    {
        if (args.Length == 0)
        {
            stderr.WriteLine("Usage: aspx-lint watch <path> [--severity error|warning|info]");
            return ExitIssuesFound;
        }
        var path = args[0];
        Severity? minSev = null;
        for (int i = 1; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--severity" when i + 1 < args.Length:
                    if (!Enum.TryParse<Severity>(args[++i], ignoreCase: true, out var s))
                    {
                        stderr.WriteLine($"--severity invalide : {args[i]}");
                        return ExitIssuesFound;
                    }
                    minSev = s;
                    break;
                default:
                    stderr.WriteLine($"Argument inconnu : {args[i]}");
                    return ExitIssuesFound;
            }
        }
        if (!Directory.Exists(path))
        {
            stderr.WriteLine($"Dossier introuvable : {path}");
            return ExitError;
        }

        var config = AspxLintConfig.LoadFromOrAbove(path);
        var useColor = SupportsColor(stdout);
        var cache = new ProjectScanner.IncrementalCache();

        // Snapshot initial (rempli le cache).
        var (current, reanalyzed0) = ProjectScanner.ScanIncremental(path, RuleRegistry.All, cache, config: config);
        var totalIssues = current.Sum(f => f.Issues.Count);
        stdout.WriteLine($"aspx-lint watch — {current.Count} fichier(s), {totalIssues} probleme(s).");
        stdout.WriteLine($"Surveille : {Path.GetFullPath(path)}  (Ctrl+C pour arreter)");

        var prevIssues = current.SelectMany(f => f.Issues.Select(i => (f.RelativePath, i)))
                                .ToHashSet();

        using var watcher = new FileSystemWatcher(path)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
            EnableRaisingEvents = true
        };
        watcher.Filters.Add("*.aspx");
        watcher.Filters.Add("*.ascx");
        watcher.Filters.Add("*.master");
        watcher.Filters.Add("*.asax");

        // Coalesce les events : un seul changement peut declencher plusieurs
        // events (size + lastWrite). On debounce 200 ms.
        var trigger = new SemaphoreSlim(0, 1);
        var lockObj = new object();
        long lastEventTick = 0;
        void Bump(object? _, FileSystemEventArgs __) {
            lock (lockObj) lastEventTick = DateTime.UtcNow.Ticks;
            trigger.Release();
        }
        watcher.Changed += Bump!;
        watcher.Created += Bump!;
        watcher.Deleted += Bump!;
        watcher.Renamed += (s, e) => Bump(s, e);

        // Loop infinie : on attend un event, debounce, re-scan, diff.
        var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (s, e) => { e.Cancel = true; cts.Cancel(); };
        while (!cts.IsCancellationRequested)
        {
            try { await trigger.WaitAsync(cts.Token); }
            catch (OperationCanceledException) { break; }

            // Debounce : on attend 200 ms apres le dernier event observe.
            while (true)
            {
                long lt; lock (lockObj) lt = lastEventTick;
                var elapsed = (DateTime.UtcNow.Ticks - lt) / TimeSpan.TicksPerMillisecond;
                if (elapsed >= 200) break;
                await Task.Delay(200, cts.Token);
            }
            // Vide les events accumules pendant le debounce.
            while (trigger.CurrentCount > 0) { try { await trigger.WaitAsync(0); } catch { break; } }

            try
            {
                var (next, reanalyzed) = ProjectScanner.ScanIncremental(path, RuleRegistry.All, cache, config: config);
                var nextSet = next.SelectMany(f => f.Issues.Select(i => (f.RelativePath, i))).ToHashSet();
                var added = nextSet.Where(p => !prevIssues.Contains(p)).ToList();
                var removed = prevIssues.Where(p => !nextSet.Contains(p)).ToList();
                var nextTotal = next.Sum(f => f.Issues.Count);
                var stamp = DateTime.Now.ToString("HH:mm:ss");

                if (added.Count == 0 && removed.Count == 0)
                {
                    stdout.WriteLine($"[{stamp}] re-scan ({reanalyzed} fichier(s) re-analyses) — pas de changement ({nextTotal} issues).");
                }
                else
                {
                    var addS = added.Count > 0 ? Color($"+{added.Count}", "31", useColor) : "";
                    var remS = removed.Count > 0 ? Color($"-{removed.Count}", "32", useColor) : "";
                    stdout.WriteLine($"[{stamp}] {addS} {remS}  total {nextTotal} ({reanalyzed} re-analyses)");
                    foreach (var (rel, i) in added.Take(10))
                        stdout.WriteLine($"  {Color("+", "31", useColor)} {rel}:{i.Line}:{i.Col} [{i.RuleId}] {i.Hint}");
                    foreach (var (rel, i) in removed.Take(10))
                        stdout.WriteLine($"  {Color("-", "32", useColor)} {rel}:{i.Line}:{i.Col} [{i.RuleId}]");
                    if (added.Count + removed.Count > 20)
                        stdout.WriteLine($"  ... ({added.Count + removed.Count - 20} de plus)");
                }
                prevIssues = nextSet;
            }
            catch (Exception ex)
            {
                stderr.WriteLine($"Erreur durant le scan : {ex.Message}");
            }
        }
        stdout.WriteLine("aspx-lint watch arrete.");
        return ExitOk;
    }

    private static string Color(string text, string ansiCode, bool useColor)
    {
        if (!useColor) return text;
        var esc = ((char)27).ToString();
        return $"{esc}[{ansiCode}m{text}{esc}[0m";
    }

    /// <summary>
    /// Init : genere un .aspxlintrc.json template + optionnellement un hook
    /// .git/hooks/pre-commit. Refuse d'ecraser un fichier existant sauf --force.
    /// </summary>
    private static async Task<int> InitAsync(string[] args, TextWriter stdout, TextWriter stderr)
    {
        bool withHook = false;
        bool force = false;
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--with-hook": withHook = true; break;
                case "--force": force = true; break;
                default:
                    stderr.WriteLine($"Argument inconnu : {args[i]}");
                    return ExitIssuesFound;
            }
        }

        var configPath = Path.Combine(Directory.GetCurrentDirectory(), ".aspxlintrc.json");
        if (File.Exists(configPath) && !force)
        {
            stderr.WriteLine($"{configPath} existe deja. Utiliser --force pour l'ecraser.");
            return ExitIssuesFound;
        }

        var template = BuildConfigTemplate();
        await File.WriteAllTextAsync(configPath, template);
        stdout.WriteLine($"OK : {configPath}");

        if (withHook)
        {
            var hookDir = Path.Combine(Directory.GetCurrentDirectory(), ".git", "hooks");
            if (!Directory.Exists(hookDir))
            {
                stderr.WriteLine("Pas de .git/hooks/ trouve. Es-tu dans un repo git ?");
                return ExitOk;   // config OK, juste pas de hook
            }
            var hookPath = Path.Combine(hookDir, "pre-commit");
            if (File.Exists(hookPath) && !force)
            {
                stderr.WriteLine($"{hookPath} existe deja. Utiliser --force pour l'ecraser.");
                return ExitOk;
            }
            await File.WriteAllTextAsync(hookPath,
                "#!/bin/sh\n" +
                "# Genere par `aspx-lint init --with-hook`. Echoue le commit s'il y a au moins\n" +
                "# une issue de severite error parmi les fichiers staged.\n" +
                "exec aspx-lint pre-commit --severity error\n");
            try
            {
                // Sur Unix, mettre executable.
                if (!OperatingSystem.IsWindows())
                    File.SetUnixFileMode(hookPath,
                        UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                        UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                        UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
            }
            catch { /* Win ou pas Unix — ignore */ }
            stdout.WriteLine($"OK : {hookPath}");
        }
        return ExitOk;
    }

    private static string BuildConfigTemplate()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("{");
        sb.AppendLine("  // Patterns d'ignore (* dans un segment, ** traverse les dossiers).");
        sb.AppendLine("  \"ignore\": [");
        sb.AppendLine("    \"**/bin/**\",");
        sb.AppendLine("    \"**/obj/**\",");
        sb.AppendLine("    \"**/Generated/**\"");
        sb.AppendLine("  ],");
        sb.AppendLine();
        sb.AppendLine("  // Override de severite par regle. Valeurs : error, warning, info, off.");
        sb.AppendLine("  // Decommente une ligne pour adapter le linter a ton projet.");
        sb.AppendLine("  \"rules\": {");
        var rulesByCategory = RuleRegistry.All
            .OrderBy(r => r.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();
        for (int i = 0; i < rulesByCategory.Count; i++)
        {
            var r = rulesByCategory[i];
            var defaultSev = r.Severity.ToString().ToLowerInvariant();
            sb.AppendLine($"    // \"{r.Id}\": \"{defaultSev}\",   // {r.Name}");
        }
        // Termine sans virgule sur la derniere ligne (tolere par le JSON laxiste,
        // mais on garde du JSON strict puisqu'on parse via System.Text.Json).
        sb.AppendLine("  }");
        sb.AppendLine("}");
        // Note : System.Text.Json ne tolere pas les commentaires par defaut,
        // mais avec ReadCommentHandling.Skip ils sont ignores. On l'active dans
        // AspxLintConfig.LoadFromOrAbove pour que le template fonctionne tel quel.
        return sb.ToString();
    }

    /// <summary>
    /// Benchmark : mesure le temps de scan total ET le temps par regle pour
    /// identifier les goulots. Lance N passes (3 par defaut), garde la mediane
    /// pour eviter le bruit JIT/cache. Affiche un tableau trie par cout.
    /// </summary>
    private static async Task<int> BenchmarkAsync(string[] args, TextWriter stdout, TextWriter stderr)
    {
        if (args.Length == 0)
        {
            stderr.WriteLine("Usage: aspx-lint benchmark <path> [--runs <N>]");
            return ExitIssuesFound;
        }
        var path = args[0];
        int runs = 3;
        for (int i = 1; i < args.Length; i++)
        {
            if (args[i] == "--runs" && i + 1 < args.Length && int.TryParse(args[++i], out var n) && n > 0)
                runs = Math.Min(n, 50);
            else
            {
                stderr.WriteLine($"Argument inconnu : {args[i]}");
                return ExitIssuesFound;
            }
        }
        if (!Directory.Exists(path))
        {
            stderr.WriteLine($"Dossier introuvable : {path}");
            return ExitError;
        }

        var config = AspxLintConfig.LoadFromOrAbove(path);
        var rules = config.ResolveRules(RuleRegistry.All);

        // Warmup : 1 passe non comptee pour eviter le coup de chauffe JIT.
        stdout.Write("Warmup… ");
        var sw0 = System.Diagnostics.Stopwatch.StartNew();
        var warm = ProjectScanner.ScanParallel(path, rules, config: config);
        sw0.Stop();
        stdout.WriteLine($"{warm.Count} fichier(s) en {sw0.ElapsedMilliseconds} ms.");

        // Mesures globales : N runs.
        stdout.WriteLine();
        stdout.Write("Scan total : ");
        var totals = new List<long>();
        for (int r = 0; r < runs; r++)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var _ = ProjectScanner.ScanParallel(path, rules, config: config);
            sw.Stop();
            totals.Add(sw.ElapsedMilliseconds);
            stdout.Write($"{sw.ElapsedMilliseconds}ms ");
        }
        stdout.WriteLine();
        var med = Median(totals);
        var min = totals.Min();
        var max = totals.Max();
        stdout.WriteLine($"  median {med}ms, min {min}ms, max {max}ms ({runs} runs)");
        stdout.WriteLine();

        // Per-rule : on benchmark Detect en charge sequentielle pour avoir
        // une mesure stable par regle (sans contention thread).
        var perRule = new Dictionary<string, long>();
        var totalIssuesPerRule = new Dictionary<string, int>();
        var fileCount = warm.Count;

        foreach (var rule in rules)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            int hits = 0;
            foreach (var f in warm)
            {
                var ext = Path.GetExtension(f.AbsolutePath).TrimStart('.').ToLowerInvariant();
                var ctx = new RuleContext(ext, f.AbsolutePath);
                var lines = f.Content.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
                hits += rule.Detect(f.Content, lines, ctx).Count();
            }
            sw.Stop();
            perRule[rule.Id] = sw.ElapsedMilliseconds;
            totalIssuesPerRule[rule.Id] = hits;
        }

        stdout.WriteLine($"Per-rule (sequential, sur {fileCount} fichier(s)) :");
        stdout.WriteLine();
        stdout.WriteLine("  Rule         Time     Per-file  Issues");
        stdout.WriteLine("  ----------   ------   --------  ------");
        foreach (var (id, ms) in perRule.OrderByDescending(kv => kv.Value))
        {
            var perFile = fileCount > 0 ? (double)ms / fileCount : 0;
            var issues = totalIssuesPerRule[id];
            stdout.WriteLine($"  {id,-12} {ms,4} ms  {perFile,6:F2} ms  {issues,5}");
        }
        stdout.WriteLine();
        stdout.WriteLine($"Total per-rule sum : {perRule.Values.Sum()} ms");

        await Task.CompletedTask;
        return ExitOk;
    }

    private static long Median(List<long> values)
    {
        var sorted = values.OrderBy(x => x).ToList();
        int n = sorted.Count;
        if (n == 0) return 0;
        return n % 2 == 1 ? sorted[n / 2] : (sorted[n / 2 - 1] + sorted[n / 2]) / 2;
    }

    /// <summary>
    /// Detecte si le terminal cible supporte les sequences ANSI. On desactive
    /// les couleurs si stdout est redirige vers un fichier ou un pipe (pour ne
    /// pas polluer les sorties consommees par jq, awk, fichiers de log, etc.).
    /// </summary>
    private static bool SupportsColor(TextWriter stdout)
    {
        // Si stdout n'est pas Console.Out (utilise pour les tests qui passent
        // un StringWriter), pas de couleurs — sinon les tests assertent sur
        // des chaines polluees par les codes ANSI.
        if (stdout != Console.Out) return false;
        if (Console.IsOutputRedirected) return false;
        // Convention universelle : NO_COLOR=1 desactive (https://no-color.org).
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NO_COLOR"))) return false;
        return true;
    }

    /// <summary>
    /// Lance le scan uniquement sur les fichiers ASPX/ASCX/MASTER/ASAX qui sont
    /// stages dans git (`git diff --cached --name-only --diff-filter=ACMR`).
    /// A cabler dans `.git/hooks/pre-commit` :
    ///   #!/bin/sh
    ///   exec aspx-lint pre-commit --severity error
    /// </summary>
    private static async Task<int> PreCommitAsync(string[] args, TextWriter stdout, TextWriter stderr)
    {
        Severity? minSev = null;
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--severity" when i + 1 < args.Length:
                    if (!Enum.TryParse<Severity>(args[++i], ignoreCase: true, out var s))
                    {
                        stderr.WriteLine($"--severity doit etre error/warning/info (recu : {args[i]}).");
                        return ExitIssuesFound;
                    }
                    minSev = s;
                    break;
                default:
                    stderr.WriteLine($"Argument inconnu : {args[i]}");
                    return ExitIssuesFound;
            }
        }

        // Recupere la liste des fichiers stages via git, en mode rapide.
        string gitOutput;
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo("git",
                "diff --cached --name-only --diff-filter=ACMR")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            var p = System.Diagnostics.Process.Start(psi);
            if (p == null) { stderr.WriteLine("Impossible de lancer git."); return ExitError; }
            gitOutput = await p.StandardOutput.ReadToEndAsync();
            await p.WaitForExitAsync();
            if (p.ExitCode != 0)
            {
                stderr.WriteLine("git a echoue. Es-tu dans un repo ?");
                return ExitError;
            }
        }
        catch (Exception ex)
        {
            stderr.WriteLine($"git introuvable ou erreur : {ex.Message}");
            return ExitError;
        }

        var stagedExt = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { ".aspx", ".ascx", ".master", ".asax" };
        var staged = gitOutput
            .Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
            .Where(p => stagedExt.Contains(Path.GetExtension(p)))
            .Where(File.Exists)
            .ToList();

        if (staged.Count == 0)
        {
            stdout.WriteLine("aspx-lint pre-commit : aucun fichier ASPX/ASCX/MASTER staged.");
            return ExitOk;
        }

        // Trouve la racine du repo pour retrouver la config + les paths relatifs.
        var repoRoot = Directory.GetCurrentDirectory();
        var config = AspxLintConfig.LoadFromOrAbove(repoRoot);

        var scannedFiles = new List<ScannedFile>();
        foreach (var rel in staged)
        {
            try
            {
                var full = Path.GetFullPath(rel);
                var bytes = File.ReadAllBytes(full);
                var hasBom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
                var content = hasBom
                    ? "﻿" + System.Text.Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3)
                    : System.Text.Encoding.UTF8.GetString(bytes);
                var issues = Analyzer.Analyze(full, content, RuleRegistry.All, config);
                var lineCount = 1;
                foreach (var c in content) if (c == '\n') lineCount++;
                scannedFiles.Add(new ScannedFile(full, rel, lineCount, content, issues));
            }
            catch (Exception ex)
            {
                stderr.WriteLine($"Lecture echouee pour {rel} : {ex.Message}");
            }
        }

        var filtered = minSev is null
            ? scannedFiles
            : scannedFiles.Select(f => f with { Issues = f.Issues.Where(i => (int)i.Severity <= (int)minSev).ToList() })
                          .Where(f => f.Issues.Count > 0)
                          .ToList();
        var totalIssues = filtered.Sum(f => f.Issues.Count);

        stdout.WriteLine($"aspx-lint pre-commit : {staged.Count} fichier(s) staged.");
        TextFormatter.Write(filtered, totalIssues, stdout, SupportsColor(stdout));

        return totalIssues > 0 ? ExitIssuesFound : ExitOk;
    }

    /// <summary>
    /// Analyse un fichier unique sans toucher au disque. Lit le contenu depuis
    /// stdin (--stdin) ou depuis un path passe en argument. Renvoie toujours
    /// du JSON sur stdout : { "issues": [...] }. Concu pour les integrations
    /// IDE (VSCode, Visual Studio, ...) qui veulent analyser le buffer en
    /// cours sans avoir a sauver d'abord.
    ///
    /// Usage :
    ///   aspx-lint analyze --ext aspx --stdin            (lit stdin)
    ///   aspx-lint analyze --ext aspx file.aspx          (lit le fichier)
    /// </summary>
    private static async Task<int> AnalyzeAsync(string[] args, TextWriter stdout, TextWriter stderr)
    {
        string? path = null;
        string ext = "aspx";
        bool useStdin = false;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--stdin":
                    useStdin = true;
                    break;
                case "--ext" when i + 1 < args.Length:
                    ext = args[++i].TrimStart('.').ToLowerInvariant();
                    break;
                default:
                    if (args[i].StartsWith("--"))
                    {
                        stderr.WriteLine($"Argument inconnu : {args[i]}");
                        return ExitIssuesFound;
                    }
                    if (path != null)
                    {
                        stderr.WriteLine("Un seul path attendu.");
                        return ExitIssuesFound;
                    }
                    path = args[i];
                    break;
            }
        }

        if (!useStdin && path == null)
        {
            stderr.WriteLine("Usage: aspx-lint analyze --ext <ext> (--stdin | <path>)");
            return ExitIssuesFound;
        }

        string content;
        string virtualPath;
        if (useStdin)
        {
            // Console.In (vs OpenStandardInput) :
            //  - respecte les pipes shell (aspx-lint fix --stdin < file.aspx)
            //  - permet a Console.SetIn de l'intercepter dans les unit tests
            content = await Console.In.ReadToEndAsync();
            virtualPath = $"stdin.{ext}";
        }
        else
        {
            if (!File.Exists(path))
            {
                stderr.WriteLine($"Fichier introuvable : {path}");
                return ExitError;
            }
            // Lecture BOM-aware (cf. ProjectScanner.AnalyzeOne).
            var bytes = await File.ReadAllBytesAsync(path);
            var hasBom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
            content = hasBom
                ? "﻿" + System.Text.Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3)
                : System.Text.Encoding.UTF8.GetString(bytes);
            virtualPath = path;
            if (string.IsNullOrEmpty(ext) || ext == "aspx")
            {
                // Devine l'ext depuis le path si pas forcee.
                var realExt = Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
                if (!string.IsNullOrEmpty(realExt)) ext = realExt;
            }
        }

        // Charge la config depuis le repertoire courant (ou parents) — utile
        // pour respecter les overrides de severite et les ignores quand l'ext
        // est appelee depuis un VSCode dans un projet aspx-lint configure.
        var workDir = Directory.GetCurrentDirectory();
        var config = AspxLintConfig.LoadFromOrAbove(workDir);
        var rules = config.ResolveRules(RuleRegistry.All);

        var issues = Analyzer.Analyze(virtualPath, content, rules, config);

        // Output : JSON simple, compatible JsonFormatter. Pas de path inutile
        // car l'IDE connait deja le path qu'il a passe.
        var payload = new
        {
            ext,
            issues = issues.Select(i => new
            {
                ruleId = i.RuleId,
                ruleName = i.RuleName,
                severity = i.Severity.ToString().ToLowerInvariant(),
                line = i.Line,
                col = i.Col,
                snippet = i.Snippet,
                hint = i.Hint
            }).ToList()
        };
        var json = System.Text.Json.JsonSerializer.Serialize(payload, new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = false
        });
        stdout.WriteLine(json);

        return issues.Count > 0 ? ExitIssuesFound : ExitOk;
    }

    /// <summary>
    /// Mode stdin de FixAsync : lit du contenu depuis stdin, applique
    /// les fixes (toutes les regles auto-fixables ou juste --rule X), et
    /// ecrit le resultat sur stdout. Concu pour les integrations IDE qui
    /// veulent appliquer un fix sur le buffer en cours sans toucher au
    /// disque.
    ///
    /// Convention exit code : 0 si on a applique au moins un fix OU si le
    /// contenu etait deja propre (le buffer recu sur stdout est valide
    /// dans les deux cas). 1 sur regle inconnue, 2 sur erreur d'IO.
    /// </summary>
    private static async Task<int> FixStdinAsync(string[] args, TextWriter stdout, TextWriter stderr)
    {
        string ext = "aspx";
        string? onlyRule = null;
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--stdin":
                    break;
                case "--ext" when i + 1 < args.Length:
                    ext = args[++i].TrimStart('.').ToLowerInvariant();
                    break;
                case "--rule" when i + 1 < args.Length:
                    onlyRule = args[++i];
                    break;
                default:
                    if (args[i].StartsWith("--"))
                    {
                        stderr.WriteLine($"Argument inconnu : {args[i]}");
                        return ExitIssuesFound;
                    }
                    break;
            }
        }

        var rules = onlyRule is null
            ? RuleRegistry.All.Where(r => r.HasFix).ToList()
            : RuleRegistry.All.Where(r => r.HasFix && r.Id.Equals(onlyRule, StringComparison.OrdinalIgnoreCase)).ToList();

        if (rules.Count == 0)
        {
            stderr.WriteLine(onlyRule is null
                ? "Aucune regle auto-fixable enregistree."
                : $"Regle inconnue ou non fixable : {onlyRule}");
            return ExitIssuesFound;
        }

        string content;
        try
        {
            content = await Console.In.ReadToEndAsync();
        }
        catch (Exception ex)
        {
            stderr.WriteLine($"Lecture stdin echouee : {ex.Message}");
            return ExitError;
        }

        var ctx = new RuleContext(ext, $"stdin.{ext}");

        // Memes 5 passes que FixAsync pour converger.
        for (int pass = 0; pass < 5; pass++)
        {
            var before = content;
            foreach (var rule in rules)
            {
                var fixedContent = rule.Fix(content, ctx);
                if (fixedContent != null && fixedContent != content)
                    content = fixedContent;
            }
            if (content == before) break;
        }

        // stdout.Write (pas WriteLine) : on ne pollue pas le contenu avec
        // un \n parasite a la fin. Si le content avait deja un trailing \n
        // il reste, sinon il n'y en a pas — fidele au comportement attendu.
        await stdout.WriteAsync(content);
        return ExitOk;
    }

    /// <summary>
    /// rules [--json] [--lang fr|en] : dump la liste des regles avec
    /// metadata (id, name, severity, hasFix, description). Concu pour
    /// les integrations IDE qui affichent les descriptions au hover.
    ///
    /// JSON par defaut : la sortie est consommee par des outils.
    /// </summary>
    private static int RulesAsync(string[] args, TextWriter stdout, TextWriter stderr)
    {
        string? lang = null;
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--json":
                    // Format par defaut, accepte pour clarte.
                    break;
                case "--lang" when i + 1 < args.Length:
                    lang = args[++i];
                    if (!Translations.AvailableLocales.Any(l => l.Equals(lang, StringComparison.OrdinalIgnoreCase)))
                    {
                        stderr.WriteLine($"--lang doit etre l'une de : {string.Join(", ", Translations.AvailableLocales)}");
                        return ExitIssuesFound;
                    }
                    break;
                default:
                    stderr.WriteLine($"Argument inconnu : {args[i]}");
                    return ExitIssuesFound;
            }
        }

        var rules = RuleRegistry.All.Select(r =>
        {
            var (name, description) = Translations.Resolve(r, lang);
            return new
            {
                id = r.Id,
                name,
                description,
                severity = r.Severity.ToString().ToLowerInvariant(),
                hasFix = r.HasFix
            };
        }).ToList();

        var json = System.Text.Json.JsonSerializer.Serialize(
            new { rules },
            new System.Text.Json.JsonSerializerOptions { WriteIndented = false });
        stdout.WriteLine(json);
        return ExitOk;
    }

    /// <summary>
    /// migrate <path> [--out <dir>] [--dry-run] [--report <file.md>]
    ///
    /// Convertit un fichier ou un dossier d'ASPX/ASCX/MASTER en .cshtml.
    /// Phase 1 : transformations syntaxiques uniquement (commentaires
    /// serveur, directives, expressions, instructions). Genere un
    /// rapport markdown listant les actions et les TODO manuels.
    /// </summary>
    private static async Task<int> MigrateAsync(string[] args, TextWriter stdout, TextWriter stderr)
    {
        if (args.Length == 0)
        {
            stderr.WriteLine("Usage: aspx-lint migrate <path> [--out <dir>] [--dry-run] [--report <file.md>]");
            return ExitIssuesFound;
        }

        var path = args[0];
        string? outDir = null;
        string? reportPath = null;
        bool dryRun = false;

        for (int i = 1; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--out" when i + 1 < args.Length:
                    outDir = args[++i];
                    break;
                case "--report" when i + 1 < args.Length:
                    reportPath = args[++i];
                    break;
                case "--dry-run":
                    dryRun = true;
                    break;
                default:
                    stderr.WriteLine($"Argument inconnu : {args[i]}");
                    return ExitIssuesFound;
            }
        }

        // Determine si c'est un fichier ou un dossier.
        bool isFile = File.Exists(path);
        bool isDir  = Directory.Exists(path);
        if (!isFile && !isDir)
        {
            stderr.WriteLine($"Path introuvable : {path}");
            return ExitError;
        }

        var supportedExts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { ".aspx", ".ascx", ".master", ".asax" };

        var sources = new List<(string Absolute, string Relative)>();
        string root;
        if (isFile)
        {
            root = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path)) ?? ".";
            sources.Add((System.IO.Path.GetFullPath(path), System.IO.Path.GetFileName(path)));
        }
        else
        {
            root = System.IO.Path.GetFullPath(path);
            foreach (var f in Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories))
            {
                if (supportedExts.Contains(System.IO.Path.GetExtension(f)))
                    sources.Add((f, System.IO.Path.GetRelativePath(root, f)));
            }
        }

        if (sources.Count == 0)
        {
            stdout.WriteLine("Aucun fichier .aspx/.ascx/.master/.asax trouve.");
            return ExitOk;
        }

        // Sortie : <out> ou <root>/migrated/ par defaut.
        var resolvedOut = outDir ?? System.IO.Path.Combine(root, "migrated");
        if (!dryRun) Directory.CreateDirectory(resolvedOut);

        var report = new global::AspxLint.Migrate.MigrationReport();
        int converted = 0;

        foreach (var (absolute, relative) in sources.OrderBy(s => s.Relative))
        {
            string content;
            try { content = await File.ReadAllTextAsync(absolute); }
            catch (Exception ex)
            {
                stderr.WriteLine($"Lecture echouee {relative} : {ex.Message}");
                continue;
            }

            var result = global::AspxLint.Migrate.Migrator.Migrate(content, relative, report);
            converted++;

            var outRelative = result.SuggestedOutputName;
            var outAbsolute = System.IO.Path.Combine(resolvedOut, outRelative);

            if (dryRun)
            {
                stdout.WriteLine($"[dry-run] {relative} → {outRelative} ({result.Actions.Count} action(s))");
            }
            else
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(outAbsolute)!);
                await File.WriteAllTextAsync(outAbsolute, result.Content);
                stdout.WriteLine($"migrate {relative} → {outRelative} ({result.Actions.Count} action(s))");
            }
        }

        // Resume + rapport.
        var auto    = report.CountBySeverity(global::AspxLint.Migrate.MigrationSeverity.Auto);
        var warning = report.CountBySeverity(global::AspxLint.Migrate.MigrationSeverity.Warning);
        var manual  = report.CountBySeverity(global::AspxLint.Migrate.MigrationSeverity.Manual);

        stdout.WriteLine();
        stdout.WriteLine($"{converted} fichier(s) {(dryRun ? "analyse(s)" : "convertis")} : {auto} auto, {warning} warning, {manual} manual.");

        if (reportPath != null)
        {
            try
            {
                await File.WriteAllTextAsync(reportPath, report.ToMarkdown());
                stdout.WriteLine($"Rapport ecrit : {reportPath}");
            }
            catch (Exception ex)
            {
                stderr.WriteLine($"Ecriture du rapport echouee : {ex.Message}");
                return ExitError;
            }
        }
        else if (manual + warning > 0)
        {
            stdout.WriteLine("Tip : passe `--report migration-report.md` pour avoir le detail.");
        }

        return ExitOk;
    }
}
