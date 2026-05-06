using System.Text.RegularExpressions;

namespace AspxLint.Core.Rules;

public sealed class Sec003LocalUrl : IRule
{
    public string Id => "SEC-003";
    public string Name => "URL hardcodee vers un environnement local";
    public Severity Severity => Severity.Warning;
    public string Description =>
        "Detecte les URLs hardcodees vers localhost / 127.0.0.1 / 0.0.0.0 / *.local / *.test, ou avec un port explicite (:5000, :8080, etc.). Ces URLs ne fonctionnent pas en production et indiquent souvent un oubli dev->prod. Pas d'auto-fix : la bonne URL depend de la config du projet.";
    public bool HasFix => false;

    /// <summary>
    /// Detecte les URLs problematiques dans les attributs href/src/action et les
    /// litteraux de chaine. Patterns matches :
    ///   http(s)://localhost(:port)/...
    ///   http(s)://127.0.0.1(:port)/...
    ///   http(s)://0.0.0.0(:port)/...
    ///   http(s)://host.local(:port)/...
    ///   http(s)://host.test(:port)/...
    /// </summary>
    private static readonly Regex DetectRegex = new(
        @"https?://(?:localhost|127\.0\.0\.1|0\.0\.0\.0|[a-zA-Z0-9\-]+\.(?:local|test|internal|dev))(?::\d+)?(?:/[^\s""']*)?",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public IEnumerable<Issue> Detect(string content, string[] lines, RuleContext ctx)
    {
        // On ne masque pas les <% %> ici : un URL dev hardcode dans une chaine
        // C# (<% var u = "http://localhost:5000"; %>) est tout aussi probleme-
        // matique en prod. On masque juste les commentaires HTML/style/script
        // pour eviter les faux positifs sur les exemples documentaires.
        var (_, maskedLines) = RuleHelpers.MaskAndSplitFull(content);
        for (int i = 0; i < maskedLines.Length; i++)
        {
            foreach (Match m in DetectRegex.Matches(maskedLines[i]))
            {
                yield return new Issue(Id, Name, Severity,
                    i + 1, m.Index + 1, m.Value,
                    "Remplacer par une URL relative ou par un parametre de config.");
            }
        }
    }

    public string? Fix(string content, RuleContext ctx) => null;
}
