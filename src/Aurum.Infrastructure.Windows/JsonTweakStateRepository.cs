using System.Text.Json;
using Aurum.Core;

namespace Aurum.Infrastructure.Windows;

public sealed class JsonTweakStateRepository : ITweakStateRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _filePath;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public JsonTweakStateRepository(string? filePath = null)
    {
        _filePath = filePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Aurum",
            "state.json");
    }

    public async Task<PersistedTweakState?> GetAsync(
        string tweakId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tweakId);
        await _gate.WaitAsync(cancellationToken);

        try
        {
            var states = await ReadAllUnsafeAsync(cancellationToken);
            return states.GetValueOrDefault(tweakId);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<PersistedTweakState>> GetAllAsync(
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

    public async Task SaveAsync(PersistedTweakState state, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        await _gate.WaitAsync(cancellationToken);

        try
        {
            var states = await ReadAllUnsafeAsync(cancellationToken);
            states[state.TweakId] = state;
            await WriteAllUnsafeAsync(states, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task RemoveAsync(string tweakId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tweakId);
        await _gate.WaitAsync(cancellationToken);

        try
        {
            var states = await ReadAllUnsafeAsync(cancellationToken);
            if (states.Remove(tweakId))
            {
                await WriteAllUnsafeAsync(states, cancellationToken);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<Dictionary<string, PersistedTweakState>> ReadAllUnsafeAsync(
        CancellationToken cancellationToken)
    {
        if (!File.Exists(_filePath))
        {
            return new Dictionary<string, PersistedTweakState>(StringComparer.Ordinal);
        }

        await using var stream = new FileStream(
            _filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 4096,
            useAsync: true);

        var states = await JsonSerializer.DeserializeAsync<Dictionary<string, PersistedTweakState>>(
            stream,
            JsonOptions,
            cancellationToken);

        return states is null
            ? throw new InvalidDataException($"Файл состояния Aurum '{_filePath}' пуст или повреждён.")
            : new Dictionary<string, PersistedTweakState>(states, StringComparer.Ordinal);
    }

    private async Task WriteAllUnsafeAsync(
        Dictionary<string, PersistedTweakState> states,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(_filePath)
            ?? throw new InvalidOperationException("У файла состояния нет родительского каталога.");
        Directory.CreateDirectory(directory);

        var temporaryPath = _filePath + ".tmp";
        await using (var stream = new FileStream(
            temporaryPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 4096,
            useAsync: true))
        {
            await JsonSerializer.SerializeAsync(stream, states, JsonOptions, cancellationToken);
            await stream.FlushAsync(cancellationToken);
        }

        File.Move(temporaryPath, _filePath, overwrite: true);
    }
}
