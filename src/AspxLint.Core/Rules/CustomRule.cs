using System.Text.RegularExpressions;

namespace AspxLint.Core.Rules;

/// <summary>
/// Regle definie en JSON par l'utilisateur dans `.aspxlintrc.json` :
/// <code>
/// "customRules": [{
///   "id": "CUSTOM-001",
///   "name": "Pas de TODO en commentaire",
///   "severity": "warning",
///   "description": "...",
///   "pattern": "TODO[: ]",
///   "hint": "Resoudre ou ouvrir un ticket avant de merger.",
///   "maskAspBlocks": true,
///   "ignoreCase": false
/// }]
/// </code>
/// Pas d'auto-fix possible (la transformation serait arbitraire). On expose
/// uniquement la detection : un Regex applique sur chaque ligne, avec masking
/// optionnel des blocs &lt;%...%&gt;.
/// </summary>
public sealed class CustomRule : IRule
{
    public string Id { get; }
    public string Name { get; }
    public Severity Severity { get; }
    public string Description { get; }
    public bool HasFix => false;

    private readonly Regex _regex;
    private readonly string _hint;
    private readonly bool _maskAspBlocks;

    public CustomRule(
        string id, string name, Severity severity, string description,
        string pattern, string hint,
        bool ignoreCase = false, bool maskAspBlocks = true)
    {
        Id = id;
        Name = name;
        Severity = severity;
        Description = description;
        _hint = hint;
        _maskAspBlocks = maskAspBlocks;
        var opts = RegexOptions.Compiled;
        if (ignoreCase) opts |= RegexOptions.IgnoreCase;
        _regex = new Regex(pattern, opts);
    }

    public IEnumerable<Issue> Detect(string content, string[] lines, RuleContext ctx)
    {
        var (_, scanLines) = _maskAspBlocks
            ? RuleHelpers.MaskAndSplit(content)
            : (content, lines);

        for (int i = 0; i < scanLines.Length; i++)
        {
            foreach (Match m in _regex.Matches(scanLines[i]))
            {
                var snippet = m.Value.Length > 60 ? m.Value[..60] + "…" : m.Value;
                yield return new Issue(Id, Name, Severity,
                    i + 1, m.Index + 1, snippet, _hint);
            }
        }
    }

    public string? Fix(string content, RuleContext ctx) => null;
}
