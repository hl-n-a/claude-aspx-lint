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
                "pre-commit"  => await PreCommitAsync(args[1..], stdout, stderr),
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

        for (int i = 1; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--json": format = "json"; break;
                case "--sarif": format = "sarif"; break;
                case "--quiet": case "-q": quiet = true; break;
                case "--no-color": noColor = true; break;
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
        var scanned = ProjectScanner.ScanParallel(path, RuleRegistry.All, config: config).ToList();
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
                await SarifFormatter.WriteAsync(filtered, RuleRegistry.All, stdout);
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
            return ExitIssuesFound;
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
        o.WriteLine("  aspx-lint scan <path> [--json | --sarif] [--severity error|warning|info] [--quiet] [--no-color]");
        o.WriteLine("  aspx-lint fix  <path> [--rule <id>] [--dry-run]");
        o.WriteLine("  aspx-lint pre-commit [--severity error|warning|info]");
        o.WriteLine("  aspx-lint --version | --help");
        o.WriteLine();
        o.WriteLine("pre-commit : ne lint que les fichiers ASPX/ASCX/MASTER staged dans git.");
        o.WriteLine("              A cabler dans .git/hooks/pre-commit.");
        o.WriteLine();
        o.WriteLine("Codes de sortie :");
        o.WriteLine("  0  ok (scan sans probleme, ou fix applique)");
        o.WriteLine("  1  scan a trouve au moins un probleme, ou usage incorrect");
        o.WriteLine("  2  erreur d'execution (path absent, IO echouee, etc.)");
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
}
