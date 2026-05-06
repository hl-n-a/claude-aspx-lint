using System.Text.RegularExpressions;

namespace AspxLint.Core.Rules;

public sealed class Script001InlineHandler : IRule
{
    public string Id => "SCRIPT-001";
    public string Name => "Handler JS inline (onclick, onload, …)";
    public Severity Severity => Severity.Info;
    public string Description =>
        "Les handlers d'evenements inline (onclick=\"…\", onload=\"…\", etc.) melangent JS et HTML, complique l'analyse de securite (CSP), et ne supportent pas plusieurs handlers sur le meme element. Preferer addEventListener depuis un script externe. Pas d'auto-fix automatique — la migration demande de definir un selecteur stable et de deplacer le code.";
    public bool HasFix => false;

    private const string EventList =
        "onclick|ondblclick|onchange|oninput|onsubmit|onload|onunload|onbeforeunload|" +
        "onfocus|onblur|onmouseover|onmouseout|onmousedown|onmouseup|onmousemove|" +
        "onkeydown|onkeyup|onkeypress|onscroll|onresize|onerror|oncontextmenu|" +
        "ondrag|ondrop|ondragstart|ondragend|ondragover|ondragenter|ondragleave|" +
        "ontoggle|onplay|onpause|onended";

    private static readonly Regex DetectRegex = new(
        $@"(?<=[\s])({EventList})\s*=\s*([""'])(?<value>[^""']+)\2",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public IEnumerable<Issue> Detect(string content, string[] lines, RuleContext ctx)
    {
        var (_, maskedLines) = RuleHelpers.MaskAndSplit(content);
        for (int i = 0; i < maskedLines.Length; i++)
        {
            foreach (Match m in DetectRegex.Matches(maskedLines[i]))
            {
                var ev = m.Groups[1].Value;
                yield return new Issue(Id, Name, Severity,
                    i + 1, m.Index + 1,
                    m.Value.Length > 80 ? m.Value[..80] + "…" : m.Value,
                    $"Remplacer par un addEventListener('{ev.Substring(2)}', …) dans un script externe.");
            }
        }
    }

    public string? Fix(string content, RuleContext ctx) => null;
}
