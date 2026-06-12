namespace HW.Application.Features.Vocabs.Dtos;

public static class VocabDtos
{
    public record VocabResponseDto(
        string Id,
        string Word,
        string? Meaning,
        string? Example,
        string? Note,
        DateTimeOffset NotedAt,
        int ReviewStage,
        DateTimeOffset? NextReviewAt,
        DateTimeOffset? LastReviewedAt,
        bool IsCompleted,
        DateTimeOffset? CreatedAt,
        string? CreatedBy,
        DateTimeOffset? UpdatedAt,
        string? UpdatedBy);

    public record VocabPagingRequestDto(string? Search, int PageIndex, int PageSize);

    public record CreateVocabRequestDto(string? Word, string? Meaning, string? Example, string? Note);

    public record UpdateVocabRequestDto(string Id, string? Word, string? Meaning, string? Example, string? Note);
}
