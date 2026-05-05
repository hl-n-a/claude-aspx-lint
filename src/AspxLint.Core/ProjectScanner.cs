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

    public static IEnumerable<ScannedFile> Scan(
        string root,
        IEnumerable<IRule> rules,
        IEnumerable<string>? extensions = null)
    {
        if (!Directory.Exists(root))
            throw new DirectoryNotFoundException($"Dossier introuvable : {root}");

        var exts = (extensions ?? DefaultExtensions)
            .Select(e => e.ToLowerInvariant())
            .ToHashSet();
        var rulesList = rules.ToList();

        foreach (var path in Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories))
        {
            var ext = Path.GetExtension(path).ToLowerInvariant();
            if (!exts.Contains(ext)) continue;

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
            catch { continue; }

            var issues = Analyzer.Analyze(path, content, rulesList);
            var lineCount = 1;
            foreach (var c in content) if (c == '\n') lineCount++;
            var rel = Path.GetRelativePath(root, path);

            yield return new ScannedFile(path, rel, lineCount, content, issues);
        }
    }
}
