using System.Text.RegularExpressions;

namespace AspxLint.Core.Rules;

public sealed class Form001FormMissingRunat : IRule
{
    public string Id => "FORM-001";
    public string Name => "Balise <form> sans runat=\"server\" dans une page ASPX";
    public Severity Severity => Severity.Error;
    public string Description =>
        "Une page ASPX qui utilise des controles serveur a besoin d'un <form runat=\"server\"> englobant. Sans cela, le ViewState et les postbacks ne fonctionnent pas.";
    public bool HasFix => true;

    private static readonly Regex MasterPageFile = new(
        @"MasterPageFile\s*=", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex AspControl = new(
        @"<asp:", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Detect sur contenu MASQUE : [^>] basique suffit.
    private static readonly Regex DetectRegex = new(
        @"<form\b([^>]*?)>", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Fix sur contenu BRUT : la regex doit savoir traverser les <%...%>
    // (cas reel : <form action="/<%= x %>/sub">) sinon on insere `runat=`
    // au milieu d'un bloc serveur.
    private static readonly Regex FixRegex = new(
        $@"<form\b({RuleHelpers.TagInnerPattern})>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex RunatPresent = new(
        @"\brunat\s*=\s*[""']?server[""']?", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public IEnumerable<Issue> Detect(string content, string[] lines, RuleContext ctx)
    {
        if (ctx.Ext != "aspx") yield break;
        if (MasterPageFile.IsMatch(content)) yield break;
        if (!AspControl.IsMatch(content)) yield break;

        // Mask <%...%> dans tout le contenu pour ne pas que la regex prenne
        // un `>` de `%>` comme fin de balise <form>.
        var (_, maskedLines) = RuleHelpers.MaskAndSplit(content);

        for (int i = 0; i < maskedLines.Length; i++)
        {
            foreach (Match m in DetectRegex.Matches(maskedLines[i]))
            {
                if (RunatPresent.IsMatch(m.Groups[1].Value)) continue;
                yield return new Issue(Id, Name, Severity,
                    i + 1, m.Index + 1, m.Value,
                    "Ajouter runat=\"server\" a la balise <form>.");
            }
        }
    }

    public string? Fix(string content, RuleContext ctx) =>
        RuleHelpers.FixOutsideAspBlocks(content, FixRegex, m =>
        {
            var attrs = m.Groups[1].Value;
            if (RunatPresent.IsMatch(attrs)) return m.Value;
            var cleanAttrs = attrs.TrimEnd();
            return $"<form{cleanAttrs} runat=\"server\">";
        });
}
