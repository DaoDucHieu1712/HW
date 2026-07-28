using HW.Domain.Abstractions.Sagas;
using HW.Domain.Entities.Sagas;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;

namespace HW.Infrastructure.Sagas;

/// <summary>
/// The event store, on the same MariaDB and the same <see cref="ApplicationDbContext"/> as
/// everything else.
///
/// <para>
/// <b>Sharing the DbContext is the whole design, not a shortcut.</b> Saga events, the inbox claim,
/// and the outbox rows for the commands the saga just decided to send all land in one change tracker
/// and commit in one local transaction. That single ACID commit at the edge is what lets the saga
/// give a non-atomic distributed workflow a dependable spine: within one service everything is
/// all-or-nothing, and only the hops between services are eventually consistent. Point this at a
/// dedicated store and you would need a second saga to keep the two in step.
/// </para>
/// </summary>
internal sealed class SagaRepository : ISagaRepository
{
    /// <summary>
    /// Same settings as <c>OutboxMessageBus</c> and <c>EFUnitOfWork</c>. <c>TypeNameHandling.None</c>
    /// matters more here than anywhere: the payload is read back years later and CLR type names
    /// embedded inside it would pin the event to an assembly layout that will have moved. The
    /// separate <c>Type</c> column is the one place a type name lives.
    /// </summary>
    private static readonly JsonSerializerSettings SerializerSettings = new()
    {
        TypeNameHandling = TypeNameHandling.None
    };

    private readonly ApplicationDbContext _dbContext;

    public SagaRepository(ApplicationDbContext dbContext) => _dbContext = dbContext;

    public async Task<TSaga?> LoadAsync<TSaga>(string sagaId, CancellationToken ct = default)
        where TSaga : EventSourcedSaga, new()
    {
        var records = await _dbContext.SagaEvents
            .AsNoTracking()
            .Where(x => x.SagaId == sagaId)
            .OrderBy(x => x.Version)
            .ToListAsync(ct);

        if (records.Count == 0) return null;

        var saga = new TSaga();
        saga.Replay(records.Select(Deserialize));

        return saga;
    }

    public Task AppendAsync(EventSourcedSaga saga, CancellationToken ct = default)
    {
        if (saga.UncommittedEvents.Count == 0) return Task.CompletedTask;

        var sagaType = saga.GetType().Name;
        var now = DateTimeOffset.UtcNow;
        var version = saga.OriginalVersion;

        foreach (var sagaEvent in saga.UncommittedEvents)
        {
            var eventType = sagaEvent.GetType();

            _dbContext.SagaEvents.Add(new SagaEventRecord
            {
                SagaId = saga.Id,
                SagaType = sagaType,
                Version = ++version,
                Type = $"{eventType.FullName}, {eventType.Assembly.GetName().Name}",
                Content = JsonConvert.SerializeObject(sagaEvent, eventType, SerializerSettings),
                OccurredOnUtc = now
            });
        }

        UpsertProjection(saga, sagaType, now);

        // Nothing is saved here. The rows join whichever transaction the caller opened, exactly like
        // OutboxMessageBus — see the class summary for why that coupling is the point.
        return Task.CompletedTask;
    }

    public async Task<bool> TryClaimMessageAsync(string sagaId, string messageId, CancellationToken ct = default)
    {
        // Two reads' worth of race remains open between here and the commit — two instances handling
        // the same redelivery could both see no row. The primary key on MessageId closes it: the
        // second commit violates it, that transaction rolls back whole, and the broker redelivers
        // into a state where the claim is now visible. The check is the fast path; the constraint is
        // the guarantee.
        var alreadyClaimed = await _dbContext.SagaInbox
            .AsNoTracking()
            .AnyAsync(x => x.MessageId == messageId, ct);

        if (alreadyClaimed) return false;

        // Guards against the same message being claimed twice inside one transaction, which the
        // database cannot see yet.
        if (_dbContext.SagaInbox.Local.Any(x => x.MessageId == messageId)) return false;

        _dbContext.SagaInbox.Add(new SagaInboxEntry
        {
            MessageId = messageId,
            SagaId = sagaId,
            HandledAtUtc = DateTimeOffset.UtcNow
        });

        return true;
    }

    /// <summary>
    /// Refreshes the queryable projection from the saga's freshly-folded state. Rewritten from state
    /// rather than patched per event, so the projection cannot drift: it is always exactly what
    /// replaying the stream produces.
    /// </summary>
    private void UpsertProjection(EventSourcedSaga saga, string sagaType, DateTimeOffset now)
    {
        var instance = _dbContext.SagaInstances.Local.FirstOrDefault(x => x.Id == saga.Id)
            ?? _dbContext.SagaInstances.Find(saga.Id);

        if (instance is null)
        {
            instance = new SagaInstance { Id = saga.Id, SagaType = sagaType, StartedAtUtc = now };
            _dbContext.SagaInstances.Add(instance);
        }

        instance.Status = saga.Status;
        instance.CurrentStep = saga.CurrentStep;
        instance.Version = saga.Version;
        instance.UpdatedAtUtc = now;

        if (saga.IsFinished) instance.FinishedAtUtc ??= now;
    }

    private static ISagaEvent Deserialize(SagaEventRecord record)
    {
        var type = ResolveType(record.Type)
            ?? throw new InvalidOperationException(
                $"Saga '{record.SagaId}' has an event at version {record.Version} of type " +
                $"'{record.Type}', which no longer exists. A stream cannot be replayed without every " +
                $"type it contains — keep the record type, even if nothing raises it any more.");

        return (ISagaEvent)JsonConvert.DeserializeObject(record.Content, type, SerializerSettings)!;
    }

    // Mirrors OutboxMessageProcessor.ResolveType: assembly-qualified first, then a scan, so streams
    // survive an assembly rename even though a type rename is still a breaking change.
    private static Type? ResolveType(string typeName)
    {
        var type = Type.GetType(typeName);
        if (type is not null) return type;

        return AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(a => { try { return a.GetTypes(); } catch { return []; } })
            .FirstOrDefault(t =>
                t.FullName == typeName ||
                $"{t.FullName}, {t.Assembly.GetName().Name}" == typeName);
    }
}
