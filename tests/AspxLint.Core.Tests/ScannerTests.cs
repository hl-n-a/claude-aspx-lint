using System.Text;

namespace AspxLint.Core.Tests;

public class ScannerTests
{
    [Fact]
    public void Scan_throws_DirectoryNotFoundException_on_missing_dir()
    {
        var bogus = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Assert.Throws<DirectoryNotFoundException>(() =>
            ProjectScanner.Scan(bogus, RuleRegistry.All).ToList());
    }

    [Fact]
    public void Scan_returns_empty_on_empty_dir()
    {
        using var tmp = new TempDir();
        var result = ProjectScanner.Scan(tmp.Path, RuleRegistry.All).ToList();
        Assert.Empty(result);
    }

    [Fact]
    public void Scan_filters_to_aspx_ascx_master_asax()
    {
        using var tmp = new TempDir();
        tmp.WriteFile("a.aspx", "<%@ Page %>\n");
        tmp.WriteFile("b.ascx", "<%@ Control %>\n");
        tmp.WriteFile("c.master", "<%@ Master %>\n");
        tmp.WriteFile("d.asax", "<%@ Application %>\n");
        tmp.WriteFile("e.txt", "ignored");
        tmp.WriteFile("f.cs", "ignored");

        var result = ProjectScanner.Scan(tmp.Path, RuleRegistry.All).ToList();
        Assert.Equal(4, result.Count);
        Assert.Contains(result, f => f.RelativePath.EndsWith("a.aspx"));
        Assert.DoesNotContain(result, f => f.RelativePath.EndsWith("e.txt"));
    }

    [Fact]
    public void Scan_recurses_subdirectories()
    {
        using var tmp = new TempDir();
        tmp.WriteFile("root.aspx", "<%@ Page %>\n");
        tmp.WriteFile("Sub/inner.aspx", "<%@ Page %>\n");
        tmp.WriteFile("Sub/Deeper/leaf.ascx", "<%@ Control %>\n");

        var result = ProjectScanner.Scan(tmp.Path, RuleRegistry.All).ToList();
        Assert.Equal(3, result.Count);
    }

    [Fact]
    public void Scan_LineCount_counts_actual_newlines_plus_one()
    {
        using var tmp = new TempDir();
        tmp.WriteFile("a.aspx", "line1\nline2\nline3\n");

        var f = ProjectScanner.Scan(tmp.Path, RuleRegistry.All).Single();
        Assert.Equal(4, f.LineCount); // 3 \n => 4 lignes (la derniere est vide)
    }

    [Fact]
    public void Scan_LineCount_one_for_single_line_no_newline()
    {
        using var tmp = new TempDir();
        tmp.WriteFile("a.aspx", "no newline");

        var f = ProjectScanner.Scan(tmp.Path, RuleRegistry.All).Single();
        Assert.Equal(1, f.LineCount);
    }

    [Fact]
    public void Scan_RelativePath_is_relative_to_root()
    {
        using var tmp = new TempDir();
        tmp.WriteFile("Sub/x.aspx", "<%@ Page %>\n");

        var f = ProjectScanner.Scan(tmp.Path, RuleRegistry.All).Single();
        Assert.Equal(Path.Combine("Sub", "x.aspx"), f.RelativePath);
    }

    [Fact]
    public void Scan_AbsolutePath_is_real_full_path()
    {
        using var tmp = new TempDir();
        var written = tmp.WriteFile("x.aspx", "<%@ Page %>\n");

        var f = ProjectScanner.Scan(tmp.Path, RuleRegistry.All).Single();
        Assert.Equal(Path.GetFullPath(written), Path.GetFullPath(f.AbsolutePath));
    }

    [Fact]
    public void Scan_collects_issues_from_all_registered_rules()
    {
        using var tmp = new TempDir();
        // Fichier deliberement crade pour declencher plusieurs regles.
        tmp.WriteBytes("bad.aspx",
            new byte[] { 0xEF, 0xBB, 0xBF }
                .Concat(Encoding.UTF8.GetBytes("<html>   \n<body>\n<br>\n</body>\n</html>"))
                .ToArray());

        var f = ProjectScanner.Scan(tmp.Path, RuleRegistry.All).Single();
        var ruleIds = f.Issues.Select(i => i.RuleId).ToHashSet();

        Assert.Contains("WS-005", ruleIds);   // BOM
        Assert.Contains("WS-001", ruleIds);   // espaces en fin (ligne 1)
        Assert.Contains("TAG-001", ruleIds);  // <br> non auto-ferme
        Assert.Contains("WS-004", ruleIds);   // pas de \n final
        Assert.Contains("DIR-001", ruleIds);  // pas de directive @Page
        Assert.Contains("DOC-001", ruleIds);  // pas de DOCTYPE
    }
}
