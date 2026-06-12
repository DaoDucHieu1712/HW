using HW.Domain.Abstractions.ValueObjects;
using HW.Domain.Exceptions;

namespace HW.Domain.ValueObjects;

public sealed class BlogContent : ValueObject<BlogContent>
{
    public string Value { get; }

    private BlogContent(string value) => Value = value;

    public static BlogContent Create(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new BadRequestException("Blog content cannot be empty.");

        return new BlogContent(value.Trim());
    }

    public static BlogContent FromPersistence(string value) => new(value);

    public override IEnumerable<object> GetAtomicValues() { yield return Value; }

    public static implicit operator string(BlogContent content) => content.Value;
    public override string ToString() => Value;
}
