namespace AspxLint.Core.Tests;

public class RegistryTests
{
    private static readonly string[] ExpectedIds =
    {
        "DIR-001",
        "TAG-001", "TAG-002", "TAG-003",
        "ATTR-001", "ATTR-002", "ATTR-003",
        "ASP-001", "ASP-002", "ASP-003", "ASP-004", "ASP-005",
        "WS-001", "WS-002", "WS-003", "WS-004", "WS-005", "WS-006",
        "CHAR-001", "COM-001",
        "SEC-001", "SEC-002", "SEC-003",
        "A11Y-001",
        "STYLE-001", "SCRIPT-001",
        "DOC-001", "FORM-001", "SM-001"
    };

    [Fact]
    public void All_29_rules_registered()
    {
        Assert.Equal(29, RuleRegistry.All.Count);
    }

    [Fact]
    public void All_documented_ids_are_present()
    {
        var registered = RuleRegistry.All.Select(r => r.Id).ToHashSet();
        var missing = ExpectedIds.Except(registered).ToList();
        Assert.True(missing.Count == 0, "Manquantes : " + string.Join(", ", missing));
    }

    [Fact]
    public void No_duplicate_ids()
    {
        var dup = RuleRegistry.All
            .GroupBy(r => r.Id)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();
        Assert.True(dup.Count == 0, "Doublons : " + string.Join(", ", dup));
    }

    [Fact]
    public void Fixable_count_is_19()
    {
        // 19 regles auto-fixables sur 29 totales. Les 5 nouvelles regles (SEC-002
        // a SCRIPT-001) ne sont auto-fixables qu'a une exception (SEC-002 ajoute
        // rel="noopener noreferrer"). Les autres demandent une decision humaine.
        Assert.Equal(19, RuleRegistry.All.Count(r => r.HasFix));
        Assert.Equal(10, RuleRegistry.All.Count(r => !r.HasFix));
    }

    [Theory]
    [MemberData(nameof(AllRules))]
    public void HasFix_false_implies_Fix_returns_null(IRule rule)
    {
        if (rule.HasFix) return; // not in scope
        var ctx = new RuleContext("aspx", "test.aspx");
        Assert.Null(rule.Fix("<%@ Page %>\n<html><body></body></html>\n", ctx));
        Assert.Null(rule.Fix("", ctx));
    }

    [Theory]
    [MemberData(nameof(AllRules))]
    public void HasFix_true_implies_Fix_returns_nonnull(IRule rule)
    {
        if (!rule.HasFix) return;
        var ctx = new RuleContext("aspx", "test.aspx");
        Assert.NotNull(rule.Fix("<%@ Page %>\n<html><body></body></html>\n", ctx));
        Assert.NotNull(rule.Fix("", ctx));
    }

    [Theory]
    [MemberData(nameof(AllRules))]
    public void All_metadata_fields_are_populated(IRule rule)
    {
        Assert.False(string.IsNullOrWhiteSpace(rule.Id));
        Assert.False(string.IsNullOrWhiteSpace(rule.Name));
        Assert.False(string.IsNullOrWhiteSpace(rule.Description));
    }

    public static IEnumerable<object[]> AllRules() =>
        RuleRegistry.All.Select(r => new object[] { r });
}
