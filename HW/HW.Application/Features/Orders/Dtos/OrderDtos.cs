using HW.Domain.Abstractions.Sagas;

namespace HW.Application.Features.Orders.Dtos;

public static class OrderDtos
{
    public record PlaceOrderRequestDto(string? CustomerId, string? Sku, int Quantity, decimal Amount);

    /// <summary>
    /// Returned by <c>POST /api/order</c>. Deliberately not "created" or "confirmed": at this point
    /// the saga has only started, and the outcome is not knowable for as long as the participants
    /// take to reply. Naming the field <c>Status</c> rather than reporting success is the API's way
    /// of telling the caller it must poll or wait for a notification.
    /// </summary>
    public record PlaceOrderResponseDto(string OrderId, string Status);

    public record SagaEventDto(long Version, string Type, DateTimeOffset OccurredOnUtc, string Payload);

    /// <summary>
    /// The projection plus, optionally, the raw stream. Both come from the same source of truth —
    /// the stream is authoritative and the rest is a fold over it — so a mismatch between them is a
    /// bug in the projection, never in the history.
    /// </summary>
    public record OrderSagaResponseDto(
        string OrderId,
        SagaStatus Status,
        string CurrentStep,
        long Version,
        DateTimeOffset StartedAtUtc,
        DateTimeOffset UpdatedAtUtc,
        DateTimeOffset? FinishedAtUtc,
        string? FailureReason,
        IReadOnlyList<SagaEventDto> History);
}
