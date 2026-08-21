using System.Text.Json;
using Aurum.Core;

namespace Aurum.Infrastructure.Windows;

public sealed class JsonServiceStateRepository : IServiceStateRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _filePath;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public JsonServiceStateRepository(string? filePath = null)
    {
        _filePath = filePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Aurum",
            "services.json");
    }

    public async Task<PersistedServiceEntry?> GetAsync(
        string serviceName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);
        await _gate.WaitAsync(cancellationToken);

        try
        {
            var states = await ReadAllUnsafeAsync(cancellationToken);
            return states.GetValueOrDefault(serviceName);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<PersistedServiceEntry>> GetAllAsync(
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);

        try
        {
            return (await ReadAllUnsafeAsync(cancellationToken)).Values
                .OrderByDescending(static state => state.AppliedAtUtc)
                .ToArray();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(PersistedServiceEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        await _gate.WaitAsync(cancellationToken);

        try
        {
            var states = await ReadAllUnsafeAsync(cancellationToken);
            states[entry.ServiceName] = entry;
            await WriteAllUnsafeAsync(states, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task RemoveAsync(string serviceName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);
        await _gate.WaitAsync(cancellationToken);

        try
        {
            var states = await ReadAllUnsafeAsync(cancellationToken);
            if (states.Remove(serviceName))
            {
                await WriteAllUnsafeAsync(states, cancellationToken);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<Dictionary<string, PersistedServiceEntry>> ReadAllUnsafeAsync(
        CancellationToken cancellationToken)
    {
        if (!File.Exists(_filePath))
        {
            return new Dictionary<string, PersistedServiceEntry>(StringComparer.OrdinalIgnoreCase);
        }

        await using var stream = File.OpenRead(_filePath);
        var entries = await JsonSerializer.DeserializeAsync<List<PersistedServiceEntry>>(
            stream,
            JsonOptions,
            cancellationToken);

        var dict = new Dictionary<string, PersistedServiceEntry>(StringComparer.OrdinalIgnoreCase);
        if (entries != null)
        {
            foreach (var entry in entries)
            {
                dict[entry.ServiceName] = entry;
            }
        }

        return dict;
    }

    private async Task WriteAllUnsafeAsync(
        Dictionary<string, PersistedServiceEntry> states,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temporaryPath = $"{_filePath}.tmp";
        await using (var stream = File.Create(temporaryPath))
        {
            await JsonSerializer.SerializeAsync(stream, states.Values, JsonOptions, cancellationToken);
        }

        File.Move(temporaryPath, _filePath, overwrite: true);
    }
}
