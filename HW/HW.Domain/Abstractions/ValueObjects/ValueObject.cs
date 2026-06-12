namespace HW.Domain.Abstractions.ValueObjects;

public abstract class ValueObject<T> : IEquatable<T> where T : ValueObject<T>
{
    public abstract IEnumerable<object> GetAtomicValues();

    public bool Equals(T? other)
        => other is not null && GetAtomicValues().SequenceEqual(other.GetAtomicValues());

    public override bool Equals(object? obj)
        => obj is T other && Equals(other);

    public override int GetHashCode()
        => GetAtomicValues().Aggregate(default(int), HashCode.Combine);

    public static bool operator ==(ValueObject<T>? left, ValueObject<T>? right)
        => left?.Equals(right as T) ?? right is null;

    public static bool operator !=(ValueObject<T>? left, ValueObject<T>? right)
        => !(left == right);
}
