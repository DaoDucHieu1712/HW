
using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using HW.Application.Abstractions.Messaging;

namespace HW.Infrastructure.Messaging;

/// <summary>
/// The wire format both adapters read and write.
///
/// Keeping one envelope across providers is what lets <c>Messaging:Provider</c> be a deployment
/// choice: a payload written by the RabbitMQ adapter deserializes unchanged under the Kafka adapter.
/// It is plain JSON with no CLR type names in the routing path, so non-.NET producers and consumers
/// can interoperate by agreeing on the topic name alone.
/// </summary>
/// <param name="MessageId">Assigned once at publish; preserved across every redelivery.</param>
/// <param name="Topic">Logical stream — also the type-resolution key on the consume side.</param>
/// <param name="PartitionKey">Ordering key, honoured by Kafka only.</param>
/// <param name="PublishedAtUtc">Publish-side timestamp.</param>
/// <param name="Headers">Correlation and tracing data.</param>
/// <param name="Payload">The message body, held un-deserialized until the target type is known.</param>
/// <param name="PayloadType">
/// CLR type name, written for diagnostics only. Deliberately not used to resolve the type: doing so
/// would make a namespace rename a breaking wire change, and would exclude non-.NET producers.
/// </param>
internal sealed record MessageEnvelope(
    string MessageId,
    string Topic,
    string? PartitionKey,
    DateTimeOffset PublishedAtUtc,
    IReadOnlyDictionary<string, string> Headers,
    JsonElement Payload,
    string? PayloadType);

/// <summary>
/// Serializes envelopes and resolves the <c>[Message]</c> topic for a CLR type.
/// </summary>
internal static class MessageSerializer
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly ConcurrentDictionary<Type, string> TopicCache = new();

    /// <summary>
    /// Returns the topic declared by <typeparamref name="TMessage"/>'s <see cref="MessageAttribute"/>.
    /// </summary>
    public static string TopicOf<TMessage>() => TopicOf(typeof(TMessage));

    /// <inheritdoc cref="TopicOf{TMessage}"/>
    /// <exception cref="InvalidOperationException">The type carries no attribute.</exception>
    public static string TopicOf(Type messageType) => TopicCache.GetOrAdd(messageType, static type =>
        type.GetCustomAttribute<MessageAttribute>()?.Topic
        ?? throw new InvalidOperationException(
            $"'{type.Name}' cannot be published or consumed without a topic. " +
            $"Annotate it: [Message(\"{ToSuggestedTopic(type.Name)}\")]."));

    public static byte[] Serialize(MessageEnvelope envelope)
        => JsonSerializer.SerializeToUtf8Bytes(envelope, Options);

    /// <summary>
    /// Reads an envelope off the wire. Returns null on malformed JSON rather than throwing: a
    /// poison payload must not be retried, and it must not take the consumer loop down.
    /// </summary>
    public static MessageEnvelope? TryDeserialize(ReadOnlySpan<byte> body)
    {
        try
        {
            return JsonSerializer.Deserialize<MessageEnvelope>(body, Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Turns <c>VocabReviewedIntegrationEvent</c> into <c>vocab.reviewed</c> for the error hint.</summary>
    private static string ToSuggestedTopic(string typeName)
    {
        var trimmed = typeName
            .Replace("IntegrationEvent", string.Empty, StringComparison.Ordinal)
            .Replace("Message", string.Empty, StringComparison.Ordinal)
            .Replace("Event", string.Empty, StringComparison.Ordinal);

        var words = new List<string>();
        var start = 0;

        for (var i = 1; i <= trimmed.Length; i++)
        {
            if (i != trimmed.Length && !char.IsUpper(trimmed[i])) continue;

            words.Add(trimmed[start..i].ToLowerInvariant());
            start = i;
        }

        return words.Count == 0 ? typeName.ToLowerInvariant() : string.Join('.', words);
    }
}
