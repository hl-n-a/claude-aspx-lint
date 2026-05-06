using System.Text.Json;
using AspxLint.Core;

namespace AspxLint.Cli;

/// <summary>
/// Format CodeClimate JSON — consomme par GitLab Code Quality (cf.
/// https://docs.gitlab.com/ee/ci/testing/code_quality.html). C'est un
/// tableau JSON d'issues, ou chaque issue a une `fingerprint` SHA1 stable
/// pour que GitLab puisse tracker les diffs entre runs.
/// </summary>
internal static class CodeClimateFormatter
{
    public static async Task WriteAsync(IReadOnlyList<ScannedFile> files, TextWriter o)
    {
        var entries = new List<object>();
        foreach (var f in files)
        {
            foreach (var i in f.Issues)
            {
                var fingerprint = MakeFingerprint(f.RelativePath, i);
                entries.Add(new
                {
                    type = "issue",
                    check_name = i.RuleId,
                    description = $"[{i.RuleId}] {i.RuleName} : {i.Hint}",
                    categories = new[] { CategoryFor(i.RuleId) },
                    location = new
                    {
                        path = f.RelativePath.Replace('\\', '/'),
                        lines = new { begin = i.Line }
                    },
                    severity = SeverityFor(i.Severity),
                    fingerprint
                });
            }
        }
        // CodeClimate attend du JSON pretty-printed avec une newline finale.
        var json = JsonSerializer.Serialize(entries,
            new JsonSerializerOptions { WriteIndented = true });
        await o.WriteAsync(json);
        await o.WriteLineAsync();
    }

    /// <summary>
    /// Severite mappee aux 5 niveaux de CodeClimate :
    /// info, minor, major, critical, blocker.
    /// </summary>
    private static string SeverityFor(Severity s) => s switch
    {
        Severity.Error   => "major",
        Severity.Warning => "minor",
        Severity.Info    => "info",
        _                => "info"
    };

    /// <summary>
    /// Categorisation : voir https://github.com/codeclimate/spec/blob/master/SPEC.md#categories
    /// On reste dans les categories standard pour eviter les warnings GitLab.
    /// </summary>
    private static string CategoryFor(string ruleId) => ruleId switch
    {
        var s when s.StartsWith("SEC-")  => "Security",
        var s when s.StartsWith("A11Y-") => "Compatibility",
        var s when s.StartsWith("WS-")   => "Style",
        "STYLE-001"                      => "Style",
        "SCRIPT-001"                     => "Style",
        "DOC-001" or "FORM-001"          => "Bug Risk",
        "TAG-003" or "ATTR-003"          => "Bug Risk",
        "ASP-002" or "DIR-001"           => "Bug Risk",
        _                                => "Style"
    };

    /// <summary>
    /// Fingerprint SHA1 stable pour tracker une issue d'un run a l'autre.
    /// Idealement on n'inclurait pas la ligne (sinon une issue qui se decale
    /// d'une ligne se duplique), mais inclure le snippet aide a desambiguiser
    /// 2 occurrences de la meme rule sur la meme ligne. Compromis :
    /// path + ruleId + line + snippet hash.
    /// </summary>
    private static string MakeFingerprint(string path, Issue i)
    {
        var input = $"{path}|{i.RuleId}|{i.Line}|{i.Snippet}";
        var bytes = System.Text.Encoding.UTF8.GetBytes(input);
        var hash = System.Security.Cryptography.SHA1.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
