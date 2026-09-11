using MediatR;

namespace HW.UnitTests.TestSupport;

/// <summary>
/// Stands in for MediatR in the agent-tool tests.
///
/// <para>
/// The tools deliberately own no data access — they translate model-generated JSON into requests and
/// the responses back into text. That translation is what these tests are about, so the sender
/// records what it was handed and returns whatever the test staged; running the real handlers here
/// would only re-test them through a second door. An unstaged request type is an error rather than a
/// default, so a tool that quietly sends something unexpected fails loudly.
/// </para>
/// </summary>
public sealed class StubSender : ISender
{
    private readonly List<object> _sent = [];
    private readonly Dictionary<Type, Func<object, object?>> _responses = [];

    /// <summary>Every request the tool sent, in order.</summary>
    public IReadOnlyList<object> Sent => _sent;

    public StubSender Responds<TRequest>(Func<TRequest, object?> respond)
    {
        _responses[typeof(TRequest)] = request => respond((TRequest)request);
        return this;
    }

    public StubSender Responds<TRequest>(object? response) => Responds<TRequest>(_ => response);

    public IReadOnlyList<TRequest> SentOf<TRequest>() => _sent.OfType<TRequest>().ToList();

    /// <summary>The single request of that type the tool sent — fails if there were none, or several.</summary>
    public TRequest OnlySent<TRequest>() => Assert.Single(_sent.OfType<TRequest>());

    public bool SentAny<TRequest>() => _sent.OfType<TRequest>().Any();

    public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        => Task.FromResult((TResponse)Record(request)!);

    public Task Send<TRequest>(TRequest request, CancellationToken cancellationToken = default)
        where TRequest : IRequest
    {
        Record(request!);
        return Task.CompletedTask;
    }

    public Task<object?> Send(object request, CancellationToken cancellationToken = default)
        => Task.FromResult(Record(request));

    public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
        IStreamRequest<TResponse> request, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("No vocab tool streams.");

    public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("No vocab tool streams.");

    private object? Record(object request)
    {
        _sent.Add(request);

        if (_responses.TryGetValue(request.GetType(), out var respond))
            return respond(request);

        throw new InvalidOperationException(
            $"StubSender was sent a {request.GetType().Name} but no response was staged for it.");
    }
}
