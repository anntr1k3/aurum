using System.Text.Json;
using Aurum.Core;

namespace Aurum.Infrastructure.Windows;

public sealed class JsonlAuditJournal : IAuditJournal
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false
    };

    private readonly string _filePath;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public JsonlAuditJournal(string? filePath = null)
    {
        _filePath = filePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Aurum",
            "audit.jsonl");
    }

    public async Task AppendAsync(AuditEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var directory = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var line = JsonSerializer.Serialize(entry, JsonOptions) + Environment.NewLine;
            await File.AppendAllTextAsync(_filePath, line, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<AuditEntry>> ReadRecentAsync(
        int count,
        CancellationToken cancellationToken = default)
    {
        if (count <= 0)
        {
            return [];
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(_filePath))
            {
                return [];
            }

            var lines = await File.ReadAllLinesAsync(_filePath, cancellationToken);
            var entries = new List<AuditEntry>(Math.Min(count, lines.Length));
            for (var index = lines.Length - 1; index >= 0 && entries.Count < count; index--)
            {
                var line = lines[index];
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                var entry = JsonSerializer.Deserialize<AuditEntry>(line, JsonOptions);
                if (entry is not null)
                {
                    entries.Add(entry);
                }
            }

            return entries;
        }
        finally
        {
            _gate.Release();
        }
    }
}
