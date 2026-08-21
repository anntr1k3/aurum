using System.Text.Json;
using Aurum.Core;

namespace Aurum.Infrastructure.Windows;

public sealed class JsonPowerPlanStateRepository : IPowerPlanStateRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    private readonly string _filePath;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public JsonPowerPlanStateRepository(string? filePath = null)
    {
        _filePath = filePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Aurum",
            "power-plan-state.json");
    }

    public async Task<PersistedPowerPlanState?> GetAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(_filePath))
            {
                return null;
            }

            await using var stream = new FileStream(
                _filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 4096,
                useAsync: true);
            return await JsonSerializer.DeserializeAsync<PersistedPowerPlanState>(
                       stream,
                       JsonOptions,
                       cancellationToken)
                   ?? throw new InvalidDataException($"Файл состояния схем питания Aurum '{_filePath}' пуст или повреждён.");
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(PersistedPowerPlanState state, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var directory = Path.GetDirectoryName(_filePath)
                ?? throw new InvalidOperationException("У файла состояния схем питания нет родительского каталога.");
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
                await JsonSerializer.SerializeAsync(stream, state, JsonOptions, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            File.Move(temporaryPath, _filePath, overwrite: true);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task RemoveAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            File.Delete(_filePath);
        }
        finally
        {
            _gate.Release();
        }
    }
}
