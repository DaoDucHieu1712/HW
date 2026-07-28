using FluentValidation;
using HW.Application.CQRS;
using HW.Application.Features.Vocabs.Dtos;
using HW.Domain.Abstractions.Repositories;
using HW.Domain.Entities;

namespace HW.Application.Features.Vocabs.Queries.GenerateVocabExam;

public record GenerateVocabExamQuery(
    int QuestionCount,
    DateTimeOffset? From,
    DateTimeOffset? To) : IQuery<List<VocabDtos.VocabExamQuestionDto>>;

public class GenerateVocabExamQueryValidator : AbstractValidator<GenerateVocabExamQuery>
{
    public GenerateVocabExamQueryValidator()
    {
        RuleFor(x => x.QuestionCount).GreaterThan(0);
    }
}

public class GenerateVocabExamQueryHandler
    : IQueryHandler<GenerateVocabExamQuery, List<VocabDtos.VocabExamQuestionDto>>
{
    private readonly IRepository<Vocab> _repository;
    private static readonly Random _rng = new();

    public GenerateVocabExamQueryHandler(IRepository<Vocab> repository)
        => _repository = repository;

    public Task<List<VocabDtos.VocabExamQuestionDto>> Handle(GenerateVocabExamQuery request, CancellationToken ct)
    {
        var query = _repository.FindAll();

        if (request.From.HasValue)
            query = query.Where(v => v.NotedAt >= request.From.Value);

        if (request.To.HasValue)
            query = query.Where(v => v.NotedAt <= request.To.Value);

        var all = query.ToList();

        if (all.Count == 0)
            throw new InvalidOperationException("No vocab words found.");

        var pool = all.OrderBy(_ => _rng.Next()).Take(request.QuestionCount).ToList();

        var allContents = all
            .Where(v => v.Content != null)
            .Select(v => v.Content!)
            .Distinct()
            .ToList();

        var questions = pool.Select(v => BuildQuestion(v, allContents)).ToList();

        return Task.FromResult(questions);
    }

    private static VocabDtos.VocabExamQuestionDto BuildQuestion(Vocab vocab, List<string> allContents)
    {
        var word = (string)vocab.Word;
        var content = vocab.Content ?? string.Empty;
        var type = _rng.Next(3); // 0=MultipleChoice, 1=TrueFalse, 2=Written

        return type switch
        {
            0 => BuildMultipleChoice(vocab.Id, word, content, allContents),
            1 => BuildTrueFalse(vocab.Id, word, content, allContents),
            _ => new VocabDtos.VocabExamQuestionDto(Guid.NewGuid().ToString(), vocab.Id, 2, word, null, null)
        };
    }

    private static VocabDtos.VocabExamQuestionDto BuildMultipleChoice(
        string vocabId, string word, string content, List<string> allContents)
    {
        var distractors = allContents
            .Where(c => !string.Equals(c, content, StringComparison.OrdinalIgnoreCase))
            .OrderBy(_ => _rng.Next())
            .Take(3)
            .ToList();

        var options = distractors.Append(content).OrderBy(_ => _rng.Next()).ToList();
        return new VocabDtos.VocabExamQuestionDto(Guid.NewGuid().ToString(), vocabId, 0, word, null, options);
    }

    private static VocabDtos.VocabExamQuestionDto BuildTrueFalse(
        string vocabId, string word, string content, List<string> allContents)
    {
        bool showCorrect = allContents.Count <= 1 || _rng.Next(2) == 0;

        var displayed = showCorrect
            ? content
            : allContents
                .Where(c => !string.Equals(c, content, StringComparison.OrdinalIgnoreCase))
                .OrderBy(_ => _rng.Next())
                .First();

        return new VocabDtos.VocabExamQuestionDto(Guid.NewGuid().ToString(), vocabId, 1, word, displayed, null);
    }
}
