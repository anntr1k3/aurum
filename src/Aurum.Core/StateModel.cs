namespace Aurum.Core;

public sealed record RegistryStateEntry(RegistryTarget Target, RegistrySnapshot OriginalValue);

public sealed record PersistedTweakState(
    string TweakId,
    DateTimeOffset AppliedAtUtc,
    IReadOnlyList<RegistryStateEntry> Entries,
    int SchemaVersion = 1);

public enum TweakStateKind
{
    Available,
    AlreadyConfigured,
    Applied,
    Drifted
}

public sealed record TweakEvaluation(
    TweakDefinition Definition,
    TweakStateKind State,
    bool MatchesDesired,
    DateTimeOffset? AppliedAtUtc);
