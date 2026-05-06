using System.Text;
using System.Text.RegularExpressions;

namespace AspxLint.Core;

/// <summary>
/// Helpers partages entre regles. Centralise notamment la gestion des blocs
/// serveur &lt;%...%&gt; pour eviter les faux-positifs : les regles HTML ne
/// doivent pas inspecter le code C# embarque.
/// </summary>
internal static class RuleHelpers
{
    /// <summary>
    /// Tous les blocs &lt;%...%&gt; : commentaires serveur &lt;%-- --%&gt;,
    /// directives &lt;%@ %&gt;, expressions &lt;%= %&gt; / &lt;%# %&gt; / &lt;%: %&gt;,
    /// et code statement &lt;% %&gt;. Multi-ligne par construction (DOTALL via [\s\S]).
    ///
    /// Les commentaires &lt;%-- --%&gt; sont matches en priorite (alternation
    /// tente la 1re branche d'abord) car ils peuvent CONTENIR des &lt;%= %&gt;
    /// litteraux (ASP ne parse pas l'interieur d'un commentaire). Sans cette
    /// priorisation, &lt;%-- ... &lt;%= x %&gt; ... --%&gt; serait masque jusqu'au
    /// premier `%&gt;` (au milieu de l'interpolation), laissant le reste expose.
    /// </summary>
    public static readonly Regex AspBlock =
        new(@"<%--[\s\S]*?--%>|<%[\s\S]*?%>", RegexOptions.Compiled);

    /// <summary>
    /// Remplace tous les blocs &lt;%...%&gt; par des espaces de meme longueur,
    /// MAIS en preservant les newlines internes. Sans ca, un bloc multi-ligne
    /// (ex: &lt;% ... %&gt; sur 18 lignes) fusionne en une seule "ligne" apres
    /// Split, et tout l'index de lignes apres le bloc devient incoherent —
    /// les detections continuent a tirer sur du C# embarque qui aurait du etre
    /// masque. Ce fut un faux-positif sur des fichiers comme Demande/Index.aspx
    /// ou les `&amp;&amp;` du C# generaient des CHAR-001.
    /// </summary>
    public static string MaskAspBlocks(string content)
    {
        return AspBlock.Replace(content, m =>
        {
            var src = m.Value;
            var buf = new char[src.Length];
            for (int i = 0; i < src.Length; i++)
            {
                var c = src[i];
                buf[i] = (c == '\n' || c == '\r') ? c : ' ';
            }
            return new string(buf);
        });
    }

    /// <summary>
    /// Variante qui retourne aussi les lignes pre-decoupees (pour les regles
    /// qui iterent ligne par ligne).
    /// </summary>
    public static (string MaskedContent, string[] MaskedLines) MaskAndSplit(string content)
    {
        var masked = MaskAspBlocks(content);
        var lines = masked.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        return (masked, lines);
    }

    private static readonly Regex ScriptBlock = new(
        @"<script\b[^>]*>[\s\S]*?<\/script>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex StyleBlock = new(
        @"<style\b[^>]*>[\s\S]*?<\/style>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex HtmlComment = new(
        @"<!--[\s\S]*?-->", RegexOptions.Compiled);

    /// <summary>
    /// Comme MaskAspBlocks, mais en plus on masque les blocs &lt;script&gt;,
    /// &lt;style&gt; et les commentaires HTML &lt;!-- --&gt;. Utile pour les regles
    /// qui detectent du contenu HTML (CHAR-001, TAG-003, etc.) et n'ont aucune
    /// raison d'inspecter du JS / CSS / commentaires.
    /// Toutes les substitutions preservent les newlines pour ne pas decaler
    /// l'index de lignes.
    /// </summary>
    public static (string MaskedContent, string[] MaskedLines) MaskAndSplitFull(string content)
    {
        var masked = MaskAspBlocks(content);
        masked = ReplacePreservingNewlines(masked, ScriptBlock);
        masked = ReplacePreservingNewlines(masked, StyleBlock);
        masked = ReplacePreservingNewlines(masked, HtmlComment);
        var lines = masked.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        return (masked, lines);
    }

    private static string ReplacePreservingNewlines(string content, Regex re)
    {
        return re.Replace(content, m =>
        {
            var src = m.Value;
            var buf = new char[src.Length];
            for (int i = 0; i < src.Length; i++)
            {
                var c = src[i];
                buf[i] = (c == '\n' || c == '\r') ? c : ' ';
            }
            return new string(buf);
        });
    }

    /// <summary>
    /// Applique une transformation aux segments NON-asp d'un contenu.
    /// Les blocs &lt;%...%&gt; sont laisses intacts (utile pour les fixes
    /// qui ne doivent pas toucher au code serveur).
    /// </summary>
    public static string ApplyOutsideAspBlocks(string content, Func<string, string> transform)
    {
        var sb = new StringBuilder(content.Length + 64);
        int last = 0;
        foreach (Match m in AspBlock.Matches(content))
        {
            sb.Append(transform(content[last..m.Index]));
            sb.Append(m.Value);
            last = m.Index + m.Length;
        }
        sb.Append(transform(content[last..]));
        return sb.ToString();
    }

    /// <summary>
    /// Pattern interne pour le contenu d'une balise HTML qui peut contenir des
    /// blocs &lt;%...%&gt; en attribut (ex: &lt;input value="&lt;%= x %&gt;"&gt;).
    /// L'inclure dans la regex au lieu de [^&gt;]*? evite que le `&gt;` du `%&gt;`
    /// soit pris pour la fin du tag.
    ///
    /// Le `(?&gt;...)` rend l'alternative server-block ATOMIQUE : une fois qu'on
    /// a matche un bloc &lt;%...%&gt;, le moteur ne backtrackera pas pour tenter
    /// de le re-matcher char-par-char via [^&gt;]. Sans cet atomic group, le regex
    /// trouve apres backtracking le `&gt;` du `%&gt;` comme fin de tag — bug visible
    /// sur `&lt;input value="&lt;%= x %&gt;"&gt;` qui devenait `&lt;input value="&lt;%= x %&gt; &gt;`.
    /// </summary>
    public const string TagInnerPattern = @"(?>(?:<%[\s\S]*?%>|[^>]))*?";

    /// <summary>
    /// Applique &lt;paramref name="evaluator"/&gt; aux matches de &lt;paramref name="regex"/&gt;
    /// dans le contenu, MAIS en sautant ceux dont la position de depart est a
    /// l'interieur d'un bloc &lt;%...%&gt;. Combine avec une regex qui utilise
    /// &lt;see cref="TagInnerPattern"/&gt; pour traverser les attributs contenant
    /// des blocs serveur.
    /// </summary>
    public static string FixOutsideAspBlocks(string content, Regex regex, MatchEvaluator evaluator)
    {
        var blocks = AspBlock.Matches(content)
            .Select(m => (start: m.Index, end: m.Index + m.Length))
            .ToArray();

        bool InsideBlock(int pos)
        {
            foreach (var (start, end) in blocks)
                if (pos >= start && pos < end) return true;
            return false;
        }

        var sb = new StringBuilder(content.Length + 64);
        int last = 0;
        foreach (Match m in regex.Matches(content))
        {
            if (InsideBlock(m.Index)) continue;
            sb.Append(content, last, m.Index - last);
            sb.Append(evaluator(m));
            last = m.Index + m.Length;
        }
        sb.Append(content, last, content.Length - last);
        return sb.ToString();
    }
}
