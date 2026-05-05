namespace AspxLint.Core.Tests;

/// <summary>
/// Pour chaque regle auto-fixable : entree fautive, on verifie que
///   1) le fix change effectivement le contenu,
///   2) appliquer le fix une 2e fois ne le change plus (idempotence),
///   3) le fix resout le ou les issues detectees au depart.
///
/// Conformement a la promesse de CLAUDE.md :
///   "exécuter le fix deux fois d'affilée doit produire le même résultat
///    que l'exécuter une fois."
/// </summary>
public class IdempotenceTests
{
    public static IEnumerable<object[]> FixCases() => new[]
    {
        // ruleId, ext, badInput
        new object[] { "WS-001", "aspx", "abc   \nok\n" },
        new object[] { "WS-002", "aspx", "\tline1\n    line2\n" },
        new object[] { "WS-003", "aspx", "a\n\n\n\n\nb\n" },
        new object[] { "WS-004", "aspx", "abc" },
        new object[] { "WS-005", "aspx", "﻿abc\n" },
        new object[] { "TAG-001", "aspx", "<br>\n" },
        new object[] { "TAG-002", "aspx", "<DIV>\n<BODY>\n</BODY>\n</DIV>\n" },
        new object[] { "ATTR-001", "aspx", "<input type=text id=x>\n" },
        new object[] { "ATTR-002", "aspx", "<a href='x.html'>link</a>\n" },
        new object[] { "ASP-001", "aspx", "<asp:Label Text=\"hi\" />\n" },
        new object[] { "ASP-005", "aspx", "<%=DateTime.Now%>\n" },
        new object[] { "DIR-001", "aspx", "<html>\n<body></body>\n</html>\n" },
        new object[] { "DOC-001", "aspx", "<%@ Page Language=\"C#\" %>\n<html>\n<body></body>\n</html>\n" },
        new object[] { "FORM-001", "aspx", "<%@ Page Language=\"C#\" %>\n<asp:Label runat=\"server\" />\n<form>\n</form>\n" },
        new object[] { "SEC-001", "aspx", "<%@ Page EnableViewStateMac=\"false\" %>\n" },
    };

    [Theory]
    [MemberData(nameof(FixCases))]
    public void Fix_changes_bad_input(string ruleId, string ext, string badInput)
    {
        var rule = GetRule(ruleId);
        var ctx = new RuleContext(ext, "test." + ext);
        var fixed1 = rule.Fix(badInput, ctx);
        Assert.NotNull(fixed1);
        Assert.NotEqual(badInput, fixed1);
    }

    [Theory]
    [MemberData(nameof(FixCases))]
    public void Fix_is_idempotent(string ruleId, string ext, string badInput)
    {
        var rule = GetRule(ruleId);
        var ctx = new RuleContext(ext, "test." + ext);
        var fixed1 = rule.Fix(badInput, ctx)!;
        var fixed2 = rule.Fix(fixed1, ctx)!;
        Assert.Equal(fixed1, fixed2);
    }

    [Theory]
    [MemberData(nameof(FixCases))]
    public void Fix_resolves_detected_issues_for_that_rule(string ruleId, string ext, string badInput)
    {
        var rule = GetRule(ruleId);
        var ctx = new RuleContext(ext, "test." + ext);

        var beforeIssues = Detect(rule, badInput, ctx);
        Assert.NotEmpty(beforeIssues); // sanity : la fixture doit declencher la regle

        var fixed1 = rule.Fix(badInput, ctx)!;
        var afterIssues = Detect(rule, fixed1, ctx);
        Assert.Empty(afterIssues);
    }

    private static IRule GetRule(string id) =>
        RuleRegistry.All.Single(r => r.Id == id);

    private static List<Issue> Detect(IRule rule, string content, RuleContext ctx)
    {
        var lines = content.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        return rule.Detect(content, lines, ctx).ToList();
    }
}
