using System.Text.RegularExpressions;

namespace AspxLint.Core.Rules;

/// <summary>
/// CFG-006 : connectionString avec password=... en clair dans Web.config.
/// Idealement on utilise Integrated Security=true (auth Windows) ou un
/// provider externe (Azure Key Vault, AWS Secrets Manager). A defaut, chiffrer
/// la section connectionStrings avec aspnet_regiis -pe.
/// Manuel : retirer le mot de passe necessite un changement d'infra.
/// </summary>
public sealed class Cfg006ConnectionStringPlaintext : IRule
{
    public string Id => "CFG-006";
    public string Name => "Mot de passe en clair dans connectionString";
    public Severity Severity => Severity.Warning;
    public string Description =>
        "Un connectionString avec \"password=...\" expose les credentials a quiconque a acces au depot ou au filesystem du serveur. Utiliser Integrated Security, un secret manager, ou chiffrer la section avec aspnet_regiis -pe.";
    public bool HasFix => false;

    // Detecte password=valeur (au moins 1 char qui n'est pas vide, ;, fin de ligne).
    // Aussi pwd= (alias court). Skippe les valeurs vides "password=;".
    private static readonly Regex DetectRegex = new(
        @"\b(?:password|pwd)\s*=\s*[^;""'<\s][^;""'<]*",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex ConnectionStringContext = new(
        @"<add\b[^>]*\bconnectionString\s*=",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public IEnumerable<Issue> Detect(string content, string[] lines, RuleContext ctx)
    {
        if (ctx.Ext != "config") yield break;
        for (int i = 0; i < lines.Length; i++)
        {
            // On ne fire que sur les lignes qui sont une connectionString (pour
            // ne pas matcher un password= dans un commentaire ou autre).
            if (!ConnectionStringContext.IsMatch(lines[i])) continue;
            foreach (Match m in DetectRegex.Matches(lines[i]))
            {
                yield return new Issue(Id, Name, Severity,
                    i + 1, m.Index + 1, m.Value,
                    "Utiliser Integrated Security=true ou chiffrer cette section avec aspnet_regiis -pe.");
            }
        }
    }

    public string? Fix(string content, RuleContext ctx) => null;
}
