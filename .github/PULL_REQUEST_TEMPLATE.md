<!--
Merci pour la PR ! Quelques info pour faciliter la revue :
- Pour une nouvelle regle, ouvre d'abord une issue "New lint rule" pour qu'on
  s'aligne sur l'ID et la severite avant le code.
- Garde la PR focalisee : une regle, un fix, une feature par PR de preference.
-->

## Quoi

<!-- Une ou deux phrases sur le changement. -->

## Pourquoi

<!-- Le contexte : bug fix, feature, regle, refacto, perf... Lie l'issue si applicable (`Closes #123`). -->

## Tests

<!-- Comment tu as valide ? Tests automatises ajoutes ? Repro manuel ? -->

## Checklist

- [ ] `dotnet test --filter "Category!=RequiresDesktop"` passe localement
- [ ] J'ai ajoute des tests xUnit pour les nouveaux comportements
- [ ] Pour une nouvelle regle : entree dans `RuleRegistry`, fixture dans
      `tests/fixtures`, idempotence verifiee
- [ ] J'ai mis a jour `CHANGELOG.md` (section `## [Unreleased]`)
- [ ] J'ai mis a jour `README.md` si l'API publique / le tableau des regles change
