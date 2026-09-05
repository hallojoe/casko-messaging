using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Casko.Messaging.Email.BulkDelivery;

public sealed class SqlServerNotificationWriter(
    NotificationDbContext db, IOptions<NotificationIngestionOptions> options,
    ILogger<SqlServerNotificationWriter> logger) : INotificationWriter
{
    private static readonly Meter Meter = new("Casko.Messaging.Notifications");
    private static readonly Histogram<double> Duration = Meter.CreateHistogram<double>("notifications.ingestion.duration", "ms");
    private static readonly Counter<long> Inserted = Meter.CreateCounter<long>("notifications.ingestion.inserted");
    private static readonly Counter<long> Reused = Meter.CreateCounter<long>("notifications.ingestion.reused");
    private static readonly Counter<long> Retries = Meter.CreateCounter<long>("notifications.ingestion.retries");
    private static readonly Counter<long> Failures = Meter.CreateCounter<long>("notifications.ingestion.failures");

    public async Task<NotificationEventResult> CreateEventAsync(CreateNotificationEventRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var result = await CreateBatchAsync(new([new(request.EntityId, request.EventType, request.Template,
            request.Message, request.IdempotencyKey, [], request.Priority)]), cancellationToken);
        var item = result.Notifications[0];
        return new(item.Id, item.CreatedUtc);
    }

    public Task<NotificationBatchResult> CreateBatchAsync(NotificationBatchRequest request, CancellationToken cancellationToken)
    {
        NotificationValidation.Validate(request, options.Value);
        return ExecuteAsync(request.Notifications, null, [], cancellationToken);
    }

    public async Task<int> AddRecipientsAsync(long eventId, IReadOnlyCollection<RecipientInput> recipients, CancellationToken cancellationToken)
    {
        NotificationValidation.ValidateRecipients(recipients, options.Value);
        var result = await ExecuteAsync([], eventId, recipients, cancellationToken);
        return result.Notifications[0].AddedRecipients;
    }

    private async Task<NotificationBatchResult> ExecuteAsync(IReadOnlyList<NotificationInput> events, long? eventId,
        IReadOnlyCollection<RecipientInput> recipients, CancellationToken ct)
    {
        var timer = Stopwatch.StartNew();
        try
        {
            for (var attempt = 0; ; attempt++)
            {
                try
                {
                    var result = await WriteAsync(events, eventId, recipients, ct);
                    var added = result.Notifications.Sum(x => x.AddedRecipients);
                    var reused = result.Notifications.Sum(x => x.ExistingRecipients);
                    Inserted.Add(added, new KeyValuePair<string, object?>("kind", "delivery"));
                    Inserted.Add(result.Notifications.Count(x => x.Created), new KeyValuePair<string, object?>("kind", "event"));
                    Reused.Add(reused, new KeyValuePair<string, object?>("kind", "delivery"));
                    Reused.Add(result.Notifications.Count(x => !x.Created), new KeyValuePair<string, object?>("kind", "event"));
                    logger.LogInformation("Notification ingestion committed: {Events} events, {Added} added deliveries, {Existing} existing deliveries, {ElapsedMs} ms",
                        result.Notifications.Count, added, reused, timer.Elapsed.TotalMilliseconds);
                    return result;
                }
                catch (SqlException ex) when (ex.Number == 1205 && attempt < 2 && !ct.IsCancellationRequested)
                {
                    Retries.Add(1);
                    await Task.Delay(TimeSpan.FromMilliseconds(Random.Shared.Next(25, 101) * (attempt + 1)), ct);
                }
            }
        }
        catch (SqlException ex) when (ct.IsCancellationRequested)
        {
            Failures.Add(1);
            throw new OperationCanceledException("Notification ingestion was cancelled.", ex, ct);
        }
        catch
        {
            Failures.Add(1);
            throw;
        }
        finally { Duration.Record(timer.Elapsed.TotalMilliseconds); }
    }

    private async Task<NotificationBatchResult> WriteAsync(IReadOnlyList<NotificationInput> events, long? eventId,
        IReadOnlyCollection<RecipientInput> recipients, CancellationToken ct)
    {
        // A fresh connection and temporary tables for every attempt; no EF tracking or ambient transaction.
        await using var connection = new SqlConnection(db.Database.GetConnectionString());
        await connection.OpenAsync(ct);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(ct);
        await ExecuteCommandAsync(connection, transaction, StageSql, ct);
        await CopyAsync(connection, transaction, "#Events",
            ["Ordinal", "EntityId", "EventType", "Template", "Payload", "IdempotencyKey", "Priority"],
            [typeof(int), typeof(Guid), typeof(string), typeof(string), typeof(string), typeof(string), typeof(int)],
            events.Select((e, i) => new object?[] { i, e.EntityId, e.EventType, e.Template, JsonSerializer.Serialize(e.Message), e.IdempotencyKey, (int)e.Priority }), ct);
        IEnumerable<object?[]> RecipientRows()
        {
            var ordinal = 0;
            for (var i = 0; i < events.Count; i++)
                foreach (var r in events[i].Recipients)
                    yield return [ordinal++, i, r.RecipientId, r.EmailAddress.Trim(), NotificationValidation.Normalize(r.EmailAddress)];
            foreach (var r in recipients)
                yield return [ordinal++, 0, r.RecipientId, r.EmailAddress.Trim(), NotificationValidation.Normalize(r.EmailAddress)];
        }
        await CopyAsync(connection, transaction, "#Recipients",
            ["Ordinal", "EventOrdinal", "RecipientId", "EmailAddress", "NormalizedEmailAddress"],
            [typeof(int), typeof(int), typeof(Guid), typeof(string), typeof(string)], RecipientRows(), ct);

        await using var command = new SqlCommand(InsertSql, connection, transaction);
        command.Parameters.AddWithValue("@eventId", (object?)eventId ?? DBNull.Value);
        command.Parameters.AddWithValue("@now", DateTimeOffset.UtcNow);
        var results = new List<NotificationWriteResult>();
        try
        {
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                results.Add(new(reader.GetString(0), reader.GetInt64(1), reader.GetFieldValue<DateTimeOffset>(2),
                    reader.GetBoolean(3), reader.GetInt32(4), reader.GetInt32(5), reader.GetInt32(6), reader.GetInt32(7)));
        }
        catch (SqlException ex) when (ex.Number == 51001) { throw new NotificationConflictException(); }
        catch (SqlException ex) when (ex.Number == 51002) { throw new KeyNotFoundException("Notification event was not found."); }
        await transaction.CommitAsync(ct);
        return new(results.AsReadOnly());
    }

    private static async Task ExecuteCommandAsync(SqlConnection c, SqlTransaction t, string sql, CancellationToken ct)
    {
        await using var command = new SqlCommand(sql, c, t);
        await command.ExecuteNonQueryAsync(ct);
    }

    private static async Task CopyAsync(SqlConnection c, SqlTransaction t, string table, string[] columns,
        Type[] types, IEnumerable<object?[]> rows, CancellationToken ct)
    {
        using var reader = new BulkRowReader(columns, types, rows);
        using var copy = new SqlBulkCopy(c, SqlBulkCopyOptions.Default, t)
        {
            DestinationTableName = table, BatchSize = 1000, EnableStreaming = true
        };
        foreach (var column in columns) copy.ColumnMappings.Add(column, column);
        await copy.WriteToServerAsync(reader, ct);
    }

    private const string StageSql = """
        SET XACT_ABORT ON;
        SELECT TOP (0) CAST(0 AS int) AS Ordinal, EntityId, EventType, Template, Payload, IdempotencyKey, Priority
        INTO #Events FROM dbo.NotificationEvents;
        SELECT TOP (0) CAST(0 AS int) AS Ordinal, CAST(0 AS int) AS EventOrdinal,
          RecipientId, EmailAddress, NormalizedEmailAddress
        INTO #Recipients FROM dbo.NotificationDeliveries;
        CREATE UNIQUE CLUSTERED INDEX IX_Events_Ordinal ON #Events(Ordinal);
        CREATE INDEX IX_Events_Key ON #Events(IdempotencyKey);
        CREATE UNIQUE CLUSTERED INDEX IX_Recipients_Ordinal ON #Recipients(Ordinal);
        CREATE INDEX IX_Recipients_Key ON #Recipients(EventOrdinal, NormalizedEmailAddress);
        """;

    private const string InsertSql = """
        SET NOCOUNT ON;
        SELECT *, MIN(Ordinal) OVER (PARTITION BY IdempotencyKey) AS CanonicalOrdinal
        INTO #Canonical FROM #Events;
        IF EXISTS (
          SELECT 1 FROM #Canonical e JOIN #Events first ON first.Ordinal = e.CanonicalOrdinal
          WHERE e.EntityId <> first.EntityId
            OR CONVERT(varbinary(max), e.EventType) <> CONVERT(varbinary(max), first.EventType)
            OR CONVERT(varbinary(max), e.Template) <> CONVERT(varbinary(max), first.Template)
            OR CONVERT(varbinary(max), e.Payload) <> CONVERT(varbinary(max), first.Payload)
            OR e.Priority <> first.Priority)
          THROW 51001, 'Conflicting event content.', 1;

        IF EXISTS (
          SELECT 1 FROM #Events e JOIN dbo.NotificationEvents n WITH (UPDLOCK, HOLDLOCK)
            ON n.IdempotencyKey = e.IdempotencyKey
          WHERE n.EntityId <> e.EntityId
            OR CONVERT(varbinary(max), n.EventType) <> CONVERT(varbinary(max), e.EventType)
            OR CONVERT(varbinary(max), n.Template) <> CONVERT(varbinary(max), e.Template)
            OR CONVERT(varbinary(max), n.Payload) <> CONVERT(varbinary(max), e.Payload)
            OR n.Priority <> e.Priority)
          THROW 51001, 'Conflicting event content.', 1;

        CREATE TABLE #NewEvents(Id bigint PRIMARY KEY);
        INSERT dbo.NotificationEvents(EntityId, EventType, Template, Payload, IdempotencyKey, Priority, CreatedUtc)
        OUTPUT inserted.Id INTO #NewEvents
        SELECT e.EntityId, e.EventType, e.Template, e.Payload, e.IdempotencyKey, e.Priority, @now
        FROM #Canonical e
        WHERE e.Ordinal = e.CanonicalOrdinal AND NOT EXISTS (
          SELECT 1 FROM dbo.NotificationEvents n WITH (UPDLOCK, HOLDLOCK) WHERE n.IdempotencyKey = e.IdempotencyKey);

        SELECT e.Ordinal, e.CanonicalOrdinal, n.Id, n.IdempotencyKey, n.CreatedUtc, n.Priority,
          CAST(CASE WHEN added.Id IS NULL THEN 0 ELSE 1 END AS bit) AS Created
        INTO #Map FROM #Canonical e
        JOIN dbo.NotificationEvents n ON n.IdempotencyKey = e.IdempotencyKey
        LEFT JOIN #NewEvents added ON added.Id = n.Id;
        IF @eventId IS NOT NULL
        BEGIN
          INSERT #Map(Ordinal, CanonicalOrdinal, Id, IdempotencyKey, CreatedUtc, Priority, Created)
          SELECT 0, 0, Id, IdempotencyKey, CreatedUtc, Priority, 0
          FROM dbo.NotificationEvents WITH (UPDLOCK, HOLDLOCK) WHERE Id = @eventId;
          IF @@ROWCOUNT = 0 THROW 51002, 'Event missing.', 1;
        END;

        SELECT r.*, m.Id AS EventId, m.Priority AS EventPriority,
          ROW_NUMBER() OVER (PARTITION BY m.Id, r.NormalizedEmailAddress ORDER BY r.Ordinal) AS Position
        INTO #UniqueRecipients FROM #Recipients r JOIN #Map m ON m.Ordinal = r.EventOrdinal;
        CREATE INDEX IX_UniqueRecipients_Key ON #UniqueRecipients(EventId, NormalizedEmailAddress);
        CREATE TABLE #AddedDeliveries(EventId bigint NOT NULL);
        INSERT dbo.NotificationDeliveries(NotificationEventId, RecipientId, EmailAddress, NormalizedEmailAddress,
            Priority, Status, Attempts, CreatedUtc)
        OUTPUT inserted.NotificationEventId INTO #AddedDeliveries
        SELECT r.EventId, r.RecipientId, r.EmailAddress, r.NormalizedEmailAddress, r.EventPriority, 0, 0, @now
        FROM #UniqueRecipients r
        WHERE r.Position = 1 AND NOT EXISTS (
          SELECT 1 FROM dbo.NotificationDeliveries d WITH (UPDLOCK, HOLDLOCK)
          WHERE d.NotificationEventId = r.EventId AND d.NormalizedEmailAddress = r.NormalizedEmailAddress);

        SELECT m.IdempotencyKey, m.Id, m.CreatedUtc, m.Created,
          COALESCE(a.Added, 0), COALESCE(r.UniqueCount, 0) - COALESCE(a.Added, 0),
          COALESCE(r.TotalCount, 0) - COALESCE(r.UniqueCount, 0), e.TotalCount - 1
        FROM #Map m
        JOIN (SELECT Id, COUNT(*) AS TotalCount FROM #Map GROUP BY Id) e ON e.Id = m.Id
        LEFT JOIN (SELECT EventId, COUNT(*) AS Added FROM #AddedDeliveries GROUP BY EventId) a ON a.EventId = m.Id
        LEFT JOIN (SELECT EventId, COUNT(*) AS TotalCount, SUM(CASE WHEN Position = 1 THEN 1 ELSE 0 END) AS UniqueCount
          FROM #UniqueRecipients GROUP BY EventId) r ON r.EventId = m.Id
        WHERE m.Ordinal = m.CanonicalOrdinal ORDER BY m.Ordinal;
        """;
}
