namespace HW.Domain.Entities.Outbox;

public class OutboxMessage
{
    public OutboxMessage()
    {
        Id = Guid.NewGuid().ToString();
    }

    public string Id { get; set; }
    public string Type { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public DateTimeOffset OccurredOnUtc { get; set; }
    public DateTimeOffset? ProcessedOnUtc { get; set; }
    public int RetryCount { get; set; } = 0;
    public DateTimeOffset? NextRetryAt { get; set; }
    public string? Error { get; set; }
}
