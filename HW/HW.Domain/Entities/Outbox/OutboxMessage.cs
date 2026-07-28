namespace HW.Domain.Entities.Outbox;

public class OutboxMessage
{
    public OutboxMessage()
    {
        Id = Guid.NewGuid().ToString();
    }

    public string Id { get; set; }

    /// <summary>
    /// Assembly-qualified name of the payload. Also decides where the row is delivered: an
    /// <c>IDomainEvent</c> goes to MediatR, a <c>[Message]</c> contract goes to the broker. See
    /// <c>OutboxMessageProcessor.RouteFor</c>.
    /// </summary>
    public string Type { get; set; } = string.Empty;

    public string Content { get; set; } = string.Empty;
    public DateTimeOffset OccurredOnUtc { get; set; }
    public DateTimeOffset? ProcessedOnUtc { get; set; }
    public int RetryCount { get; set; } = 0;
    public DateTimeOffset? NextRetryAt { get; set; }
    public string? Error { get; set; }
}
