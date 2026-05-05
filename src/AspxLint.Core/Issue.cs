namespace AspxLint.Core;

public sealed record Issue(
    string RuleId,
    string RuleName,
    Severity Severity,
    int Line,
    int Col,
    string? Snippet,
    string? Hint
);
