using System.Text.Json;
using System.Text.Json.Serialization;
using AspxLint.Core.Rules;

namespace AspxLint.Core;

/// <summary>
/// Definition JSON d'une regle custom utilisateur, instanciable via ToRule().
/// </summary>
public sealed class CustomRuleDefinition
{
    [JsonPropertyName("id")]            public string Id { get; set; } = "";
    [JsonPropertyName("name")]          public string Name { get; set; } = "";
    [JsonPropertyName("severity")]      public string Severity { get; set; } = "warning";
    [JsonPropertyName("description")]   public string Description { get; set; } = "";
    [JsonPropertyName("pattern")]       public string Pattern { get; set; } = "";
    [JsonPropertyName("hint")]          public string Hint { get; set; } = "";
    [JsonPropertyName("ignoreCase")]    public bool IgnoreCase { get; set; }
    [JsonPropertyName("maskAspBlocks")] public bool MaskAspBlocks { get; set; } = true;

    public CustomRule ToRule()
    {
        if (string.IsNullOrWhiteSpace(Id) || string.IsNullOrWhiteSpace(Pattern))
            throw new ArgumentException("Custom rule needs id + pattern.");
        var sev = Severity.ToLowerInvariant() switch
        {
            "error"   => Core.Severity.Error,
            "info"    => Core.Severity.Info,
            _         => Core.Severity.Warning
        };
        return new CustomRule(
            Id, Name.Length > 0 ? Name : Id, sev,
            Description.Length > 0 ? Description : "Regle custom",
            Pattern,
            Hint.Length > 0 ? Hint : $"Match du pattern {Pattern}",
            IgnoreCase, MaskAspBlocks);
    }
}

/// <summary>
/// Configuration projet chargee depuis un fichier `.aspxlintrc.json`. Le CLI
/// remonte l'arborescence depuis le path scanne pour le trouver.
///
/// Schema JSON :
/// {
///   "ignore": ["**/Generated/**", "**/bin/**"],
///   "rules": {
///     "TAG-003": "off",        // desactive la regle
///     "CHAR-001": "info",      // change la severite (error|warning|info|off)
///     "ASP-005": "warning"
///   }
/// }
/// </summary>
public sealed class AspxLintConfig
{
    [JsonPropertyName("ignore")]
    public List<string> Ignore { get; set; } = new();

    [JsonPropertyName("rules")]
    public Dictionary<string, string> Rules { get; set; } = new();

    [JsonPropertyName("customRules")]
    public List<CustomRuleDefinition> CustomRules { get; set; } = new();

    /// <summary>
    /// Compose la liste des regles a appliquer : les regles built-in + les
    /// custom definies dans la config. Les custom apparaissent apres pour
    /// que leur ordre dans le rapport soit predictible.
    /// </summary>
    public IReadOnlyList<IRule> ResolveRules(IReadOnlyList<IRule> builtIn)
    {
        if (CustomRules.Count == 0) return builtIn;
        var all = new List<IRule>(builtIn.Count + CustomRules.Count);
        all.AddRange(builtIn);
        foreach (var def in CustomRules)
        {
            try { all.Add(def.ToRule()); }
            catch { /* regex invalide ou champs manquants : on saute silencieusement */ }
        }
        return all;
    }

    /// <summary>
    /// Cherche un fichier .aspxlintrc.json en remontant depuis le dossier
    /// donne jusqu'a la racine. Renvoie une config vide si aucun fichier
    /// n'est trouve (cas ou la regle est : "pas de config = comportement par defaut").
    /// </summary>
    public static AspxLintConfig LoadFromOrAbove(string startDir)
    {
        try
        {
            var dir = new DirectoryInfo(startDir);
            while (dir != null)
            {
                var f = Path.Combine(dir.FullName, ".aspxlintrc.json");
                if (File.Exists(f))
                {
                    var json = File.ReadAllText(f);
                    return JsonSerializer.Deserialize<AspxLintConfig>(json,
                        new JsonSerializerOptions
                        {
                            PropertyNameCaseInsensitive = true,
                            ReadCommentHandling = JsonCommentHandling.Skip,
                            AllowTrailingCommas = true
                        })
                        ?? new AspxLintConfig();
                }
                dir = dir.Parent;
            }
        }
        catch { /* JSON invalide, IO, etc. — on ignore et on retombe en defaut */ }
        return new AspxLintConfig();
    }

    /// <summary>
    /// True si le path donne (relatif ou absolu) matche un des patterns d'ignore.
    /// Glob simple : `*` (n'importe quoi sauf `/`), `**` (n'importe quoi
    /// incluant `/`), reste litteral. Pas de `?` ni de `[...]`.
    /// </summary>
    public bool IsIgnored(string relativePath)
    {
        if (Ignore.Count == 0) return false;
        // Normalise les slashes pour la comparaison.
        var normalized = relativePath.Replace('\\', '/');
        foreach (var pattern in Ignore)
        {
            var pat = pattern.Replace('\\', '/');
            if (GlobMatch(pat, normalized)) return true;
        }
        return false;
    }

    /// <summary>
    /// Resout la severite finale d'une regle apres application de la config.
    /// Renvoie null si la regle est desactivee ("off").
    /// </summary>
    public Severity? ResolveSeverity(IRule rule)
    {
        if (!Rules.TryGetValue(rule.Id, out var sev) &&
            !Rules.TryGetValue(rule.Id.ToUpperInvariant(), out sev))
        {
            return rule.Severity;
        }
        return sev.ToLowerInvariant() switch
        {
            "off"     => null,
            "error"   => Severity.Error,
            "warning" => Severity.Warning,
            "info"    => Severity.Info,
            _         => rule.Severity   // valeur inconnue : on garde le defaut
        };
    }

    /// <summary>
    /// Glob match minimal compatible avec les patterns courants : `*` ne matche
    /// pas `/`, `**` matche tout. Implementation par recursion.
    /// </summary>
    private static bool GlobMatch(string pattern, string path)
    {
        return Match(pattern, 0, path, 0);

        static bool Match(string p, int pi, string s, int si)
        {
            while (pi < p.Length)
            {
                if (p[pi] == '*')
                {
                    if (pi + 1 < p.Length && p[pi + 1] == '*')
                    {
                        // ** : skip n'importe quoi
                        pi += 2;
                        if (pi < p.Length && p[pi] == '/') pi++;
                        if (pi == p.Length) return true;
                        for (int k = si; k <= s.Length; k++)
                            if (Match(p, pi, s, k)) return true;
                        return false;
                    }
                    // * : skip jusqu'au prochain '/'
                    pi++;
                    if (pi == p.Length)
                    {
                        // * en fin : matche le reste du segment
                        for (int k = si; k <= s.Length; k++)
                            if (k == s.Length || s[k] == '/') return Match(p, pi, s, k);
                        return false;
                    }
                    for (int k = si; k <= s.Length; k++)
                    {
                        if (Match(p, pi, s, k)) return true;
                        if (k < s.Length && s[k] == '/') break;
                    }
                    return false;
                }
                if (si >= s.Length) return false;
                if (p[pi] != s[si]) return false;
                pi++; si++;
            }
            return si == s.Length;
        }
    }
}
