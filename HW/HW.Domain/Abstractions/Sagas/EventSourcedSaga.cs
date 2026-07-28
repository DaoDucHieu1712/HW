namespace HW.Domain.Abstractions.Sagas;

/// <summary>
/// Base for an orchestrator whose state is derived, never stored. Two collections drive the whole
/// pattern:
///
/// <list type="bullet">
/// <item><see cref="UncommittedEvents"/> — what just became true, appended to the saga's stream.</item>
/// <item><see cref="PendingMessages"/> — what the saga wants done next, published to participants.</item>
/// </list>
///
/// <para>
/// Both are flushed by the infrastructure inside <b>one</b> database transaction (the event rows and
/// the outbox rows commit together), which is what makes the orchestrator crash-safe: a saga cannot
/// end up having recorded a decision it never sent, or sent a command it never recorded.
/// </para>
///
/// <para>
/// <b>The split between <see cref="Apply"/> and the decision methods is the rule that keeps replay
/// honest.</b> <c>Apply</c> mutates state and nothing else — no clocks, no random ids, no
/// <see cref="Send"/> — because it runs again on every single load. Decision methods
/// (<c>OnStockReserved</c>, …) do the thinking, call <see cref="Raise"/>, and call <see cref="Send"/>;
/// they run once, when a real message arrives. Put a <c>Send</c> inside <c>Apply</c> and every
/// rehydration re-sends the saga's entire command history.
/// </para>
/// </summary>
public abstract class EventSourcedSaga
{
    private readonly List<ISagaEvent> _uncommittedEvents = [];
    private readonly List<object> _pendingMessages = [];

    /// <summary>Correlation id of the instance. Same value for the whole conversation.</summary>
    public string Id { get; protected set; } = string.Empty;

    /// <summary>
    /// Number of events applied so far — the version of the most recent one. Persisted per event row
    /// and used for optimistic concurrency: two replies racing to advance the same saga both try to
    /// write the same version, and the loser is rejected by a unique constraint rather than silently
    /// overwriting the winner's decision.
    /// </summary>
    public long Version { get; private set; }

    public SagaStatus Status { get; protected set; } = SagaStatus.Running;

    public bool IsFinished => Status is SagaStatus.Completed or SagaStatus.Compensated or SagaStatus.Failed;

    /// <summary>
    /// Short label of which step the saga is waiting on, copied into the queryable projection.
    /// Overridden by sagas with a step machine finer-grained than <see cref="SagaStatus"/>.
    /// </summary>
    public virtual string CurrentStep => Status.ToString();

    /// <summary>Events raised since the saga was loaded, in order. Cleared once persisted.</summary>
    public IReadOnlyList<ISagaEvent> UncommittedEvents => _uncommittedEvents.AsReadOnly();

    /// <summary>Messages to publish once the events above are durable. Cleared once queued.</summary>
    public IReadOnlyList<object> PendingMessages => _pendingMessages.AsReadOnly();

    /// <summary>
    /// Folds one event into current state. Called both when an event is first raised and on every
    /// replay, so it must be a pure state transition — see the note on this class.
    /// </summary>
    protected abstract void Apply(ISagaEvent sagaEvent);

    /// <summary>Records a new fact: applies it, bumps the version, and queues it for the stream.</summary>
    protected void Raise(ISagaEvent sagaEvent)
    {
        Apply(sagaEvent);
        Version++;
        _uncommittedEvents.Add(sagaEvent);
    }

    /// <summary>
    /// Queues a command or event for a participant. Not part of the stream: what the saga <i>asked
    /// for</i> is recoverable from the events it raised, and duplicating it would give two sources of
    /// truth that can disagree.
    /// </summary>
    protected void Send(object message) => _pendingMessages.Add(message);

    /// <summary>
    /// Rebuilds state from the stored stream. No events are raised and nothing is sent — replay
    /// re-derives, it does not re-decide.
    /// </summary>
    public void Replay(IEnumerable<ISagaEvent> history)
    {
        foreach (var sagaEvent in history)
        {
            Apply(sagaEvent);
            Version++;
        }
    }

    /// <summary>Version the stored stream was at when this instance was loaded.</summary>
    public long OriginalVersion => Version - _uncommittedEvents.Count;

    public void ClearUncommittedEvents() => _uncommittedEvents.Clear();

    public void ClearPendingMessages() => _pendingMessages.Clear();
}
