using System.Text.Json;
using Aurum.Core;

namespace Aurum.Infrastructure.Windows;

public sealed class JsonNetworkTuningStateRepository : INetworkTuningStateRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _filePath;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public JsonNetworkTuningStateRepository(string? filePath = null)
    {
        _filePath = filePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Aurum",
            "network_tuning.json");
    }

    public async Task<PersistedNetworkAdapterTuningState?> GetAsync(
        string adapterName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(adapterName);
        await _gate.WaitAsync(cancellationToken);

        try
        {
            var states = await ReadAllUnsafeAsync(cancellationToken);
            return states.GetValueOrDefault(adapterName);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(
        PersistedNetworkAdapterTuningState state,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        await _gate.WaitAsync(cancellationToken);

        try
        {
            var states = await ReadAllUnsafeAsync(cancellationToken);
            states[state.AdapterName] = state;
            await WriteAllUnsafeAsync(states, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task RemoveAsync(
        string adapterName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(adapterName);
        await _gate.WaitAsync(cancellationToken);

        try
        {
            var states = await ReadAllUnsafeAsync(cancellationToken);
            if (states.Remove(adapterName))
            {
                await WriteAllUnsafeAsync(states, cancellationToken);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<Dictionary<string, PersistedNetworkAdapterTuningState>> ReadAllUnsafeAsync(
        CancellationToken cancellationToken)
    {
        if (!File.Exists(_filePath))
        {
            return new Dictionary<string, PersistedNetworkAdapterTuningState>(StringComparer.OrdinalIgnoreCase);
        }

        await using var stream = File.OpenRead(_filePath);
        var entries = await JsonSerializer.DeserializeAsync<List<PersistedNetworkAdapterTuningState>>(
            stream,
            JsonOptions,
            cancellationToken);

        return (entries ?? [])
            .ToDictionary(static entry => entry.AdapterName, StringComparer.OrdinalIgnoreCase);
    }

    private async Task WriteAllUnsafeAsync(
        Dictionary<string, PersistedNetworkAdapterTuningState> states,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        // Written to a temporary file first so an interrupted save cannot truncate the
        // only snapshot that can restore the previous DNS configuration.
        var temporaryPath = _filePath + ".tmp";
        await using (var stream = new FileStream(
            temporaryPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 4096,
            useAsync: true))
        {
            await JsonSerializer.SerializeAsync(stream, states.Values, JsonOptions, cancellationToken);
            await stream.FlushAsync(cancellationToken);
        }

        File.Move(temporaryPath, _filePath, overwrite: true);
    }
}
