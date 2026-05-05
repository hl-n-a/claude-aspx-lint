namespace AspxLint.Core;

public interface IRule
{
    string Id { get; }
    string Name { get; }
    Severity Severity { get; }
    string Description { get; }
    bool HasFix { get; }

    IEnumerable<Issue> Detect(string content, string[] lines, RuleContext ctx);

    string? Fix(string content, RuleContext ctx);
}
