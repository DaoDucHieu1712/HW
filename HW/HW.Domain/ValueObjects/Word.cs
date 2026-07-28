using HW.Domain.Abstractions.ValueObjects;
using HW.Domain.Exceptions;

namespace HW.Domain.ValueObjects;

public sealed class Word : ValueObject<Word>
{
    public const int MaxLength = 200;

    public string Value { get; }

    private Word(string value) => Value = value;

    public static Word Create(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new BadRequestException("Word cannot be empty.");

        var trimmed = value.Trim();

        if (trimmed.Length > MaxLength)
            throw new BadRequestException($"Word cannot exceed {MaxLength} characters.");

        return new Word(trimmed);
    }

    public static Word FromPersistence(string value) => new(value);

    public override IEnumerable<object> GetAtomicValues() { yield return Value; }

    public static implicit operator string(Word word) => word.Value;
    public override string ToString() => Value;
}
