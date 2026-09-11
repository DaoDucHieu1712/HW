using System.Collections;
using System.Collections.Concurrent;
using System.Reflection;

namespace HW.Application.Logging;

/// <summary>
/// Turns a request into something safe to write to a log sink.
///
/// <para>
/// Necessary because the pipeline logs the request payload, and commands carry credentials —
/// <c>RegisterCommand</c> and <c>LoginQuery</c> both hold a plaintext password. Logging the object
/// directly would put those passwords in the console, on disk, and in Seq's searchable index, where
/// they outlive the request by however long the retention policy says.
/// </para>
///
/// <para>
/// Values are masked by property name rather than by an attribute on the property. An attribute has
/// to be remembered on every new command; a name rule covers the ones nobody thought about, which
/// is precisely the case that leaks.
/// </para>
/// </summary>
public static class SensitiveDataRedactor
{
    private const string Mask = "***REDACTED***";

    /// <summary>
    /// Long values are truncated rather than dropped: an agent prompt or a note body can run to
    /// thousands of characters, and the first few hundred identify the request without turning
    /// every log event into a document.
    /// </summary>
    private const int MaxStringLength = 512;

    private static readonly string[] SensitiveNameFragments =
    [
        "password", "secret", "token", "apikey", "api_key",
        "credential", "authorization", "connectionstring", "privatekey", "signature"
    ];

    /// <summary>Reflection is done once per request type, not once per request.</summary>
    private static readonly ConcurrentDictionary<Type, PropertyInfo[]> PropertyCache = new();

    /// <summary>
    /// A property bag for structured logging: sensitive values masked, long values clipped, nested
    /// objects left as-is for the sink's own destructuring to handle.
    /// </summary>
    public static IReadOnlyDictionary<string, object?> Describe(object? request)
    {
        if (request is null) return new Dictionary<string, object?>();

        var properties = PropertyCache.GetOrAdd(
            request.GetType(),
            static type => type
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(property => property.CanRead && property.GetIndexParameters().Length == 0)
                .ToArray());

        var described = new Dictionary<string, object?>(properties.Length);

        foreach (var property in properties)
        {
            if (IsSensitive(property.Name))
            {
                described[property.Name] = Mask;
                continue;
            }

            // A property getter on a request is expected to be trivial, but this runs on a logging
            // path: a throwing getter must not take the request down with it.
            try
            {
                described[property.Name] = Summarize(property.GetValue(request));
            }
            catch (Exception ex)
            {
                described[property.Name] = $"<unreadable: {ex.GetType().Name}>";
            }
        }

        return described;
    }

    public static bool IsSensitive(string propertyName)
        => SensitiveNameFragments.Any(fragment =>
            propertyName.Contains(fragment, StringComparison.OrdinalIgnoreCase));

    private static object? Summarize(object? value) => value switch
    {
        null => null,
        string text => Truncate(text),

        // Collections are reported by size. The contents are rarely what identifies a request, and
        // a bulk command would otherwise write its entire payload on every call.
        ICollection collection => $"<{collection.Count} item(s)>",
        _ => value
    };

    private static string Truncate(string text)
        => text.Length <= MaxStringLength
            ? text
            : string.Concat(text.AsSpan(0, MaxStringLength), $"… (+{text.Length - MaxStringLength} chars)");
}
