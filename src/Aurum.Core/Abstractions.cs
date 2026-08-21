namespace Aurum.Core;

public interface ISystemStore
{
    Task<RegistrySnapshot> ReadRegistryAsync(RegistryTarget target, CancellationToken cancellationToken = default);

    Task WriteRegistryAsync(
        RegistryTarget target,
        RegistryValue value,
        CancellationToken cancellationToken = default);

    Task DeleteRegistryValueAsync(RegistryTarget target, CancellationToken cancellationToken = default);
}

public interface ITweakStateRepository
{
    Task<PersistedTweakState?> GetAsync(string tweakId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PersistedTweakState>> GetAllAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(PersistedTweakState state, CancellationToken cancellationToken = default);

    Task RemoveAsync(string tweakId, CancellationToken cancellationToken = default);
}
