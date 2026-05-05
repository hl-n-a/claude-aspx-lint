namespace AspxLint.Core;

public static class Analyzer
{
    public static IReadOnlyList<Issue> Analyze(
        string filePath,
        string content,
        IEnumerable<IRule> rules)
    {
        var ext = Path.GetExtension(filePath).TrimStart('.').ToLowerInvariant();
        var ctx = new RuleContext(ext, filePath);
        var lines = content.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);

        var all = new List<Issue>();
        foreach (var rule in rules)
            all.AddRange(rule.Detect(content, lines, ctx));
        return all;
    }
}
