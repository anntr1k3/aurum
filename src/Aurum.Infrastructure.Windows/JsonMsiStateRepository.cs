using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Aurum.Core;

namespace Aurum.Infrastructure.Windows;

public sealed class JsonMsiStateRepository : IMsiStateRepository
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _filePath;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public JsonMsiStateRepository(string? filePath = null)
    {
        _filePath = string.IsNullOrWhiteSpace(filePath)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Aurum",
                "msi_state.json")
            : filePath;
    }

    public async Task<MsiStateSnapshot?> ReadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

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

            // A corrupt snapshot must surface as an error. Reporting "no state" here
            // would make a revert silently no-op while the devices stay modified.
            try
            {
                return await JsonSerializer.DeserializeAsync<MsiStateSnapshot>(stream, JsonOptions, cancellationToken).ConfigureAwait(false)
                    ?? throw new InvalidDataException($"Файл состояния MSI «{_filePath}» пуст или повреждён.");
            }
            catch (JsonException error)
            {
                throw new InvalidDataException(
                    $"Файл состояния MSI «{_filePath}» повреждён и не может быть использован для откката. Удалите его вручную, если откат больше не требуется.",
                    error);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task WriteAsync(MsiStateSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var directory = Path.GetDirectoryName(_filePath)
                ?? throw new InvalidOperationException("Файл состояния MSI не имеет родительского каталога.");
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
                await JsonSerializer.SerializeAsync(stream, snapshot, JsonOptions, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            File.Move(temporaryPath, _filePath, overwrite: true);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            if (File.Exists(_filePath))
            {
                File.Delete(_filePath);
            }
        }
        finally
        {
            _gate.Release();
        }
    }
}
