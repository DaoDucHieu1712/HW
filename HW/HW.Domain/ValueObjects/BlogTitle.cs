using HW.Domain.Abstractions.ValueObjects;
using HW.Domain.Exceptions;

namespace HW.Domain.ValueObjects;

public sealed class BlogTitle : ValueObject<BlogTitle>
{
    public const int MaxLength = 200;

    public string Value { get; }

    private BlogTitle(string value) => Value = value;

    public static BlogTitle Create(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new BadRequestException("Blog title cannot be empty.");

        var trimmed = value.Trim();

        if (trimmed.Length > MaxLength)
            throw new BadRequestException($"Blog title cannot exceed {MaxLength} characters.");

        return new BlogTitle(trimmed);
    }

    public static BlogTitle FromPersistence(string value) => new(value);

    public override IEnumerable<object> GetAtomicValues() { yield return Value; }

    public static implicit operator string(BlogTitle title) => title.Value;
    public override string ToString() => Value;
}
