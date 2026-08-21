# Contributing

Thank you for helping make Windows customization more transparent.

## Adding a tweak

1. Add a declarative definition to `BuiltInTweakCatalog`.
2. Give it a stable, namespaced identifier.
3. Describe the user-visible effect and side effects without performance claims.
4. Use the lowest accurate risk level.
5. Add an engine self-test if new behavior is introduced.
6. Verify apply and revert on every supported Windows build.

A tweak must not execute arbitrary shell code. New operation types require a
separate security review and transactional rollback support.
