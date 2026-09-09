using HW.Application.Features.Vocabs.Dtos;

namespace HW.Application.Features.Vocabs.Agent;

public interface IVocabAgent
{
    Task<VocabAgentDtos.VocabAgentResponseDto> RunAsync(
        VocabAgentDtos.AskVocabAgentRequestDto request,
        CancellationToken ct = default);
}
