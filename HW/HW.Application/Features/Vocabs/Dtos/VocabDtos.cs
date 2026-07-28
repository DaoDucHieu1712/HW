namespace HW.Application.Features.Vocabs.Dtos;

public static class VocabDtos
{
    public record VocabResponseDto(
        string Id,
        string Word,
        string? Content,
        DateTimeOffset NotedAt,
        int ReviewStage,
        DateTimeOffset? NextReviewAt,
        DateTimeOffset? LastReviewedAt,
        bool IsCompleted,
        DateTimeOffset? CreatedAt,
        string? CreatedBy,
        DateTimeOffset? UpdatedAt,
        string? UpdatedBy);

    public record VocabPagingRequestDto(string? Search, DateTimeOffset? FromDate, DateTimeOffset? ToDate, int PageIndex, int PageSize);

    public record CreateVocabRequestDto(string? Word, string? Content);

    public record UpdateVocabRequestDto(string? Word, string? Content);

    // --- FlashCard ---

    public record GetFlashCardsRequestDto(int? Count, int? ReviewStage, bool UseDaily = false);

    public record FlashCardDto(
        string VocabId,
        string Word,
        string? Content,
        int ReviewStage);

    public record FlashCardResultRequestDto(string VocabId, bool Knew);

    public record SubmitFlashCardSessionRequestDto(List<FlashCardResultRequestDto> Results);

    public record FlashCardSessionResultDto(
        int TotalCards,
        int KnewCount,
        int DidntKnowCount,
        List<string> AdvancedVocabIds);

    // --- Exam ---

    public record GenerateVocabExamRequestDto(int QuestionCount, DateTimeOffset? From, DateTimeOffset? To);

    // 0 = MultipleChoice, 1 = TrueFalse, 2 = Written
    public record VocabExamQuestionDto(
        string QuestionId,
        string VocabId,
        int Type,
        string Word,
        string? DisplayedContent,   // TrueFalse: the content shown to the user
        List<string>? Options);     // MultipleChoice: the four choices (Content values)

    public record ExamAnswerDto(
        string QuestionId,
        string VocabId,
        int Type,
        string? DisplayedContent,  // required for TrueFalse grading
        string Answer);

    public record GradeVocabExamRequestDto(List<ExamAnswerDto> Answers);

    public record VocabExamAnswerResultDto(
        string QuestionId,
        string Word,
        string CorrectAnswer,
        string SubmittedAnswer,
        bool IsCorrect);

    public record VocabExamResultDto(
        int TotalQuestions,
        int CorrectCount,
        double Score,
        List<VocabExamAnswerResultDto> Results);
}
