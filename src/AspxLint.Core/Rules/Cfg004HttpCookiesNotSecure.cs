using System.Text.RegularExpressions;

namespace AspxLint.Core.Rules;

/// <summary>
/// CFG-004 : &lt;httpCookies&gt; sans httpOnlyCookies="true" et/ou requireSSL="true".
/// httpOnlyCookies=true bloque l'acces JS aux cookies (XSS mitigation).
/// requireSSL=true force le flag Secure (MITM mitigation).
/// Manuel : auto-fix risquerait de casser un site non-HTTPS.
/// </summary>
public sealed class Cfg004HttpCookiesNotSecure : IRule
{
    public string Id => "CFG-004";
    public string Name => "httpCookies sans httpOnlyCookies/requireSSL";
    public Severity Severity => Severity.Warning;
    public string Description =>
        "Pour proteger les cookies de session : ajouter httpOnlyCookies=\"true\" (bloque l'acces JS, mitige XSS) et requireSSL=\"true\" (force le flag Secure, mitige MITM). Ne pas activer requireSSL si le site n'est pas servi en HTTPS.";
    public bool HasFix => false;

    private static readonly Regex HttpCookiesTag = new(
        @"<httpCookies\b[^>]*/?>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public IEnumerable<Issue> Detect(string content, string[] lines, RuleContext ctx)
    {
        if (ctx.Ext != "config") yield break;
        for (int i = 0; i < lines.Length; i++)
        {
            foreach (Match m in HttpCookiesTag.Matches(lines[i]))
            {
                var tag = m.Value;
                bool hasHttpOnly = Regex.IsMatch(tag, @"\bhttpOnlyCookies\s*=\s*[""']true[""']", RegexOptions.IgnoreCase);
                bool hasRequireSsl = Regex.IsMatch(tag, @"\brequireSSL\s*=\s*[""']true[""']", RegexOptions.IgnoreCase);
                if (hasHttpOnly && hasRequireSsl) continue;

                var missing = (!hasHttpOnly, !hasRequireSsl) switch
                {
                    (true, true)  => "httpOnlyCookies=\"true\" et requireSSL=\"true\"",
                    (true, false) => "httpOnlyCookies=\"true\"",
                    (false, true) => "requireSSL=\"true\"",
                    _             => ""
                };
                yield return new Issue(Id, Name, Severity,
                    i + 1, m.Index + 1, tag,
                    $"Ajouter {missing}.");
            }
        }
    }

    public string? Fix(string content, RuleContext ctx) => null;
}
