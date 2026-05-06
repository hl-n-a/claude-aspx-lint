using System.Text;
using System.Xml;
using AspxLint.Core;

namespace AspxLint.Cli;

/// <summary>
/// Format JUnit XML — consomme par Jenkins, GitLab CI, Azure DevOps, etc.
/// Chaque fichier devient un &lt;testsuite&gt;, chaque issue un &lt;testcase&gt;
/// avec un &lt;failure&gt; (pour les errors) ou &lt;skipped&gt; (warning/info).
/// </summary>
internal static class JunitFormatter
{
    public static async Task WriteAsync(IReadOnlyList<ScannedFile> files, int totalIssues, TextWriter o)
    {
        var settings = new XmlWriterSettings
        {
            Async = true,
            Indent = true,
            IndentChars = "  ",
            Encoding = new UTF8Encoding(false)
        };
        await using var xw = XmlWriter.Create(o, settings);
        await xw.WriteStartDocumentAsync();
        await xw.WriteStartElementAsync(null, "testsuites", null);
        await xw.WriteAttributeStringAsync(null, "name", null, "aspx-lint");
        await xw.WriteAttributeStringAsync(null, "tests", null, totalIssues.ToString());
        await xw.WriteAttributeStringAsync(null, "failures", null,
            files.Sum(f => f.Issues.Count(i => i.Severity == Severity.Error)).ToString());

        foreach (var f in files)
        {
            if (f.Issues.Count == 0) continue;
            await xw.WriteStartElementAsync(null, "testsuite", null);
            await xw.WriteAttributeStringAsync(null, "name", null, f.RelativePath);
            await xw.WriteAttributeStringAsync(null, "tests", null, f.Issues.Count.ToString());
            await xw.WriteAttributeStringAsync(null, "failures", null,
                f.Issues.Count(i => i.Severity == Severity.Error).ToString());

            foreach (var i in f.Issues)
            {
                await xw.WriteStartElementAsync(null, "testcase", null);
                await xw.WriteAttributeStringAsync(null, "name", null,
                    $"{i.RuleId} L{i.Line}:{i.Col}");
                await xw.WriteAttributeStringAsync(null, "classname", null, f.RelativePath);

                if (i.Severity == Severity.Error)
                {
                    await xw.WriteStartElementAsync(null, "failure", null);
                    await xw.WriteAttributeStringAsync(null, "type", null, i.RuleId);
                    await xw.WriteAttributeStringAsync(null, "message", null, i.Hint);
                    await xw.WriteStringAsync($"{i.RuleName}\n{i.Snippet}");
                    await xw.WriteEndElementAsync();
                }
                else
                {
                    // Warnings/info -> skipped (ne fait pas echouer la suite mais
                    // reste visible dans Jenkins/GitLab).
                    await xw.WriteStartElementAsync(null, "skipped", null);
                    await xw.WriteAttributeStringAsync(null, "message", null,
                        $"[{i.Severity.ToString().ToLowerInvariant()}] {i.RuleId}: {i.Hint}");
                    await xw.WriteEndElementAsync();
                }

                await xw.WriteEndElementAsync();   // </testcase>
            }

            await xw.WriteEndElementAsync();   // </testsuite>
        }

        await xw.WriteEndElementAsync();   // </testsuites>
        await xw.FlushAsync();
    }
}
