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

    public JsonMsiStateRepository(string? filePath = null)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var dir = Path.Combine(appData, "Aurum");
            Directory.CreateDirectory(dir);
            _filePath = Path.Combine(dir, "msi_state.json");
        }
        else
        {
            _filePath = filePath;
        }
    }

    public async Task<MsiStateSnapshot?> ReadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_filePath))
        {
            return null;
        }

        try
        {
            await using var stream = File.OpenRead(_filePath);
            return await JsonSerializer.DeserializeAsync<MsiStateSnapshot>(stream, JsonOptions, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            return null;
        }
    }

    public async Task WriteAsync(MsiStateSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var dir = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        await using var stream = File.Create(_filePath);
        await JsonSerializer.SerializeAsync(stream, snapshot, JsonOptions, cancellationToken).ConfigureAwait(false);
    }

    public Task ClearAsync(CancellationToken cancellationToken = default)
    {
        if (File.Exists(_filePath))
        {
            try
            {
                File.Delete(_filePath);
            }
            catch
            {
                // Игнорируем ошибки удаления файла
            }
        }

        return Task.CompletedTask;
    }
}
