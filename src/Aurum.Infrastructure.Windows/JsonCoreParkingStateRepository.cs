using System.Text.Json;
using Aurum.Core;

namespace Aurum.Infrastructure.Windows;

public sealed class JsonCoreParkingStateRepository : ICoreParkingStateRepository
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    private readonly string _path;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public JsonCoreParkingStateRepository(string? path = null) => _path = path ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Aurum", "core-parking-state.json");

    public async Task<PersistedCoreParkingState?> GetAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(_path)) return null;
            await using var stream = File.OpenRead(_path);
            return await JsonSerializer.DeserializeAsync<PersistedCoreParkingState>(stream, Options, cancellationToken)
                ?? throw new InvalidDataException("Файл состояния парковки ядер Aurum повреждён.");
        }
        finally { _gate.Release(); }
    }

    public async Task SaveAsync(PersistedCoreParkingState state, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var temporary = _path + ".tmp";
            await using (var stream = File.Create(temporary))
            {
                await JsonSerializer.SerializeAsync(stream, state, Options, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }
            File.Move(temporary, _path, true);
        }
        finally { _gate.Release(); }
    }

    public async Task RemoveAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try { File.Delete(_path); }
        finally { _gate.Release(); }
    }
}
