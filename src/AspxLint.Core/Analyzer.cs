namespace AspxLint.Core;

public static class Analyzer
{
    public static IReadOnlyList<Issue> Analyze(
        string filePath,
        string content,
        IEnumerable<IRule> rules,
        AspxLintConfig? config = null)
    {
        var ext = Path.GetExtension(filePath).TrimStart('.').ToLowerInvariant();
        var ctx = new RuleContext(ext, filePath);
        var lines = content.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);

        var all = new List<Issue>();
        foreach (var rule in rules)
        {
            // Skip si la regle est desactivee dans la config (sev = null).
            Severity? sev = config?.ResolveSeverity(rule);
            if (config != null && sev == null) continue;

            foreach (var issue in rule.Detect(content, lines, ctx))
            {
                // Si la config override la severite, on remplace.
                if (config != null && sev.HasValue && sev.Value != issue.Severity)
                {
                    all.Add(issue with { Severity = sev.Value });
                }
                else
                {
                    all.Add(issue);
                }
            }
        }

        // Filtre les `<%-- aspx-lint disable RULE --%>` inline.
        return IssueFilter.ApplyDisableComments(content, all).ToList();
    }
}
