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

## Language of error messages

The interface is Russian, and the status bar prints exception messages inline, so any
exception a user can trigger must carry a Russian message. Mixing languages produced
lines like `Диагностика завершилась с ошибкой: The selected power plan is already
active.`

Guard clauses that assert a programmer contract are the exception and stay in English:
`ArgumentException` for a duplicate cleanup category or a tweak with no mutations
reports a bug in the calling code, not a condition the user can act on.

## Trusting persisted state

State files live in the user's profile, where any process running as that user can edit
them without elevation, while the operation that reads them usually runs elevated.
Treat a location or a service name read from a state file as untrusted input:

- Tweak revert checks every target in the snapshot against the targets the tweak
  declares in the catalog.
- Service repair refuses names in `ServiceAnalyzer`'s protected set, and service revert
  writes nothing unless the service is currently disabled.

A new manager that reads a path, a name or a device identifier out of persisted state
needs an equivalent check before it writes anything.
