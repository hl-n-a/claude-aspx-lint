using AspxLint.Core.Rules;

namespace AspxLint.Core;

public static class RuleRegistry
{
    public static readonly IReadOnlyList<IRule> All = new IRule[]
    {
        // Directives
        new Dir001PageDirective(),

        // Balises
        new Tag001VoidElementsXhtml(),
        new Tag002TagCase(),
        new Tag003UnbalancedTags(),

        // Attributs
        new Attr001MissingQuotes(),
        new Attr002MixedQuotes(),
        new Attr003DuplicateAttribute(),

        // ASP.NET controls
        new Asp001ControlMissingRunat(),
        new Asp002DuplicateControlId(),
        new Asp003ContentPlaceHolderMissingId(),
        new Asp004ContentMissingPlaceHolderId(),
        new Asp005ServerTagSpaces(),

        // Whitespace
        new Ws001TrailingWhitespace(),
        new Ws002MixedIndent(),
        new Ws003ConsecutiveBlankLines(),
        new Ws004FinalNewline(),
        new Ws005Bom(),
        new Ws006TrailingBlankLines(),

        // Encodage
        new Char001UnescapedAmpersand(),

        // Commentaires
        new Com001NestedDashes(),

        // Securite
        new Sec001ViewStateMacFalse(),
        new Sec002TargetBlankNoopener(),
        new Sec003LocalUrl(),

        // Accessibilite
        new A11y001ImgWithoutAlt(),

        // Style / scripts inline
        new Style001InlineStyle(),
        new Script001InlineHandler(),

        // DOCTYPE
        new Doc001MissingDoctype(),

        // Form runat
        new Form001FormMissingRunat(),

        // Script Manager
        new Sm001MultipleScriptManager(),
    };
}
