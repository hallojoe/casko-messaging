using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Casko.Messaging.Email.BulkDelivery;

public sealed class SqlServerNotificationQueueStore(NotificationDbContext db) : INotificationQueueStore
{
    public async Task<IReadOnlyList<ClaimedDelivery>> ClaimAsync(int batchSize, string workerId, TimeSpan lease, CancellationToken cancellationToken)
        => await ClaimAsync(batchSize, workerId, lease, new NotificationPriorityRange(), cancellationToken);

    public async Task<IReadOnlyList<ClaimedDelivery>> ClaimAsync(int batchSize, string workerId, TimeSpan lease, NotificationPriorityRange priorityRange, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var until = now.Add(lease);
        const string sql = """
;WITH candidates AS (
 SELECT TOP (@batchSize) d.Id FROM NotificationDeliveries d WITH (UPDLOCK, READPAST, ROWLOCK)
 WHERE (d.Status = 0 OR (d.Status = 2 AND d.NextAttemptUtc <= @now) OR (d.Status = 1 AND d.ProcessingLeaseUntilUtc < @now))
   AND (@minimumPriority IS NULL OR d.Priority >= @minimumPriority)
   AND (@maximumPriority IS NULL OR d.Priority <= @maximumPriority)
 ORDER BY CASE WHEN d.Priority = 0 AND @bulkPromotionAfterSeconds IS NOT NULL
                     AND d.CreatedUtc <= DATEADD(second, -@bulkPromotionAfterSeconds, @now) THEN 1 ELSE d.Priority END DESC,
          d.CreatedUtc, d.Id
)
UPDATE d SET Status = 1, ProcessingStartedUtc = @now, ProcessingLeaseUntilUtc = @until, ProcessingWorkerId = @workerId, NextAttemptUtc = NULL
OUTPUT inserted.Id, inserted.NotificationEventId, inserted.EmailAddress,
       e.Payload, e.Template, inserted.Attempts, inserted.Priority
FROM NotificationDeliveries d
INNER JOIN candidates c ON c.Id = d.Id
INNER JOIN NotificationEvents e ON e.Id = d.NotificationEventId;
""";
        await db.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            await using var command = db.Database.GetDbConnection().CreateCommand();
            command.CommandText = sql;
            command.Transaction = db.Database.CurrentTransaction?.GetDbTransaction();
            command.Parameters.Add(new SqlParameter("@batchSize", batchSize)); command.Parameters.Add(new SqlParameter("@now", now)); command.Parameters.Add(new SqlParameter("@until", until)); command.Parameters.Add(new SqlParameter("@workerId", workerId));
            command.Parameters.Add(new SqlParameter("@minimumPriority", System.Data.SqlDbType.Int) { Value = priorityRange.Minimum is { } minimum ? (object)(int)minimum : DBNull.Value });
            command.Parameters.Add(new SqlParameter("@maximumPriority", System.Data.SqlDbType.Int) { Value = priorityRange.Maximum is { } maximum ? (object)(int)maximum : DBNull.Value });
            command.Parameters.Add(new SqlParameter("@bulkPromotionAfterSeconds", System.Data.SqlDbType.Int) { Value = priorityRange.BulkPromotionAfter is { } aging ? (object)(int)aging.TotalSeconds : DBNull.Value });
            var output = new List<ClaimedDelivery>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken)) output.Add(new ClaimedDelivery(reader.GetInt64(0), reader.GetInt64(1), reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetInt32(5), workerId, (NotificationPriority)reader.GetInt32(6)));
            return output;
        }
        finally { await db.Database.CloseConnectionAsync(); }
    }

    public Task<bool> RenewLeaseAsync(long id, string workerId, TimeSpan lease, CancellationToken ct) => GuardedUpdateAsync(id, workerId, new { }, ct, $"ProcessingLeaseUntilUtc = DATEADD(second, {(int)lease.TotalSeconds}, SYSUTCDATETIME())");
    public async Task<bool> MarkSentAsync(long id, string workerId, string messageId, CancellationToken ct) => await GuardedUpdateAsync(id, workerId, new { }, ct, "Status = 3, SentUtc = SYSUTCDATETIME(), LastAttemptUtc = SYSUTCDATETIME(), Attempts = Attempts + 1, SmtpMessageId = @messageId, ProcessingWorkerId = NULL, ProcessingLeaseUntilUtc = NULL", messageId);
    public async Task<bool> MarkFailureAsync(long id, string workerId, string error, int maximumAttempts, TimeSpan? retryAfter, CancellationToken ct)
    {
        var terminal = retryAfter is null;
        var sql = terminal ? "Status = 4, NextAttemptUtc = NULL" : "Status = 2, NextAttemptUtc = DATEADD(second, @retrySeconds, SYSUTCDATETIME())";
        return await GuardedUpdateAsync(id, workerId, new { }, ct, $"{sql}, LastAttemptUtc = SYSUTCDATETIME(), Attempts = Attempts + 1, LastError = @error, ProcessingWorkerId = NULL, ProcessingLeaseUntilUtc = NULL", error, retryAfter is null ? null : (int)retryAfter.Value.TotalSeconds);
    }
    public async Task<bool> RetryAsync(long id, CancellationToken ct) => await db.NotificationDeliveries.Where(x => x.Id == id && x.Status == NotificationDeliveryStatus.Failed).ExecuteUpdateAsync(s => s.SetProperty(x => x.Status, NotificationDeliveryStatus.Pending).SetProperty(x => x.NextAttemptUtc, (DateTimeOffset?)null).SetProperty(x => x.ProcessingWorkerId, (string?)null).SetProperty(x => x.ProcessingLeaseUntilUtc, (DateTimeOffset?)null), ct) > 0;
    private async Task<bool> GuardedUpdateAsync(long id, string workerId, object _, CancellationToken ct, string set, string? messageId = null, int? retrySeconds = null)
    {
        var sql = $"UPDATE NotificationDeliveries SET {set} WHERE Id = @id AND Status = 1 AND ProcessingWorkerId = @workerId AND ProcessingLeaseUntilUtc > SYSUTCDATETIME();";
        await db.Database.OpenConnectionAsync(ct); try { await using var c = db.Database.GetDbConnection().CreateCommand(); c.CommandText = sql; c.Parameters.Add(new SqlParameter("@id", id)); c.Parameters.Add(new SqlParameter("@workerId", workerId)); if (messageId is not null) c.Parameters.Add(new SqlParameter("@messageId", messageId)); if (retrySeconds is not null) c.Parameters.Add(new SqlParameter("@retrySeconds", retrySeconds)); if (set.Contains("@error")) c.Parameters.Add(new SqlParameter("@error", messageId ?? "")); return await c.ExecuteNonQueryAsync(ct) > 0; } finally { await db.Database.CloseConnectionAsync(); }
    }
    public static string Normalize(string value) => value.Trim().ToUpperInvariant();
}
