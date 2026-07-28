using FluentValidation;
using HW.Application.CQRS;
using HW.Application.Features.Vocabs.Dtos;
using HW.Domain.Abstractions.Repositories;
using HW.Domain.Entities;

namespace HW.Application.Features.Vocabs.Queries.GradeVocabExam;

public record GradeVocabExamQuery(List<VocabDtos.ExamAnswerDto> Answers)
    : IQuery<VocabDtos.VocabExamResultDto>;

public class GradeVocabExamQueryValidator : AbstractValidator<GradeVocabExamQuery>
{
    public GradeVocabExamQueryValidator()
    {
        RuleFor(x => x.Answers).NotEmpty();
        RuleForEach(x => x.Answers).ChildRules(a =>
        {
            a.RuleFor(x => x.QuestionId).NotEmpty();
            a.RuleFor(x => x.VocabId).NotEmpty();
            a.RuleFor(x => x.Answer).NotNull();
        });
    }
}

public class GradeVocabExamQueryHandler
    : IQueryHandler<GradeVocabExamQuery, VocabDtos.VocabExamResultDto>
{
    private readonly IRepository<Vocab> _repository;

    public GradeVocabExamQueryHandler(IRepository<Vocab> repository)
        => _repository = repository;

    public async Task<VocabDtos.VocabExamResultDto> Handle(GradeVocabExamQuery request, CancellationToken ct)
    {
        var vocabIds = request.Answers.Select(a => a.VocabId).Distinct().ToList();
        var vocabs = _repository.FindAll(v => vocabIds.Contains(v.Id))
                                .ToDictionary(v => v.Id);

        var results = new List<VocabDtos.VocabExamAnswerResultDto>();
        int correct = 0;

        foreach (var answer in request.Answers)
        {
            vocabs.TryGetValue(answer.VocabId, out var vocab);
            var word = vocab is not null ? (string)vocab.Word : answer.VocabId;
            var correctAnswer = Grade(answer, vocab);
            var isCorrect = correctAnswer.IsCorrect;
            if (isCorrect) correct++;

            results.Add(new VocabDtos.VocabExamAnswerResultDto(
                answer.QuestionId,
                word,
                correctAnswer.ExpectedAnswer,
                answer.Answer?.Trim() ?? string.Empty,
                isCorrect));
        }

        var total = request.Answers.Count;
        var score = total > 0 ? Math.Round((double)correct / total * 100, 2) : 0;

        return new VocabDtos.VocabExamResultDto(total, correct, score, results);
    }

    private static (bool IsCorrect, string ExpectedAnswer) Grade(VocabDtos.ExamAnswerDto answer, Vocab? vocab)
    {
        var submitted = answer.Answer?.Trim() ?? string.Empty;
        var content = vocab?.Content ?? string.Empty;

        // TrueFalse: answer is "true"/"false"; correct if displayed content matches vocab content
        if (answer.Type == 1)
        {
            var displayedMatchesReal = string.Equals(
                answer.DisplayedContent?.Trim(), content, StringComparison.OrdinalIgnoreCase);
            var expected = displayedMatchesReal ? "true" : "false";
            return (string.Equals(submitted, expected, StringComparison.OrdinalIgnoreCase), expected);
        }

        // MultipleChoice / Written: answer must match vocab content
        return (string.Equals(submitted, content, StringComparison.OrdinalIgnoreCase), content);
    }
}
