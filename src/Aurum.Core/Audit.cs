using System.Text.Json.Serialization;

namespace Aurum.Core;

public enum AuditAction
{
    Applied,
    Reverted,
    Repaired,
    Failed
}

public sealed record AuditEntry(
    DateTimeOffset AtUtc,
    string Area,
    string Subject,
    AuditAction Action,
    bool Succeeded,
    string Detail)
{
    [JsonIgnore]
    public string TimeLabel => AtUtc.ToLocalTime().ToString("HH:mm:ss");

    [JsonIgnore]
    public string ActionLabel => (Action, Succeeded) switch
    {
        (AuditAction.Applied, true) => "Применено",
        (AuditAction.Reverted, true) => "Откат",
        (AuditAction.Repaired, true) => "Восстановлено",
        (_, false) => "Ошибка",
        _ => Action.ToString()
    };
}

public interface IAuditJournal
{
    Task AppendAsync(AuditEntry entry, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AuditEntry>> ReadRecentAsync(int count, CancellationToken cancellationToken = default);
}

public static class AuditJournal
{
    public static async Task RecordAsync(
        IAuditJournal? journal,
        string area,
        string subject,
        AuditAction action,
        bool succeeded,
        string detail,
        CancellationToken cancellationToken = default)
    {
        if (journal is null)
        {
            return;
        }

        try
        {
            await journal.AppendAsync(
                new AuditEntry(DateTimeOffset.UtcNow, area, subject, action, succeeded, detail),
                cancellationToken);
        }
        catch
        {
            // The journal must not fail a system transaction.
        }
    }
}
