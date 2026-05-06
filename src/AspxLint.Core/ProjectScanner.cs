using System.Collections.Concurrent;

namespace AspxLint.Core;

public sealed record ScannedFile(
    string AbsolutePath,
    string RelativePath,
    int LineCount,
    string Content,
    IReadOnlyList<Issue> Issues
);

public static class ProjectScanner
{
    public static readonly string[] DefaultExtensions = { ".aspx", ".ascx", ".master", ".asax" };

    /// <summary>
    /// Scan classique. Streamble (yield) — pratique pour le CLI qui veut afficher
    /// au fur et a mesure. Pour un scan parallele groupe (plus rapide sur gros
    /// projets), utiliser <see cref="ScanParallel"/>.
    /// </summary>
    public static IEnumerable<ScannedFile> Scan(
        string root,
        IEnumerable<IRule> rules,
        IEnumerable<string>? extensions = null,
        AspxLintConfig? config = null)
    {
        var (paths, exts, rulesList) = PrepareScan(root, rules, extensions, config);

        foreach (var path in paths)
        {
            var rel = Path.GetRelativePath(root, path);
            if (config != null && config.IsIgnored(rel)) continue;
            var sf = AnalyzeOne(path, rel, rulesList, config);
            if (sf != null) yield return sf;
        }
    }

    /// <summary>
    /// Variante parallele : analyse les fichiers en parallele via Parallel.ForEach.
    /// Sur un projet de ~350 fichiers, divise le temps total par ~le nombre de
    /// coeurs (chaque fichier est independant). Le resultat n'est PAS streamble
    /// — on attend la fin du scan complet, mais l'ordre relatif est preserve
    /// par tri sur le path. Recommande pour fix-all et pour les CI.
    /// </summary>
    public static IReadOnlyList<ScannedFile> ScanParallel(
        string root,
        IEnumerable<IRule> rules,
        IEnumerable<string>? extensions = null,
        AspxLintConfig? config = null,
        int? maxDegreeOfParallelism = null)
    {
        var (paths, exts, rulesList) = PrepareScan(root, rules, extensions, config);
        var bag = new ConcurrentBag<ScannedFile>();
        var pathList = paths.ToList();

        var po = new ParallelOptions
        {
            MaxDegreeOfParallelism = maxDegreeOfParallelism ?? Environment.ProcessorCount
        };

        Parallel.ForEach(pathList, po, path =>
        {
            var rel = Path.GetRelativePath(root, path);
            if (config != null && config.IsIgnored(rel)) return;
            var sf = AnalyzeOne(path, rel, rulesList, config);
            if (sf != null) bag.Add(sf);
        });

        return bag.OrderBy(f => f.RelativePath, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static (IEnumerable<string> Paths, HashSet<string> Exts, List<IRule> Rules)
        PrepareScan(string root, IEnumerable<IRule> rules, IEnumerable<string>? extensions, AspxLintConfig? config)
    {
        if (!Directory.Exists(root))
            throw new DirectoryNotFoundException($"Dossier introuvable : {root}");

        var exts = (extensions ?? DefaultExtensions)
            .Select(e => e.ToLowerInvariant())
            .ToHashSet();
        var rulesList = rules.ToList();

        var paths = Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories)
            .Where(p => exts.Contains(Path.GetExtension(p).ToLowerInvariant()));

        return (paths, exts, rulesList);
    }

    private static ScannedFile? AnalyzeOne(string path, string rel, List<IRule> rulesList, AspxLintConfig? config)
    {
        string content;
        try
        {
            // Lecture BOM-aware : on conserve le BOM dans la chaine sous forme de
            // caractere ﻿, ce qui permet a WS-005 de le detecter et a /api/save
            // de le re-emettre fidelement (UTF-8 encode ﻿ en EF BB BF).
            var bytes = File.ReadAllBytes(path);
            var hasBom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
            content = hasBom
                ? "﻿" + System.Text.Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3)
                : System.Text.Encoding.UTF8.GetString(bytes);
        }
        catch { return null; }

        var issues = Analyzer.Analyze(path, content, rulesList, config);
        var lineCount = 1;
        foreach (var c in content) if (c == '\n') lineCount++;
        return new ScannedFile(path, rel, lineCount, content, issues);
    }
}
