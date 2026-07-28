using HW.Domain.Abstractions.Entities;
using HW.Domain.Abstractions.Sagas;

namespace HW.Domain.Entities.Sagas;

/// <summary>
/// Flat, queryable projection of a saga's current state, rebuilt from the stream on every append.
///
/// <para>
/// <b>Derived data, never a source of truth.</b> It exists because "list every saga stuck
/// compensating for over an hour" is an operational question you cannot answer by replaying
/// thousands of streams. Delete this table and nothing is lost — every row can be regenerated from
/// <see cref="SagaEventRecord"/>. That asymmetry is the point of event sourcing: one append-only
/// truth, as many disposable read models as you need.
/// </para>
/// </summary>
public class SagaInstance : Entity
{
    // Id is the saga's correlation id, assigned by the repository — the base class's generated Guid
    // is always overwritten. Deriving from Entity is what lets queries read this through the usual
    // IRepository<T> instead of a bespoke read seam.

    public string SagaType { get; set; } = string.Empty;

    public SagaStatus Status { get; set; }

    public string CurrentStep { get; set; } = string.Empty;

    /// <summary>Stream version this projection reflects. Lets a stale read be spotted.</summary>
    public long Version { get; set; }

    public DateTimeOffset StartedAtUtc { get; set; }

    public DateTimeOffset UpdatedAtUtc { get; set; }

    public DateTimeOffset? FinishedAtUtc { get; set; }

    /// <summary>Why the saga compensated or failed, when it did.</summary>
    public string? FailureReason { get; set; }
}
