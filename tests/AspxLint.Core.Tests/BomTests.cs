using System.Text;

namespace AspxLint.Core.Tests;

public class BomTests
{
    private static readonly byte[] Bom = { 0xEF, 0xBB, 0xBF };

    private static IRule Ws005 = RuleRegistry.All.Single(r => r.Id == "WS-005");

    [Fact]
    public void Detect_fires_on_BOM()
    {
        var content = "﻿<%@ Page %>\n";
        var lines = content.Split('\n');
        var issues = Ws005.Detect(content, lines, new RuleContext("aspx", "x.aspx")).ToList();
        Assert.Single(issues);
        Assert.Equal(1, issues[0].Line);
        Assert.Equal(1, issues[0].Col);
    }

    [Fact]
    public void Detect_silent_when_no_BOM()
    {
        var content = "<%@ Page %>\n";
        var lines = content.Split('\n');
        var issues = Ws005.Detect(content, lines, new RuleContext("aspx", "x.aspx")).ToList();
        Assert.Empty(issues);
    }

    [Fact]
    public void Fix_strips_only_leading_BOM()
    {
        var input = "﻿abc﻿def"; // BOM en tete + un BOM "litteral" au milieu
        var fixed1 = Ws005.Fix(input, new RuleContext("aspx", "x.aspx"));
        Assert.Equal("abc﻿def", fixed1); // garde le BOM du milieu intact
    }

    [Fact]
    public void Scanner_preserves_BOM_in_returned_content()
    {
        using var tmp = new TempDir();
        var bytes = Bom.Concat(Encoding.UTF8.GetBytes("<%@ Page %>\n")).ToArray();
        tmp.WriteBytes("page.aspx", bytes);

        var scanned = ProjectScanner.Scan(tmp.Path, RuleRegistry.All).Single();
        Assert.Equal('﻿', scanned.Content[0]);
        Assert.StartsWith("﻿<%@ Page", scanned.Content);
    }

    [Fact]
    public void Scanner_does_not_inject_BOM_when_absent()
    {
        using var tmp = new TempDir();
        tmp.WriteBytes("page.aspx", Encoding.UTF8.GetBytes("<%@ Page %>\n"));

        var scanned = ProjectScanner.Scan(tmp.Path, RuleRegistry.All).Single();
        Assert.NotEqual('﻿', scanned.Content[0]);
    }

    [Fact]
    public void Roundtrip_preserves_BOM_via_UTF8_GetBytes()
    {
        // Simule ce que /api/save fait : Encoding.UTF8.GetBytes(content).
        // Si le contenu commence par ﻿, on doit retrouver EF BB BF en sortie.
        var content = "﻿abc";
        var bytes = Encoding.UTF8.GetBytes(content);
        Assert.Equal(0xEF, bytes[0]);
        Assert.Equal(0xBB, bytes[1]);
        Assert.Equal(0xBF, bytes[2]);
    }

    [Fact]
    public void WS005_emits_no_issue_after_fix()
    {
        var content = "﻿abc";
        var ctx = new RuleContext("aspx", "x.aspx");
        var fixed1 = Ws005.Fix(content, ctx)!;
        var lines = fixed1.Split('\n');
        Assert.Empty(Ws005.Detect(fixed1, lines, ctx));
    }
}
