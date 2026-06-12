using System.Text.RegularExpressions;
using HW.Domain.Abstractions.ValueObjects;
using HW.Domain.Exceptions;

namespace HW.Domain.ValueObjects;

public sealed class Email : ValueObject<Email>
{
    private static readonly Regex EmailRegex = new(
        @"^[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase,
        TimeSpan.FromMilliseconds(100));

    public string Value { get; }

    private Email(string value) => Value = value;

    public static Email Create(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new BadRequestException("Email cannot be empty.");

        var normalized = value.Trim().ToLowerInvariant();

        if (!EmailRegex.IsMatch(normalized))
            throw new BadRequestException($"'{value}' is not a valid email address.");

        return new Email(normalized);
    }

    public override IEnumerable<object> GetAtomicValues() { yield return Value; }

    public static implicit operator string(Email email) => email.Value;
    public override string ToString() => Value;
}
