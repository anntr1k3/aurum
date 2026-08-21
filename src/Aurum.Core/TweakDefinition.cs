namespace Aurum.Core;

public enum TweakRisk
{
    Safe,
    Moderate,
    Advanced
}

public enum RestartRequirement
{
    None,
    Explorer,
    SignOut,
    Restart
}

public sealed class TweakDefinition
{
    public TweakDefinition(
        string id,
        string category,
        string name,
        string description,
        string impact,
        TweakRisk risk,
        RestartRequirement restart,
        bool requiresAdministrator,
        params RegistryMutation[] mutations)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(category);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        ArgumentException.ThrowIfNullOrWhiteSpace(impact);
        ArgumentNullException.ThrowIfNull(mutations);

        if (mutations.Length == 0)
        {
            throw new ArgumentException("A tweak must contain at least one mutation.", nameof(mutations));
        }

        if (mutations.Select(static mutation => mutation.Target).Distinct().Count() != mutations.Length)
        {
            throw new ArgumentException("A tweak cannot write the same target twice.", nameof(mutations));
        }

        Id = id;
        Category = category;
        Name = name;
        Description = description;
        Impact = impact;
        Risk = risk;
        Restart = restart;
        RequiresAdministrator = requiresAdministrator;
        Mutations = Array.AsReadOnly(mutations);
    }

    public string Id { get; }

    public string Category { get; }

    public string Name { get; }

    public string Description { get; }

    public string Impact { get; }

    public TweakRisk Risk { get; }

    public RestartRequirement Restart { get; }

    public bool RequiresAdministrator { get; }

    public IReadOnlyList<RegistryMutation> Mutations { get; }
}

public sealed record TweakProfile(string Id, string Name, string Description, IReadOnlySet<string> TweakIds);
