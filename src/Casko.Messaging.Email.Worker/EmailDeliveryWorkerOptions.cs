namespace Casko.Messaging.Email.Worker;

public sealed class EmailDeliveryWorkerOptions
{
    public int BatchSize { get; init; } = 10;
    public int Concurrency { get; init; } = 1;
    public int CriticalBatchSize { get; init; } = 2;
    public int CriticalConcurrency { get; init; } = 1;
    public int MaximumAttempts { get; init; } = 3;
    public int MaximumMessagesPerSecond { get; init; } = 1;
    public int CriticalMaximumMessagesPerSecond { get; init; } = 1;
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(2);
    public TimeSpan ProcessingLeaseDuration { get; init; } = TimeSpan.FromMinutes(5);
    public TimeSpan BulkPromotionAfter { get; init; } = TimeSpan.FromHours(1);
}
