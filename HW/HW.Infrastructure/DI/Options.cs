using System.ComponentModel.DataAnnotations;

namespace HW.Infrastructure.DI;

public static class Options
{
    public record MariaDbRetryOptions
    {
        [Required, Range(5, 20)] public int MaxRetryCount { get; init; }
        [Required, Timestamp] public TimeSpan MaxRetryDelay { get; init; }
        public int[]? ErrorNumbersToAdd { get; init; }
    }
}
