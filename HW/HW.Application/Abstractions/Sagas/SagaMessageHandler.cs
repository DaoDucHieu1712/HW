using HW.Application.Abstractions.Messaging;
using HW.Domain.Abstractions;
using HW.Domain.Abstractions.Sagas;
using Microsoft.Extensions.Logging;

namespace HW.Application.Abstractions.Sagas;

/// <summary>
/// The one place the load → decide → append → publish cycle is written. Every saga reply handler
/// derives from this and supplies only <see cref="Decide"/>, which is the actual business step;
/// the transaction, deduplication, and dispatch are identical for all of them and are easy to get
/// subtly wrong once per handler.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why <see cref="IUnitOfWork.ExecuteAsync"/> appears here even though CLAUDE.md forbids it in
/// command handlers.</b> That rule holds because <c>TransactionBehavior</c> already wraps every
/// <c>IBaseCommand</c> going through MediatR. A message handler is not a MediatR request — it is
/// invoked by the broker consumer in its own DI scope, with no pipeline around it — so nothing else
/// would open a transaction or call <c>SaveChanges</c>. Without this, <c>AppendAsync</c>'s event rows
/// and <c>PublishAsync</c>'s outbox rows would both be tracked, never saved, and dropped with the
/// scope: the saga would silently stop advancing.
/// </para>
/// <para>
/// <b>Everything commits together on purpose.</b> The inbox claim, the new events, and the outbox
/// rows for the next commands share one transaction. Split them and each seam becomes a way to lose
/// a saga: claim without events means the message is swallowed and the saga hangs forever; events
/// without outbox rows means the saga believes it asked for something nobody was told to do.
/// </para>
/// </remarks>
public abstract class SagaMessageHandler<TSaga, TMessage> : IMessageHandler<TMessage>
    where TSaga : EventSourcedSaga, new()
    where TMessage : class, ISagaMessage
{
    private readonly ISagaRepository _sagas;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IMessageBus _bus;
    private readonly ILogger _logger;

    protected SagaMessageHandler(ISagaRepository sagas, IUnitOfWork unitOfWork, IMessageBus bus, ILogger logger)
    {
        _sagas = sagas;
        _unitOfWork = unitOfWork;
        _bus = bus;
        _logger = logger;
    }

    /// <summary>
    /// Advances the saga. Call its decision methods and nothing else — no I/O, no publishing. The
    /// saga records what happened and queues what it wants sent; this class flushes both.
    /// </summary>
    protected abstract void Decide(TSaga saga, TMessage message);

    public async Task HandleAsync(TMessage message, MessageContext context, CancellationToken ct)
    {
        await _unitOfWork.ExecuteAsync(async () =>
        {
            if (!await _sagas.TryClaimMessageAsync(message.SagaId, context.MessageId, ct))
            {
                _logger.LogDebug(
                    "[Saga] {Message} {MessageId} already applied to saga {SagaId}; ignoring redelivery.",
                    typeof(TMessage).Name, context.MessageId, message.SagaId);

                return;
            }

            // Throwing rolls the claim back with everything else, so the retry gets a clean attempt.
            // A missing saga is a real fault — with the outbox in play, a reply cannot outrun the
            // committed stream that caused it — so let it retry and then dead-letter for a human.
            var saga = await _sagas.LoadAsync<TSaga>(message.SagaId, ct)
                ?? throw new InvalidOperationException(
                    $"No {typeof(TSaga).Name} stream exists for '{message.SagaId}', but a " +
                    $"{typeof(TMessage).Name} arrived for it.");

            Decide(saga, message);

            if (saga.UncommittedEvents.Count == 0)
            {
                // The saga's step guard rejected the message: a duplicate that beat the inbox, or a
                // reply for a step already past. Not an error — the claim still commits so it stops
                // here next time.
                _logger.LogInformation(
                    "[Saga] {Saga} {SagaId} is at {Step} and ignored {Message}.",
                    typeof(TSaga).Name, saga.Id, saga.CurrentStep, typeof(TMessage).Name);

                return;
            }

            await _sagas.AppendAsync(saga, ct);

            foreach (var pending in saga.PendingMessages)
                await SagaMessagePublisher.PublishAsync(_bus, pending, ct);

            _logger.LogInformation(
                "[Saga] {Saga} {SagaId} → {Step} ({Status}) after {Message}; {Events} event(s), {Sent} message(s).",
                typeof(TSaga).Name, saga.Id, saga.CurrentStep, saga.Status, typeof(TMessage).Name,
                saga.UncommittedEvents.Count, saga.PendingMessages.Count);

            saga.ClearUncommittedEvents();
            saga.ClearPendingMessages();
        });
    }
}
