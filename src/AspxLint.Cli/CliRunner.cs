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
                "scan" => await ScanAsync(args[1..], stdout, stderr),
                "fix"  => await FixAsync(args[1..], stdout, stderr),
                _      => UnknownCommand(args[0], stderr),
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

        for (int i = 1; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--json": format = "json"; break;
                case "--sarif": format = "sarif"; break;
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

        var scanned = ProjectScanner.Scan(path, RuleRegistry.All).ToList();
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
                TextFormatter.Write(filtered, totalIssues, stdout);
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

        int totalFiles = 0, modifiedFiles = 0, totalFixes = 0;

        foreach (var f in ProjectScanner.Scan(path, RuleRegistry.All))
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
        o.WriteLine("  aspx-lint scan <path> [--json | --sarif] [--severity error|warning|info]");
        o.WriteLine("  aspx-lint fix  <path> [--rule <id>] [--dry-run]");
        o.WriteLine("  aspx-lint --version | --help");
        o.WriteLine();
        o.WriteLine("Codes de sortie :");
        o.WriteLine("  0  ok (scan sans probleme, ou fix applique)");
        o.WriteLine("  1  scan a trouve au moins un probleme, ou usage incorrect");
        o.WriteLine("  2  erreur d'execution (path absent, IO echouee, etc.)");
    }
}
